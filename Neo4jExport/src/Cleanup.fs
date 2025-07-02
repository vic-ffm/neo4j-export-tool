namespace Neo4jExport

open System.IO
open System.Diagnostics

module Cleanup =
    let registerTempFile (context: ApplicationContext) (path: string) = AppContext.addTempFile context path

    let performCleanup (context: ApplicationContext) (reason: string) =
        Log.info (sprintf "Performing cleanup: %s" reason)

        if not (AppContext.isCancellationRequested context) then
            AppContext.cancel context

        // First terminate processes before deleting files they might be using
        for proc in context.ActiveProcesses do
            try
                if not proc.HasExited then
                    // Attempt graceful shutdown first
                    let mutable exited = false

                    // Try to close the main window for GUI apps
                    if proc.MainWindowHandle <> System.IntPtr.Zero then
                        proc.CloseMainWindow() |> ignore
                        exited <- proc.WaitForExit(5000) // Wait 5 seconds

                    // If still running, forcefully terminate
                    if not exited && not proc.HasExited then
                        proc.Kill()
                        Log.debug (sprintf "Forcefully terminated process: %d" proc.Id)
                    else
                        Log.debug (sprintf "Gracefully terminated process: %d" proc.Id)
            with ex ->
                // Include process ID in error message for consistency
                let procId =
                    try
                        proc.Id.ToString()
                    with _ ->
                        "unknown"

                Log.warn (sprintf "Failed to terminate process %s: %s" procId ex.Message)

        // Now safe to delete temporary files
        for file in context.TempFiles do
            try
                if File.Exists(file) then
                    File.Delete(file)
                    Log.debug (sprintf "Deleted temp file: %s" file)
            with ex ->
                Log.warn (sprintf "Failed to delete temp file %s: %s" file ex.Message)

        Log.info "Cleanup completed"
