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
        let Uri = "N4JET_NEO4J_URI"
        let User = "N4JET_NEO4J_USER"
        let Password = "N4JET_NEO4J_PASSWORD"
        let OutputDirectory = "N4JET_OUTPUT_DIRECTORY"
        let MinDiskGb = "N4JET_MIN_DISK_GB"
        let MaxMemoryMb = "N4JET_MAX_MEMORY_MB"

        let SkipSchemaCollection =
            "N4JET_SKIP_SCHEMA_COLLECTION"

        let MaxRetries = "N4JET_MAX_RETRIES"
        let RetryDelayMs = "N4JET_RETRY_DELAY_MS"
        let MaxRetryDelayMs = "N4JET_MAX_RETRY_DELAY_MS"

        let QueryTimeoutSeconds =
            "N4JET_QUERY_TIMEOUT_SECONDS"

        let EnableDebugLogging = "N4JET_DEBUG"
        let ValidateJsonOutput = "N4JET_VALIDATE_JSON"
        let AllowInsecure = "N4JET_ALLOW_INSECURE"
        let BatchSize = "N4JET_BATCH_SIZE"

        let AverageRecordSize =
            "N4JET_NEO4J_EXPORT_AVG_RECORD_SIZE"

        let OverheadMultiplier =
            "N4JET_NEO4J_EXPORT_OVERHEAD_MULTIPLIER"

        let MinMemoryReservation =
            "N4JET_NEO4J_EXPORT_MIN_MEMORY_RESERVATION"

        let JsonBufferSizeKb = "N4JET_JSON_BUFFER_SIZE_KB"

        // Path serialization safety thresholds
        // [<Literal>] attribute ensures these are compile-time constants, enabling
        // their use in pattern matching and other contexts requiring const values
        [<Literal>]
        let MAX_PATH_LENGTH = "N4JET_MAX_PATH_LENGTH"

        [<Literal>]
        let PATH_FULL_MODE_LIMIT =
            "N4JET_PATH_FULL_MODE_LIMIT"

        [<Literal>]
        let PATH_COMPACT_MODE_LIMIT =
            "N4JET_PATH_COMPACT_MODE_LIMIT"

        [<Literal>]
        let PATH_PROPERTY_DEPTH =
            "N4JET_PATH_PROPERTY_DEPTH"

        // Nested graph element safety thresholds
        [<Literal>]
        let MAX_NESTED_DEPTH = "N4JET_MAX_NESTED_DEPTH"

        [<Literal>]
        let NESTED_SHALLOW_MODE_DEPTH =
            "N4JET_NESTED_SHALLOW_MODE_DEPTH"

        [<Literal>]
        let NESTED_REFERENCE_MODE_DEPTH =
            "N4JET_NESTED_REFERENCE_MODE_DEPTH"

        // Label truncation limits
        [<Literal>]
        let MAX_LABELS_PER_NODE =
            "N4JET_MAX_LABELS_PER_NODE"

        [<Literal>]
        let MAX_LABELS_IN_REFERENCE_MODE =
            "N4JET_MAX_LABELS_IN_REFERENCE_MODE"

        [<Literal>]
        let MAX_LABELS_IN_PATH_COMPACT =
            "N4JET_MAX_LABELS_IN_PATH_COMPACT"

        // Collection limits
        [<Literal>]
        let MAX_COLLECTION_ITEMS =
            "N4JET_MAX_COLLECTION_ITEMS"

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

        // Convert GB to bytes for runtime memory calculations
        // Using explicit multiplication for clarity over bit shifting
        let ConservativeMemoryFallback =
            ConservativeMemoryFallbackGb
            * 1024L
            * 1024L
            * 1024L

        let MinimumMemoryReservationMb = 100L

        // Convert MB to bytes for runtime memory calculations
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
