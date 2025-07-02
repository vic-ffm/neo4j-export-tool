namespace Neo4jExport

open System
open System.Collections.Generic
open System.IO
open System.Threading
open Neo4j.Driver


/// Thread-safe wrapper that prevents concurrent session usage
type SafeSession(session: IAsyncSession) =
    let inUse = ref 0

    member _.RunAsync(query: string, ?parameters: IDictionary<string, obj>) : Async<IResultCursor> =
        async {
            if Interlocked.CompareExchange(inUse, 1, 0) <> 0 then
                return
                    raise (
                        InvalidOperationException(
                            "Session is already in use. Neo4j sessions are not thread-safe. "
                            + "Create separate sessions for concurrent operations."
                        )
                    )

            try
                let! result =
                    match parameters with
                    | Some p -> session.RunAsync(query, p) |> Async.AwaitTask
                    | None -> session.RunAsync(query) |> Async.AwaitTask

                return result
            finally
                Interlocked.Exchange(inUse, 0) |> ignore
        }

    interface IDisposable with
        member _.Dispose() = session.Dispose()

/// Neo4j database interaction with resilience patterns
module Neo4j =
    module private Validation =
        /// Result builder for validation pipelines
        type ValidationBuilder() =
            member _.Return(x) = Ok x
            member _.ReturnFrom(x) = x
            member _.Bind(x, f) = Result.bind f x
            member _.Zero() = Ok()

        let validation = ValidationBuilder()

        /// Validate with a predicate and error message
        let validate pred error value =
            if pred value then Ok value else Error error

        /// Validation combinators
        let notEmpty fieldName value =
            validate
                (String.IsNullOrWhiteSpace >> not)
                (QueryError(value, sprintf "%s cannot be empty" fieldName, None))
                value

        let positive fieldName value =
            validate (fun v -> v > 0) (ConfigError(sprintf "%s must be positive" fieldName)) value

        /// Composed validators
        let validateQuery = notEmpty "Query"

        let validateMaxResults =
            positive "maxResults"

    type private CircuitState =
        | Closed
        | Open of DateTime
        | HalfOpen

    /// Opaque circuit breaker handle for thread-safe resilience
    type CircuitBreaker =
        private
            { mutable State: CircuitState
              mutable ConsecutiveFailures: int
              mutable SuccessesInHalfOpen: int // ADD THIS
              Threshold: int
              Duration: TimeSpan
              RequiredSuccesses: int // ADD THIS
              Lock: obj } // For thread-safe state mutations

    /// Creates a new circuit breaker instance
    let createCircuitBreaker threshold duration =
        { State = Closed
          ConsecutiveFailures = 0
          SuccessesInHalfOpen = 0
          Threshold = threshold
          Duration = duration
          RequiredSuccesses = 3 // Require 3 successes in half-open
          Lock = obj () }

    /// Thread-safe random number generator for jitter
    module private RandomGen =
        let private random = Random()
        let private randomLock = obj ()

        let next minValue maxValue =
            lock randomLock (fun () -> random.Next(minValue, maxValue))

    /// Executes operations with circuit breaker pattern and exponential backoff retry
    let private executeWithResilience<'T> (breaker: CircuitBreaker) (config: ExportConfig) (operation: Async<'T>) =
        async {
            let checkCircuitBreaker () =
                lock breaker.Lock (fun () ->
                    match breaker.State with
                    | Open endTime when DateTime.UtcNow < endTime ->
                        raise (InvalidOperationException("Circuit breaker is open - too many recent failures"))
                    | Open _ ->
                        breaker.State <- HalfOpen
                        breaker.SuccessesInHalfOpen <- 0 // ADD THIS
                        Log.info "Circuit breaker entering half-open state"
                    | _ -> ())

            let recordSuccess () =
                lock breaker.Lock (fun () ->
                    match breaker.State with
                    | HalfOpen ->
                        breaker.SuccessesInHalfOpen <- breaker.SuccessesInHalfOpen + 1

                        if
                            breaker.SuccessesInHalfOpen
                            >= breaker.RequiredSuccesses
                        then
                            breaker.State <- Closed
                            breaker.ConsecutiveFailures <- 0
                            breaker.SuccessesInHalfOpen <- 0
                            Log.info "Circuit breaker closed after sufficient successes"
                    | _ -> breaker.ConsecutiveFailures <- 0)

            let recordFailure () =
                lock breaker.Lock (fun () ->
                    breaker.ConsecutiveFailures <- breaker.ConsecutiveFailures + 1

                    if breaker.ConsecutiveFailures >= breaker.Threshold then
                        breaker.State <- Open(DateTime.UtcNow.Add(breaker.Duration))

                        Log.error (
                            sprintf "Circuit breaker opened due to %d consecutive failures" breaker.ConsecutiveFailures
                        ))

            let isRetryable (ex: Exception) =
                match ex with
                | :? ServiceUnavailableException -> true
                | :? SessionExpiredException -> true
                | :? TransientException -> true
                | :? IOException -> true
                | :? TimeoutException -> true
                | _ -> false

            let rec attemptWithRetry retryCount =
                async {
                    try
                        checkCircuitBreaker ()
                        let! result = operation
                        recordSuccess ()
                        return result
                    with
                    | ex when isRetryable ex && retryCount < config.MaxRetries ->
                        let delay =
                            let exponentialDelay =
                                config.RetryDelayMs
                                * int (Math.Pow(2.0, float retryCount))

                            min config.MaxRetryDelayMs exponentialDelay

                        let jitter = RandomGen.next 0 (delay / 4)
                        let totalDelay = delay + jitter

                        Log.warn (
                            sprintf
                                "Retry %d/%d after %dms: %s"
                                (retryCount + 1)
                                config.MaxRetries
                                totalDelay
                                ex.Message
                        )

                        do! Async.Sleep totalDelay
                        return! attemptWithRetry (retryCount + 1)
                    | ex ->
                        recordFailure ()
                        return raise ex
                }

            return! attemptWithRetry 0
        }

    /// Executes a Neo4j query with streaming results and comprehensive resilience
    let executeQueryStreaming
        (session: SafeSession)
        (breaker: CircuitBreaker)
        (config: ExportConfig)
        (query: string)
        (processRecord: IRecord -> Async<unit>)
        =
        async {
            try
                let! result =
                    executeWithResilience
                        breaker
                        config
                        (async {
                            let! cursor = session.RunAsync(query)

                            try
                                // Use while loop for constant memory usage with large datasets
                                let mutable continueProcessing = true

                                while continueProcessing do
                                    let! hasMore = cursor.FetchAsync() |> Async.AwaitTask

                                    if hasMore then
                                        do! processRecord cursor.Current
                                    else
                                        continueProcessing <- false

                                // Explicitly consume the cursor for proper cleanup and to get result summary
                                let! _ = cursor.ConsumeAsync() |> Async.AwaitTask
                                return ()
                            with ex ->
                                // On exception, ensure cursor is consumed to free server resources
                                try
                                    do!
                                        cursor.ConsumeAsync()
                                        |> Async.AwaitTask
                                        |> Async.Ignore
                                with _ ->
                                    () // Ignore any errors during cleanup

                                raise ex // Re-raise the original exception
                        })

                return Ok result
            with
            | :? AuthenticationException as ex ->
                // Use ConnectionError to preserve exception details
                return Error(ConnectionError(sprintf "Authentication failed: %s" ex.Message, Some ex))
            | :? ClientException as ex when ex.Message.Contains("Neo.ClientError.Security") ->
                // Use ConnectionError to preserve exception details
                return Error(ConnectionError("Authentication failed: Invalid credentials", Some ex))
            | :? ClientException as ex when ex.Message.Contains("Neo.ClientError.Procedure.ProcedureNotFound") ->
                return Error(QueryError(query, "Procedure not found", Some ex))
            | :? TimeoutException as ex ->
                // Use QueryError to preserve exception details for timeout
                return
                    Error(
                        QueryError(
                            query,
                            sprintf "Query timed out after %d seconds" config.QueryTimeoutSeconds,
                            Some ex
                        )
                    )
            | ex -> return Error(QueryError(query, ex.Message, Some ex))
        }

    /// Executes a Neo4j query and returns results as a list - ONLY for small result sets
    let executeQueryList
        (session: SafeSession)
        (breaker: CircuitBreaker)
        (config: ExportConfig)
        (query: string)
        (f: IRecord -> 'T)
        (maxResults: int)
        =
        async {
            match Validation.validateQuery query, Validation.validateMaxResults maxResults with
            | Ok validQuery, Ok validMax ->
                let results = ResizeArray<'T>(validMax)
                let mutable count = 0

                let mutable exceeded = false

                let processRecord record =
                    async {
                        if count >= validMax then
                            exceeded <- true
                        else
                            results.Add(f record)
                            count <- count + 1
                    }

                match! executeQueryStreaming session breaker config validQuery processRecord with
                | Ok _ ->
                    if exceeded then
                        return
                            Error(
                                QueryError(
                                    validQuery,
                                    $"Query returned more than {validMax} records. Use executeQueryStreaming for large result sets.",
                                    None
                                )
                            )
                    else
                        return Ok(results |> List.ofSeq)
                | Error e -> return Error e
            | Error e1, Error e2 ->
                // Both validations failed - combine error messages
                let combinedMessage =
                    match e1, e2 with
                    | QueryError(_, msg1, _), QueryError(_, msg2, _) ->
                        sprintf "Multiple query errors: %s; %s" msg1 msg2
                    | QueryError(_, msg1, _), ConfigError msg2
                    | ConfigError msg1, QueryError(_, msg2, _) -> sprintf "Query and config errors: %s; %s" msg1 msg2
                    | ConfigError msg1, ConfigError msg2 -> sprintf "Multiple config errors: %s; %s" msg1 msg2
                    | QueryError(_, msg1, _), err2 -> sprintf "Multiple errors: %s; %s" msg1 (err2.ToString())
                    | err1, QueryError(_, msg2, _) -> sprintf "Multiple errors: %s; %s" (err1.ToString()) msg2
                    | err1, err2 -> sprintf "Multiple errors: %s; %s" (err1.ToString()) (err2.ToString())

                return Error(ConfigError combinedMessage)
            | Error e, _
            | _, Error e -> return Error e
        }
