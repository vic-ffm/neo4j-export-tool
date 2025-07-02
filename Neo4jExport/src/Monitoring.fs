module Neo4jExport.Monitoring

open System
open System.IO
open Neo4jExport

type ResourceMonitor(context: ApplicationContext, config: ExportConfig) =
    let checkInterval =
        TimeSpan.FromSeconds(30.0)

    let agent =
        MailboxProcessor.Start(fun inbox ->
            let rec loop (state: ResourceState) =
                async {
                    if
                        state.IsRunning
                        && DateTime.UtcNow - state.LastCheck >= checkInterval
                    then
                        // Perform resource check
                        let drive =
                            DriveInfo(Path.GetPathRoot(config.OutputDirectory))

                        let availableGB =
                            float drive.AvailableFreeSpace / 1073741824.0

                        if availableGB < float config.MinDiskGb then
                            Log.fatal (sprintf "Disk space critically low: %.2f GB" availableGB)
                            context.CancellationTokenSource.Cancel()

                        return!
                            loop
                                { state with
                                    LastCheck = DateTime.UtcNow }
                    else
                        // Wait for message or timeout
                        let! msgOpt = inbox.TryReceive(1000)

                        match msgOpt with
                        | Some(CheckResources reply) ->
                            // Immediate check requested
                            reply.Reply(Ok())

                            return!
                                loop
                                    { state with
                                        LastCheck = DateTime.UtcNow }
                        | Some Stop -> return () // Exit
                        | None -> return! loop state
                }

            loop
                { LastCheck = DateTime.UtcNow
                  IsRunning = true })

    member _.Start() = () // Agent starts automatically

    member _.Stop() = agent.Post(Stop)

    interface IDisposable with
        member _.Dispose() =
            agent.Post(Stop)
            (agent :> IDisposable).Dispose()

let startResourceMonitor context config =
    let monitor =
        new ResourceMonitor(context, config)

    monitor.Start()
    monitor
