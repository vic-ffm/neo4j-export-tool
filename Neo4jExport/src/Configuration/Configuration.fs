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
open ErrorAccumulation
open ConfigurationValidationHelpers
open ConfigurationFieldValidators
open FieldExtractors

module Configuration =

    /// Generates descriptive filename: [dbname]_[timestamp]_[nodes]n_[rels]r_[id].jsonl
    let generateMetadataFilename (outputDir: string) (metadata: FullMetadata) =
        let dbName =
            metadata.SourceSystem.Database.Name
            // Sanitize filename to prevent filesystem issues - only allow alphanumeric and underscore
            |> String.filter (fun c -> Char.IsLetterOrDigit c || c = '_')
            // Limit length for filesystem compatibility and readability
            |> fun s -> if s.Length > 20 then s.Substring(0, 20) else s
            // Provide fallback name if database name is empty after sanitization
            |> fun s -> if String.IsNullOrWhiteSpace(s) then "export" else s

        // Extract node count from stats dictionary, defaulting to 0 if not found or unparseable
        // TryGetValue returns a tuple of (bool * value) for safe dictionary access
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

            let user =
                Utils.getEnvVar Env.User Defaults.User

            let password =
                Utils.getEnvVar Env.Password Defaults.Password

            let outputPath =
                Utils.getEnvVar Env.OutputDirectory Defaults.OutputDirectory

            let minDiskGbStr =
                Utils.getEnvVar Env.MinDiskGb (string Defaults.MinDiskGb)

            let maxMemoryMbStr =
                Utils.getEnvVar Env.MaxMemoryMb (string Defaults.MaxMemoryMb)

            let skipSchemaStr =
                Utils.getEnvVar Env.SkipSchemaCollection (string Defaults.SkipSchemaCollection)

            let maxRetriesStr =
                Utils.getEnvVar Env.MaxRetries (string Defaults.MaxRetries)

            let retryDelayMsStr =
                Utils.getEnvVar Env.RetryDelayMs (string Defaults.RetryDelayMs)

            let maxRetryDelayMsStr =
                Utils.getEnvVar Env.MaxRetryDelayMs (string Defaults.MaxRetryDelayMs)

            let queryTimeoutStr =
                Utils.getEnvVar Env.QueryTimeoutSeconds (string Defaults.QueryTimeoutSeconds)

            let enableDebugStr =
                Utils.getEnvVar Env.EnableDebugLogging (string Defaults.EnableDebugLogging)

            let validateJsonStr =
                Utils.getEnvVar Env.ValidateJsonOutput (string Defaults.ValidateJsonOutput)

            let allowInsecureStr =
                Utils.getEnvVar Env.AllowInsecure (string Defaults.AllowInsecure)

            let batchSizeStr =
                Utils.getEnvVar Env.BatchSize (string Defaults.BatchSize)

            let jsonBufferSizeKbStr =
                Utils.getEnvVar Env.JsonBufferSizeKb (string Defaults.JsonBufferSizeKb)

            let maxPathLengthStr =
                Utils.getEnvVar Env.MAX_PATH_LENGTH (string Defaults.MaxPathLength)

            let pathFullModeLimitStr =
                Utils.getEnvVar Env.PATH_FULL_MODE_LIMIT (string Defaults.PathFullModeLimit)

            let pathCompactModeLimitStr =
                Utils.getEnvVar Env.PATH_COMPACT_MODE_LIMIT (string Defaults.PathCompactModeLimit)

            let pathPropertyDepthStr =
                Utils.getEnvVar Env.PATH_PROPERTY_DEPTH (string Defaults.PathPropertyDepth)

            let maxNestedDepthStr =
                Utils.getEnvVar Env.MAX_NESTED_DEPTH (string Defaults.MaxNestedDepth)

            let nestedShallowModeDepthStr =
                Utils.getEnvVar Env.NESTED_SHALLOW_MODE_DEPTH (string Defaults.NestedShallowModeDepth)

            let nestedReferenceModeDepthStr =
                Utils.getEnvVar Env.NESTED_REFERENCE_MODE_DEPTH (string Defaults.NestedReferenceModeDepth)

            let maxCollectionItemsStr =
                Utils.getEnvVar Env.MAX_COLLECTION_ITEMS (string Defaults.MaxCollectionItems)

            let maxLabelsPerNodeStr =
                Utils.getEnvVar Env.MAX_LABELS_PER_NODE (string Defaults.MaxLabelsPerNode)

            let maxLabelsInReferenceModeStr =
                Utils.getEnvVar Env.MAX_LABELS_IN_REFERENCE_MODE (string Defaults.MaxLabelsInReferenceMode)

            let maxLabelsInPathCompactStr =
                Utils.getEnvVar Env.MAX_LABELS_IN_PATH_COMPACT (string Defaults.MaxLabelsInPathCompact)

            let enableHashedIdsStr =
                Utils.getEnvVar Env.EnableHashedIds (string Defaults.EnableHashedIds)

            // Build validation list where each tuple contains (field_name, validation_result)
            // This pattern allows us to collect all validation errors at once rather than failing on first error
            let validations =
                [ ("uri", validateUri uriStr)
                  ("outputDir", validateOutputDirectory outputPath)
                  ("minDiskGb", validateInt64 "MIN_DISK_GB" (Some 1L) None minDiskGbStr)
                  ("maxMemoryMb", validateInt64 "MAX_MEMORY_MB" (Some 128L) None maxMemoryMbStr)
                  ("skipSchema", validateBool "SKIP_SCHEMA_COLLECTION" skipSchemaStr)
                  ("maxRetries", validateInt "MAX_RETRIES" (Some 0) (Some 100) maxRetriesStr)
                  ("retryDelayMs", validateInt "RETRY_DELAY_MS" (Some 100) None retryDelayMsStr)
                  ("maxRetryDelayMs", validateInt "MAX_RETRY_DELAY_MS" (Some 100) None maxRetryDelayMsStr)
                  ("queryTimeout", validateInt "QUERY_TIMEOUT_SECONDS" (Some 1) None queryTimeoutStr)
                  ("enableDebug", validateBool "DEBUG" enableDebugStr)
                  ("validateJson", validateBool "VALIDATE_JSON" validateJsonStr)
                  ("allowInsecure", validateBool "ALLOW_INSECURE" allowInsecureStr)
                  ("batchSize", validateInt "BATCH_SIZE" (Some 1) (Some 100000) batchSizeStr)
                  ("jsonBufferSizeKb", validateJsonBufferSize jsonBufferSizeKbStr)
                  ("maxPathLength", validateInt64 "MAX_PATH_LENGTH" (Some 1L) None maxPathLengthStr)
                  ("pathFullModeLimit", validateInt64 "PATH_FULL_MODE_LIMIT" (Some 1L) None pathFullModeLimitStr)
                  ("pathCompactModeLimit",
                   validateInt64 "PATH_COMPACT_MODE_LIMIT" (Some 1L) None pathCompactModeLimitStr)
                  ("pathPropertyDepth", validateInt "PATH_PROPERTY_DEPTH" (Some 0) (Some 10) pathPropertyDepthStr)
                  ("maxNestedDepth", validateInt "MAX_NESTED_DEPTH" (Some 1) (Some 100) maxNestedDepthStr)
                  ("nestedShallowModeDepth",
                   validateInt "NESTED_SHALLOW_MODE_DEPTH" (Some 0) None nestedShallowModeDepthStr)
                  ("nestedReferenceModeDepth",
                   validateInt "NESTED_REFERENCE_MODE_DEPTH" (Some 0) None nestedReferenceModeDepthStr)
                  ("maxCollectionItems", validateInt "MAX_COLLECTION_ITEMS" (Some 1) None maxCollectionItemsStr)
                  ("maxLabelsPerNode", validateInt "MAX_LABELS_PER_NODE" (Some 1) None maxLabelsPerNodeStr)
                  ("maxLabelsInReferenceMode",
                   validateInt "MAX_LABELS_IN_REFERENCE_MODE" (Some 1) None maxLabelsInReferenceModeStr)
                  ("maxLabelsInPathCompact",
                   validateInt "MAX_LABELS_IN_PATH_COMPACT" (Some 1) None maxLabelsInPathCompactStr)
                  ("enableHashedIds", validateBool "ENABLE_HASHED_IDS" enableHashedIdsStr) ]

            match validateAll validations with
            | Error errors ->
                // Transform string errors into AppError type hierarchy
                let appErrors =
                    errors |> List.map ConfigError

                // Ensure we always return at least one error (NonEmptyList enforces this constraint)
                match NonEmptyList.ofList appErrors with
                | Some nel -> Error(AggregateError nel)
                | None -> Error(ConfigError "Unknown validation error")

            | Ok fields ->
                // Extract validated fields using type-safe accessors
                // Each getter enforces the correct type at compile time
                Ok
                    { Uri = getUri fields "uri"
                      User = user
                      Password = password
                      OutputDirectory = getString fields "outputDir"
                      MinDiskGb = getInt64 fields "minDiskGb"
                      MaxMemoryMb = getInt64 fields "maxMemoryMb"
                      SkipSchemaCollection = getBool fields "skipSchema"
                      MaxRetries = getInt fields "maxRetries"
                      RetryDelayMs = getInt fields "retryDelayMs"
                      MaxRetryDelayMs = getInt fields "maxRetryDelayMs"
                      QueryTimeoutSeconds = getInt fields "queryTimeout"
                      EnableDebugLogging = getBool fields "enableDebug"
                      ValidateJsonOutput = getBool fields "validateJson"
                      AllowInsecure = getBool fields "allowInsecure"
                      BatchSize = getInt fields "batchSize"
                      JsonBufferSizeKb = getInt fields "jsonBufferSizeKb"
                      MaxPathLength = getInt64 fields "maxPathLength"
                      PathFullModeLimit = getInt64 fields "pathFullModeLimit"
                      PathCompactModeLimit = getInt64 fields "pathCompactModeLimit"
                      PathPropertyDepth = getInt fields "pathPropertyDepth"
                      MaxNestedDepth = getInt fields "maxNestedDepth"
                      NestedShallowModeDepth = getInt fields "nestedShallowModeDepth"
                      NestedReferenceModeDepth = getInt fields "nestedReferenceModeDepth"
                      MaxCollectionItems = getInt fields "maxCollectionItems"
                      MaxLabelsPerNode = getInt fields "maxLabelsPerNode"
                      MaxLabelsInReferenceMode = getInt fields "maxLabelsInReferenceMode"
                      MaxLabelsInPathCompact = getInt fields "maxLabelsInPathCompact"
                      EnableHashedIds = getBool fields "enableHashedIds" }

        with ex ->
            Error(ConfigError(sprintf "Invalid configuration: %s" (ErrorAccumulation.exceptionToString ex)))
