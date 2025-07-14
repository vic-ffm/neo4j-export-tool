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

module Neo4jExport.Tests.Properties.ErrorDeduplicationProperties

open System
open System.Collections.Generic
open Expecto
open FsCheck
open Swensen.Unquote
open Neo4jExport
open Neo4jExport.ErrorDeduplication
open Neo4jExport.ErrorAccumulation
open Neo4jExport.Tests.Helpers.Generators
open Neo4jExport.Tests.Helpers.TestHelpers

// Helper to create realistic ErrorInfo from AppError
let private createErrorInfo (error: AppError) (entityType: string) : ErrorInfo =
    let errorType = error.GetType().Name
    let message = appErrorToString error // Uses the actual conversion function

    { ExceptionType = errorType
      Message = message
      EntityType = entityType
      ErrorType = "error" }

// Helper to create exception from AppError for key generation
let private appErrorToException (error: AppError) : exn = Exception(appErrorToString error)

[<Tests>]
let tests =
    testList
        "Error Deduplication Properties"
        [

          // 1. Bounded Memory Property
          testPropertyWithConfig fsCheckConfig "accumulator never stores more than 5 samples per error"
          <| fun (baseError: AppError) (elementIds: string list) ->
              let acc = createAccumulator 100

              // Add same error many times with different element IDs
              elementIds
              |> List.iter (fun elemId ->
                  let ex = appErrorToException baseError
                  let key = generateErrorKey ex

                  let errorInfo =
                      createErrorInfo baseError "node"

                  accumulateError acc key errorInfo elemId)

              // Check that we have at most 5 samples
              let errors = acc.Errors |> Seq.toList

              match errors with
              | [] -> true
              | kvp :: _ ->
                  let (_, stats) = kvp.Value
                  stats.SampleCount <= 5

          // 2. Deterministic Key Generation
          testPropertyWithConfig fsCheckConfig "same error always generates same key"
          <| fun (error: AppError) ->
              let ex = appErrorToException error
              let key1 = generateErrorKey ex
              let key2 = generateErrorKey ex
              key1 = key2

          // 3. Count Accuracy Property
          testPropertyWithConfig fsCheckConfig "error count is accurate"
          <| fun (errors: (AppError * string) list) ->
              let acc = createAccumulator 100

              errors
              |> List.iter (fun (error, elemId) ->
                  let ex = appErrorToException error
                  let key = generateErrorKey ex
                  let errorInfo = createErrorInfo error "node"
                  accumulateError acc key errorInfo elemId)

              let totalAccumulated = acc.TotalErrors
              totalAccumulated = errors.Length

          // 4. First Occurrence Tracking
          testPropertyWithConfig fsCheckConfig "tracks first occurrence correctly"
          <| fun (error: AppError) (elementIds: string list) ->
              match elementIds with
              | [] -> true // Skip empty case
              | firstId :: restIds ->
                  let acc = createAccumulator 100

                  // Add first occurrence
                  let ex = appErrorToException error
                  let key = generateErrorKey ex
                  let errorInfo = createErrorInfo error "node"
                  accumulateError acc key errorInfo firstId
                  let firstIndex = acc.CurrentIndex

                  // Add more occurrences
                  restIds
                  |> List.iter (fun elemId -> accumulateError acc key errorInfo elemId)

                  // Verify first occurrence index is preserved
                  match acc.Errors |> Seq.tryHead with
                  | Some kvp ->
                      let (_, stats) = kvp.Value
                      stats.FirstOccurrenceIndex = firstIndex
                  | None -> false

          // 5. Message Truncation Property
          testPropertyWithConfig fsCheckConfig "long messages are truncated for keys"
          <| fun (n: int) ->
              let longMessage =
                  String.replicate (abs n + 101) "x"

              let ex1 = Exception(longMessage)
              let ex2 = Exception(longMessage + "extra")

              let key1 = generateErrorKey ex1
              let key2 = generateErrorKey ex2

              // Keys should be same because only first 100 chars are used
              key1 = key2

          // 6. Clear Operation Property
          testPropertyWithConfig fsCheckConfig "clear resets accumulator state"
          <| fun (errors: (AppError * string) list) ->
              let acc = createAccumulator 100

              // Add errors
              errors
              |> List.iter (fun (error, elemId) ->
                  let ex = appErrorToException error
                  let key = generateErrorKey ex
                  let errorInfo = createErrorInfo error "node"
                  accumulateError acc key errorInfo elemId)

              // Clear
              clearAccumulator acc

              // Verify reset
              acc.Errors.Count = 0
              && acc.CurrentIndex = 0
              && acc.TotalErrors = 0 ]
