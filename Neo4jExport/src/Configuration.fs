namespace Neo4jExport

open System
open Constants

/// Configuration validation and environment variable handling
module Configuration =
    /// Safe parsing helpers to prevent FormatException crashes
    module private SafeParsing =
        let tryParseInt64 (name: string) (value: string) : Result<int64, string> =
            match Int64.TryParse(value) with
            | true, parsed -> Ok parsed
            | false, _ -> Error(sprintf "%s must be a valid integer (got: '%s')" name value)

        let tryParseInt (name: string) (value: string) : Result<int, string> =
            match Int32.TryParse(value) with
            | true, parsed -> Ok parsed
            | false, _ -> Error(sprintf "%s must be a valid integer (got: '%s')" name value)

        let tryParseBool (name: string) (value: string) : Result<bool, string> =
            match value.ToLowerInvariant() with
            | "true"
            | "yes"
            | "1" -> Ok true
            | "false"
            | "no"
            | "0" -> Ok false
            | _ -> Error(sprintf "%s must be a valid boolean (true/false, yes/no, 1/0) (got: '%s')" name value)

    /// Helper to extract Ok value when we know all errors have been checked
    let private getOkValue =
        function
        | Ok value -> value
        | Error _ -> failwith "Internal error: attempted to extract Ok value from Error result"

    /// Convert AppError to user-friendly string message
    let private appErrorToString =
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

    let private validateUri uriStr =
        try
            let uri = Uri(uriStr)

            if
                uri.Scheme <> "bolt"
                && uri.Scheme <> "neo4j"
                && uri.Scheme <> "bolt+s"
                && uri.Scheme <> "neo4j+s"
            then
                Result.Error "URI scheme must be one of: bolt, neo4j, bolt+s, neo4j+s"
            else
                Result.Ok uri
        with ex ->
            Result.Error(sprintf "Invalid URI: %s" ex.Message)

    let private validateOutputDirectory path =
        // Validates path syntax without restricting base directory.
        // OS handles permissions - we only ensure the path is well-formed.
        match Security.validatePathSyntax path with
        | Result.Error e -> Result.Error e
        | Result.Ok validPath ->
            match Utils.ensureDirectoryExists validPath with
            | Result.Error e -> Result.Error e
            | Result.Ok() -> Result.Ok validPath

    /// Generates descriptive filename including database info and export metadata
    let generateMetadataFilename (outputDir: string) (metadata: FullMetadata) =
        let dbName =
            metadata.SourceSystem.Database.Name
            |> String.filter (fun c -> Char.IsLetterOrDigit c || c = '_')
            |> fun s -> if s.Length > 20 then s.Substring(0, 20) else s
            |> fun s -> if String.IsNullOrWhiteSpace(s) then "export" else s

        let nodeCount =
            match metadata.DatabaseStatistics.TryGetValue("nodeCount") with
            | true, value ->
                match JsonHelpers.tryGetInt64 value with
                | Ok v -> v
                | Error _ -> 0L
            | _ -> 0L

        let relCount =
            match metadata.DatabaseStatistics.TryGetValue("relCount") with
            | true, value ->
                match JsonHelpers.tryGetInt64 value with
                | Ok v -> v
                | Error _ -> 0L
            | _ -> 0L

        let exportIdShort =
            metadata.ExportMetadata.ExportId.ToString("N").Substring(0, 8)

        let timestamp =
            metadata.ExportMetadata.ExportTimestampUtc.ToString("yyyyMMddTHHmmssZ")

        let filename =
            sprintf "%s_%s_%dn_%dr_%s.jsonl" dbName timestamp nodeCount relCount exportIdShort

        System.IO.Path.Combine(outputDir, filename)

    /// Loads configuration from environment variables with validation
    let getConfig () : Result<ExportConfig, AppError> =
        try
            let uriStr =
                Utils.getEnvVar Env.Uri Defaults.Uri

            let uriResult = validateUri uriStr

            let outputPath =
                let outputFile =
                    Utils.getEnvVar Env.OutputFile ""

                if outputFile <> "" then
                    System.IO.Path.GetDirectoryName(outputFile)
                else
                    Utils.getEnvVar Env.OutputDirectory Defaults.OutputDirectory

            let outputResult =
                validateOutputDirectory outputPath

            let minDiskGb =
                SafeParsing.tryParseInt64 Env.MinDiskGb (Utils.getEnvVar Env.MinDiskGb (string Defaults.MinDiskGb))

            let maxMemoryMb =
                SafeParsing.tryParseInt64
                    Env.MaxMemoryMb
                    (Utils.getEnvVar Env.MaxMemoryMb (string Defaults.MaxMemoryMb))

            let skipSchema =
                SafeParsing.tryParseBool
                    Env.SkipSchemaCollection
                    (Utils.getEnvVar Env.SkipSchemaCollection (string Defaults.SkipSchemaCollection))

            let maxRetries =
                SafeParsing.tryParseInt Env.MaxRetries (Utils.getEnvVar Env.MaxRetries (string Defaults.MaxRetries))

            let retryDelayMs =
                SafeParsing.tryParseInt
                    Env.RetryDelayMs
                    (Utils.getEnvVar Env.RetryDelayMs (string Defaults.RetryDelayMs))

            let maxRetryDelayMs =
                SafeParsing.tryParseInt
                    Env.MaxRetryDelayMs
                    (Utils.getEnvVar Env.MaxRetryDelayMs (string Defaults.MaxRetryDelayMs))

            let queryTimeout =
                SafeParsing.tryParseInt
                    Env.QueryTimeoutSeconds
                    (Utils.getEnvVar Env.QueryTimeoutSeconds (string Defaults.QueryTimeoutSeconds))

            let enableDebug =
                SafeParsing.tryParseBool
                    Env.EnableDebugLogging
                    (Utils.getEnvVar Env.EnableDebugLogging (string Defaults.EnableDebugLogging))

            let validateJson =
                SafeParsing.tryParseBool
                    Env.ValidateJsonOutput
                    (Utils.getEnvVar Env.ValidateJsonOutput (string Defaults.ValidateJsonOutput))

            let allowInsecure =
                SafeParsing.tryParseBool
                    Env.AllowInsecure
                    (Utils.getEnvVar Env.AllowInsecure (string Defaults.AllowInsecure))

            let batchSize =
                SafeParsing.tryParseInt Env.BatchSize (Utils.getEnvVar Env.BatchSize (string Defaults.BatchSize))

            let jsonBufferSizeKb =
                let value =
                    SafeParsing.tryParseInt
                        Env.JsonBufferSizeKb
                        (Utils.getEnvVar Env.JsonBufferSizeKb (string Defaults.JsonBufferSizeKb))

                match value with
                | Ok size when size < 1 || size > 1024 ->
                    Error(sprintf "JSON_BUFFER_SIZE_KB must be between 1 and 1024 KB (got: %d)" size)
                | result -> result

            let allValidationResults =
                [ uriResult
                  |> Result.map (fun _ -> ())
                  |> Result.mapError id
                  outputResult
                  |> Result.map (fun _ -> ())
                  |> Result.mapError appErrorToString
                  minDiskGb
                  |> Result.map (fun _ -> ())
                  |> Result.mapError id
                  maxMemoryMb
                  |> Result.map (fun _ -> ())
                  |> Result.mapError id
                  skipSchema
                  |> Result.map (fun _ -> ())
                  |> Result.mapError id
                  maxRetries
                  |> Result.map (fun _ -> ())
                  |> Result.mapError id
                  retryDelayMs
                  |> Result.map (fun _ -> ())
                  |> Result.mapError id
                  maxRetryDelayMs
                  |> Result.map (fun _ -> ())
                  |> Result.mapError id
                  queryTimeout
                  |> Result.map (fun _ -> ())
                  |> Result.mapError id
                  enableDebug
                  |> Result.map (fun _ -> ())
                  |> Result.mapError id
                  validateJson
                  |> Result.map (fun _ -> ())
                  |> Result.mapError id
                  allowInsecure
                  |> Result.map (fun _ -> ())
                  |> Result.mapError id
                  batchSize
                  |> Result.map (fun _ -> ())
                  |> Result.mapError id
                  jsonBufferSizeKb
                  |> Result.map (fun _ -> ())
                  |> Result.mapError id ]

            let errors =
                allValidationResults
                |> List.choose (function
                    | Error e -> Some e
                    | Ok _ -> None)

            match errors with
            | [] ->
                Result.Ok
                    { Uri = getOkValue uriResult
                      User = Utils.getEnvVar Env.User Defaults.User
                      Password = Utils.getEnvVar Env.Password Defaults.Password
                      OutputDirectory = getOkValue outputResult
                      MinDiskGb = getOkValue minDiskGb
                      MaxMemoryMb = getOkValue maxMemoryMb
                      SkipSchemaCollection = getOkValue skipSchema
                      MaxRetries = getOkValue maxRetries
                      RetryDelayMs = getOkValue retryDelayMs
                      MaxRetryDelayMs = getOkValue maxRetryDelayMs
                      QueryTimeoutSeconds = getOkValue queryTimeout
                      EnableDebugLogging = getOkValue enableDebug
                      ValidateJsonOutput = getOkValue validateJson
                      AllowInsecure = getOkValue allowInsecure
                      BatchSize = getOkValue batchSize
                      JsonBufferSizeKb = getOkValue jsonBufferSizeKb }
            | errors -> Result.Error(ConfigError(String.concat "; " errors))
        with ex ->
            Result.Error(ConfigError(sprintf "Invalid configuration: %s" ex.Message))
