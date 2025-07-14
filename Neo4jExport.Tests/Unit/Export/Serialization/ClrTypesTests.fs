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

module Neo4jExport.Tests.Unit.Export.Serialization.ClrTypesTests

open System
open Expecto
open Swensen.Unquote
open Neo4jExport
open Neo4jExport.SerializationTemporal
open Neo4jExport.Tests.Helpers.TestHelpers

[<Tests>]
let tests =
    testList
        "Serialization - CLR Types"
        [

          testList
              ".NET DateTime serialization"
              [ testCase "serializes DateTime in ISO 8601 format"
                <| fun () ->
                    let dt =
                        DateTime(2024, 1, 15, 14, 30, 15, 123, DateTimeKind.Utc).AddTicks(4567L)

                    let json =
                        serializeToJson (fun writer -> serializeDateTime writer dt)

                    match validateJson json with
                    | Ok doc ->
                        let value = getJsonValue doc
                        let actual = value.GetString()
                        test <@ actual = "2024-01-15T14:30:15.1234567Z" @>
                    | Error msg -> failtest msg

                testCase "serializes DateTime with local timezone"
                <| fun () ->
                    let dt =
                        DateTime(2024, 6, 15, 9, 0, 0, DateTimeKind.Local)

                    let json =
                        serializeToJson (fun writer -> serializeDateTime writer dt)

                    match validateJson json with
                    | Ok doc ->
                        let value = getJsonValue doc
                        let actual = value.GetString()
                        // Should contain timezone offset
                        test <@ actual.Contains("2024-06-15T09:00:00") @>
                        test <@ actual.Contains("+") || actual.Contains("-") @>
                    | Error msg -> failtest msg

                testCase "serializes DateTime unspecified as local"
                <| fun () ->
                    let dt =
                        DateTime(2024, 3, 10, 12, 0, 0, DateTimeKind.Unspecified)

                    let json =
                        serializeToJson (fun writer -> serializeDateTime writer dt)

                    match validateJson json with
                    | Ok doc ->
                        let value = getJsonValue doc
                        let actual = value.GetString()
                        test <@ actual.Contains("2024-03-10T12:00:00") @>
                    | Error msg -> failtest msg ]

          testList
              ".NET DateTimeOffset serialization"
              [ testCase "serializes DateTimeOffset with positive offset"
                <| fun () ->
                    let dto =
                        DateTimeOffset(2024, 1, 15, 14, 30, 15, 123, TimeSpan.FromHours(2.0)).AddTicks(4567L)

                    let json =
                        serializeToJson (fun writer -> serializeDateTimeOffset writer dto)

                    match validateJson json with
                    | Ok doc ->
                        let value = getJsonValue doc
                        let actual = value.GetString()
                        test <@ actual = "2024-01-15T14:30:15.1234567+02:00" @>
                    | Error msg -> failtest msg

                testCase "serializes DateTimeOffset with negative offset"
                <| fun () ->
                    let dto =
                        DateTimeOffset(2024, 6, 15, 9, 0, 0, TimeSpan.FromHours(-5.0))

                    let json =
                        serializeToJson (fun writer -> serializeDateTimeOffset writer dto)

                    match validateJson json with
                    | Ok doc ->
                        let value = getJsonValue doc
                        let actual = value.GetString()
                        test <@ actual.Contains("2024-06-15T09:00:00") @>
                        test <@ actual.Contains("-05:00") @>
                    | Error msg -> failtest msg

                testCase "serializes DateTimeOffset with UTC"
                <| fun () ->
                    let dto =
                        DateTimeOffset(2024, 12, 31, 23, 59, 59, 999, TimeSpan.Zero)

                    let json =
                        serializeToJson (fun writer -> serializeDateTimeOffset writer dto)

                    match validateJson json with
                    | Ok doc ->
                        let value = getJsonValue doc
                        let actual = value.GetString()
                        test <@ actual.Contains("2024-12-31T23:59:59.999") @>
                        test <@ actual.EndsWith("+00:00") || actual.EndsWith("Z") @>
                    | Error msg -> failtest msg

                testCase "preserves 7 decimal places precision"
                <| fun () ->
                    let dto =
                        DateTimeOffset(DateTime(2024, 1, 1).Ticks + 1234567L, TimeSpan.Zero)

                    let json =
                        serializeToJson (fun writer -> serializeDateTimeOffset writer dto)

                    match validateJson json with
                    | Ok doc ->
                        let value = getJsonValue doc
                        let actual = value.GetString()
                        test <@ actual.Contains(".1234567") @>
                    | Error msg -> failtest msg ] ]
