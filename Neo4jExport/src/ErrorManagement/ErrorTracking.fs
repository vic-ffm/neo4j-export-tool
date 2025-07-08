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

module Neo4jExport.ErrorTracking

open System
open System.Collections.Generic
open Neo4jExport

/// Thread-safe error tracking using F# agents
type internal ErrorTracker() =
    let agent =
        MailboxProcessor.Start(fun inbox ->
            let rec loop (state: ErrorTrackingState) =
                async {
                    let! msg = inbox.Receive()

                    match msg with
                    | AddError(message, elementId, details, reply) ->
                        let error =
                            { Type = "error"
                              Timestamp = DateTime.UtcNow
                              Line = Some state.CurrentLine
                              Message = message
                              Details = details
                              ElementId = elementId }

                        reply.Reply()

                        // MailboxProcessor guarantees the entire message handler completes atomically
                        // before processing the next message, ensuring state updates are visible
                        return!
                            loop
                                { state with
                                    Errors = error :: state.Errors
                                    ErrorCount = state.ErrorCount + 1L }

                    | AddWarning(message, elementId, details, reply) ->
                        let warning =
                            { Type = "warning"
                              Timestamp = DateTime.UtcNow
                              Line = Some state.CurrentLine
                              Message = message
                              Details = details
                              ElementId = elementId }

                        reply.Reply()

                        return!
                            loop
                                { state with
                                    Errors = warning :: state.Errors
                                    WarningCount = state.WarningCount + 1L }

                    | IncrementLine reply ->
                        reply.Reply()

                        return!
                            loop
                                { state with
                                    CurrentLine = state.CurrentLine + 1L }

                    | GetState reply ->
                        reply.Reply(state)
                        return! loop state
                }

            loop
                { Errors = []
                  ErrorCount = 0L
                  WarningCount = 0L
                  // IMPORTANT: Line tracking off-by-one fix
                  // This starts at 2L because line 1 of the output file contains metadata.
                  // The first data record (node/relationship) appears on line 2.
                  //
                  // LIMITATION: This is a pragmatic fix for a deeper architectural issue.
                  // Both LineTrackingState and ErrorTrackingState maintain separate line counters
                  // that must stay synchronized.
                  // TODO: The proper solution would be to pass line numbers
                  // explicitly to error tracking functions, eliminating duplicate state.
                  //

                  CurrentLine = 2L })

    member _.AddError(message, ?elementId, ?details) =
        agent.PostAndReply(fun ch -> AddError(message, elementId, details, ch))

    member _.AddWarning(message, ?elementId, ?details) =
        agent.PostAndReply(fun ch -> AddWarning(message, elementId, details, ch))

    member _.IncrementLine() = agent.PostAndReply(IncrementLine)

    member _.GetErrors() =
        let state = agent.PostAndReply(GetState)
        state.Errors |> List.rev // Reverse to maintain original order

    member _.GetErrorCount() =
        let state = agent.PostAndReply(GetState)
        state.ErrorCount

    member _.GetWarningCount() =
        let state = agent.PostAndReply(GetState)
        state.WarningCount

    member _.HasErrors() =
        let state = agent.PostAndReply(GetState)
        state.ErrorCount > 0L || state.WarningCount > 0L

    interface IDisposable with
        member _.Dispose() = (agent :> IDisposable).Dispose()

/// Error tracking functions providing controlled access to error management operations.
///
/// DESIGN RATIONALE:
/// This type encapsulates all error tracking operations to enable making ErrorTracker internal.
/// The design groups queries separately from commands to document the command-query separation,
/// though it still violates pure CQS principles by mixing both in one type.
///
/// DESIGN NOTES:
/// - IncrementLine is part of error tracking because errors need line numbers for debugging
/// - Line tracking here is for error correlation, not export progress (see LineTrackingState for that)
/// - The apparent "conflation" of concerns is actually cohesive design
/// - This design correctly groups operations that are used together
type ErrorTrackingFunctions =
    { // Commands (mutations)
      TrackError: string -> string option -> IDictionary<string, JsonValue> option -> unit
      TrackWarning: string -> string option -> IDictionary<string, JsonValue> option -> unit
      IncrementLine: unit -> unit
      // Queries (reads) - grouped for clarity
      Queries:
          {| GetErrors: unit -> ErrorRecord list
             GetErrorCount: unit -> int64
             GetWarningCount: unit -> int64
             HasErrors: unit -> bool |}
      // Disposal
      Dispose: unit -> unit }

/// Creates error tracking functions that encapsulate an ErrorTracker instance.
/// This factory function is the bridge between the internal ErrorTracker implementation
/// and the public ErrorTrackingFunctions interface.
///
/// PERFORMANCE NOTE:
/// The function indirection here has negligible overhead as these operations are not
/// in the hot path of record serialization. The actual performance-critical path is
/// the streaming serialization of nodes/relationships, not error tracking.
let internal createErrorTracker (exportId: Guid) (errorTracker: ErrorTracker) =
    { TrackError =
        fun message elementId details -> errorTracker.AddError(message, ?elementId = elementId, ?details = details)
      TrackWarning =
        fun message elementId details -> errorTracker.AddWarning(message, ?elementId = elementId, ?details = details)
      IncrementLine = fun () -> errorTracker.IncrementLine()
      Queries =
        {| GetErrors = fun () -> errorTracker.GetErrors()
           GetErrorCount = fun () -> errorTracker.GetErrorCount()
           GetWarningCount = fun () -> errorTracker.GetWarningCount()
           HasErrors = fun () -> errorTracker.HasErrors() |}
      Dispose = fun () -> (errorTracker :> IDisposable).Dispose() }

/// Creates a complete error tracking system with proper encapsulation.
/// Returns ErrorTrackingFunctions that internally manages an ErrorTracker instance.
/// The returned functions handle disposal of the underlying ErrorTracker.
///
/// This is the primary factory function that should be used to create error tracking.
/// It encapsulates the ErrorTracker instance completely, allowing ErrorTracker to be
/// made internal without exposing it to external modules.
let createErrorTrackingSystem (exportId: Guid) : ErrorTrackingFunctions =
    let errorTracker = new ErrorTracker()
    createErrorTracker exportId errorTracker

/// Helper functions to provide cleaner APIs when only a subset of operations is needed
module ErrorTrackingHelpers =
    /// Extract command operations (error/warning tracking and line increment)
    /// Useful for modules that track errors but don't query them
    let getCommands (funcs: ErrorTrackingFunctions) =
        {| TrackError = funcs.TrackError
           TrackWarning = funcs.TrackWarning
           IncrementLine = funcs.IncrementLine |}

    /// Extract only query operations for reporting modules
    let getQueries (funcs: ErrorTrackingFunctions) = funcs.Queries

    /// Extract only error/warning tracking (no line tracking)
    /// Rarely used since most error tracking needs line correlation
    let getErrorReporter (funcs: ErrorTrackingFunctions) =
        {| TrackError = funcs.TrackError
           TrackWarning = funcs.TrackWarning |}
