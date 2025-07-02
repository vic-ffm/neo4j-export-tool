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
        // Deprecated - use OUTPUT_DIRECTORY instead
        let OutputFile = "OUTPUT_FILE"
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

    let getVersion () = App.Version
    let getVersionString () = App.VersionString
