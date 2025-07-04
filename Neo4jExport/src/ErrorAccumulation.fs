namespace Neo4jExport

open System
open System.Collections.Generic

module ErrorAccumulation =
    /// Opaque type for accumulating errors
    type ErrorAccumulator = private ErrorAccumulator of NonEmptyList<AppError>

    // --- Utility Functions  ---

    /// Format an exception with type name
    /// NOTE: This is used for both error returns (AppError instances) and log messages.
    /// The distinction is WHERE this formatted string ends up:
    /// - In an AppError instance → Business logic failure that affects operation outcome
    /// - In a Log.warn/error call → Infrastructure failure for diagnostics only
    let exceptionToString (ex: exn) =
        sprintf "%s: %s" (ex.GetType().Name) ex.Message

    /// Format an AppError as a string
    /// AppError instances represent BUSINESS LOGIC FAILURES that:
    /// - Are part of the normal application flow
    /// - Can be handled by callers (retry, fallback, user notification)
    /// - Affect the operation's outcome (e.g., export cannot proceed)
    ///
    /// DO NOT create AppError for infrastructure failures like:
    /// - Signal handler registration failures
    /// - Logging system failures
    /// - Resource disposal errors during shutdown
    /// These should use Log.error/warn instead
    let rec appErrorToString =
        function
        | ConfigError msg -> msg
        | SecurityError msg -> msg
        | FileSystemError(path, msg, _) -> sprintf "%s: %s" path msg
        | ConnectionError(msg, _) -> msg
        | AuthenticationError msg -> msg
        | QueryError(_, msg, _) -> msg
        | DataCorruptionError(line, msg, _) -> sprintf "Line %d: %s" line msg
        | DiskSpaceError(required, available) ->
            sprintf
                "Insufficient disk space: required %d GB, available %d GB"
                (required / 1073741824L)
                (available / 1073741824L)
        | MemoryError msg -> msg
        | ExportError(msg, _) -> msg
        | TimeoutError(operation, duration) -> sprintf "Operation '%s' timed out after %A" operation duration
        | AggregateError nel ->
            let errors = NonEmptyList.toList nel

            match errors with
            | [ single ] -> appErrorToString single
            | many ->
                many
                |> List.map appErrorToString
                |> String.concat "; "

    // --- Accumulation Operations ---

    /// Create an accumulator with a single error
    let singleton (error: AppError) : ErrorAccumulator =
        ErrorAccumulator(NonEmptyList.singleton error)

    /// Add an error to the front of the accumulator
    let cons (error: AppError) (ErrorAccumulator nel) : ErrorAccumulator =
        ErrorAccumulator(NonEmptyList.cons error nel)

    /// Append two accumulators
    let append (ErrorAccumulator nel1) (ErrorAccumulator nel2) : ErrorAccumulator =
        ErrorAccumulator(NonEmptyList.append nel1 nel2)

    /// Create from a list of errors (returns None if empty)
    let ofList (errors: AppError list) : ErrorAccumulator option =
        errors
        |> NonEmptyList.ofList
        |> Option.map ErrorAccumulator

    /// Extract errors from a list of Results
    let fromResults (results: Result<'a, AppError> list) : ErrorAccumulator option =
        results
        |> List.choose (function
            | Error e -> Some e
            | Ok _ -> None)
        |> ofList

    // --- Deduplication Operations ---

    /// Remove duplicate errors based on a key function
    let deduplicate (keyFn: AppError -> 'key) (ErrorAccumulator nel) : ErrorAccumulator option =
        nel
        |> NonEmptyList.toList
        |> List.distinctBy keyFn
        |> NonEmptyList.ofList
        |> Option.map ErrorAccumulator

    /// Remove duplicate errors using default equality
    let deduplicateDefault (ErrorAccumulator nel) : ErrorAccumulator option =
        nel
        |> NonEmptyList.toList
        |> List.distinct
        |> NonEmptyList.ofList
        |> Option.map ErrorAccumulator

    /// Remove duplicate errors based on error message
    let deduplicateByMessage (ErrorAccumulator nel) : ErrorAccumulator option =
        deduplicate appErrorToString (ErrorAccumulator nel)

    // --- Interpretation Operations ---

    /// Convert to AppError (single error or AggregateError)
    let toAppError (ErrorAccumulator nel) : AppError =
        match nel with
        | NonEmptyList(single, []) -> single
        | many -> AggregateError many

    /// Convert to ConfigError with all error messages
    let toConfigError (ErrorAccumulator nel) : AppError =
        nel
        |> NonEmptyList.toList
        |> List.map appErrorToString
        |> String.concat "; "
        |> ConfigError

    /// Get just the first error (for compatibility)
    let toFirstError (ErrorAccumulator nel) : AppError = NonEmptyList.head nel

    /// Get all errors as a list
    let toList (ErrorAccumulator nel) : AppError list = NonEmptyList.toList nel

    /// Convert to detailed string representation
    let toDetailedMessage (ErrorAccumulator nel) : string =
        let errors = NonEmptyList.toList nel

        match errors with
        | [ single ] -> appErrorToString single
        | many ->
            let header =
                sprintf "Multiple errors occurred (%d):" (List.length many)

            let details =
                many
                |> List.mapi (fun i e -> sprintf "  %d. %s" (i + 1) (appErrorToString e))

            header :: details |> String.concat "\n"
