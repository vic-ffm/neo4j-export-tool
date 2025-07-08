// MIT License
//
// Copyright (c) 2025-present State Government of Victoria
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

namespace Neo4jExport

open System
open System.Collections.Generic
open System.IO
open System.Threading
open Neo4j.Driver


/// Prevents concurrent session usage. Neo4j sessions are not thread-safe.
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

/// Neo4j database operations with circuit breaker and retry logic
module Neo4j =
    module private Validation =
        let notEmpty fieldName value =
            if String.IsNullOrWhiteSpace(value) |> not then
                Ok value
            else
                Error(QueryError(value, sprintf "%s cannot be empty" fieldName, None))

        let positive fieldName value =
            if value > 0 then
                Ok value
            else
                Error(ConfigError(sprintf "%s must be positive" fieldName))

        let validateQuery = notEmpty "Query"

        let validateMaxResults =
            positive "maxResults"

    type private CircuitState =
        | Closed
        | Open of DateTime
        | HalfOpen

    /// Circuit breaker for handling transient failures
    type CircuitBreaker =
        private
            { mutable State: CircuitState
              mutable ConsecutiveFailures: int
              mutable SuccessesInHalfOpen: int
              Threshold: int
              Duration: TimeSpan
              RequiredSuccesses: int
              Lock: obj }

    let createCircuitBreaker threshold duration =
        { State = Closed
          ConsecutiveFailures = 0
          SuccessesInHalfOpen = 0
          Threshold = threshold
          Duration = duration
          RequiredSuccesses = 3
          Lock = obj () }

    module private RandomGen =
        let private random = Random()
        let private randomLock = obj ()

        let next minValue maxValue =
            lock randomLock (fun () -> random.Next(minValue, maxValue))

    /// Retry outcome types for deduplication
    type RetryOutcome<'T> =
        | Success of 'T
        | FailedAfterRetries of RetryInfo

    and RetryInfo =
        { FirstException: exn
          RetryCount: int
          TotalDelayMs: int
          LastException: exn }

    type RetryState =
        { Attempt: int
          FirstException: exn option
          TotalDelayMs: int }

    let private executeWithResilience<'T> (breaker: CircuitBreaker) (config: ExportConfig) (operation: Async<'T>) =
        async {
            let checkCircuitBreaker () =
                lock breaker.Lock (fun () ->
                    match breaker.State with
                    | Open endTime when DateTime.UtcNow < endTime ->
                        raise (InvalidOperationException("Circuit breaker is open - too many recent failures"))
                    | Open _ ->
                        breaker.State <- HalfOpen
                        breaker.SuccessesInHalfOpen <- 0
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

            let calculateDelay attempt =
                let exponentialDelay =
                    config.RetryDelayMs
                    * int (Math.Pow(2.0, float attempt))

                let delay =
                    min config.MaxRetryDelayMs exponentialDelay

                let jitter = RandomGen.next 0 (delay / 4)
                delay + jitter

            let rec attemptWithRetry (state: RetryState) =
                async {
                    try
                        checkCircuitBreaker ()
                        let! result = operation
                        recordSuccess ()
                        return Success result
                    with
                    | ex when
                        isRetryable ex
                        && state.Attempt < config.MaxRetries
                        ->
                        // NO Log.warn here - accumulate instead
                        let delay = calculateDelay state.Attempt
                        do! Async.Sleep delay

                        let newState =
                            { Attempt = state.Attempt + 1
                              FirstException = state.FirstException |> Option.orElse (Some ex)
                              TotalDelayMs = state.TotalDelayMs + delay }

                        return! attemptWithRetry newState
                    | ex ->
                        recordFailure ()

                        match state.FirstException with
                        | Some firstEx ->
                            return
                                FailedAfterRetries
                                    { FirstException = firstEx
                                      RetryCount = state.Attempt
                                      TotalDelayMs = state.TotalDelayMs
                                      LastException = ex }
                        | None -> return raise ex
                }

            let! retryResult =
                attemptWithRetry
                    { Attempt = 0
                      FirstException = None
                      TotalDelayMs = 0 }

            match retryResult with
            | Success result -> return result
            | FailedAfterRetries info ->
                // Single consolidated log entry
                Log.error (
                    sprintf
                        "Operation failed after %d retries over %dms. First error: %s. Last error: %s"
                        info.RetryCount
                        info.TotalDelayMs
                        (ErrorAccumulation.exceptionToString info.FirstException)
                        (ErrorAccumulation.exceptionToString info.LastException)
                )

                return raise info.LastException
        }

    /// Executes a Neo4j query with streaming results
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
                                let mutable continueProcessing = true

                                while continueProcessing do
                                    let! hasMore = cursor.FetchAsync() |> Async.AwaitTask

                                    if hasMore then
                                        do! processRecord cursor.Current
                                    else
                                        continueProcessing <- false

                                let! _ = cursor.ConsumeAsync() |> Async.AwaitTask
                                return ()
                            with ex ->
                                try
                                    do!
                                        cursor.ConsumeAsync()
                                        |> Async.AwaitTask
                                        |> Async.Ignore
                                with _ ->
                                    ()

                                raise ex
                        })

                return Ok result
            with
            | :? AuthenticationException as ex ->
                return
                    Error(
                        ConnectionError(
                            sprintf "Authentication failed: %s" (ErrorAccumulation.exceptionToString ex),
                            Some ex
                        )
                    )
            | :? ClientException as ex when ex.Message.Contains("Neo.ClientError.Security") ->
                return Error(ConnectionError("Authentication failed: Invalid credentials", Some ex))
            | :? ClientException as ex when ex.Message.Contains("Neo.ClientError.Procedure.ProcedureNotFound") ->
                return Error(QueryError(query, "Procedure not found", Some ex))
            | :? TimeoutException as ex ->
                return
                    Error(
                        QueryError(
                            query,
                            sprintf "Query timed out after %d seconds" config.QueryTimeoutSeconds,
                            Some ex
                        )
                    )
            | ex -> return Error(QueryError(query, ErrorAccumulation.exceptionToString ex, Some ex))
        }

    /// Executes a query and returns results as a list. Use only for small result sets.
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
                let combinedError =
                    ErrorAccumulation.singleton e1
                    |> ErrorAccumulation.cons e2
                    |> ErrorAccumulation.toConfigError

                return Error combinedError
            | Error e, _
            | _, Error e -> return Error e
        }

    /// Public query execution functions returned by factory
    [<AbstractClass>]
    type QueryExecutors() =
        abstract member StreamQuery: string -> (IRecord -> Async<unit>) -> Async<Result<unit, AppError>>
        abstract member ListQuery<'T> : string -> (IRecord -> 'T) -> int -> Async<Result<'T list, AppError>>

    /// Create query executors with captured database context
    let createQueryExecutors session breaker config =
        { new QueryExecutors() with
            member _.StreamQuery query processRecord =
                executeQueryStreaming session breaker config query processRecord

            member _.ListQuery query mapper maxResults =
                executeQueryList session breaker config query mapper maxResults }
