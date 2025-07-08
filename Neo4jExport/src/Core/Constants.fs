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

/// Central configuration for all application-wide constants
module Constants =

    /// Application version information
    module App =
        let private getVersion () =
            let assembly =
                System.Reflection.Assembly.GetExecutingAssembly()

            let version = assembly.GetName().Version

            if version <> null then
                sprintf "%d.%d.%d" version.Major version.Minor version.Build
            else
                "0.10.0"

        let Version = getVersion ()
        let VersionString = sprintf "v%s" Version

    /// Environment variable names
    module Env =
        let Uri = "NEO4J_URI"
        let User = "NEO4J_USER"
        let Password = "NEO4J_PASSWORD"
        let OutputDirectory = "OUTPUT_DIRECTORY"
        let MinDiskGb = "MIN_DISK_GB"
        let MaxMemoryMb = "MAX_MEMORY_MB"

        let SkipSchemaCollection =
            "SKIP_SCHEMA_COLLECTION"

        let MaxRetries = "MAX_RETRIES"
        let RetryDelayMs = "RETRY_DELAY_MS"
        let MaxRetryDelayMs = "MAX_RETRY_DELAY_MS"

        let QueryTimeoutSeconds =
            "QUERY_TIMEOUT_SECONDS"

        let EnableDebugLogging = "DEBUG"
        let ValidateJsonOutput = "VALIDATE_JSON"
        let AllowInsecure = "ALLOW_INSECURE"
        let BatchSize = "BATCH_SIZE"

        let AverageRecordSize =
            "NEO4J_EXPORT_AVG_RECORD_SIZE"

        let OverheadMultiplier =
            "NEO4J_EXPORT_OVERHEAD_MULTIPLIER"

        let MinMemoryReservation =
            "NEO4J_EXPORT_MIN_MEMORY_RESERVATION"

        let JsonBufferSizeKb = "JSON_BUFFER_SIZE_KB"

        // Path serialization safety thresholds
        [<Literal>]
        let MAX_PATH_LENGTH = "MAX_PATH_LENGTH"

        [<Literal>]
        let PATH_FULL_MODE_LIMIT =
            "PATH_FULL_MODE_LIMIT"

        [<Literal>]
        let PATH_COMPACT_MODE_LIMIT =
            "PATH_COMPACT_MODE_LIMIT"

        [<Literal>]
        let PATH_PROPERTY_DEPTH =
            "PATH_PROPERTY_DEPTH"

        // Nested graph element safety thresholds
        [<Literal>]
        let MAX_NESTED_DEPTH = "MAX_NESTED_DEPTH"

        [<Literal>]
        let NESTED_SHALLOW_MODE_DEPTH =
            "NESTED_SHALLOW_MODE_DEPTH"

        [<Literal>]
        let NESTED_REFERENCE_MODE_DEPTH =
            "NESTED_REFERENCE_MODE_DEPTH"

        // Label truncation limits
        [<Literal>]
        let MAX_LABELS_PER_NODE =
            "MAX_LABELS_PER_NODE"

        [<Literal>]
        let MAX_LABELS_IN_REFERENCE_MODE =
            "MAX_LABELS_IN_REFERENCE_MODE"

        [<Literal>]
        let MAX_LABELS_IN_PATH_COMPACT =
            "MAX_LABELS_IN_PATH_COMPACT"

        // Collection limits
        [<Literal>]
        let MAX_COLLECTION_ITEMS =
            "MAX_COLLECTION_ITEMS"

    /// Default values for all configurable settings
    module Defaults =
        let Uri = "bolt://localhost:7687"
        let User = "neo4j"
        let Password = ""
        let OutputDirectory = "."
        let MinDiskGb = 10L
        let MaxMemoryMb = 1024L
        let SkipSchemaCollection = false
        let MaxRetries = 5
        let RetryDelayMs = 1000
        let MaxRetryDelayMs = 30000
        let QueryTimeoutSeconds = 300
        let EnableDebugLogging = false
        let ValidateJsonOutput = true
        let AllowInsecure = false
        let BatchSize = 10000
        let ConservativeMemoryFallbackGb = 2L

        let ConservativeMemoryFallback =
            ConservativeMemoryFallbackGb
            * 1024L
            * 1024L
            * 1024L

        let MinimumMemoryReservationMb = 100L

        let MinimumMemoryReservation =
            MinimumMemoryReservationMb * 1024L * 1024L

        let AverageRecordSizeBytes = 1024L
        let ProcessingOverheadMultiplier = 2.0
        let JsonBufferSizeKb = 16

        // Path defaults
        let MaxPathLength = 100000L
        let PathFullModeLimit = 1000L
        let PathCompactModeLimit = 10000L
        let PathPropertyDepth = 5

        // Nested element defaults
        let MaxNestedDepth = 10
        let NestedShallowModeDepth = 5
        let NestedReferenceModeDepth = 8

        // Label truncation defaults
        let MaxLabelsPerNode = 100
        let MaxLabelsInReferenceMode = 10
        let MaxLabelsInPathCompact = 5

        // Collection limits
        let MaxCollectionItems = 10000

    let getVersion () = App.Version
    let getVersionString () = App.VersionString
