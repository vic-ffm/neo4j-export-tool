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

module Neo4jExport.SerializationContext

open System
open System.Text.Json
open System.Collections.Generic
open Neo4jExport
open Neo4jExport.ExportTypes
open Neo4jExport.ExportUtils
open JsonHelpers
open ErrorTracking

let createWriterContext config errorTracker exportId =
    { Config = config
      ErrorTracker = errorTracker
      ExportId = exportId }

let determineNestedLevel (depth: SerializationDepth) (config: ExportConfig) : NestedSerializationLevel =
    let d = SerializationDepth.value depth

    if d >= config.NestedReferenceModeDepth then Reference
    elif d >= config.NestedShallowModeDepth then Shallow
    else Deep

let determinePathLevel (nodeCount: int) (config: ExportConfig) : PathSerializationLevel =
    if int64 nodeCount > config.PathCompactModeLimit then
        IdsOnly
    elif int64 nodeCount > config.PathFullModeLimit then
        Compact
    else
        Full

let writeSerializationError (writer: Utf8JsonWriter) (errorType: string) (context: string) =
    writer.WriteStartObject()
    writer.WriteString("_error", "unrecoverable_serialization_failure")
    writer.WriteString("_error_type", errorType)
    writer.WriteString("_context", context)
    writer.WriteEndObject()

let writeDepthExceeded (writer: Utf8JsonWriter) (value: obj) (depth: SerializationDepth) =
    writer.WriteStartObject()
    writer.WriteString("_truncated", "depth_limit")
    writer.WriteNumber("_depth", SerializationDepth.value depth)

    writer.WriteString(
        "_type",
        try
            if value = null then "null" else value.GetType().FullName
        with _ ->
            "unknown"
    )

    writer.WriteEndObject()

let writeUnknownType (writer: Utf8JsonWriter) (value: obj) =
    writer.WriteStartObject()

    writer.WriteString(
        "_type",
        try
            value.GetType().FullName
        with _ ->
            "unknown"
    )

    writer.WriteString(
        "_assembly",
        try
            value.GetType().Assembly.GetName().Name
        with _ ->
            "unknown"
    )

    writer.WriteString("_note", "unserializable_type")
    writer.WriteEndObject()

let createErrorContext (elementId: string option) (additionalInfo: (string * obj) list) =
    let details =
        Dictionary<string, JsonValue>()

    additionalInfo
    |> List.iter (fun (key, value) ->
        match JsonHelpers.toJsonValue value with
        | Ok jsonValue -> details.[key] <- jsonValue
        | Error _ -> details.[key] <- JString(value.ToString()))

    if details.Count > 0 then
        Some(details :> IDictionary<string, JsonValue>)
    else
        None

let trackSerializationError
    (errorTracker: ErrorTracker)
    (message: string)
    (elementId: string)
    (entityType: string)
    (exceptionType: string)
    =
    let context =
        [ "entity_type", box entityType
          "exception_type", box exceptionType
          "serialization_phase", box "write" ]

    let details =
        createErrorContext (Some elementId) context

    errorTracker.AddError(message, ?elementId = Some elementId, ?details = details)

let handleSerializationError (writer: Utf8JsonWriter) (ex: exn) (depth: SerializationDepth) =
    try
        writer.WriteStartObject()
        writer.WriteString("_serialization_error", ex.GetType().Name)
        writer.WriteString("_at_depth", string (SerializationDepth.value depth))
        writer.WriteEndObject()
    with _ ->
        writeSerializationError writer "catastrophic" (sprintf "depth_%d" (SerializationDepth.value depth))
