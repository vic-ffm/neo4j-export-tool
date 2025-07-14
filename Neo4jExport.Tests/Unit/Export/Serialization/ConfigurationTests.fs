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

module Neo4jExport.Tests.Unit.Export.Serialization.ConfigurationTests

open System
open System.Collections.Generic
open System.Text.Json
open Expecto
open Swensen.Unquote
open Neo4jExport
open Neo4jExport.ExportTypes
open Neo4jExport.SerializationEngine
open Neo4jExport.SerializationCollections
open Neo4jExport.SerializationGraphElements
open Neo4jExport.Tests.Helpers.TestHelpers

// Ensure SerializationEngine module is initialized
let private ensureEngineInitialized =
    // Explicitly initialize the serialization engine
    initializeSerializationEngine ()

[<Tests>]
let tests =
    testList
        "Serialization - Configuration"
        [

          testList
              "Configurable limits enforcement"
              [ testCase "respects MAX_COLLECTION_ITEMS configuration"
                <| fun () ->
                    let items =
                        ResizeArray<obj>(
                            [ for i in 1..1000 do
                                  yield box i ]
                        )

                    let buffer, writer, context =
                        createTestWriterContextWithLimits 10 500 10000
                    // ArrayBufferWriter is not IDisposable
                    use _ = writer

                    writer.WriteStartObject()
                    writer.WritePropertyName("value")
                    serializeList writer context SerializationDepth.zero items
                    writer.WriteEndObject()
                    writer.Flush()

                    let json =
                        System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan.ToArray())

                    match validateJson json with
                    | Ok doc ->
                        let arr = getJsonValue doc
                        let arrLength = arr.GetArrayLength()
                        test <@ arrLength = 501 @> // 500 items + truncation marker
                        let lastElement = arr[500]

                        let lastelement__shown_items_int =
                            lastElement.GetProperty("_shown_items").GetInt32()

                        test <@ lastelement__shown_items_int = 500 @>
                    | Error msg -> failtest msg

                testCase "respects MAX_LABELS_PER_NODE configuration"
                <| fun () ->
                    let buffer, writer, context =
                        createTestWriterContext ()
                    // ArrayBufferWriter is not IDisposable
                    use _ = writer

                    let limitedContext =
                        { context with
                            Config =
                                { context.Config with
                                    MaxLabelsPerNode = 50 } }

                    let labels =
                        [ for i in 1..200 do
                              yield $"Label{i}" ]
                        :> IReadOnlyList<string>

                    let elementId = "element:1"
                    let stableId = "hash123"

                    let properties =
                        Dictionary<string, obj>() :> IReadOnlyDictionary<string, obj>

                    writer.WriteStartObject()
                    writer.WritePropertyName("value")
                    writeNodeDirect writer elementId stableId labels properties limitedContext
                    writer.WriteEndObject()
                    writer.Flush()

                    let json =
                        System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan.ToArray())

                    withValidatedJson json (fun doc ->
                        let obj = getJsonValue doc
                        let labels = obj.GetProperty("labels")
                        let labelsLength = labels.GetArrayLength()
                        test <@ labelsLength = 50 @>)

                testCase "respects MAX_NESTED_DEPTH configuration"
                <| fun () ->
                    let nestedStructure =
                        createNestedStructure 20

                    let buffer, writer, context =
                        createTestWriterContextWithLimits 5 10000 10000
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
                        // Should truncate at depth 5
                        let mutable current = getJsonValue doc

                        for i in 1..4 do
                            let mutable elem =
                                Unchecked.defaultof<JsonElement>

                            let hasNested =
                                current.TryGetProperty("nested", &elem)

                            test <@ hasNested @>
                            current <- current.GetProperty("nested")

                        // At depth 5, should see truncation
                        let truncated =
                            current.GetProperty("nested")

                        let truncated__truncated_str =
                            truncated.GetProperty("_truncated").GetString()

                        test <@ truncated__truncated_str = "depth_limit_exceeded" @>

                        let truncated__depth_int =
                            truncated.GetProperty("_depth").GetInt32()

                        test <@ truncated__depth_int = 5 @>
                    | Error msg -> failtest msg ]

          testList
              "Configuration combinations"
              [ testCase "handles multiple limits simultaneously"
                <| fun () ->
                    // Create a structure that tests multiple limits
                    let outerMap = Dictionary<string, obj>()

                    // Add many properties (test property limit)
                    for i in 1..100 do
                        outerMap[$"prop{i}"] <- box i

                    // Add a large list (test collection limit)
                    let largeList =
                        ResizeArray<obj>(
                            [ for i in 1..1000 do
                                  yield box i ]
                        )

                    outerMap["large_list"] <- box largeList

                    // Add deep nesting (test depth limit)
                    outerMap["deep"] <- createNestedStructure 10

                    let buffer, writer, context =
                        createTestWriterContextWithLimits 5 50 50
                    // ArrayBufferWriter is not IDisposable
                    use _ = writer

                    writer.WriteStartObject()
                    writer.WritePropertyName("value")

                    Neo4jExport.SerializationCollections.serializeMap
                        writer
                        context
                        SerializationDepth.zero
                        (outerMap :> System.Collections.IDictionary)

                    writer.WriteEndObject()
                    writer.Flush()

                    let json =
                        System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan.ToArray())

                    match validateJson json with
                    | Ok doc ->
                        let obj = getJsonValue doc

                        // Check that map was truncated
                        let mutable elem =
                            Unchecked.defaultof<JsonElement>

                        let hasTruncated =
                            obj.TryGetProperty("_truncated", &elem)

                        test <@ hasTruncated @>

                        let obj__shown_entries_int =
                            obj.GetProperty("_shown_entries").GetInt32()

                        test <@ obj__shown_entries_int = 50 @>

                        // Check that list within was also truncated
                        let mutable elemList =
                            Unchecked.defaultof<JsonElement>

                        if obj.TryGetProperty("large_list", &elemList) then
                            let list = obj.GetProperty("large_list")
                            let listLength = list.GetArrayLength()
                            test <@ listLength <= 51 @> // 50 items + truncation marker
                    | Error msg -> failtest msg

                testCase "different configs for different contexts"
                <| fun () ->
                    // Test that we can have different configurations for different serialization contexts
                    let map1 = Dictionary<string, obj>()

                    for i in 1..100 do
                        map1[$"key{i}"] <- box i

                    let map2 = Dictionary<string, obj>()

                    for i in 1..100 do
                        map2[$"key{i}"] <- box i

                    // Serialize with different limits
                    let buffer1, writer1, context1 =
                        createTestWriterContextWithLimits 10 20 20
                    // ArrayBufferWriter is not IDisposable1
                    use _ = writer1

                    writer1.WriteStartObject()
                    writer1.WritePropertyName("value")

                    Neo4jExport.SerializationCollections.serializeMap
                        writer1
                        context1
                        SerializationDepth.zero
                        (map1 :> System.Collections.IDictionary)

                    writer1.WriteEndObject()
                    writer1.Flush()

                    let json1 =
                        System.Text.Encoding.UTF8.GetString(buffer1.WrittenSpan.ToArray())

                    let buffer2, writer2, context2 =
                        createTestWriterContextWithLimits 10 80 80
                    // ArrayBufferWriter is not IDisposable2
                    use _ = writer2

                    writer2.WriteStartObject()
                    writer2.WritePropertyName("value")

                    Neo4jExport.SerializationCollections.serializeMap
                        writer2
                        context2
                        SerializationDepth.zero
                        (map2 :> System.Collections.IDictionary)

                    writer2.WriteEndObject()
                    writer2.Flush()

                    let json2 =
                        System.Text.Encoding.UTF8.GetString(buffer2.WrittenSpan.ToArray())

                    match validateJson json1, validateJson json2 with
                    | Ok doc1, Ok doc2 ->
                        let obj1 = getJsonValue doc1
                        let obj2 = getJsonValue doc2

                        // First should be truncated at 20
                        let obj1__shown_entries_int =
                            obj1.GetProperty("_shown_entries").GetInt32()

                        test <@ obj1__shown_entries_int = 20 @>

                        // Second should be truncated at 80
                        let obj2__shown_entries_int =
                            obj2.GetProperty("_shown_entries").GetInt32()

                        test <@ obj2__shown_entries_int = 80 @>
                    | _ -> failtest "JSON parsing failed" ]

          testList
              "Hash ID configuration"
              [ testCase "respects EnableHashedIds setting"
                <| fun () ->
                    let buffer1, writer1, context1 =
                        createTestWriterContext ()
                    // ArrayBufferWriter is not IDisposable1
                    use _ = writer1

                    // With hash IDs enabled (default)
                    let elementId = "element:123"
                    let stableId = "abcdef0123456789"

                    let labels =
                        [ "Test" ] :> IReadOnlyList<string>

                    let properties =
                        Dictionary<string, obj>() :> IReadOnlyDictionary<string, obj>

                    writer1.WriteStartObject()
                    writer1.WritePropertyName("value")
                    writeNodeDirect writer1 elementId stableId labels properties context1
                    writer1.WriteEndObject()
                    writer1.Flush()

                    let json1 =
                        System.Text.Encoding.UTF8.GetString(buffer1.WrittenSpan.ToArray())

                    // With hash IDs disabled
                    let buffer2, writer2, context2 =
                        createTestWriterContext ()
                    // ArrayBufferWriter is not IDisposable2
                    use _ = writer2

                    let noHashContext =
                        { context2 with
                            Config =
                                { context2.Config with
                                    EnableHashedIds = false } }

                    writer2.WriteStartObject()
                    writer2.WritePropertyName("value")
                    writeNodeDirect writer2 elementId stableId labels properties noHashContext
                    writer2.WriteEndObject()
                    writer2.Flush()

                    let json2 =
                        System.Text.Encoding.UTF8.GetString(buffer2.WrittenSpan.ToArray())

                    match validateJson json1, validateJson json2 with
                    | Ok doc1, Ok doc2 ->
                        let obj1 = getJsonValue doc1
                        let obj2 = getJsonValue doc2

                        // First should have hash
                        let mutable elem =
                            Unchecked.defaultof<JsonElement>

                        let obj1HasHash =
                            obj1.TryGetProperty("NET_node_content_hash", &elem)

                        test <@ obj1HasHash @>

                        // Second should not have hash
                        let mutable elemHash =
                            Unchecked.defaultof<JsonElement>

                        let obj2HasHash =
                            obj2.TryGetProperty("NET_node_content_hash", &elemHash)

                        test <@ obj2HasHash = false @>
                    | _ -> failtest "JSON parsing failed" ]

          testList
              "Label limit variations"
              [ testCase "different label limits for different contexts"
                <| fun () ->
                    let labels =
                        [ for i in 1..20 do
                              yield $"Label{i}" ]
                        :> IReadOnlyList<string>

                    let elementId = "element:1"
                    let stableId = "hash123"

                    let properties =
                        Dictionary<string, obj>() :> IReadOnlyDictionary<string, obj>

                    // Test MaxLabelsPerNode
                    let buffer1, writer1, context1 =
                        createTestWriterContext ()
                    // ArrayBufferWriter is not IDisposable1
                    use _ = writer1

                    let context5Labels =
                        { context1 with
                            Config =
                                { context1.Config with
                                    MaxLabelsPerNode = 5 } }

                    writer1.WriteStartObject()
                    writer1.WritePropertyName("value")
                    writeNodeDirect writer1 elementId stableId labels properties context5Labels
                    writer1.WriteEndObject()
                    writer1.Flush()

                    let json1 =
                        System.Text.Encoding.UTF8.GetString(buffer1.WrittenSpan.ToArray())

                    // Test with different limit
                    let buffer2, writer2, context2 =
                        createTestWriterContext ()
                    // ArrayBufferWriter is not IDisposable2
                    use _ = writer2

                    let context15Labels =
                        { context2 with
                            Config =
                                { context2.Config with
                                    MaxLabelsPerNode = 15 } }

                    writer2.WriteStartObject()
                    writer2.WritePropertyName("value")
                    writeNodeDirect writer2 elementId stableId labels properties context15Labels
                    writer2.WriteEndObject()
                    writer2.Flush()

                    let json2 =
                        System.Text.Encoding.UTF8.GetString(buffer2.WrittenSpan.ToArray())

                    match validateJson json1, validateJson json2 with
                    | Ok doc1, Ok doc2 ->
                        let obj1 = getJsonValue doc1
                        let obj2 = getJsonValue doc2

                        let labels1 = obj1.GetProperty("labels")
                        let labels2 = obj2.GetProperty("labels")

                        let labels1Length = labels1.GetArrayLength()
                        test <@ labels1Length = 5 @>
                        let labels2Length = labels2.GetArrayLength()
                        test <@ labels2Length = 15 @>
                    | _ -> failtest "JSON parsing failed" ] ]
