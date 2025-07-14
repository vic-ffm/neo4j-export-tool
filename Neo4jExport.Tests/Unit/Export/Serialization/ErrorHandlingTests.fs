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

module Neo4jExport.Tests.Unit.Export.Serialization.ErrorHandlingTests

open System
open System.Collections.Generic
open System.Text.Json
open Expecto
open Swensen.Unquote
open Neo4jExport
open Neo4jExport.SerializationEngine
open Neo4jExport.SerializationCollections
open Neo4jExport.Tests.Helpers.TestHelpers

// Ensure SerializationEngine module is initialized
let private ensureEngineInitialized =
    // Explicitly initialize the serialization engine
    initializeSerializationEngine ()

[<Tests>]
let tests =
    testList
        "Serialization - Error Handling"
        [

          testList
              "Unsupported type handling"
              [ testCase "serializes unsupported type with metadata"
                <| fun () ->
                    let unsupported = UnsupportedCustomType()

                    let json =
                        serializeToJsonWithContext (fun writer ctx depth ->
                            serializeValue writer ctx depth (box unsupported))

                    match validateJson json with
                    | Ok doc ->
                        let obj = getJsonValue doc

                        let typeStr =
                            obj.GetProperty("_type").GetString()

                        let containsUnsupported =
                            typeStr.Contains("UnsupportedCustomType")

                        test <@ containsUnsupported @>

                        let mutable elem =
                            Unchecked.defaultof<JsonElement>

                        let hasAssembly =
                            obj.TryGetProperty("_assembly", &elem)

                        test <@ hasAssembly @>

                        let obj__note_str =
                            obj.GetProperty("_note").GetString()

                        test <@ obj__note_str = "unserializable_type" @>
                    | Error msg -> failtest msg

                testCase "handles function type gracefully"
                <| fun () ->
                    let func = fun x -> x + 1

                    let json =
                        serializeToJsonWithContext (fun writer ctx depth -> serializeValue writer ctx depth (box func))

                    match validateJson json with
                    | Ok doc ->
                        let obj = getJsonValue doc

                        let mutable elem =
                            Unchecked.defaultof<JsonElement>

                        let hasType =
                            obj.TryGetProperty("_type", &elem)

                        test <@ hasType @>

                        let obj__note_str =
                            obj.GetProperty("_note").GetString()

                        test <@ obj__note_str = "unserializable_type" @>
                    | Error msg -> failtest msg

                testCase "handles System.Type instances"
                <| fun () ->
                    let typeInstance = typeof<string>

                    let json =
                        serializeToJsonWithContext (fun writer ctx depth ->
                            serializeValue writer ctx depth (box typeInstance))

                    match validateJson json with
                    | Ok doc ->
                        let obj = getJsonValue doc

                        let typeString =
                            obj.GetProperty("_type").GetString()

                        test
                            <@
                                typeString = "System.RuntimeType"
                                || typeString = "System.Type"
                            @>

                        let obj__note_str =
                            obj.GetProperty("_note").GetString()

                        test <@ obj__note_str = "unserializable_type" @>
                    | Error msg -> failtest msg

                testCase "handles custom exception types"
                <| fun () ->
                    let ex =
                        InvalidOperationException("Test exception")

                    let json =
                        serializeToJsonWithContext (fun writer ctx depth -> serializeValue writer ctx depth (box ex))

                    match validateJson json with
                    | Ok doc ->
                        let obj = getJsonValue doc

                        let obj__type_str =
                            obj.GetProperty("_type").GetString()

                        test <@ obj__type_str = "System.InvalidOperationException" @>

                        let obj__note_str =
                            obj.GetProperty("_note").GetString()

                        test <@ obj__note_str = "unserializable_type" @>
                    | Error msg -> failtest msg ]

          testList
              "Property serialization errors"
              [ testCase "continues after property error"
                <| fun () ->
                    let map = Dictionary<string, obj>()
                    map["good1"] <- box "value1"
                    map["bad"] <- box (fun () -> failwith "Should not call") // Function that would error
                    map["good2"] <- box "value2"

                    let json =
                        serializeToJsonWithContext (fun writer ctx depth ->
                            Neo4jExport.SerializationCollections.serializeMap
                                writer
                                ctx
                                depth
                                (map :> System.Collections.IDictionary))

                    match validateJson json with
                    | Ok doc ->
                        let obj = getJsonValue doc

                        let obj__good1_str =
                            obj.GetProperty("good1").GetString()

                        test <@ obj__good1_str = "value1" @>

                        let obj__good2_str =
                            obj.GetProperty("good2").GetString()

                        test <@ obj__good2_str = "value2" @>
                        // Bad property should have error marker
                        let bad = obj.GetProperty("bad")

                        let mutable elem =
                            Unchecked.defaultof<JsonElement>

                        let badHasType =
                            bad.TryGetProperty("_type", &elem)

                        test <@ badHasType @>

                        let bad__note_str =
                            bad.GetProperty("_note").GetString()

                        test <@ bad__note_str = "unserializable_type" @>
                    | Error msg -> failtest msg

                testCase "handles null key in map gracefully"
                <| fun () ->
                    // Create a dictionary that might have null keys (edge case)
                    let map = Dictionary<string, obj>()
                    map["valid"] <- box "value"
                    // Note: Can't actually add null key to Dictionary, but we handle the case

                    let json =
                        serializeToJsonWithContext (fun writer ctx depth ->
                            Neo4jExport.SerializationCollections.serializeMap
                                writer
                                ctx
                                depth
                                (map :> System.Collections.IDictionary))

                    match validateJson json with
                    | Ok doc ->
                        let obj = getJsonValue doc

                        let obj__valid_str =
                            obj.GetProperty("valid").GetString()

                        test <@ obj__valid_str = "value" @>
                    | Error msg -> failtest msg ]

          testList
              "Stack overflow protection"
              [ testCase "prevents stack overflow with extreme depth"
                <| fun () ->
                    // Create extremely deep nesting that would cause stack overflow
                    let veryDeep = createNestedStructure 1000

                    let buffer, writer, context =
                        createTestWriterContextWithLimits 50 10000 10000
                    // ArrayBufferWriter is not IDisposable
                    use _ = writer

                    writer.WriteStartObject()
                    writer.WritePropertyName("value")
                    serializeValue writer context SerializationDepth.zero veryDeep
                    writer.WriteEndObject()
                    writer.Flush()

                    let json =
                        System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan.ToArray())

                    match validateJson json with
                    | Ok doc ->
                        // Should truncate before stack overflow
                        test <@ doc.RootElement.GetRawText().Contains("depth_limit") @>
                    | Error msg -> failtest msg

                testCase "handles self-referential structures"
                <| fun () ->
                    let list = ResizeArray<obj>()
                    list.Add(box "item1")
                    list.Add(box list) // Self-reference

                    let buffer, writer, context =
                        createTestWriterContextWithLimits 10 10000 10000
                    // ArrayBufferWriter is not IDisposable
                    use _ = writer

                    writer.WriteStartObject()
                    writer.WritePropertyName("value")
                    serializeList writer context SerializationDepth.zero list
                    writer.WriteEndObject()
                    writer.Flush()

                    let json =
                        System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan.ToArray())

                    match validateJson json with
                    | Ok _ ->
                        // Should handle without crashing
                        test <@ true @>
                    | Error msg -> failtest msg ]

          testList
              "Numeric edge cases"
              [ testCase "handles numeric overflow gracefully"
                <| fun () ->
                    let values =
                        ResizeArray<obj>(
                            [ box Double.MaxValue
                              box Double.MinValue
                              box Double.Epsilon
                              box -0.0 ]
                        )

                    let json =
                        serializeToJsonWithContext (fun writer ctx depth -> serializeList writer ctx depth values)

                    match validateJson json with
                    | Ok doc ->
                        let arr = getJsonValue doc
                        let arrLength = arr.GetArrayLength()
                        test <@ arrLength = 4 @>
                        let arr0 = arr[0].GetDouble()
                        test <@ arr0 = Double.MaxValue @>
                        let arr1 = arr[1].GetDouble()
                        test <@ arr1 = Double.MinValue @>
                    | Error msg -> failtest msg ]

          testList
              "Error accumulation"
              [ testCase "collects multiple errors during serialization"
                <| fun () ->
                    let map = Dictionary<string, obj>()
                    map["error1"] <- box (UnsupportedCustomType())
                    map["good"] <- box "value"
                    map["error2"] <- box (fun x -> x)

                    map["nested"] <-
                        box (
                            Dictionary<string, obj>(
                                [ ("bad", box (Type.GetType("System.String"))) ]
                                |> dict
                            )
                        )

                    let json =
                        serializeToJsonWithContext (fun writer ctx depth ->
                            Neo4jExport.SerializationCollections.serializeMap
                                writer
                                ctx
                                depth
                                (map :> System.Collections.IDictionary))

                    match validateJson json with
                    | Ok doc ->
                        let obj = getJsonValue doc
                        // Good values should serialize
                        let obj__good_str =
                            obj.GetProperty("good").GetString()

                        test <@ obj__good_str = "value" @>
                        // Errors should be marked
                        let obj__error1__note_str =
                            obj.GetProperty("error1").GetProperty("_note").GetString()

                        test <@ obj__error1__note_str = "unserializable_type" @>

                        let obj__error2__note_str =
                            obj.GetProperty("error2").GetProperty("_note").GetString()

                        test <@ obj__error2__note_str = "unserializable_type" @>
                    | Error msg -> failtest msg ]

          testList
              "Depth exceeded error format"
              [ testCase "writes correct depth exceeded format"
                <| fun () ->
                    let deeplyNested = createNestedStructure 15

                    let buffer, writer, context =
                        createTestWriterContextWithLimits 5 10000 10000
                    // ArrayBufferWriter is not IDisposable
                    use _ = writer

                    writer.WriteStartObject()
                    writer.WritePropertyName("value")
                    serializeValue writer context SerializationDepth.zero deeplyNested
                    writer.WriteEndObject()
                    writer.Flush()

                    let json =
                        System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan.ToArray())

                    match validateJson json with
                    | Ok doc ->
                        // Navigate to truncation point
                        let mutable current = getJsonValue doc

                        for i in 1..4 do
                            current <- current.GetProperty("nested")

                        let truncated =
                            current.GetProperty("nested")

                        let truncated__truncated_str =
                            truncated.GetProperty("_truncated").GetString()

                        test <@ truncated__truncated_str = "depth_limit_exceeded" @>

                        let truncated__depth_int =
                            truncated.GetProperty("_depth").GetInt32()

                        test <@ truncated__depth_int = 5 @>

                        let truncatedType =
                            truncated.GetProperty("_type").GetString()

                        let containsDict =
                            truncatedType.Contains("Dictionary")

                        test <@ containsDict @>
                    | Error msg -> failtest msg ]

          testList
              "Null handling edge cases"
              [ testCase "handles null in various contexts"
                <| fun () ->
                    let map = Dictionary<string, obj>()
                    map["null_value"] <- null
                    map["null_in_list"] <- box (ResizeArray<obj>([ box "before"; null; box "after" ]))

                    let nullKeyMap = Dictionary<obj, obj>()
                    nullKeyMap[box "key"] <- box "value"
                    // Can't add null key to dictionary, but serializer should handle if it somehow occurs

                    map["mixed_map"] <- box nullKeyMap

                    let json =
                        serializeToJsonWithContext (fun writer ctx depth ->
                            Neo4jExport.SerializationCollections.serializeMap
                                writer
                                ctx
                                depth
                                (map :> System.Collections.IDictionary))

                    match validateJson json with
                    | Ok doc ->
                        let obj = getJsonValue doc

                        let nullValueKind =
                            obj.GetProperty("null_value").ValueKind

                        test <@ nullValueKind = JsonValueKind.Null @>

                        let list = obj.GetProperty("null_in_list")
                        let list_0_str = list[0].GetString()
                        test <@ list_0_str = "before" @>
                        let list1Kind = list[1].ValueKind
                        test <@ list1Kind = JsonValueKind.Null @>
                        let list_2_str = list[2].GetString()
                        test <@ list_2_str = "after" @>
                    | Error msg -> failtest msg ] ]
