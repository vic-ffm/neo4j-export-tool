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

/// Common test utilities and data builders for the Neo4j Export test suite
module Neo4jExport.Tests.Helpers.TestHelpers

open System
open System.Collections
open System.Collections.Generic
open System.Buffers
open System.Text.Json
open Neo4jExport
open Neo4jExport.ExportTypes
open Neo4jExport.ErrorTracking
open Neo4jExport.JsonConfig
open Neo4jExport.SerializationContext
open FsCheck
open Expecto
open Swensen.Unquote

/// FsCheck configuration for property tests
let fsCheckConfig =
    { FsCheckConfig.defaultConfig with
        maxTest = 100
        arbitrary = [ typeof<Neo4jExport.Tests.Helpers.Generators.CustomGenerators> ] }

/// Creates a minimal valid ExportConfig for testing
let createTestConfig () : ExportConfig =
    { Uri = Uri("bolt://localhost:7687")
      User = "neo4j"
      Password = "password"
      OutputDirectory = "/tmp/test-exports"
      MinDiskGb = 10L
      MaxMemoryMb = 1024L
      SkipSchemaCollection = false
      MaxRetries = 3
      RetryDelayMs = 1000
      MaxRetryDelayMs = 30000
      QueryTimeoutSeconds = 300
      EnableDebugLogging = false
      ValidateJsonOutput = false
      AllowInsecure = false
      BatchSize = 10_000
      JsonBufferSizeKb = 64

      // Path serialization thresholds
      MaxPathLength = 100_000L
      PathFullModeLimit = 10L
      PathCompactModeLimit = 100L
      PathPropertyDepth = 2

      // Nested element thresholds
      MaxNestedDepth = 10
      NestedShallowModeDepth = 2
      NestedReferenceModeDepth = 4

      // Collection limits
      MaxCollectionItems = 1000

      // Label truncation limits
      MaxLabelsPerNode = 10
      MaxLabelsInReferenceMode = 3
      MaxLabelsInPathCompact = 5

      // Content-based hashing
      EnableHashedIds = true }

/// Creates a test AppError for error handling tests
let createTestError message = ConfigError message

/// Creates a dictionary of properties for test nodes/relationships
let createTestProperties (count: int) : IDictionary<string, obj> =
    let props = Dictionary<string, obj>()

    for i in 1..count do
        props.Add($"prop{i}", box $"value{i}")

    props :> IDictionary<string, obj>

/// Creates a test ExportManifestDetails
let createTestManifest durationSeconds =
    { TotalExportDurationSeconds = durationSeconds
      FileStatistics = [] }

/// Asserts that a Result is Ok and returns the value
let assertOk (result: Result<'a, 'b>) : 'a =
    match result with
    | Ok value -> value
    | Error err -> failwithf "Expected Ok but got Error: %A" err

/// Asserts that a Result is Error and returns the error
let assertError (result: Result<'a, 'b>) : 'b =
    match result with
    | Ok value -> failwithf "Expected Error but got Ok: %A" value
    | Error err -> err

