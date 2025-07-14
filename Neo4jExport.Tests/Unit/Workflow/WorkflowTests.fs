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

module Neo4jExport.Tests.Unit.Workflow.WorkflowTests

open Expecto
open Expecto.Flip
open FsCheck
open Swensen.Unquote
open Neo4jExport
open Neo4jExport.Workflow
open Neo4jExport.Tests.Helpers.TestHelpers

[<Tests>]
let tests =
    testList
        "Workflow"
        [ testList
              "handleError"
              [
                // Individual error type tests
                testCase "ConfigError returns 6"
                <| fun () ->
                    let error = ConfigError "Test config"
                    let result = Workflow.handleError error
                    test <@ result = 6 @>

                testCase "ConnectionError returns 2"
                <| fun () ->
                    let error =
                        ConnectionError("Test connection", None)

                    let result = Workflow.handleError error
                    test <@ result = 2 @>

                testCase "AuthenticationError returns 6"
                <| fun () ->
                    let error = AuthenticationError "Test auth"
                    let result = Workflow.handleError error
                    test <@ result = 6 @>

                testCase "QueryError returns 7"
                <| fun () ->
                    let error =
                        QueryError("MATCH (n)", "Test error", None)

                    let result = Workflow.handleError error
                    test <@ result = 7 @>

                testCase "DataCorruptionError returns 5"
                <| fun () ->
                    let error =
                        DataCorruptionError(42, "Test data", None)

                    let result = Workflow.handleError error
                    test <@ result = 5 @>

                testCase "DiskSpaceError returns 3"
                <| fun () ->
                    let error = DiskSpaceError(10L, 5L)
                    let result = Workflow.handleError error
                    test <@ result = 3 @>

                testCase "MemoryError returns 3"
                <| fun () ->
                    let error = MemoryError "Test memory"
                    let result = Workflow.handleError error
                    test <@ result = 3 @>

                testCase "ExportError returns 5"
                <| fun () ->
                    let error = ExportError("Test export", None)
                    let result = Workflow.handleError error
                    test <@ result = 5 @>

                testCase "FileSystemError returns 3"
                <| fun () ->
                    let error =
                        FileSystemError("/tmp/test", "Test file", None)

                    let result = Workflow.handleError error
                    test <@ result = 3 @>

                testCase "SecurityError returns 6"
                <| fun () ->
                    let error = SecurityError "Test security"
                    let result = Workflow.handleError error
                    test <@ result = 6 @>

                testCase "TimeoutError returns 5"
                <| fun () ->
                    let error =
                        TimeoutError("Query", System.TimeSpan.FromSeconds(30.0))

                    let result = Workflow.handleError error
                    test <@ result = 5 @>

                testCase "PaginationError returns 7"
                <| fun () ->
                    let error =
                        PaginationError("Node", "Test pagination")

                    let result = Workflow.handleError error
                    test <@ result = 7 @>

                testCase "AggregateError returns 6"
                <| fun () ->
                    let errors =
                        NonEmptyList(ConfigError "Error 1", [ MemoryError "Error 2" ])

                    let error = AggregateError errors
                    let result = Workflow.handleError error
                    test <@ result = 6 @>

                // Property test - all errors return valid exit codes
                testPropertyWithConfig fsCheckConfig "all errors return exit codes between 1 and 10"
                <| fun (error: AppError) ->
                    let exitCode = Workflow.handleError error
                    exitCode >= 1 && exitCode <= 10 ] ]
