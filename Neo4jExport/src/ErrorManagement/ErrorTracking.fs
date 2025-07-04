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
type ErrorTracker() =
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
                  CurrentLine = 1L })

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
