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

module Neo4jExport.Tests.Unit.Export.Serialization.PrimitivesTests

open System
open Expecto
open Swensen.Unquote
open Neo4jExport
open Neo4jExport.SerializationPrimitives
open Neo4jExport.Tests.Helpers.TestHelpers
open System.Text.Json

[<Tests>]
let tests =
    testList
        "Serialization - Primitives"
        [ testCase "serializes null value"
          <| fun () ->
              let json =
                  serializeToJson (fun writer -> serializeNull writer)

              assertJsonValue "null" json

          testList
              "Boolean serialization"
              [ testCase "serializes true"
                <| fun () ->
                    let json =
                        serializeToJson (fun writer -> serializeBoolean writer true)

                    assertJsonValue "true" json

                testCase "serializes false"
                <| fun () ->
                    let json =
                        serializeToJson (fun writer -> serializeBoolean writer false)

                    assertJsonValue "false" json ]

          testList
              "Integer serialization"
              [ testCase "serializes positive int64"
                <| fun () ->
                    let json =
                        serializeToJson (fun writer -> serializeNumeric writer (box 42L))

                    assertJsonValue "42" json

                testCase "serializes negative int64"
                <| fun () ->
                    let json =
                        serializeToJson (fun writer -> serializeNumeric writer (box -42L))

                    assertJsonValue "-42" json

                testCase "serializes int64 max value"
                <| fun () ->
                    let json =
                        serializeToJson (fun writer -> serializeNumeric writer (box Int64.MaxValue))

                    assertJsonValue "9223372036854775807" json

                testCase "serializes int64 min value"
                <| fun () ->
                    let json =
                        serializeToJson (fun writer -> serializeNumeric writer (box Int64.MinValue))

                    assertJsonValue "-9223372036854775808" json

                testCase "serializes zero"
                <| fun () ->
                    let json =
                        serializeToJson (fun writer -> serializeNumeric writer (box 0L))

                    assertJsonValue "0" json ]

          testList
              "Float serialization"
              [ testCase "serializes positive float"
                <| fun () ->
                    let json =
                        serializeToJson (fun writer -> serializeNumeric writer (box 3.14159))

                    match validateJson json with
                    | Ok doc ->
                        let value = getJsonValue doc
                        let actual = value.GetDouble()
                        test <@ actual = 3.14159 @>
                    | Error msg -> failtest msg

                testCase "serializes negative float"
                <| fun () ->
                    let json =
                        serializeToJson (fun writer -> serializeNumeric writer (box -2.71828))

                    match validateJson json with
                    | Ok doc ->
                        let value = getJsonValue doc
                        let actual = value.GetDouble()
                        test <@ actual = -2.71828 @>
                    | Error msg -> failtest msg

                testCase "serializes scientific notation"
                <| fun () ->
                    let json =
                        serializeToJson (fun writer -> serializeNumeric writer (box 1.23e-10))

                    match validateJson json with
                    | Ok doc ->
                        let value = getJsonValue doc
                        let actual = value.GetDouble()
                        let diff = abs (actual - 1.23e-10)
                        test <@ diff < 1e-15 @>
                    | Error msg -> failtest msg

                testCase "serializes NaN as string"
                <| fun () ->
                    let json =
                        serializeToJson (fun writer -> serializeSpecialFloat writer Double.NaN)

                    assertJsonValue "\"NaN\"" json

                testCase "serializes positive infinity as string"
                <| fun () ->
                    let json =
                        serializeToJson (fun writer -> serializeSpecialFloat writer Double.PositiveInfinity)

                    assertJsonValue "\"Infinity\"" json

                testCase "serializes negative infinity as string"
                <| fun () ->
                    let json =
                        serializeToJson (fun writer -> serializeSpecialFloat writer Double.NegativeInfinity)

                    assertJsonValue "\"-Infinity\"" json ]

          testList
              "String serialization"
              [ testCase "serializes simple string"
                <| fun () ->
                    let json =
                        serializeToJsonWithConfig (fun writer config -> serializeString writer "hello" config)

                    assertJsonValue "\"hello\"" json

                testCase "serializes empty string"
                <| fun () ->
                    let json =
                        serializeToJsonWithConfig (fun writer config -> serializeString writer "" config)

                    assertJsonValue "\"\"" json

                testCase "serializes string with quotes"
                <| fun () ->
                    let json =
                        serializeToJsonWithConfig (fun writer config -> serializeString writer "say \"hello\"" config)

                    match validateJson json with
                    | Ok doc ->
                        let value = getJsonValue doc
                        let actual = value.GetString()
                        test <@ actual = "say \"hello\"" @>
                    | Error msg -> failtest msg

                testCase "serializes string with newlines"
                <| fun () ->
                    let json =
                        serializeToJsonWithConfig (fun writer config ->
                            serializeString writer "line1\nline2\r\nline3" config)

                    match validateJson json with
                    | Ok doc ->
                        let value = getJsonValue doc
                        let actual = value.GetString()
                        test <@ actual = "line1\nline2\r\nline3" @>
                    | Error msg -> failtest msg

                testCase "serializes unicode string"
                <| fun () ->
                    let json =
                        serializeToJsonWithConfig (fun writer config -> serializeString writer "Hello 世界 🌍" config)

                    match validateJson json with
                    | Ok doc ->
                        let value = getJsonValue doc
                        let actual = value.GetString()
                        test <@ actual = "Hello 世界 🌍" @>
                    | Error msg -> failtest msg

                testCase "serializes string with control characters"
                <| fun () ->
                    let json =
                        serializeToJsonWithConfig (fun writer config -> serializeString writer "\t\b\f" config)

                    match validateJson json with
                    | Ok doc ->
                        let value = getJsonValue doc
                        let actual = value.GetString()
                        test <@ actual = "\t\b\f" @>
                    | Error msg -> failtest msg ]

          testList
              "Byte array serialization"
              [ testCase "serializes byte array as base64"
                <| fun () ->
                    let bytes = [| 1uy; 2uy; 3uy; 255uy |]

                    let json =
                        serializeToJsonWithConfig (fun writer config -> serializeBinary writer bytes config)

                    match validateJson json with
                    | Ok doc ->
                        let value = getJsonValue doc

                        let decoded =
                            Convert.FromBase64String(value.GetString())

                        test <@ decoded = bytes @>
                    | Error msg -> failtest msg

                testCase "serializes empty byte array"
                <| fun () ->
                    let bytes = [||]

                    let json =
                        serializeToJsonWithConfig (fun writer config -> serializeBinary writer bytes config)

                    match validateJson json with
                    | Ok doc ->
                        let value = getJsonValue doc
                        let actual = value.GetString()
                        test <@ actual = "" @>
                    | Error msg -> failtest msg

                testCase "serializes large byte array"
                <| fun () ->
                    let bytes =
                        Array.init 1000 (fun i -> byte (i % 256))

                    let json =
                        serializeToJsonWithConfig (fun writer config -> serializeBinary writer bytes config)

                    match validateJson json with
                    | Ok doc ->
                        let value = getJsonValue doc

                        let decoded =
                            Convert.FromBase64String(value.GetString())

                        test <@ decoded = bytes @>
                    | Error msg -> failtest msg ] ]
