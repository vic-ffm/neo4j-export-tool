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

module Neo4jExport.ErrorDeduplication

open System
open System.Collections.Generic
open Neo4jExport
open ErrorTracking

// --- Pure Key Generation Functions ---

/// Struct key for efficient deduplication in hot path
[<Struct>]
type ErrorKey =
    { ExceptionTypeHash: int
      MessagePrefixHash: int }

/// Pure function to generate deduplication key from exception
let inline generateErrorKey (ex: exn) : ErrorKey =
    let exceptionType = ex.GetType().Name
    let message = ex.Message

    // Extract fixed-length prefix to prevent unbounded keys
    let messagePrefix =
        if message.Length > 100 then
            message.Substring(0, 100)
        else
            message

    { ExceptionTypeHash = exceptionType.GetHashCode(StringComparison.Ordinal)
      MessagePrefixHash = messagePrefix.GetHashCode(StringComparison.Ordinal) }

/// Pure function to generate key from error details
let inline generateErrorKeyFromDetails (exceptionType: string) (message: string) : ErrorKey =
    let messagePrefix =
        if message.Length > 100 then
            message.Substring(0, 100)
        else
            message

    { ExceptionTypeHash = exceptionType.GetHashCode(StringComparison.Ordinal)
      MessagePrefixHash = messagePrefix.GetHashCode(StringComparison.Ordinal) }

// --- Mutable Statistics Types ---

/// Statistics for efficient accumulation (using class for mutability)
type ErrorStatistics() =
    member val Count = 1 with get, set
    member val FirstOccurrenceIndex = 0L with get, set
    member val SampleElementIds = Array.zeroCreate<string> 5 with get, set
    member val SampleCount = 1 with get, set

/// Create new error statistics with bounded sample collection
let createErrorStatistics (firstIndex: int64) (firstElementId: string) : ErrorStatistics =
    let stats = ErrorStatistics()
    stats.FirstOccurrenceIndex <- firstIndex
    stats.SampleElementIds.[0] <- firstElementId
    stats

/// Update statistics with new occurrence (pure calculation of what to update)
let inline shouldAddSample (stats: ErrorStatistics) : bool =
    stats.SampleCount < stats.SampleElementIds.Length

// --- Batch Error Accumulator ---

/// Information stored for each unique error
type ErrorInfo =
    { ExceptionType: string
      Message: string
      EntityType: string
      ErrorType: string }

/// Batch-scoped error accumulator with pre-allocated structures
type BatchErrorAccumulator =
    { Errors: Dictionary<ErrorKey, ErrorInfo * ErrorStatistics>
      mutable CurrentIndex: int64
      mutable TotalErrors: int64 }

/// Create a new accumulator with expected capacity
let createAccumulator (capacity: int) : BatchErrorAccumulator =
    { Errors = Dictionary<ErrorKey, ErrorInfo * ErrorStatistics>(capacity)
      CurrentIndex = 0L
      TotalErrors = 0L }

/// Add error to accumulator (mutating operation isolated here)
let accumulateError (accumulator: BatchErrorAccumulator) (key: ErrorKey) (errorInfo: ErrorInfo) (elementId: string) =

    accumulator.CurrentIndex <- accumulator.CurrentIndex + 1L
    accumulator.TotalErrors <- accumulator.TotalErrors + 1L

    match accumulator.Errors.TryGetValue(key) with
    | true, (info, stats) ->
        // Update existing error statistics
        stats.Count <- stats.Count + 1

        if shouldAddSample stats then
            stats.SampleElementIds.[stats.SampleCount] <- elementId
            stats.SampleCount <- stats.SampleCount + 1
    | false, _ ->
        // Add new error
        let stats =
            createErrorStatistics accumulator.CurrentIndex elementId

        accumulator.Errors.[key] <- (errorInfo, stats)

/// Clear accumulator for reuse
let clearAccumulator (accumulator: BatchErrorAccumulator) =
    accumulator.Errors.Clear()
    accumulator.CurrentIndex <- 0L
    accumulator.TotalErrors <- 0L

// --- Formatting Functions (Pure) ---

/// Format error message with deduplication statistics
let formatDedupedError (info: ErrorInfo) (stats: ErrorStatistics) (batchSize: int64) : string =
    let percentage =
        if batchSize > 0L then
            (float stats.Count / float batchSize) * 100.0
        else
            0.0

    let samples =
        stats.SampleElementIds
        |> Array.take stats.SampleCount
        |> String.concat ", "

    sprintf
        "%s [Occurred %d times (%.1f%% of batch), first at index %d, sample IDs: %s]"
        info.Message
        stats.Count
        percentage
        stats.FirstOccurrenceIndex
        samples

/// Create error details for ErrorTracker
let createErrorDetails (info: ErrorInfo) (stats: ErrorStatistics) : IDictionary<string, JsonValue> option =
    let details =
        Dictionary<string, JsonValue>()

    details.["entity_type"] <- JString info.EntityType
    details.["exception_type"] <- JString info.ErrorType
    details.["serialization_phase"] <- JString "write"
    details.["occurrence_count"] <- JNumber(decimal stats.Count)
    details.["first_occurrence_index"] <- JNumber(decimal stats.FirstOccurrenceIndex)

    let sampleIds =
        stats.SampleElementIds
        |> Array.take stats.SampleCount
        |> Array.map JString
        |> List.ofArray
        |> JArray

    details.["sample_element_ids"] <- sampleIds

    Some(details :> IDictionary<string, JsonValue>)

// --- Flush Operations ---

/// Flush accumulated errors to ErrorTracker
let flushErrors (accumulator: BatchErrorAccumulator) (errorTracker: ErrorTracker) (batchSize: int64) =

    // Fast path: no errors
    if accumulator.Errors.Count = 0 then
        ()
    else
        // Transform and write all accumulated errors
        for KeyValue(_, (info, stats)) in accumulator.Errors do
            let message =
                formatDedupedError info stats batchSize

            let details = createErrorDetails info stats

            // Use first sample as primary element ID
            let primaryElementId =
                if stats.SampleCount > 0 then
                    Some stats.SampleElementIds.[0]
                else
                    None

            errorTracker.AddError(message, ?elementId = primaryElementId, ?details = details)

// --- Integration Helper ---

/// Track serialization error with deduplication
let trackSerializationErrorDedup
    (accumulator: BatchErrorAccumulator)
    (ex: exn)
    (elementId: string)
    (entityType: string)
    (errorType: string)
    =

    let key = generateErrorKey ex

    let errorInfo =
        { ExceptionType = ex.GetType().Name
          Message = sprintf "%s serialization failed: %s" entityType (ErrorAccumulation.exceptionToString ex)
          EntityType = entityType
          ErrorType = errorType }

    accumulateError accumulator key errorInfo elementId

/// Track serialization error by message with deduplication
let trackSerializationErrorByMessage
    (accumulator: BatchErrorAccumulator)
    (message: string)
    (elementId: string)
    (entityType: string)
    (errorType: string)
    =

    let key =
        generateErrorKeyFromDetails errorType message

    let errorInfo =
        { ExceptionType = errorType
          Message = message
          EntityType = entityType
          ErrorType = errorType }

    accumulateError accumulator key errorInfo elementId
