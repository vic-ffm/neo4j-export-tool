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

module Neo4jExport.Tests.Unit.Export.Serialization.TruncationTests

open System
open System.Collections.Generic
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Expecto
open Swensen.Unquote
open Neo4jExport
open Neo4jExport.SerializationEngine
open Neo4jExport.SerializationPrimitives
open Neo4jExport.SerializationCollections
open Neo4jExport.Tests.Helpers.TestHelpers

// Ensure SerializationEngine module is initialized
let private ensureEngineInitialized =
    // Explicitly initialize the serialization engine
    initializeSerializationEngine ()

[<Tests>]
let tests =
    testList
        "Serialization - Truncation"
        [

          testList
              "String truncation"
              [ testCase "truncates string over 10MB"
                <| fun () ->
                    let largeString =
                        createLargeString 11_000_000 // 11MB

                    let json =
                        serializeToJsonWithConfig (fun writer config -> serializeString writer largeString config)

                    match validateJson json with
                    | Ok doc ->
                        let obj = getJsonValue doc

                        let obj__truncated_str =
                            obj.GetProperty("_truncated").GetString()

                        test <@ obj__truncated_str = "string_too_large" @>

                        let length =
                            obj.GetProperty("_length").GetInt32()

                        test <@ length = 11_000_000 @>

                        let prefixLength =
                            obj.GetProperty("_prefix").GetString().Length

                        test <@ prefixLength = 1000 @>

                        let mutable elem =
                            Unchecked.defaultof<JsonElement>

                        let hasSha256 =
                            obj.TryGetProperty("_sha256", &elem)

                        test <@ hasSha256 @>

                        // Verify SHA256 hash
                        let bytes =
                            Encoding.UTF8.GetBytes(largeString)

                        use sha = SHA256.Create()
                        let hash = sha.ComputeHash(bytes)

                        let expectedHash =
                            Convert.ToBase64String(hash)

                        let sha256Str =
                            obj.GetProperty("_sha256").GetString()

                        test <@ sha256Str = expectedHash @>
                    | Error msg -> failtest msg

                testCase "does not truncate string under 10MB"
                <| fun () ->
                    let normalString =
                        createLargeString 5_000_000 // 5MB

                    let json =
                        serializeToJsonWithConfig (fun writer config -> serializeString writer normalString config)

                    match validateJson json with
                    | Ok doc ->
                        let value = getJsonValue doc
                        let valueKind = value.ValueKind
                        test <@ valueKind = JsonValueKind.String @>
                        let valueStr = value.GetString()
                        test <@ valueStr = normalString @>
                    | Error msg -> failtest msg ]

          testList
              "ByteArray truncation"
              [ testCase "truncates byte array over 50MB"
                <| fun () ->
                    let largeByteArray =
                        createLargeByteArray 51_000_000 // 51MB

                    let json =
                        serializeToJsonWithConfig (fun writer config -> serializeBinary writer largeByteArray config)

                    match validateJson json with
                    | Ok doc ->
                        let obj = getJsonValue doc

                        let obj__truncated_str =
                            obj.GetProperty("_truncated").GetString()

                        test <@ obj__truncated_str = "binary_too_large" @>

                        let lengthValue =
                            obj.GetProperty("_length").GetInt32()

                        test <@ lengthValue = 51_000_000 @>

                        let mutable elem =
                            Unchecked.defaultof<JsonElement>

                        let hasSha256Prop =
                            obj.TryGetProperty("_sha256", &elem)

                        test <@ hasSha256Prop @>

                        // Verify SHA256 hash
                        use sha = SHA256.Create()
                        let hash = sha.ComputeHash(largeByteArray)

                        let expectedHash =
                            Convert.ToBase64String(hash)

                        let sha256Str =
                            obj.GetProperty("_sha256").GetString()

                        test <@ sha256Str = expectedHash @>
                    | Error msg -> failtest msg

                testCase "does not truncate byte array under 50MB"
                <| fun () ->
                    let normalByteArray =
                        createLargeByteArray 10_000_000 // 10MB

                    let json =
                        serializeToJsonWithConfig (fun writer config -> serializeBinary writer normalByteArray config)

                    match validateJson json with
                    | Ok doc ->
                        let value = getJsonValue doc
                        let valueKind = value.ValueKind
                        test <@ valueKind = JsonValueKind.String @>

                        let decoded =
                            Convert.FromBase64String(value.GetString())

                        test <@ decoded = normalByteArray @>
                    | Error msg -> failtest msg ]

          testList
              "Collection truncation"
              [ testCase "truncates list over configured limit"
                <| fun () ->
                    let items =
                        ResizeArray<obj>(
                            [ for i in 1..15000 do
                                  yield box i ]
                        )

                    let buffer, writer, context =
                        createTestWriterContextWithLimits 10 10000 10000
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
                        test <@ arrLength = 10001 @> // 10000 items + truncation marker

                        // Last element should be truncation info
                        let lastElement = arr[10000]

                        let lastelement__truncated_str =
                            lastElement.GetProperty("_truncated").GetString()

                        test <@ lastelement__truncated_str = "list_too_large" @>

                        let lastelement__total_items_int =
                            lastElement.GetProperty("_total_items").GetInt32()

                        test <@ lastelement__total_items_int = 15000 @>

                        let lastelement__shown_items_int =
                            lastElement.GetProperty("_shown_items").GetInt32()

                        test <@ lastelement__shown_items_int = 10000 @>
                    | Error msg -> failtest msg

                testCase "does not truncate list under limit"
                <| fun () ->
                    let items =
                        ResizeArray<obj>(
                            [ for i in 1..500 do
                                  yield box i ]
                        )

                    let json =
                        serializeToJsonWithContext (fun writer ctx depth -> serializeList writer ctx depth items)

                    match validateJson json with
                    | Ok doc ->
                        let arr = getJsonValue doc
                        let arrLength = arr.GetArrayLength()
                        test <@ arrLength = 500 @>
                    | Error msg -> failtest msg

                testCase "truncates map over configured limit"
                <| fun () ->
                    let map = Dictionary<string, obj>()

                    for i in 1..15000 do
                        map[$"key{i}"] <- box i

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
                        // Should have truncation marker
                        let mutable elem =
                            Unchecked.defaultof<JsonElement>

                        let hasTruncatedProp =
                            obj.TryGetProperty("_truncated", &elem)

                        test <@ hasTruncatedProp @>

                        let obj__truncated_str =
                            obj.GetProperty("_truncated").GetString()

                        test <@ obj__truncated_str = "map_too_large" @>

                        let obj__total_entries_int =
                            obj.GetProperty("_total_entries").GetInt32()

                        test <@ obj__total_entries_int = 15000 @>

                        let obj__shown_entries_int =
                            obj.GetProperty("_shown_entries").GetInt32()

                        test <@ obj__shown_entries_int = 10000 @>
                    | Error msg -> failtest msg ]

          testList
              "Truncation preserves critical data"
              [ testCase "string truncation preserves prefix for identification"
                <| fun () ->
                    let identifiableString =
                        "UNIQUE_IDENTIFIER_"
                        + createLargeString 11_000_000

                    let json =
                        serializeToJsonWithConfig (fun writer config ->
                            serializeString writer identifiableString config)

                    match validateJson json with
                    | Ok doc ->
                        let obj = getJsonValue doc

                        let prefix =
                            obj.GetProperty("_prefix").GetString()

                        test <@ prefix.StartsWith("UNIQUE_IDENTIFIER_") @>
                        test <@ prefix.Length = 1000 @>
                    | Error msg -> failtest msg

                testCase "collection truncation preserves order"
                <| fun () ->
                    let items = ResizeArray<obj>()

                    for i in 1..15000 do
                        items.Add(box $"item_{i:D5}")

                    let buffer, writer, context =
                        createTestWriterContextWithLimits 10 1000 1000
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
                        // First item should be "item_00001"
                        let arr_0_str = arr[0].GetString()
                        test <@ arr_0_str = "item_00001" @>
                        // 999th item should be "item_00999"
                        let arr_998_str = arr[998].GetString()
                        test <@ arr_998_str = "item_00999" @>
                        // 1000th item should be "item_01000"
                        let arr_999_str = arr[999].GetString()
                        test <@ arr_999_str = "item_01000" @>
                        // Last should be truncation marker
                        let arr_1000_truncated_str =
                            arr[1000].GetProperty("_truncated").GetString()

                        test <@ arr_1000_truncated_str = "list_too_large" @>
                    | Error msg -> failtest msg ] ]
