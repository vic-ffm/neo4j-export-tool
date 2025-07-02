namespace Neo4jExport

open System
open System.Collections.Generic
open System.Text.Json.Serialization

/// Core domain types for Neo4j export operations
/// For tracking in-progress exports
type ExportProgress =
    { RecordsProcessed: int64
      RecordsSkipped: int64
      BytesWritten: int64
      StartTime: DateTime }

/// For completed exports only
type CompletedExportStats =
    { RecordsProcessed: int64
      RecordsSkipped: int64
      BytesWritten: int64
      StartTime: DateTime
      EndTime: DateTime
      Duration: TimeSpan }

/// Helper functions for export statistics
module ExportStats =
    /// Convert in-progress export to completed stats
    let complete (progress: ExportProgress) (endTime: DateTime) : CompletedExportStats =
        { RecordsProcessed = progress.RecordsProcessed
          RecordsSkipped = progress.RecordsSkipped
          BytesWritten = progress.BytesWritten
          StartTime = progress.StartTime
          EndTime = endTime
          Duration = endTime - progress.StartTime }

type ExportConfig =
    { Uri: Uri
      User: string
      Password: string
      OutputDirectory: string
      MinDiskGb: int64
      MaxMemoryMb: int64
      SkipSchemaCollection: bool
      MaxRetries: int
      RetryDelayMs: int
      MaxRetryDelayMs: int
      QueryTimeoutSeconds: int
      EnableDebugLogging: bool
      ValidateJsonOutput: bool
      AllowInsecure: bool
      BatchSize: int
      JsonBufferSizeKb: int }

/// Discriminated union representing all possible application errors
type AppError =
    | ConfigError of message: string
    | ConnectionError of message: string * exn: exn option
    | AuthenticationError of message: string
    | QueryError of query: string * message: string * exn: exn option
    | DataCorruptionError of line: int * message: string * sample: string option
    | DiskSpaceError of required: int64 * available: int64
    | MemoryError of message: string
    | ExportError of message: string * exn: exn option
    | FileSystemError of path: string * message: string * exn: exn option
    | SecurityError of message: string
    | TimeoutError of operation: string * duration: TimeSpan

/// Mutable context for managing application lifecycle and cleanup
type ApplicationContext =
    { CancellationTokenSource: System.Threading.CancellationTokenSource
      TempFiles: System.Collections.Concurrent.ConcurrentBag<string>
      ActiveProcesses: System.Collections.Concurrent.ConcurrentBag<System.Diagnostics.Process> }

    interface IDisposable with
        member this.Dispose() =
            // Dispose the cancellation token source
            try
                this.CancellationTokenSource.Dispose()
            with ex ->
                eprintfn "[WARN] Failed to dispose CancellationTokenSource: %s" ex.Message

            // Clean up temp files
            for tempFile in this.TempFiles do
                try
                    if System.IO.File.Exists(tempFile) then
                        System.IO.File.Delete(tempFile)
                with ex ->
                    eprintfn "[WARN] Failed to delete temporary file '%s': %s" tempFile ex.Message

            // Terminate and dispose active processes
            for proc in this.ActiveProcesses do
                try
                    if not proc.HasExited then
                        proc.Kill()

                    proc.Dispose()
                with ex ->
                    eprintfn "[WARN] Failed to terminate/dispose process (PID %d): %s" proc.Id ex.Message

type ExportScriptMetadata =
    { [<JsonPropertyName("name")>]
      Name: string
      [<JsonPropertyName("version")>]
      Version: string
      [<JsonPropertyName("checksum")>]
      Checksum: string
      [<JsonPropertyName("runtime_version")>]
      RuntimeVersion: string }

type ExportMetadata =
    { [<JsonPropertyName("export_id")>]
      ExportId: Guid
      [<JsonPropertyName("export_timestamp_utc")>]
      ExportTimestampUtc: DateTime
      [<JsonPropertyName("export_script")>]
      ExportScript: ExportScriptMetadata
      [<JsonPropertyName("export_mode")>]
      ExportMode: string }

type DatabaseMetadata =
    { [<JsonPropertyName("name")>]
      Name: string
      [<JsonPropertyName("creation_date")>]
      CreationDate: DateTime option
      [<JsonPropertyName("size_bytes")>]
      SizeBytes: int64 option }

type SourceSystemMetadata =
    { [<JsonPropertyName("type")>]
      Type: string
      [<JsonPropertyName("version")>]
      Version: string
      [<JsonPropertyName("edition")>]
      Edition: string
      [<JsonPropertyName("database")>]
      Database: DatabaseMetadata }

type EnvironmentMetadata =
    { [<JsonPropertyName("hostname")>]
      Hostname: string
      [<JsonPropertyName("operating_system")>]
      OperatingSystem: string
      [<JsonPropertyName("user")>]
      User: string
      [<JsonPropertyName("runtime")>]
      Runtime: string
      [<JsonPropertyName("processors")>]
      Processors: int
      [<JsonPropertyName("memory_gb")>]
      MemoryGb: float }

type SecurityMetadata =
    { [<JsonPropertyName("encryption_enabled")>]
      EncryptionEnabled: bool
      [<JsonPropertyName("auth_method")>]
      AuthMethod: string
      [<JsonPropertyName("data_validation")>]
      DataValidation: bool }

type FileLevelStatistics =
    { [<JsonPropertyName("label")>]
      Label: string
      [<JsonPropertyName("record_count")>]
      RecordCount: int64
      [<JsonPropertyName("bytes_written")>]
      BytesWritten: int64
      [<JsonPropertyName("export_duration_ms")>]
      ExportDurationMs: int64 }

type ExportManifestDetails =
    { [<JsonPropertyName("total_export_duration_seconds")>]
      TotalExportDurationSeconds: float
      [<JsonPropertyName("file_statistics")>]
      FileStatistics: FileLevelStatistics list }

/// Type-safe representation of JSON values
type JsonValue =
    | JString of string
    | JNumber of decimal
    | JBool of bool
    | JNull
    | JObject of IDictionary<string, JsonValue>
    | JArray of JsonValue list

type FullMetadata =
    { [<JsonPropertyName("export_metadata")>]
      ExportMetadata: ExportMetadata
      [<JsonPropertyName("source_system")>]
      SourceSystem: SourceSystemMetadata
      [<JsonPropertyName("database_statistics")>]
      DatabaseStatistics: IDictionary<string, JsonValue>
      [<JsonPropertyName("database_schema")>]
      DatabaseSchema: IDictionary<string, JsonValue>
      [<JsonPropertyName("environment")>]
      Environment: EnvironmentMetadata
      [<JsonPropertyName("security")>]
      Security: SecurityMetadata
      [<JsonPropertyName("export_manifest")>]
      ExportManifest: ExportManifestDetails option }

type ReservedMetadata =
    { Purpose: string
      Version: string
      Padding: string option }

type MetadataSerializationResult =
    | ExactFit of bytes: byte[]
    | NeedsPadding of baseBytes: byte[] * bytesNeeded: int
    | TooLarge of actualSize: int * maxSize: int