/// Measures memory usage for a function
let measureMemory (f: unit -> 'a) : int64 * 'a =
    GC.Collect()
    GC.WaitForPendingFinalizers()
    GC.Collect()
    let before = GC.GetTotalMemory(false)
    let result = f ()
    GC.Collect()
    GC.WaitForPendingFinalizers()
    GC.Collect()
    let after = GC.GetTotalMemory(false)
    (after - before), result

// ===== Serialization Test Helpers =====

/// Creates a test writer context with memory buffer
let createTestWriterContext () =
    let buffer =
        new ArrayBufferWriter<byte>(1024)

    let writer =
        new Utf8JsonWriter(buffer, JsonConfig.createWriterOptions ())

    let config = createTestConfig ()

    let errorFuncs =
        createErrorTrackingSystem (System.Guid.NewGuid())

    let exportId = System.Guid.NewGuid()

    let context =
        SerializationContext.createWriterContext config errorFuncs exportId

    buffer, writer, context

/// Serializes a value and returns the JSON string
let serializeToJson (serializeFunc: Utf8JsonWriter -> unit) =
    let buffer, writer, context =
        createTestWriterContext ()

    use _ = writer

    writer.WriteStartObject()
    writer.WritePropertyName("value")
    serializeFunc writer
    writer.WriteEndObject()
    writer.Flush()

    let bytes = buffer.WrittenSpan.ToArray()
    System.Text.Encoding.UTF8.GetString(bytes)

/// Serializes a value with config and returns the JSON string
let serializeToJsonWithConfig (serializeFunc: Utf8JsonWriter -> ExportConfig -> unit) =
    let buffer, writer, context =
        createTestWriterContext ()

    use _ = writer

    writer.WriteStartObject()
    writer.WritePropertyName("value")
    serializeFunc writer context.Config
    writer.WriteEndObject()
    writer.Flush()

    let bytes = buffer.WrittenSpan.ToArray()
    System.Text.Encoding.UTF8.GetString(bytes)

/// Parses and validates JSON output
let validateJson (json: string) =
    try
        let doc = JsonDocument.Parse(json)
        Ok doc
    with ex ->
        Error $"Invalid JSON: {ex.Message}"

/// Gets the value from a test JSON document
let getJsonValue (doc: JsonDocument) = doc.RootElement.GetProperty("value")

/// Asserts JSON value matches expected
let assertJsonValue (expected: string) (actual: string) =
    match validateJson actual with
    | Ok doc ->
        use _ = doc
        let value = getJsonValue doc
        let actual = value.GetRawText()
        test <@ actual = expected @>
    | Error msg -> failtest msg

/// Validates JSON and executes a function with the parsed document, ensuring proper disposal
let withValidatedJson (json: string) (f: JsonDocument -> unit) =
    match validateJson json with
    | Ok doc ->
        use _ = doc
        f doc
    | Error msg -> failtest msg

/// Test helper for temporal types with nanosecond precision
let createTestDateTime year month day hour minute second nanos =
    let ticks =
        DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc).Ticks

    let adjustedTicks = ticks + (nanos / 100L) // Convert nanos to 100-nano ticks
    DateTime(adjustedTicks, DateTimeKind.Utc)

/// Creates a large string for truncation testing
let createLargeString size = String.replicate size "x"

/// Creates a large byte array for truncation testing
let createLargeByteArray size = Array.create size 0uy

/// Test helper for creating test paths
type TestPath =
    { Nodes: obj list
      Relationships: obj list
      Sequence: (string * int) list } // ("node", 0) or ("relationship", 0)

/// Serialization helper that provides writer context and depth
let serializeToJsonWithContext (serializeFunc: Utf8JsonWriter -> WriterContext -> SerializationDepth -> unit) =
    let buffer, writer, context =
        createTestWriterContext ()

    use _ = writer

    writer.WriteStartObject()
    writer.WritePropertyName("value")
    let depth = SerializationDepth.zero // Use the module function
    serializeFunc writer context depth
    writer.WriteEndObject()
    writer.Flush()

    let bytes = buffer.WrittenSpan.ToArray()
    System.Text.Encoding.UTF8.GetString(bytes)

/// Creates a test writer context with custom limits
let createTestWriterContextWithLimits maxDepth maxCollectionItems maxLabelsPerNode =
    let buffer =
        new ArrayBufferWriter<byte>(1024)

    let writer =
        new Utf8JsonWriter(buffer, JsonConfig.createWriterOptions ())

    let config =
        { createTestConfig () with
            MaxNestedDepth = maxDepth
            MaxCollectionItems = maxCollectionItems
            MaxLabelsPerNode = maxLabelsPerNode }

    let errorFuncs =
        createErrorTrackingSystem (System.Guid.NewGuid())

    let exportId = System.Guid.NewGuid()

    let context =
        SerializationContext.createWriterContext config errorFuncs exportId

    buffer, writer, context

/// Creates a deeply nested structure for depth testing
let rec createNestedStructure depth =
    if depth <= 0 then
        box "leaf"
    else
        let dict = Dictionary<string, obj>()
        dict["nested"] <- createNestedStructure (depth - 1)
        box dict

/// Creates a custom type for unsupported type testing
type UnsupportedCustomType() = class end


/// Test types for nodes and relationships
type TestNode =
    { Id: int64
      ElementId: string
      Labels: Set<string>
      Properties: IDictionary<string, obj> }

type TestRelationship =
    { Id: int64
      ElementId: string
      Type: string
      StartId: int64
      StartElementId: string
      EndId: int64
      EndElementId: string
      Properties: IDictionary<string, obj> }
