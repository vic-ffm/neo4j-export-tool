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

module Neo4jExport.Tests.Unit.Export.Serialization.CollectionsTests

open System
open System.Collections.Generic
open System.Text.Json
open Expecto
open Swensen.Unquote
open Neo4jExport
open Neo4jExport.SerializationCollections
open Neo4jExport.SerializationEngine
open Neo4jExport.Tests.Helpers.TestHelpers

// Ensure SerializationEngine module is initialized
let private ensureEngineInitialized =
    // Explicitly initialize the serialization engine
    initializeSerializationEngine ()

[<Tests>]
let tests =
    testList
        "Serialization - Collections"
        [

          testList
              "List serialization"
              [ testCase "serializes empty list"
                <| fun () ->
                    let items = ResizeArray<obj>()

                    let json =
                        serializeToJsonWithContext (fun writer ctx depth -> serializeList writer ctx depth items)

                    assertJsonValue "[]" json

                testCase "serializes homogeneous list"
                <| fun () ->
                    let items =
                        ResizeArray<obj>([ box 1L; box 2L; box 3L ])

                    let json =
                        serializeToJsonWithContext (fun writer ctx depth -> serializeList writer ctx depth items)

                    match validateJson json with
                    | Ok doc ->
                        let arr = getJsonValue doc
                        let arrLength = arr.GetArrayLength()
                        test <@ arrLength = 3 @>
                        let arr0 = arr[0].GetInt64()
                        test <@ arr0 = 1L @>
                        let arr1 = arr[1].GetInt64()
                        test <@ arr1 = 2L @>
                        let arr2 = arr[2].GetInt64()
                        test <@ arr2 = 3L @>
                    | Error msg -> failtest msg

                testCase "serializes heterogeneous list"
                <| fun () ->
                    let items =
                        ResizeArray<obj>(
                            [ box 42L
                              box "hello"
                              box true
                              box 3.14
                              null ]
                        )

                    let json =
                        serializeToJsonWithContext (fun writer ctx depth -> serializeList writer ctx depth items)

                    match validateJson json with
                    | Ok doc ->
                        let arr = getJsonValue doc
                        let arrLength = arr.GetArrayLength()
                        test <@ arrLength = 5 @>
                        let arr0 = arr[0].GetInt64()
                        test <@ arr0 = 42L @>
                        let arr_1_str = arr[1].GetString()
                        test <@ arr_1_str = "hello" @>
                        let arr_2_bool = arr[2].GetBoolean()
                        test <@ arr_2_bool = true @>
                        let arr_3_dbl = arr[3].GetDouble()
                        test <@ arr_3_dbl = 3.14 @>
                        let arr4Kind = arr[4].ValueKind
                        test <@ arr4Kind = JsonValueKind.Null @>
                    | Error msg -> failtest msg

                testCase "serializes nested lists"
                <| fun () ->
                    let innerList1 =
                        ResizeArray<obj>([ box 1L; box 2L ])

                    let innerList2 =
                        ResizeArray<obj>([ box 3L; box 4L ])

                    let items =
                        ResizeArray<obj>([ box innerList1; box innerList2 ])

                    let json =
                        serializeToJsonWithContext (fun writer ctx depth -> serializeList writer ctx depth items)

                    match validateJson json with
                    | Ok doc ->
                        let arr = getJsonValue doc
                        let arrLength = arr.GetArrayLength()
                        test <@ arrLength = 2 @>
                        let arr0Length = arr[0].GetArrayLength()
                        test <@ arr0Length = 2 @>
                        let arr1Length = arr[1].GetArrayLength()
                        test <@ arr1Length = 2 @>
                        let arr0_0 = (arr[0]).[0].GetInt64()
                        test <@ arr0_0 = 1L @>
                        let arr1_1 = (arr[1]).[1].GetInt64()
                        test <@ arr1_1 = 4L @>
                    | Error msg -> failtest msg

                testCase "handles special float values in list"
                <| fun () ->
                    let items =
                        ResizeArray<obj>(
                            [ box Double.NaN
                              box Double.PositiveInfinity
                              box Double.NegativeInfinity
                              box 1.23 ]
                        )

                    let json =
                        serializeToJsonWithContext (fun writer ctx depth -> serializeList writer ctx depth items)

                    match validateJson json with
                    | Ok doc ->
                        let arr = getJsonValue doc
                        let arr_0_str = arr[0].GetString()
                        test <@ arr_0_str = "NaN" @>
                        let arr_1_str = arr[1].GetString()
                        test <@ arr_1_str = "Infinity" @>
                        let arr_2_str = arr[2].GetString()
                        test <@ arr_2_str = "-Infinity" @>
                        let arr_3_dbl = arr[3].GetDouble()
                        test <@ arr_3_dbl = 1.23 @>
                    | Error msg -> failtest msg ]

          testList
              "Map serialization"
              [ testCase "serializes empty map"
                <| fun () ->
                    let map = Dictionary<string, obj>()

                    let json =
                        serializeToJsonWithContext (fun writer ctx depth ->
                            Neo4jExport.SerializationCollections.serializeMap
                                writer
                                ctx
                                depth
                                (map :> System.Collections.IDictionary))

                    assertJsonValue "{}" json

                testCase "serializes simple map"
                <| fun () ->
                    let map = Dictionary<string, obj>()
                    map["name"] <- box "Alice"
                    map["age"] <- box 30L
                    map["active"] <- box true

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

                        let obj__name_str =
                            obj.GetProperty("name").GetString()

                        test <@ obj__name_str = "Alice" @>
                        let age = obj.GetProperty("age").GetInt64()
                        test <@ age = 30L @>

                        let obj__active_bool =
                            obj.GetProperty("active").GetBoolean()

                        test <@ obj__active_bool = true @>
                    | Error msg -> failtest msg

                testCase "serializes map with null values"
                <| fun () ->
                    let map = Dictionary<string, obj>()
                    map["exists"] <- box "yes"
                    map["missing"] <- null

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

                        let obj__exists_str =
                            obj.GetProperty("exists").GetString()

                        test <@ obj__exists_str = "yes" @>

                        let missingKind =
                            obj.GetProperty("missing").ValueKind

                        test <@ missingKind = JsonValueKind.Null @>
                    | Error msg -> failtest msg

                testCase "serializes nested maps"
                <| fun () ->
                    let innerMap = Dictionary<string, obj>()
                    innerMap["x"] <- box 10L
                    innerMap["y"] <- box 20L

                    let outerMap = Dictionary<string, obj>()
                    outerMap["point"] <- box innerMap
                    outerMap["name"] <- box "origin"

                    let json =
                        serializeToJsonWithContext (fun writer ctx depth ->
                            Neo4jExport.SerializationCollections.serializeMap
                                writer
                                ctx
                                depth
                                (outerMap :> System.Collections.IDictionary))

                    match validateJson json with
                    | Ok doc ->
                        let obj = getJsonValue doc

                        let obj__name_str =
                            obj.GetProperty("name").GetString()

                        test <@ obj__name_str = "origin" @>
                        let point = obj.GetProperty("point")

                        let pointX =
                            point.GetProperty("x").GetInt64()

                        test <@ pointX = 10L @>

                        let pointY =
                            point.GetProperty("y").GetInt64()

                        test <@ pointY = 20L @>
                    | Error msg -> failtest msg

                testCase "handles special characters in keys"
                <| fun () ->
                    let map = Dictionary<string, obj>()
                    map["normal-key"] <- box "value1"
                    map["key.with.dots"] <- box "value2"
                    map["key with spaces"] <- box "value3"
                    map["key\"with\"quotes"] <- box "value4"
                    map[""] <- box "empty key"

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

                        let obj__normal_key_str =
                            obj.GetProperty("normal-key").GetString()

                        test <@ obj__normal_key_str = "value1" @>

                        let obj__key_with_dots_str =
                            obj.GetProperty("key.with.dots").GetString()

                        test <@ obj__key_with_dots_str = "value2" @>

                        let obj__key_with_spaces_str =
                            obj.GetProperty("key with spaces").GetString()

                        test <@ obj__key_with_spaces_str = "value3" @>

                        let obj__key_with_quotes_str =
                            obj.GetProperty("key\"with\"quotes").GetString()

                        test <@ obj__key_with_quotes_str = "value4" @>

                        let obj_str =
                            obj.GetProperty("").GetString()

                        test <@ obj_str = "empty key" @>
                    | Error msg -> failtest msg

                testCase "maintains key order"
                <| fun () ->
                    // While JSON doesn't guarantee order, verify our implementation is consistent
                    let map = Dictionary<string, obj>()

                    for i in [ 1..10 ] do
                        map[$"key{i}"] <- box i

                    let json1 =
                        serializeToJsonWithContext (fun writer ctx depth ->
                            Neo4jExport.SerializationCollections.serializeMap
                                writer
                                ctx
                                depth
                                (map :> System.Collections.IDictionary))

                    let json2 =
                        serializeToJsonWithContext (fun writer ctx depth ->
                            Neo4jExport.SerializationCollections.serializeMap
                                writer
                                ctx
                                depth
                                (map :> System.Collections.IDictionary))

                    // Same input should produce same output
                    test <@ json1 = json2 @>

                testCase "handles duplicate keys with renaming"
                <| fun () ->
                    // Test map key deduplication feature
                    let map = Dictionary<string, obj>()
                    // Simulate duplicate keys that would be renamed
                    map["key"] <- box "first"
                    map["key_1"] <- box "second" // This simulates what happens with duplicate "key"
                    map["key_2"] <- box "third" // This simulates another duplicate

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

                        let obj__key_str =
                            obj.GetProperty("key").GetString()

                        test <@ obj__key_str = "first" @>

                        let obj__key_1_str =
                            obj.GetProperty("key_1").GetString()

                        test <@ obj__key_1_str = "second" @>

                        let obj__key_2_str =
                            obj.GetProperty("key_2").GetString()

                        test <@ obj__key_2_str = "third" @>
                    | Error msg -> failtest msg ]

          testList
              "Mixed collection scenarios"
              [ testCase "list containing maps"
                <| fun () ->
                    let map1 = Dictionary<string, obj>()
                    map1["id"] <- box 1L
                    map1["name"] <- box "First"

                    let map2 = Dictionary<string, obj>()
                    map2["id"] <- box 2L
                    map2["name"] <- box "Second"

                    let items =
                        ResizeArray<obj>([ box map1; box map2 ])

                    let json =
                        serializeToJsonWithContext (fun writer ctx depth -> serializeList writer ctx depth items)

                    match validateJson json with
                    | Ok doc ->
                        let arr = getJsonValue doc
                        let arrLength = arr.GetArrayLength()
                        test <@ arrLength = 2 @>

                        let arr0Id =
                            arr[0].GetProperty("id").GetInt64()

                        test <@ arr0Id = 1L @>

                        let arr1Id =
                            arr[1].GetProperty("id").GetInt64()

                        test <@ arr1Id = 2L @>
                    | Error msg -> failtest msg

                testCase "map containing lists"
                <| fun () ->
                    let map = Dictionary<string, obj>()
                    map["numbers"] <- box (ResizeArray<obj>([ box 1L; box 2L; box 3L ]))
                    map["strings"] <- box (ResizeArray<obj>([ box "a"; box "b"; box "c" ]))

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
                        let numbers = obj.GetProperty("numbers")
                        let strings = obj.GetProperty("strings")
                        let numbersLength = numbers.GetArrayLength()
                        test <@ numbersLength = 3 @>
                        let stringsLength = strings.GetArrayLength()
                        test <@ stringsLength = 3 @>
                    | Error msg -> failtest msg

                testCase "complex nested structure"
                <| fun () ->
                    // Create a realistic complex structure
                    let address = Dictionary<string, obj>()
                    address["street"] <- box "123 Main St"
                    address["city"] <- box "Anytown"
                    address["coordinates"] <- box (ResizeArray<obj>([ box -73.9857; box 40.7484 ]))

                    let person = Dictionary<string, obj>()
                    person["name"] <- box "John Doe"
                    person["age"] <- box 30L
                    person["address"] <- box address

                    person["hobbies"] <-
                        box (
                            ResizeArray<obj>(
                                [ box "reading"
                                  box "coding"
                                  box "gaming" ]
                            )
                        )

                    let json =
                        serializeToJsonWithContext (fun writer ctx depth ->
                            Neo4jExport.SerializationCollections.serializeMap
                                writer
                                ctx
                                depth
                                (person :> System.Collections.IDictionary))

                    match validateJson json with
                    | Ok doc ->
                        let obj = getJsonValue doc

                        let obj__name_str =
                            obj.GetProperty("name").GetString()

                        test <@ obj__name_str = "John Doe" @>
                        let addr = obj.GetProperty("address")

                        let addr__city_str =
                            addr.GetProperty("city").GetString()

                        test <@ addr__city_str = "Anytown" @>
                        let coords = addr.GetProperty("coordinates")
                        let coordsLength = coords.GetArrayLength()
                        test <@ coordsLength = 2 @>
                    | Error msg -> failtest msg ] ]
