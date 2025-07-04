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

namespace Neo4jExport

open System

/// Application entry point and workflow coordination
module Program =

    let private loadConfiguration () =
        // FAIL-FAST DESIGN: Configuration validation happens first, before any expensive operations.
        // This includes creating the output directory to verify path validity and permissions.
        // By failing early on configuration issues, we avoid wasting time establishing database
        // connections or allocating resources for exports that cannot succeed.
        try
            Configuration.getConfig ()
        with ex ->
            Error(ConfigError(sprintf "Failed to load configuration: %s" (ErrorAccumulation.exceptionToString ex)))

    let private executeMain (context: ApplicationContext) =
        Log.info (sprintf "=== Neo4j Export Tool %s ===" (Constants.getVersionString ()))
        Log.info "Neo4j database export to JSONL format"

        match loadConfiguration () with
        | Error err -> Workflow.handleError err
        | Ok config ->
            if config.EnableDebugLogging then
                Log.setMinLevel "Debug"
                Log.debug "Debug logging enabled"

            try
                Workflow.runExport context config
                |> Async.RunSynchronously
                |> function
                    | Ok() ->
                        Log.info "Export completed successfully"
                        0
                    | Error err -> Workflow.handleError err
            with
            | :? OperationCanceledException ->
                Log.warn "Export cancelled by user"
                130
            | ex ->
                Log.fatal (sprintf "Unexpected error: %s" (ErrorAccumulation.exceptionToString ex))
                Log.logException ex
                1

    let private performFinalCleanup (context: ApplicationContext) =
        try
            if
                not (Seq.isEmpty context.TempFiles)
                || not (Seq.isEmpty context.ActiveProcesses)
            then
                Log.debug "Performing final cleanup of resources"
                Cleanup.performCleanup context "Application exit"
                Log.debug "Cleanup completed successfully"
        with ex ->
            Log.error (sprintf "Error during final cleanup: %s" (ErrorAccumulation.exceptionToString ex))
            Log.logException ex

    [<EntryPoint>]
    let main argv =
        use context = AppContext.create ()

        let signalRegistration =
            SignalHandling.registerHandlers context

        try
            let exitCode = executeMain context
            performFinalCleanup context

            // Dispose signal registration if present
            signalRegistration
            |> Option.iter (fun reg -> reg.Dispose())

            exitCode
        with ex ->
            Log.fatal (sprintf "Catastrophic error: %s" (ErrorAccumulation.exceptionToString ex))
            Log.logException ex
            performFinalCleanup context

            // Dispose signal registration if present
            signalRegistration
            |> Option.iter (fun reg -> reg.Dispose())

            1
