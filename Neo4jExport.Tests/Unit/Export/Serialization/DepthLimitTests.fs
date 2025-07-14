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

module Neo4jExport.Tests.Unit.Export.Serialization.DepthLimitTests

open System
open System.Collections.Generic
open System.Text.Json
open Expecto
open Swensen.Unquote
open Neo4jExport
open Neo4jExport.SerializationEngine
open Neo4jExport.SerializationContext
open Neo4jExport.ExportTypes
open Neo4jExport.Tests.Helpers.TestHelpers

// Ensure SerializationEngine module is initialized
let private ensureEngineInitialized =
    // Explicitly initialize the serialization engine
    initializeSerializationEngine ()

[<Tests>]
let tests =
    testList
        "Serialization - Depth Limits"
        [

          testList
              "Depth truncation"
              [ testCase "truncates at configured depth limit"
                <| fun () ->
                    let nestedStructure =
                        createNestedStructure 15 // 15 levels deep

                    let buffer, writer, context =
                        createTestWriterContextWithLimits 10 10000 10000
                    // ArrayBufferWriter is not IDisposable
                    use _ = writer

                    writer.WriteStartObject()
                    writer.WritePropertyName("value")
                    serializeValue writer context SerializationDepth.zero nestedStructure
                    writer.WriteEndObject()
                    writer.Flush()

                    let json =
                        System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan.ToArray())

                    match validateJson json with
                    | Ok doc ->
                        // Navigate to depth 10 and verify truncation
                        let mutable current = getJsonValue doc

                        for i in 1..9 do
                            let mutable elem =
                                Unchecked.defaultof<JsonElement>

                            let hasNested =
                                current.TryGetProperty("nested", &elem)

                            test <@ hasNested @>
                            current <- current.GetProperty("nested")

                        // At depth 10, should see truncation
                        let mutable elemNested =
                            Unchecked.defaultof<JsonElement>

                        let hasNestedElem =
                            current.TryGetProperty("nested", &elemNested)

                        test <@ hasNestedElem @>

                        let truncated =
                            current.GetProperty("nested")

                        let truncated__truncated_str =
                            truncated.GetProperty("_truncated").GetString()

                        test <@ truncated__truncated_str = "depth_limit_exceeded" @>

                        let truncated__depth_int =
                            truncated.GetProperty("_depth").GetInt32()

                        test <@ truncated__depth_int = 10 @>

                        let mutable elemType =
                            Unchecked.defaultof<JsonElement>

                        let hasType =
                            truncated.TryGetProperty("_type", &elemType)

                        test <@ hasType @>
                    | Error msg -> failtest msg

                testCase "does not truncate within depth limit"
                <| fun () ->
                    let nestedStructure =
                        createNestedStructure 5 // 5 levels deep

                    let json =
                        serializeToJsonWithContext (fun writer ctx depth ->
                            serializeValue writer ctx depth nestedStructure)

                    match validateJson json with
                    | Ok doc ->
                        // Should be able to navigate all 5 levels
                        let mutable current = getJsonValue doc

                        for i in 1..4 do
                            let mutable elem =
                                Unchecked.defaultof<JsonElement>

                            let hasNested =
                                current.TryGetProperty("nested", &elem)

                            test <@ hasNested @>
                            current <- current.GetProperty("nested")

                        // Leaf should be a string
                        let current__nested_str =
                            current.GetProperty("nested").GetString()

                        test <@ current__nested_str = "leaf" @>
                    | Error msg -> failtest msg

                testCase "handles circular references gracefully"
                <| fun () ->
                    let dict1 = Dictionary<string, obj>()
                    let dict2 = Dictionary<string, obj>()

                    dict1["name"] <- box "dict1"
                    dict1["ref"] <- box dict2

                    dict2["name"] <- box "dict2"
                    dict2["ref"] <- box dict1 // Circular reference

                    let buffer, writer, context =
                        createTestWriterContextWithLimits 10 10000 10000
                    // ArrayBufferWriter is not IDisposable
                    use _ = writer

                    writer.WriteStartObject()
                    writer.WritePropertyName("value")
                    serializeValue writer context SerializationDepth.zero (box dict1)
                    writer.WriteEndObject()
                    writer.Flush()

                    let json =
                        System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan.ToArray())

                    match validateJson json with
                    | Ok doc ->
                        // Should truncate at depth limit, preventing infinite recursion
                        test <@ doc.RootElement.GetRawText().Length > 0 @>
                    | Error msg -> failtest msg ]

          testList
              "Nested element modes"
              [ testCase "determines nested mode based on depth"
                <| fun () ->
                    let buffer, writer, context =
                        createTestWriterContext ()
                    // ArrayBufferWriter is not IDisposable
                    use _ = writer

                    // Test that depth determines mode
                    let depth0 = SerializationDepth.zero

                    let depth2 =
                        depth0
                        |> SerializationDepth.increment
                        |> SerializationDepth.increment

                    let depth4 =
                        depth2
                        |> SerializationDepth.increment
                        |> SerializationDepth.increment

                    let mode0 =
                        determineNestedLevel depth0 context.Config

                    let mode2 =
                        determineNestedLevel depth2 context.Config

                    let mode4 =
                        determineNestedLevel depth4 context.Config

                    // Default config has NestedShallowModeDepth=2 and NestedReferenceModeDepth=4
                    test <@ mode0 = Deep @>
                    test <@ mode2 = Shallow @>
                    test <@ mode4 = Reference @>

                testCase "serialization depth module functions work correctly"
                <| fun () ->
                    let d0 = SerializationDepth.zero
                    test <@ SerializationDepth.value d0 = 0 @>

                    let d1 = SerializationDepth.increment d0
                    test <@ SerializationDepth.value d1 = 1 @>

                    let d5 =
                        d1
                        |> SerializationDepth.increment
                        |> SerializationDepth.increment
                        |> SerializationDepth.increment
                        |> SerializationDepth.increment

                    test <@ SerializationDepth.value d5 = 5 @>

                    test <@ SerializationDepth.exceedsLimit 5 d5 = true @>
                    test <@ SerializationDepth.exceedsLimit 6 d5 = false @> ]

          testList
              "Property count limits"
              [ testCase "handles objects with many properties"
                <| fun () ->
                    let map = Dictionary<string, obj>()

                    for i in 1..15000 do
                        map[$"prop{i}"] <- box $"value{i}"

                    let buffer, writer, context =
                        createTestWriterContextWithLimits 10 10000 10000
                    // ArrayBufferWriter is not IDisposable
                    use _ = writer

                    writer.WriteStartObject()
                    writer.WritePropertyName("value")

                    Neo4jExport.SerializationCollections.serializeMap
                        writer
                        context
                        SerializationDepth.zero
                        (map :> System.Collections.IDictionary)

                    writer.WriteEndObject()
                    writer.Flush()

                    let json =
                        System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan.ToArray())

                    match validateJson json with
                    | Ok doc ->
                        let obj = getJsonValue doc
                        // Should have property limit marker
                        let mutable elem =
                            Unchecked.defaultof<JsonElement>

                        let hasTruncated =
                            obj.TryGetProperty("_truncated", &elem)

                        test <@ hasTruncated @>

                        let obj__truncated_str =
                            obj.GetProperty("_truncated").GetString()

                        test <@ obj__truncated_str = "map_too_large" @>

                        let obj__total_entries_int =
                            obj.GetProperty("_total_entries").GetInt32()

                        test <@ obj__total_entries_int = 15000 @>

                        let shownEntries =
                            obj.GetProperty("_shown_entries").GetInt32()

                        test <@ shownEntries <= 10000 @>
                    | Error msg -> failtest msg ]

          testList
              "Complex nesting scenarios"
              [ testCase "handles deeply nested mixed collections"
                <| fun () ->
                    // Create a structure with mixed lists and maps
                    let rec createMixedNesting depth =
                        if depth <= 0 then
                            box "leaf"
                        else
                            let map = Dictionary<string, obj>()
                            map["depth"] <- box depth

                            map["list"] <-
                                box (
                                    ResizeArray<obj>(
                                        [ box "item1"
                                          createMixedNesting (depth - 1)
                                          box "item3" ]
                                    )
                                )

                            map["map"] <- createMixedNesting (depth - 1)
                            box map

                    let nested = createMixedNesting 12

                    let buffer, writer, context =
                        createTestWriterContextWithLimits 10 10000 10000
                    // ArrayBufferWriter is not IDisposable
                    use _ = writer

                    writer.WriteStartObject()
                    writer.WritePropertyName("value")
                    serializeValue writer context SerializationDepth.zero nested
                    writer.WriteEndObject()
                    writer.Flush()

                    let json =
                        System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan.ToArray())

                    match validateJson json with
                    | Ok doc ->
                        // Should serialize up to depth 10 then truncate
                        test <@ doc.RootElement.GetRawText().Contains("depth_limit_exceeded") @>
                    | Error msg -> failtest msg

                testCase "handles alternating collection types"
                <| fun () ->
                    // Alternate between lists and maps at each level
                    let rec createAlternating depth isMap =
                        if depth <= 0 then
                            box "end"
                        elif isMap then
                            let map = Dictionary<string, obj>()
                            map["type"] <- box "map"
                            map["level"] <- box depth
                            map["child"] <- createAlternating (depth - 1) false
                            box map
                        else
                            let list = ResizeArray<obj>()
                            list.Add(box "list")
                            list.Add(box depth)
                            list.Add(createAlternating (depth - 1) true)
                            box list

                    let structure = createAlternating 15 true

                    let buffer, writer, context =
                        createTestWriterContextWithLimits 10 10000 10000
                    // ArrayBufferWriter is not IDisposable
                    use _ = writer

                    writer.WriteStartObject()
                    writer.WritePropertyName("value")
                    serializeValue writer context SerializationDepth.zero structure
                    writer.WriteEndObject()
                    writer.Flush()

                    let json =
                        System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan.ToArray())

                    match validateJson json with
                    | Ok doc ->
                        test <@ doc.RootElement.GetRawText().Length > 0 @>
                        test <@ doc.RootElement.GetRawText().Contains("depth_limit_exceeded") @>
                    | Error msg -> failtest msg ] ]
