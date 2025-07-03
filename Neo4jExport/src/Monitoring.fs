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
                        let! msgOpt = inbox.TryReceive(1000)

                        match msgOpt with
                        | Some(CheckResources reply) ->
                            reply.Reply(Ok())

                            return!
                                loop
                                    { state with
                                        LastCheck = DateTime.UtcNow }
                        | Some Stop -> return ()
                        | None -> return! loop state
                }

            loop
                { LastCheck = DateTime.UtcNow
                  IsRunning = true })

    member _.Start() = ()

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
