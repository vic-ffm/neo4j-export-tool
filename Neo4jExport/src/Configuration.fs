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
open Constants

module Configuration =
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
        // IMPORTANT: This function intentionally creates the directory during configuration validation.
        // This is a fail-fast design decision to ensure we catch invalid paths or permission issues
        // BEFORE establishing expensive database connections.
        match Security.validatePathSyntax path with
        | Result.Error e -> Result.Error e
        | Result.Ok validPath ->
            match Utils.ensureDirectoryExists validPath with
            | Result.Error e -> Result.Error e
            | Result.Ok() -> Result.Ok validPath

    /// Generates descriptive filename: [dbname]_[timestamp]_[nodes]n_[rels]r_[id].jsonl
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

            let maxPathLength =
                SafeParsing.tryParseInt64
                    Env.MAX_PATH_LENGTH
                    (Utils.getEnvVar Env.MAX_PATH_LENGTH (string Defaults.MaxPathLength))

            let pathFullModeLimit =
                SafeParsing.tryParseInt64
                    Env.PATH_FULL_MODE_LIMIT
                    (Utils.getEnvVar Env.PATH_FULL_MODE_LIMIT (string Defaults.PathFullModeLimit))

            let pathCompactModeLimit =
                SafeParsing.tryParseInt64
                    Env.PATH_COMPACT_MODE_LIMIT
                    (Utils.getEnvVar Env.PATH_COMPACT_MODE_LIMIT (string Defaults.PathCompactModeLimit))

            let pathPropertyDepth =
                SafeParsing.tryParseInt
                    Env.PATH_PROPERTY_DEPTH
                    (Utils.getEnvVar Env.PATH_PROPERTY_DEPTH (string Defaults.PathPropertyDepth))

            let maxNestedDepth =
                SafeParsing.tryParseInt
                    Env.MAX_NESTED_DEPTH
                    (Utils.getEnvVar Env.MAX_NESTED_DEPTH (string Defaults.MaxNestedDepth))

            let nestedShallowModeDepth =
                SafeParsing.tryParseInt
                    Env.NESTED_SHALLOW_MODE_DEPTH
                    (Utils.getEnvVar Env.NESTED_SHALLOW_MODE_DEPTH (string Defaults.NestedShallowModeDepth))

            let nestedReferenceModeDepth =
                SafeParsing.tryParseInt
                    Env.NESTED_REFERENCE_MODE_DEPTH
                    (Utils.getEnvVar Env.NESTED_REFERENCE_MODE_DEPTH (string Defaults.NestedReferenceModeDepth))

            let maxCollectionItems =
                SafeParsing.tryParseInt
                    Env.MAX_COLLECTION_ITEMS
                    (Utils.getEnvVar Env.MAX_COLLECTION_ITEMS (string Defaults.MaxCollectionItems))

            let maxLabelsPerNode =
                SafeParsing.tryParseInt
                    Env.MAX_LABELS_PER_NODE
                    (Utils.getEnvVar Env.MAX_LABELS_PER_NODE (string Defaults.MaxLabelsPerNode))

            let maxLabelsInReferenceMode =
                SafeParsing.tryParseInt
                    Env.MAX_LABELS_IN_REFERENCE_MODE
                    (Utils.getEnvVar Env.MAX_LABELS_IN_REFERENCE_MODE (string Defaults.MaxLabelsInReferenceMode))

            let maxLabelsInPathCompact =
                SafeParsing.tryParseInt
                    Env.MAX_LABELS_IN_PATH_COMPACT
                    (Utils.getEnvVar Env.MAX_LABELS_IN_PATH_COMPACT (string Defaults.MaxLabelsInPathCompact))

            // Pattern match on all validation results to extract values safely
            match
                uriResult,
                outputResult,
                minDiskGb,
                maxMemoryMb,
                skipSchema,
                maxRetries,
                retryDelayMs,
                maxRetryDelayMs,
                queryTimeout,
                enableDebug,
                validateJson,
                allowInsecure,
                batchSize,
                jsonBufferSizeKb,
                maxPathLength,
                pathFullModeLimit,
                pathCompactModeLimit,
                pathPropertyDepth,
                maxNestedDepth,
                nestedShallowModeDepth,
                nestedReferenceModeDepth,
                maxCollectionItems,
                maxLabelsPerNode,
                maxLabelsInReferenceMode,
                maxLabelsInPathCompact
            with
            | Ok uri,
              Ok output,
              Ok diskGb,
              Ok memMb,
              Ok skipSch,
              Ok maxRet,
              Ok retDelay,
              Ok maxRetDelay,
              Ok queryTo,
              Ok debug,
              Ok valJson,
              Ok insecure,
              Ok batch,
              Ok jsonBuf,
              Ok pathLen,
              Ok pathFull,
              Ok pathCompact,
              Ok pathDepth,
              Ok nestDepth,
              Ok nestShallow,
              Ok nestRef,
              Ok collItems,
              Ok labelsNode,
              Ok labelsRef,
              Ok labelsCompact ->
                Result.Ok
                    { Uri = uri
                      User = Utils.getEnvVar Env.User Defaults.User
                      Password = Utils.getEnvVar Env.Password Defaults.Password
                      OutputDirectory = output
                      MinDiskGb = diskGb
                      MaxMemoryMb = memMb
                      SkipSchemaCollection = skipSch
                      MaxRetries = maxRet
                      RetryDelayMs = retDelay
                      MaxRetryDelayMs = maxRetDelay
                      QueryTimeoutSeconds = queryTo
                      EnableDebugLogging = debug
                      ValidateJsonOutput = valJson
                      AllowInsecure = insecure
                      BatchSize = batch
                      JsonBufferSizeKb = jsonBuf
                      MaxPathLength = pathLen
                      PathFullModeLimit = pathFull
                      PathCompactModeLimit = pathCompact
                      PathPropertyDepth = pathDepth
                      MaxNestedDepth = nestDepth
                      NestedShallowModeDepth = nestShallow
                      NestedReferenceModeDepth = nestRef
                      MaxCollectionItems = collItems
                      MaxLabelsPerNode = labelsNode
                      MaxLabelsInReferenceMode = labelsRef
                      MaxLabelsInPathCompact = labelsCompact }
            | _ ->
                // Collect all errors from the Results
                let errors =
                    [ match uriResult with
                      | Error e -> Some e
                      | _ -> None
                      match outputResult with
                      | Error e -> Some(appErrorToString e)
                      | _ -> None
                      match minDiskGb with
                      | Error e -> Some e
                      | _ -> None
                      match maxMemoryMb with
                      | Error e -> Some e
                      | _ -> None
                      match skipSchema with
                      | Error e -> Some e
                      | _ -> None
                      match maxRetries with
                      | Error e -> Some e
                      | _ -> None
                      match retryDelayMs with
                      | Error e -> Some e
                      | _ -> None
                      match maxRetryDelayMs with
                      | Error e -> Some e
                      | _ -> None
                      match queryTimeout with
                      | Error e -> Some e
                      | _ -> None
                      match enableDebug with
                      | Error e -> Some e
                      | _ -> None
                      match validateJson with
                      | Error e -> Some e
                      | _ -> None
                      match allowInsecure with
                      | Error e -> Some e
                      | _ -> None
                      match batchSize with
                      | Error e -> Some e
                      | _ -> None
                      match jsonBufferSizeKb with
                      | Error e -> Some e
                      | _ -> None
                      match maxPathLength with
                      | Error e -> Some e
                      | _ -> None
                      match pathFullModeLimit with
                      | Error e -> Some e
                      | _ -> None
                      match pathCompactModeLimit with
                      | Error e -> Some e
                      | _ -> None
                      match pathPropertyDepth with
                      | Error e -> Some e
                      | _ -> None
                      match maxNestedDepth with
                      | Error e -> Some e
                      | _ -> None
                      match nestedShallowModeDepth with
                      | Error e -> Some e
                      | _ -> None
                      match nestedReferenceModeDepth with
                      | Error e -> Some e
                      | _ -> None
                      match maxCollectionItems with
                      | Error e -> Some e
                      | _ -> None
                      match maxLabelsPerNode with
                      | Error e -> Some e
                      | _ -> None
                      match maxLabelsInReferenceMode with
                      | Error e -> Some e
                      | _ -> None
                      match maxLabelsInPathCompact with
                      | Error e -> Some e
                      | _ -> None ]
                    |> List.choose id

                Result.Error(ConfigError(String.concat "; " errors))
        with ex ->
            Result.Error(ConfigError(sprintf "Invalid configuration: %s" ex.Message))
