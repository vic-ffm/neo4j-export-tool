namespace Neo4jExport

open System
open System.IO

module Monitoring =
    /// Validates if the path is suitable for DriveInfo monitoring
    let private isValidDriveInfoPath (path: string) =
        try
            if String.IsNullOrWhiteSpace(path) then false
            elif path.StartsWith(@"\\") then false
            else Path.IsPathRooted(path)
        with _ ->
            false

    let startResourceMonitor (context: ApplicationContext) (config: ExportConfig) =
        let monitorTask =
            async {
                Log.debug "Starting resource monitor..."

                let canMonitorDisk =
                    isValidDriveInfoPath config.OutputDirectory

                if not canMonitorDisk then
                    Log.warn (
                        sprintf
                            "Cannot monitor disk space for path: %s (UNC or invalid path format)"
                            config.OutputDirectory
                    )

                while not (AppContext.isCancellationRequested context) do
                    try
                        if canMonitorDisk then
                            let drive =
                                DriveInfo(config.OutputDirectory)

                            let availableGb =
                                drive.AvailableFreeSpace / 1073741824L

                            if availableGb < config.MinDiskGb then
                                Log.fatal (
                                    sprintf
                                        "Low disk space - %dGB remaining (threshold: %dGB)"
                                        availableGb
                                        config.MinDiskGb
                                )

                                AppContext.cancel context

                        let memoryMb =
                            GC.GetTotalMemory(false) / 1048576L

                        if memoryMb > config.MaxMemoryMb then
                            Log.warn (sprintf "High memory usage: %dMB (limit: %dMB)" memoryMb config.MaxMemoryMb)

                        do! Async.Sleep 30000
                    with ex ->
                        Log.logException ex
            }

        Async.Start(monitorTask, AppContext.getCancellationToken context)
