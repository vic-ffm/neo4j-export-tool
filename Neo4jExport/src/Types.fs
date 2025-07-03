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
open System.Text.Json.Serialization
open Neo4j.Driver

/// Core domain types for Neo4j export operations

// Serialization level types - no strings!
[<Struct>]
type PathSerializationLevel =
    | Full
    | Compact
    | IdsOnly

[<Struct>]
type NestedSerializationLevel =
    | Deep
    | Shallow
    | Reference

type SerializationDepth = private SerializationDepth of int

module SerializationDepth =
    let zero = SerializationDepth 0
    let increment (SerializationDepth d) = SerializationDepth(d + 1)
    let value (SerializationDepth d) = d
    let exceedsLimit limit (SerializationDepth d) = d >= limit

type SerializationError =
    | DepthExceeded of currentDepth: int * maxDepth: int
    | PathTooLong of length: int64 * maxLength: int64
    | CircularReference of nodeId: int64
    | PropertySerializationFailed of key: string * error: string
    | InvalidValue of message: string

type GraphElement =
    | Node of INode
    | Relationship of IRelationship
    | Path of IPath

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
      JsonBufferSizeKb: int

      // Path serialization thresholds
      MaxPathLength: int64
      PathFullModeLimit: int64
      PathCompactModeLimit: int64
      PathPropertyDepth: int

      // Nested element thresholds
      MaxNestedDepth: int
      NestedShallowModeDepth: int
      NestedReferenceModeDepth: int

      // Collection limits
      MaxCollectionItems: int

      // Label truncation limits
      MaxLabelsPerNode: int
      MaxLabelsInReferenceMode: int
      MaxLabelsInPathCompact: int }

/// All possible application errors
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
            try
                this.CancellationTokenSource.Dispose()
            with ex ->
                eprintfn "[WARN] Failed to dispose CancellationTokenSource: %s" ex.Message

            for tempFile in this.TempFiles do
                try
                    if System.IO.File.Exists(tempFile) then
                        System.IO.File.Delete(tempFile)
                with ex ->
                    eprintfn "[WARN] Failed to delete temporary file '%s': %s" tempFile ex.Message

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

type FormatInfo =
    { [<JsonPropertyName("type")>]
      Type: string
      [<JsonPropertyName("metadata_line")>]
      MetadataLine: int }

type ExportMetadata =
    { [<JsonPropertyName("export_id")>]
      ExportId: Guid
      [<JsonPropertyName("export_timestamp_utc")>]
      ExportTimestampUtc: DateTime
      [<JsonPropertyName("export_mode")>]
      ExportMode: string
      [<JsonPropertyName("format")>]
      Format: FormatInfo option }

type DatabaseMetadata =
    { [<JsonPropertyName("name")>]
      Name: string }

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

/// Error summary for the export
type ErrorSummary =
    { [<JsonPropertyName("error_count")>]
      ErrorCount: int64
      [<JsonPropertyName("warning_count")>]
      WarningCount: int64
      [<JsonPropertyName("has_errors")>]
      HasErrors: bool }

/// Type-safe representation of JSON values
type JsonValue =
    | JString of string
    | JNumber of decimal
    | JBool of bool
    | JNull
    | JObject of IDictionary<string, JsonValue>
    | JArray of JsonValue list

/// Reserved metadata for future use
type ReservedMetadata =
    { Purpose: string
      Padding: string option }

/// Defines a record type that can appear in the JSONL file
type RecordTypeDefinition =
    { [<JsonPropertyName("type_name")>]
      TypeName: string
      [<JsonPropertyName("description")>]
      Description: string
      [<JsonPropertyName("required_fields")>]
      RequiredFields: string list
      [<JsonPropertyName("optional_fields")>]
      OptionalFields: string list option }

/// Compression hints for the exported file
type CompressionHints =
    { [<JsonPropertyName("recommended")>]
      Recommended: string
      [<JsonPropertyName("compatible")>]
      Compatible: string list
      [<JsonPropertyName("expected_ratio")>]
      ExpectedRatio: float option
      [<JsonPropertyName("suffix")>]
      Suffix: string }

/// Backward compatibility information
type CompatibilityInfo =
    { [<JsonPropertyName("minimum_reader_version")>]
      MinimumReaderVersion: string
      [<JsonPropertyName("deprecated_fields")>]
      DeprecatedFields: string list
      [<JsonPropertyName("breaking_change_version")>]
      BreakingChangeVersion: string }

/// Error record for tracking export issues
type ErrorRecord =
    { [<JsonPropertyName("type")>]
      Type: string // "error" or "warning"
      [<JsonPropertyName("timestamp")>]
      Timestamp: DateTime
      [<JsonPropertyName("line")>]
      Line: int64 option
      [<JsonPropertyName("message")>]
      Message: string
      [<JsonPropertyName("details")>]
      Details: IDictionary<string, JsonValue> option
      [<JsonPropertyName("element_id")>]
      ElementId: string option }

/// Immutable state for tracking line numbers functionally
type LineTrackingState =
    { CurrentLine: int64
      RecordTypeStartLines: Map<string, int64> }

/// Helper module for functional line tracking
module LineTracking =
    let create () =
        { CurrentLine = 2L
          RecordTypeStartLines = Map.empty }

    let incrementLine state =
        { state with
            CurrentLine = state.CurrentLine + 1L }

    let recordTypeStart (recordType: string) (state: LineTrackingState) =
        if state.RecordTypeStartLines.ContainsKey(recordType) then
            state
        else
            { state with
                RecordTypeStartLines = state.RecordTypeStartLines.Add(recordType, state.CurrentLine) }

/// Immutable state for error tracking with agent pattern
type ErrorTrackingState =
    { Errors: ErrorRecord list
      ErrorCount: int64
      WarningCount: int64
      CurrentLine: int64 }

/// Messages for error tracking agent
type ErrorTrackingMessage =
    | AddError of
        message: string *
        elementId: string option *
        details: IDictionary<string, JsonValue> option *
        AsyncReplyChannel<unit>
    | AddWarning of
        message: string *
        elementId: string option *
        details: IDictionary<string, JsonValue> option *
        AsyncReplyChannel<unit>
    | IncrementLine of AsyncReplyChannel<unit>
    | GetState of AsyncReplyChannel<ErrorTrackingState>

/// Messages for resource monitoring agent
type MonitoringMessage =
    | CheckResources of AsyncReplyChannel<Result<unit, string>>
    | Stop

/// State for resource monitoring
type ResourceState =
    { LastCheck: DateTime; IsRunning: bool }

type FullMetadata =
    { [<JsonPropertyName("format_version")>]
      FormatVersion: string
      [<JsonPropertyName("export_metadata")>]
      ExportMetadata: ExportMetadata
      [<JsonPropertyName("producer")>]
      Producer: ExportScriptMetadata
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
      ExportManifest: ExportManifestDetails option
      [<JsonPropertyName("error_summary")>]
      ErrorSummary: ErrorSummary option
      [<JsonPropertyName("supported_record_types")>]
      RecordTypes: RecordTypeDefinition list
      [<JsonPropertyName("compatibility")>]
      Compatibility: CompatibilityInfo
      [<JsonPropertyName("compression")>]
      Compression: CompressionHints
      [<JsonPropertyName("_reserved")>]
      Reserved: ReservedMetadata option }
