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

module Neo4jExport.Tests.Unit.Configuration.FieldValidatorsTests

open System
open Expecto
open FsCheck
open Swensen.Unquote
open Neo4jExport
open Neo4jExport.Tests.Helpers.TestHelpers

// Import the module containing validation functions
open Neo4jExport.ConfigurationValidationHelpers
open Neo4jExport.ConfigurationFieldValidators

[<Tests>]
let tests =
    testList
        "FieldValidators"
        [ testList
              "validateUri"
              [ testCase "accepts bolt:// URI"
                <| fun () ->
                    let result =
                        validateUri "bolt://localhost:7687"

                    match result with
                    | Ok(VUri uri) -> test <@ uri = Uri("bolt://localhost:7687") @>
                    | Ok other -> failtest $"Expected VUri but got {other}"
                    | Error msg -> failtest $"Expected Ok but got Error: {msg}"

                testCase "accepts neo4j:// URI"
                <| fun () ->
                    let result =
                        validateUri "neo4j://localhost:7687"

                    test <@ Result.isOk result @>

                testCase "accepts bolt+s:// URI"
                <| fun () ->
                    let result =
                        validateUri "bolt+s://secure.host:7687"

                    test <@ Result.isOk result @>

                testCase "accepts neo4j+s:// URI"
                <| fun () ->
                    let result =
                        validateUri "neo4j+s://secure.host:7687"

                    test <@ Result.isOk result @>

                testCase "rejects http:// URI"
                <| fun () ->
                    let result =
                        validateUri "http://localhost:7687"

                    test <@ Result.isError result @>

                    match result with
                    | Error msg -> test <@ msg.Contains("scheme") @>
                    | _ -> failtest "Expected Error"

                testCase "rejects empty string"
                <| fun () ->
                    let result = validateUri ""
                    test <@ Result.isError result @>

                testCase "rejects invalid URI format"
                <| fun () ->
                    let result = validateUri "not a uri"
                    test <@ Result.isError result @> ]

          testList
              "validateInt"
              [ testCase "accepts value within range"
                <| fun () ->
                    let result =
                        validateInt "test" (Some 1) (Some 100) "50"

                    match result with
                    | Ok(VInt value) -> test <@ value = 50 @>
                    | Ok other -> failtest $"Expected VInt but got {other}"
                    | Error msg -> failtest $"Expected Ok but got Error: {msg}"

                testCase "accepts minimum value"
                <| fun () ->
                    let result =
                        validateInt "test" (Some 1) (Some 100) "1"

                    match result with
                    | Ok(VInt value) -> test <@ value = 1 @>
                    | Ok other -> failtest $"Expected VInt but got {other}"
                    | Error msg -> failtest $"Expected Ok but got Error: {msg}"

                testCase "accepts maximum value"
                <| fun () ->
                    let result =
                        validateInt "test" (Some 1) (Some 100) "100"

                    match result with
                    | Ok(VInt value) -> test <@ value = 100 @>
                    | Ok other -> failtest $"Expected VInt but got {other}"
                    | Error msg -> failtest $"Expected Ok but got Error: {msg}"

                testCase "rejects value below minimum"
                <| fun () ->
                    let result =
                        validateInt "test" (Some 1) (Some 100) "0"

                    test <@ Result.isError result @>

                    match result with
                    | Error msg -> test <@ msg.Contains("must be at least 1") @>
                    | _ -> failtest "Expected Error"

                testCase "rejects value above maximum"
                <| fun () ->
                    let result =
                        validateInt "test" (Some 1) (Some 100) "101"

                    test <@ Result.isError result @>

                testCase "rejects non-numeric string"
                <| fun () ->
                    let result =
                        validateInt "test" (Some 1) (Some 100) "abc"

                    test <@ Result.isError result @>

                testCase "rejects empty string"
                <| fun () ->
                    let result =
                        validateInt "test" (Some 1) (Some 100) ""

                    test <@ Result.isError result @>

                testCase "accepts any value when no bounds"
                <| fun () ->
                    let result =
                        validateInt "test" None None "999999"

                    match result with
                    | Ok(VInt value) -> test <@ value = 999999 @>
                    | Ok other -> failtest $"Expected VInt but got {other}"
                    | Error msg -> failtest $"Expected Ok but got Error: {msg}"

                // Property test for valid ranges
                testPropertyWithConfig fsCheckConfig "validates correctly within range"
                <| fun (value: int) ->
                    let min = 1
                    let max = 1000

                    let clampedValue =
                        Math.Max(min, Math.Min(max, value))

                    let result =
                        validateInt "test" (Some min) (Some max) (string clampedValue)

                    match result with
                    | Ok(VInt v) -> v = clampedValue
                    | Ok _ -> false
                    | Error _ -> false ]

          testList
              "validateInt64"
              [ testCase "accepts large int64 value"
                <| fun () ->
                    let largeValue = 1_000_000_000L

                    let result =
                        validateInt64 "test" (Some 0L) (Some 2_000_000_000L) (string largeValue)

                    match result with
                    | Ok(VInt64 value) -> test <@ value = largeValue @>
                    | Ok other -> failtest $"Expected VInt64 but got {other}"
                    | Error msg -> failtest $"Expected Ok but got Error: {msg}"

                testCase "handles int64 boundaries"
                <| fun () ->
                    let result =
                        validateInt64 "test" (Some 0L) (Some Int64.MaxValue) "9223372036854775807"

                    match result with
                    | Ok(VInt64 value) -> test <@ value = Int64.MaxValue @>
                    | Ok other -> failtest $"Expected VInt64 but got {other}"
                    | Error msg -> failtest $"Expected Ok but got Error: {msg}" ]

          testList
              "validateBool"
              [
                // Test all accepted formats
                testCase "accepts 'true'"
                <| fun () ->
                    match validateBool "test" "true" with
                    | Ok(VBool value) -> test <@ value = true @>
                    | Ok other -> failtest $"Expected VBool but got {other}"
                    | Error msg -> failtest $"Expected Ok but got Error: {msg}"

                testCase "accepts 'TRUE' (case insensitive)"
                <| fun () ->
                    match validateBool "test" "TRUE" with
                    | Ok(VBool value) -> test <@ value = true @>
                    | Ok other -> failtest $"Expected VBool but got {other}"
                    | Error msg -> failtest $"Expected Ok but got Error: {msg}"

                testCase "accepts 'false'"
                <| fun () ->
                    match validateBool "test" "false" with
                    | Ok(VBool value) -> test <@ value = false @>
                    | Ok other -> failtest $"Expected VBool but got {other}"
                    | Error msg -> failtest $"Expected Ok but got Error: {msg}"

                testCase "accepts 'yes'"
                <| fun () ->
                    match validateBool "test" "yes" with
                    | Ok(VBool value) -> test <@ value = true @>
                    | Ok other -> failtest $"Expected VBool but got {other}"
                    | Error msg -> failtest $"Expected Ok but got Error: {msg}"

                testCase "accepts 'no'"
                <| fun () ->
                    match validateBool "test" "no" with
                    | Ok(VBool value) -> test <@ value = false @>
                    | Ok other -> failtest $"Expected VBool but got {other}"
                    | Error msg -> failtest $"Expected Ok but got Error: {msg}"

                testCase "accepts '1'"
                <| fun () ->
                    match validateBool "test" "1" with
                    | Ok(VBool value) -> test <@ value = true @>
                    | Ok other -> failtest $"Expected VBool but got {other}"
                    | Error msg -> failtest $"Expected Ok but got Error: {msg}"

                testCase "accepts '0'"
                <| fun () ->
                    match validateBool "test" "0" with
                    | Ok(VBool value) -> test <@ value = false @>
                    | Ok other -> failtest $"Expected VBool but got {other}"
                    | Error msg -> failtest $"Expected Ok but got Error: {msg}"

                testCase "rejects invalid string"
                <| fun () ->
                    let result = validateBool "test" "maybe"
                    test <@ Result.isError result @>

                    match result with
                    | Error msg ->
                        test <@ msg.Contains("valid boolean") @>
                        test <@ msg.Contains("true/false, yes/no, 1/0") @>
                    | _ -> failtest "Expected Error" ]

          testList
              "validateOutputDirectory"
              [ testCase "creates directory if it doesn't exist"
                <| fun () ->
                    let tempPath =
                        System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString())

                    try
                        let result =
                            validateOutputDirectory tempPath

                        match result with
                        | Ok(VString path) ->
                            test <@ path = tempPath @>
                            test <@ System.IO.Directory.Exists(tempPath) @>
                        | Ok other -> failtest $"Expected VString but got {other}"
                        | Error msg -> failtest $"Expected Ok but got Error: {msg}"
                    finally
                        if System.IO.Directory.Exists(tempPath) then
                            System.IO.Directory.Delete(tempPath)

                testCase "accepts existing directory"
                <| fun () ->
                    let tempPath = System.IO.Path.GetTempPath()

                    let result =
                        validateOutputDirectory tempPath

                    match result with
                    | Ok(VString path) -> test <@ path = tempPath @>
                    | Ok other -> failtest $"Expected VString but got {other}"
                    | Error msg -> failtest $"Expected Ok but got Error: {msg}" ]

          testList
              "validateJsonBufferSize"
              [ testCase "accepts valid buffer size"
                <| fun () ->
                    let result = validateJsonBufferSize "64"

                    match result with
                    | Ok(VInt value) -> test <@ value = 64 @>
                    | Ok other -> failtest $"Expected VInt but got {other}"
                    | Error msg -> failtest $"Expected Ok but got Error: {msg}"

                testCase "accepts minimum size (1)"
                <| fun () ->
                    let result = validateJsonBufferSize "1"

                    match result with
                    | Ok(VInt value) -> test <@ value = 1 @>
                    | Ok other -> failtest $"Expected VInt but got {other}"
                    | Error msg -> failtest $"Expected Ok but got Error: {msg}"

                testCase "accepts maximum size (1024)"
                <| fun () ->
                    let result = validateJsonBufferSize "1024"

                    match result with
                    | Ok(VInt value) -> test <@ value = 1024 @>
                    | Ok other -> failtest $"Expected VInt but got {other}"
                    | Error msg -> failtest $"Expected Ok but got Error: {msg}"

                testCase "rejects size below 1"
                <| fun () ->
                    let result = validateJsonBufferSize "0"
                    test <@ Result.isError result @>

                testCase "rejects size above 1024"
                <| fun () ->
                    let result = validateJsonBufferSize "2048"
                    test <@ Result.isError result @> ] ]
