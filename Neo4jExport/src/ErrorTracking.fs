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
                    | AddError(message, nodeId, relId, details, reply) ->
                        let error =
                            { Type = "error"
                              Timestamp = DateTime.UtcNow
                              Line = Some state.CurrentLine
                              Message = message
                              Details = details
                              NodeId = nodeId
                              RelationshipId = relId }

                        reply.Reply()

                        return!
                            loop
                                { state with
                                    Errors = error :: state.Errors
                                    ErrorCount = state.ErrorCount + 1L }

                    | AddWarning(message, nodeId, relId, details, reply) ->
                        let warning =
                            { Type = "warning"
                              Timestamp = DateTime.UtcNow
                              Line = Some state.CurrentLine
                              Message = message
                              Details = details
                              NodeId = nodeId
                              RelationshipId = relId }

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

    member _.AddError(message, ?nodeId, ?relId, ?details) =
        agent.PostAndReply(fun ch -> AddError(message, nodeId, relId, details, ch))

    member _.AddWarning(message, ?nodeId, ?relId, ?details) =
        agent.PostAndReply(fun ch -> AddWarning(message, nodeId, relId, details, ch))

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
