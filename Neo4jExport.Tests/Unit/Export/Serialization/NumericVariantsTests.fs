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

module Neo4jExport.Tests.Unit.Export.Serialization.NumericVariantsTests

open System
open Expecto
open Swensen.Unquote
open Neo4jExport
open Neo4jExport.SerializationPrimitives
open Neo4jExport.Tests.Helpers.TestHelpers

[<Tests>]
let tests =
    testList
        "Serialization - Numeric Variants"
        [

          testList
              "Signed integer variants"
              [ testCase "serializes int16"
                <| fun () ->
                    let json =
                        serializeToJson (fun writer -> serializeNumeric writer (box 32767s))

                    assertJsonValue "32767" json

                testCase "serializes int16 min/max"
                <| fun () ->
                    let json1 =
                        serializeToJson (fun writer -> serializeNumeric writer (box Int16.MinValue))

                    let json2 =
                        serializeToJson (fun writer -> serializeNumeric writer (box Int16.MaxValue))

                    assertJsonValue "-32768" json1
                    assertJsonValue "32767" json2

                testCase "serializes int32"
                <| fun () ->
                    let json =
                        serializeToJson (fun writer -> serializeNumeric writer (box 2147483647))

                    assertJsonValue "2147483647" json

                testCase "serializes int32 min/max"
                <| fun () ->
                    let json1 =
                        serializeToJson (fun writer -> serializeNumeric writer (box Int32.MinValue))

                    let json2 =
                        serializeToJson (fun writer -> serializeNumeric writer (box Int32.MaxValue))

                    assertJsonValue "-2147483648" json1
                    assertJsonValue "2147483647" json2

                testCase "serializes sbyte"
                <| fun () ->
                    let json =
                        serializeToJson (fun writer -> serializeNumeric writer (box -128y))

                    assertJsonValue "-128" json

                testCase "serializes sbyte min/max"
                <| fun () ->
                    let json1 =
                        serializeToJson (fun writer -> serializeNumeric writer (box SByte.MinValue))

                    let json2 =
                        serializeToJson (fun writer -> serializeNumeric writer (box SByte.MaxValue))

                    assertJsonValue "-128" json1
                    assertJsonValue "127" json2 ]

          testList
              "Unsigned integer variants"
              [ testCase "serializes uint16"
                <| fun () ->
                    let json =
                        serializeToJson (fun writer -> serializeNumeric writer (box 65535us))

                    assertJsonValue "65535" json

                testCase "serializes uint16 min/max"
                <| fun () ->
                    let json1 =
                        serializeToJson (fun writer -> serializeNumeric writer (box UInt16.MinValue))

                    let json2 =
                        serializeToJson (fun writer -> serializeNumeric writer (box UInt16.MaxValue))

                    assertJsonValue "0" json1
                    assertJsonValue "65535" json2

                testCase "serializes uint32"
                <| fun () ->
                    let json =
                        serializeToJson (fun writer -> serializeNumeric writer (box 4294967295u))

                    assertJsonValue "4294967295" json

                testCase "serializes uint32 min/max"
                <| fun () ->
                    let json1 =
                        serializeToJson (fun writer -> serializeNumeric writer (box UInt32.MinValue))

                    let json2 =
                        serializeToJson (fun writer -> serializeNumeric writer (box UInt32.MaxValue))

                    assertJsonValue "0" json1
                    assertJsonValue "4294967295" json2

                testCase "serializes uint64"
                <| fun () ->
                    let json =
                        serializeToJson (fun writer -> serializeNumeric writer (box 18446744073709551615UL))

                    assertJsonValue "18446744073709551615" json

                testCase "serializes uint64 min/max"
                <| fun () ->
                    let json1 =
                        serializeToJson (fun writer -> serializeNumeric writer (box UInt64.MinValue))

                    let json2 =
                        serializeToJson (fun writer -> serializeNumeric writer (box UInt64.MaxValue))

                    assertJsonValue "0" json1
                    assertJsonValue "18446744073709551615" json2

                testCase "serializes byte"
                <| fun () ->
                    let json =
                        serializeToJson (fun writer -> serializeNumeric writer (box 255uy))

                    assertJsonValue "255" json

                testCase "serializes byte min/max"
                <| fun () ->
                    let json1 =
                        serializeToJson (fun writer -> serializeNumeric writer (box Byte.MinValue))

                    let json2 =
                        serializeToJson (fun writer -> serializeNumeric writer (box Byte.MaxValue))

                    assertJsonValue "0" json1
                    assertJsonValue "255" json2 ]

          testList
              "Float type variants"
              [ testCase "serializes float32"
                <| fun () ->
                    let json =
                        serializeToJson (fun writer -> serializeNumeric writer (box 3.14159f))

                    match validateJson json with
                    | Ok doc ->
                        let value = getJsonValue doc
                        let actual = float (value.GetSingle())
                        let diff = Math.Abs(actual - 3.14159)
                        test <@ diff < 0.00001 @>
                    | Error msg -> failtest msg

                testCase "serializes float32 special values"
                <| fun () ->
                    let jsonNaN =
                        serializeToJson (fun writer -> serializeSpecialFloat32 writer Single.NaN)

                    let jsonPosInf =
                        serializeToJson (fun writer -> serializeSpecialFloat32 writer Single.PositiveInfinity)

                    let jsonNegInf =
                        serializeToJson (fun writer -> serializeSpecialFloat32 writer Single.NegativeInfinity)

                    assertJsonValue "\"NaN\"" jsonNaN
                    assertJsonValue "\"Infinity\"" jsonPosInf
                    assertJsonValue "\"-Infinity\"" jsonNegInf

                testCase "serializes decimal"
                <| fun () ->
                    let json =
                        serializeToJson (fun writer -> serializeNumeric writer (box 123.456789012345678901234567890m))

                    match validateJson json with
                    | Ok doc ->
                        let value = getJsonValue doc
                        let actual = value.GetDecimal()
                        test <@ actual = 123.456789012345678901234567890m @>
                    | Error msg -> failtest msg

                testCase "serializes decimal min/max"
                <| fun () ->
                    let json1 =
                        serializeToJson (fun writer -> serializeNumeric writer (box Decimal.MinValue))

                    let json2 =
                        serializeToJson (fun writer -> serializeNumeric writer (box Decimal.MaxValue))

                    match validateJson json1 with
                    | Ok doc ->
                        let value = getJsonValue doc
                        let actual = value.GetDecimal()
                        test <@ actual = Decimal.MinValue @>
                    | Error msg -> failtest msg

                    match validateJson json2 with
                    | Ok doc ->
                        let value = getJsonValue doc
                        let actual = value.GetDecimal()
                        test <@ actual = Decimal.MaxValue @>
                    | Error msg -> failtest msg ] ]
