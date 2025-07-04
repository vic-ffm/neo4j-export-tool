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
open System.IO
open System.Text
open Neo4j.Driver
open ErrorTracking

/// Main workflow orchestration for the export process
module Workflow =
    let handleError (error: AppError) =
        let message =
            ErrorAccumulation.appErrorToString error

        Log.fatal message

        // Return appropriate exit code based on error type
        match error with
        | ConfigError _ -> 6
        | ConnectionError _ -> 2
        | AuthenticationError _ -> 6
        | QueryError _ -> 7
        | DataCorruptionError _ -> 5
        | DiskSpaceError _ -> 3
        | MemoryError _ -> 3
        | ExportError _ -> 5
        | FileSystemError _ -> 3
        | SecurityError _ -> 6
        | TimeoutError _ -> 5
        | AggregateError _ -> 6

    /// Performs export with efficient single-pass data writing and statistics collection
    let private performExport context session config metadata errorTracker : Async<Result<unit, AppError>> =
        async {
            let exportId =
                metadata.ExportMetadata.ExportId

            let exportStartTime = DateTime.UtcNow

            let newlineBytes =
                Encoding.UTF8.GetBytes(Environment.NewLine)

            let finalTempFile =
                Path.Combine(
                    config.OutputDirectory,
                    sprintf
                        "neo4j_export.tmp.%d.%s"
                        (System.Diagnostics.Process.GetCurrentProcess().Id)
                        (Path.GetRandomFileName())
                )

            Cleanup.registerTempFile context finalTempFile

            try
                use finalStream =
                    new FileStream(
                        finalTempFile,
                        FileMode.Create,
                        FileAccess.ReadWrite,
                        FileShare.Read,
                        bufferSize = 65536,
                        options = FileOptions.RandomAccess
                    )

                // Write metadata placeholder
                let placeholderSize =
                    Metadata.estimateMaxMetadataSize config metadata

                let placeholder =
                    Array.create placeholderSize (byte ' ')

                do!
                    finalStream.WriteAsync(placeholder, 0, placeholder.Length)
                    |> Async.AwaitTask

                do!
                    finalStream.WriteAsync(newlineBytes, 0, newlineBytes.Length)
                    |> Async.AwaitTask

                let dataStartPosition = finalStream.Position

                let initialStats =
                    { RecordsProcessed = 0L
                      RecordsSkipped = 0L
                      BytesWritten = 0L
                      StartTime = DateTime.UtcNow }

                let lineTracker = LineTracking.create ()

                let! nodeResult =
                    Export.exportNodesUnified
                        context
                        session
                        config
                        finalStream
                        initialStats
                        exportId
                        errorTracker
                        lineTracker

                match nodeResult with
                | Error e -> return Error e
                | Ok(nodeStats, labelStats, lineStateAfterNodes) ->
                    match!
                        Export.exportRelationships
                            context
                            session
                            config
                            finalStream
                            nodeStats
                            exportId
                            errorTracker
                            lineStateAfterNodes
                    with
                    | Error e -> return Error e
                    | Ok(finalStats, lineStateAfterRels) ->
                        // Export any error/warning records if they exist
                        let mutable finalStatsWithErrors =
                            finalStats

                        let mutable finalLineState =
                            lineStateAfterRels

                        if errorTracker.HasErrors() then
                            let! (errorCount, lineStateAfterErrors) =
                                Export.exportErrors finalStream errorTracker exportId lineStateAfterRels

                            Log.warn (sprintf "Exported %d error/warning records" errorCount)
                            finalLineState <- lineStateAfterErrors

                            finalStatsWithErrors <-
                                { finalStats with
                                    RecordsProcessed = finalStats.RecordsProcessed + errorCount }

                        do! finalStream.FlushAsync() |> Async.AwaitTask

                        let completedStats =
                            ExportStats.complete finalStatsWithErrors DateTime.UtcNow

                        let exportDuration =
                            (DateTime.UtcNow - exportStartTime).TotalSeconds

                        let enhancedMetadata =
                            metadata
                            |> fun m -> Metadata.enhanceWithManifest m labelStats exportDuration
                            |> fun m -> Metadata.addErrorSummary m errorTracker
                            |> fun m -> Metadata.addFormatInfo m finalLineState

                        let dataEndPosition = finalStream.Position

                        finalStream.Seek(0L, SeekOrigin.Begin) |> ignore

                        match
                            MetadataWriter.writeMetadataDirectly
                                finalStream
                                enhancedMetadata
                                placeholderSize
                                (JsonConfig.createWriterOptions ())
                                finalLineState
                        with
                        | Error msg -> return Error(ExportError(sprintf "Failed to write metadata: %s" msg, None))
                        | Ok() ->
                            do!
                                finalStream.WriteAsync(newlineBytes, 0, newlineBytes.Length)
                                |> Async.AwaitTask

                            match!
                                Export.finalizeExport context config enhancedMetadata finalTempFile completedStats
                            with
                            | Error e -> return Error e
                            | Ok() -> return Ok()
            with
            | :? OperationCanceledException -> return Error(ExportError("Export cancelled by user", None))
            | ex ->
                Log.logException ex
                return Error(ExportError("Export failed", Some ex))
        }

    /// Executes the complete export workflow with error handling
    let runExport (context: ApplicationContext) (config: ExportConfig) =
        async {
            use driver =
                GraphDatabase.Driver(config.Uri, AuthTokens.Basic(config.User, config.Password))

            let! connectionResult =
                driver.VerifyConnectivityAsync()
                |> Async.AwaitTask
                |> Async.Catch

            match connectionResult with
            | Choice1Of2 _ ->
                Log.info "Successfully connected to Neo4j"

                use session =
                    new SafeSession(driver.AsyncSession())

                let breaker =
                    Neo4j.createCircuitBreaker 5 (TimeSpan.FromSeconds(30.0))

                use monitor =
                    Monitoring.startResourceMonitor context config

                // Create ErrorTracker early to capture all warnings/errors
                let errorTracker = new ErrorTracker()

                match! Preflight.initializeFileSystem config with
                | Error e -> return Error e
                | Ok() ->
                    match! Preflight.runAllChecks context session breaker config with
                    | Error e -> return Error e
                    | Ok() ->
                        match! Metadata.collect context session breaker config errorTracker with
                        | Error e -> return Error e
                        | Ok metadata ->
                            let finalFilename =
                                Configuration.generateMetadataFilename config.OutputDirectory metadata

                            Log.info (sprintf "Export filename: %s" (System.IO.Path.GetFileName(finalFilename)))
                            Log.info "Collecting detailed statistics for export manifest"
                            return! performExport context session config metadata errorTracker
            | Choice2Of2 ex -> return Error(ConnectionError("Failed to connect to Neo4j", Some ex))
        }
