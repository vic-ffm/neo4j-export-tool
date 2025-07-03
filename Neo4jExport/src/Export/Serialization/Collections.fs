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

module Neo4jExport.SerializationCollections

open System
open System.Text.Json
open Neo4jExport
open Neo4jExport.ExportTypes
open Neo4jExport.ExportUtils
open ErrorTracking

/// Forward declaration needed due to circular dependency
let mutable serializeValueFunc
    : (Utf8JsonWriter -> obj -> SerializationDepth -> ExportConfig -> ErrorTracker -> unit) option =
    None

let serializeList
    (writer: Utf8JsonWriter)
    (list: Collections.IList)
    (depth: SerializationDepth)
    (config: ExportConfig)
    (errorTracker: ErrorTracker)
    =
    writer.WriteStartArray()

    let items =
        list
        |> Seq.cast<obj>
        |> Seq.truncate config.MaxCollectionItems
        |> Seq.toList

    items
    |> List.iter (fun item ->
        serializeValueFunc.Value writer item (SerializationDepth.increment depth) config errorTracker)

    if list.Count > config.MaxCollectionItems then
        writer.WriteStartObject()
        writer.WriteString("_truncated", "list_too_large")
        writer.WriteNumber("_total_items", list.Count)
        writer.WriteNumber("_shown_items", config.MaxCollectionItems)
        writer.WriteEndObject()

    writer.WriteEndArray()

let serializeMap
    (writer: Utf8JsonWriter)
    (dict: Collections.IDictionary)
    (depth: SerializationDepth)
    (config: ExportConfig)
    (errorTracker: ErrorTracker)
    =
    writer.WriteStartObject()
    let keyTracker = createKeyTracker ()

    let entries =
        dict.Keys
        |> Seq.cast<obj>
        |> Seq.truncate config.MaxCollectionItems
        |> Seq.toList

    entries
    |> List.iter (fun key ->
        let keyStr =
            try
                if key = null then
                    "null"
                else
                    ensureUniqueKey (key.ToString()) keyTracker
            with _ ->
                "_key_error"

        writer.WritePropertyName keyStr
        serializeValueFunc.Value writer dict.[key] (SerializationDepth.increment depth) config errorTracker)

    if dict.Count > config.MaxCollectionItems then
        writer.WriteString("_truncated", "map_too_large")
        writer.WriteNumber("_total_entries", dict.Count)
        writer.WriteNumber("_shown_entries", config.MaxCollectionItems)

    writer.WriteEndObject()

let serializeProperties
    (writer: Utf8JsonWriter)
    (properties: Collections.Generic.IReadOnlyDictionary<string, obj>)
    (depth: SerializationDepth)
    (config: ExportConfig)
    (errorTracker: ErrorTracker)
    =
    properties
    |> Seq.truncate config.MaxCollectionItems
    |> Seq.fold
        (fun keyTracker kvp ->
            let safePropName =
                ensureUniqueKey kvp.Key keyTracker

            writer.WritePropertyName(safePropName)
            serializeValueFunc.Value writer kvp.Value depth config errorTracker
            keyTracker)
        (createKeyTracker ())
    |> ignore

    if properties.Count > config.MaxCollectionItems then
        writer.WritePropertyName("_truncated")
        writer.WriteStringValue(sprintf "too_many_properties: %d total" properties.Count)
