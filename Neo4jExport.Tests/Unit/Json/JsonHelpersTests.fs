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

module Neo4jExport.Tests.Unit.Json.JsonHelpersTests

open System
open System.IO
open System.Text
open System.Text.Json
open Expecto
open FsCheck
open Swensen.Unquote
open Neo4jExport
open Neo4jExport.Tests.Helpers.TestHelpers

[<Tests>]
let tests =
    testList
        "JsonHelpers"
        [ testList
              "toJsonValue"
              [ testCase "converts null to JNull"
                <| fun () ->
                    let result = JsonHelpers.toJsonValue null

                    match result with
                    | Ok JNull -> ()
                    | Ok other -> failtest $"Expected JNull but got {other}"
                    | Error msg -> failtest $"Expected Ok but got Error: {msg}"

                testCase "converts bool to JBool"
                <| fun () ->
                    let result =
                        JsonHelpers.toJsonValue (box true)

                    match result with
                    | Ok(JBool true) -> ()
                    | Ok other -> failtest $"Expected JBool true but got {other}"
                    | Error msg -> failtest $"Expected Ok but got Error: {msg}"

                testCase "converts int to JNumber"
                <| fun () ->
                    let result =
                        JsonHelpers.toJsonValue (box 42)

                    match result with
                    | Ok(JNumber value) -> test <@ value = 42m @>
                    | Ok other -> failtest $"Expected JNumber but got {other}"
                    | Error msg -> failtest $"Expected Ok but got Error: {msg}"

                testCase "converts int64 to JNumber"
                <| fun () ->
                    let result =
                        JsonHelpers.toJsonValue (box 123L)

                    match result with
                    | Ok(JNumber value) -> test <@ value = 123m @>
                    | Ok other -> failtest $"Expected JNumber but got {other}"
                    | Error msg -> failtest $"Expected Ok but got Error: {msg}"

                testCase "converts float to JNumber"
                <| fun () ->
                    let result =
                        JsonHelpers.toJsonValue (box 3.14)

                    match result with
                    | Ok(JNumber value) -> test <@ abs (float value - 3.14) < 0.001 @>
                    | Ok other -> failtest $"Expected JNumber but got {other}"
                    | Error msg -> failtest $"Expected Ok but got Error: {msg}"

                testCase "converts string to JString"
                <| fun () ->
                    let result =
                        JsonHelpers.toJsonValue (box "hello")

                    match result with
                    | Ok(JString "hello") -> ()
                    | Ok other -> failtest $"Expected JString but got {other}"
                    | Error msg -> failtest $"Expected Ok but got Error: {msg}"

                testCase "converts DateTime to ISO 8601 string"
                <| fun () ->
                    let dt =
                        DateTime(2023, 12, 25, 10, 30, 45, DateTimeKind.Utc)

                    let result =
                        JsonHelpers.toJsonValue (box dt)

                    match result with
                    | Ok(JString s) -> test <@ s = "2023-12-25T10:30:45.0000000Z" @>
                    | Ok other -> failtest $"Expected JString but got {other}"
                    | Error msg -> failtest $"Expected Ok but got Error: {msg}"

                testCase "converts array to JArray"
                <| fun () ->
                    let arr = [| box 1; box 2; box 3 |]

                    let result =
                        JsonHelpers.toJsonValue (box arr)

                    match result with
                    | Ok(JArray values) ->
                        test <@ values.Length = 3 @>

                        match values.[0] with
                        | JNumber n -> test <@ n = 1m @>
                        | _ -> failtest "Expected JNumber"
                    | Ok other -> failtest $"Expected JArray but got {other}"
                    | Error msg -> failtest $"Expected Ok but got Error: {msg}"

                testCase "converts Dictionary to JObject"
                <| fun () ->
                    let dict = dict [ "a", box 1; "b", box 2 ]

                    let result =
                        JsonHelpers.toJsonValue (box dict)

                    match result with
                    | Ok(JObject props) ->
                        match props.["a"] with
                        | JNumber n -> test <@ n = 1m @>
                        | _ -> failtest "Expected JNumber for key 'a'"
                    | Ok other -> failtest $"Expected JObject but got {other}"
                    | Error msg -> failtest $"Expected Ok but got Error: {msg}"

                testCase "returns error for unsupported type"
                <| fun () ->
                    let unsupported = box (new MemoryStream())

                    let result =
                        JsonHelpers.toJsonValue unsupported

                    test <@ Result.isError result @>

                    match result with
                    | Error msg -> test <@ msg.Contains("Unsupported type") @>
                    | _ -> failtest "Expected Error" ]

          testList
              "toJsonValueWithDefault"
              [ testCase "returns value for supported type"
                <| fun () ->
                    let logWarning = fun _ -> () // dummy warning logger

                    let result =
                        JsonHelpers.toJsonValueWithDefault (JString "default") logWarning (box 42)

                    match result with
                    | JNumber n -> test <@ n = 42m @>
                    | other -> failtest $"Expected JNumber but got {other}"

                testCase "returns default for unsupported type"
                <| fun () ->
                    let logWarning = fun _ -> ()
                    let unsupported = box (new MemoryStream())

                    let result =
                        JsonHelpers.toJsonValueWithDefault (JString "fallback") logWarning unsupported

                    match result with
                    | JString "fallback" -> ()
                    | other -> failtest $"Expected JString 'fallback' but got {other}"

                testCase "returns default on exception"
                <| fun () ->
                    let mutable warningLogged = false

                    let logWarning =
                        fun _ -> warningLogged <- true
                    // Non-zero based arrays are actually handled by toJsonValue, converting to JArray
                    // Let's create a truly unsupported type
                    let badValue =
                        box (System.Reflection.Assembly.GetExecutingAssembly()) // Assembly type is not supported

                    let result =
                        JsonHelpers.toJsonValueWithDefault (JString "error-default") logWarning badValue

                    match result with
                    | JString "error-default" -> test <@ warningLogged = true @> // Verify warning was logged
                    | other -> failtest $"Expected JString 'error-default' but got {other}" ]

          testList
              "tryGetString"
              [ testCase "extracts string from JString"
                <| fun () ->
                    let json = JString "test"
                    let result = JsonHelpers.tryGetString json

                    match result with
                    | Ok value -> test <@ value = "test" @>
                    | Error msg -> failtest $"Expected Ok but got Error: {msg}"

                testCase "returns Error for non-string"
                <| fun () ->
                    let json = JNumber 42m
                    let result = JsonHelpers.tryGetString json
                    test <@ Result.isError result @> ]

          testList
              "tryGetInt64"
              [ testCase "extracts int64 from JNumber"
                <| fun () ->
                    let json = JNumber 123m
                    let result = JsonHelpers.tryGetInt64 json

                    match result with
                    | Ok value -> test <@ value = 123L @>
                    | Error msg -> failtest $"Expected Ok but got Error: {msg}"

                testCase "rounds decimal to nearest int64"
                <| fun () ->
                    let json = JNumber 123.7m
                    let result = JsonHelpers.tryGetInt64 json

                    match result with
                    | Ok value -> test <@ value = 124L @>
                    | Error msg -> failtest $"Expected Ok but got Error: {msg}"

                testCase "returns Error for non-numeric"
                <| fun () ->
                    let json = JString "not a number"
                    let result = JsonHelpers.tryGetInt64 json
                    test <@ Result.isError result @> ]

          testList
              "fromJsonValue"
              [ testCase "converts JNull to null"
                <| fun () ->
                    let result = JsonHelpers.fromJsonValue JNull
                    test <@ result = null @>

                testCase "converts JBool to bool"
                <| fun () ->
                    let result =
                        JsonHelpers.fromJsonValue (JBool true)

                    test <@ unbox result = true @>

                testCase "converts JNumber to decimal"
                <| fun () ->
                    let result =
                        JsonHelpers.fromJsonValue (JNumber 42m)

                    test <@ unbox<decimal> result = 42m @>

                testCase "converts JString to string"
                <| fun () ->
                    let result =
                        JsonHelpers.fromJsonValue (JString "test")

                    test <@ unbox result = "test" @>

                testCase "converts JArray to obj array"
                <| fun () ->
                    let json = JArray [ JNumber 1m; JNumber 2m ]
                    let result = JsonHelpers.fromJsonValue json

                    match result with
                    | :? (obj[]) as arr ->
                        test <@ arr.Length = 2 @>
                        test <@ unbox<decimal> arr.[0] = 1m @>
                    | _ -> failtest "Expected obj[]"

                testCase "converts JObject to dictionary"
                <| fun () ->
                    let json = JObject(dict [ "a", JNumber 1m ])
                    let result = JsonHelpers.fromJsonValue json

                    match result with
                    | :? System.Collections.Generic.IDictionary<string, obj> as dict ->
                        test <@ dict.["a"] |> unbox<decimal> = 1m @>
                    | _ -> failtest "Expected IDictionary" ]

          testList
              "writeJsonValue"
              [ testCase "writes null correctly"
                <| fun () ->
                    use stream = new MemoryStream()
                    use writer = new Utf8JsonWriter(stream)
                    writer.WriteStartObject()
                    writer.WritePropertyName("test")
                    JsonHelpers.writeJsonValue writer JNull
                    writer.WriteEndObject()
                    writer.Flush()

                    let json =
                        Encoding.UTF8.GetString(stream.ToArray())

                    test <@ json = """{"test":null}""" @>

                testCase "writes nested structure correctly"
                <| fun () ->
                    use stream = new MemoryStream()
                    use writer = new Utf8JsonWriter(stream)

                    let value =
                        JObject(
                            dict
                                [ "name", JString "test"
                                  "count", JNumber 42m
                                  "items", JArray [ JNumber 1m; JNumber 2m ] ]
                        )

                    writer.WriteStartObject()
                    writer.WritePropertyName("data")
                    JsonHelpers.writeJsonValue writer value
                    writer.WriteEndObject()
                    writer.Flush()

                    let json =
                        Encoding.UTF8.GetString(stream.ToArray())

                    use doc = JsonDocument.Parse(json)

                    let doc_rootelement__data__name_str =
                        doc.RootElement.GetProperty("data").GetProperty("name").GetString()

                    test <@ doc_rootelement__data__name_str = "test" @>

                    let doc_rootelement__data__count_int =
                        doc.RootElement.GetProperty("data").GetProperty("count").GetInt32()

                    test <@ doc_rootelement__data__count_int = 42 @> ]

          testList
              "roundtrip properties"
              [ testPropertyWithConfig fsCheckConfig "toJsonValue -> fromJsonValue preserves primitives"
                <| fun (value: Choice<bool, decimal, string>) ->
                    let original =
                        match value with
                        | Choice1Of3 b -> box b
                        | Choice2Of3 d -> box d
                        | Choice3Of3 s -> if isNull s then box "" else box s // handle null strings

                    let jsonResult =
                        JsonHelpers.toJsonValue original

                    match jsonResult with
                    | Ok json ->
                        let roundtripped =
                            JsonHelpers.fromJsonValue json

                        match value with
                        | Choice1Of3 b -> unbox<bool> roundtripped = b
                        | Choice2Of3 d -> unbox<decimal> roundtripped = d
                        | Choice3Of3 s ->
                            let expected = if isNull s then "" else s
                            unbox<string> roundtripped = expected
                    | Error _ -> false ] ] // conversion failed
