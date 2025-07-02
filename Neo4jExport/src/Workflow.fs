namespace Neo4jExport

open System
open System.IO
open System.Text
open Neo4j.Driver
open ErrorTracking

/// Main workflow orchestration for the export process
module Workflow =
    let handleError (error: AppError) =
        let exitCode, message =
            match error with
            | ConfigError msg -> 6, sprintf "Configuration Error: %s" msg
            | ConnectionError(msg, _) -> 2, sprintf "Connection Error: %s" msg
            | AuthenticationError msg -> 6, sprintf "Authentication Error: %s" msg
            | QueryError(query, msg, _) ->
                7, sprintf "Query Error: %s\nQuery: %s" msg (Security.sanitizeForLogging query 200)
            | DataCorruptionError(line, msg, sample) ->
                5,
                sprintf
                    "Data Corruption at line %d: %s%s"
                    line
                    msg
                    (sample
                     |> Option.map (sprintf "\nSample: %s")
                     |> Option.defaultValue "")
            | DiskSpaceError(required, available) ->
                3,
                sprintf
                    "Insufficient Disk Space: Need %s, have %s"
                    (Utils.formatBytes required)
                    (Utils.formatBytes available)
            | MemoryError msg -> 3, sprintf "Memory Error: %s" msg
            | ExportError(msg, _) -> 5, sprintf "Export Error: %s" msg
            | FileSystemError(path, msg, _) -> 3, sprintf "File System Error: %s\nPath: %s" msg path
            | SecurityError msg -> 6, sprintf "Security Error: %s" msg
            | TimeoutError(op, duration) -> 5, sprintf "Timeout Error: %s timed out after %O" op duration

        Log.fatal message
        exitCode

    /// Performs export with efficient single-pass data writing and statistics collection
    let private performExport context session config metadata : Async<Result<unit, AppError>> =
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
                        (Guid.NewGuid().ToString("N").Substring(0, 8))
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

                // Write metadata placeholder - estimate must be larger than final
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

                // Create error tracker for the export
                let errorTracker = ErrorTracker()

                // Create line tracker for the export
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

                Monitoring.startResourceMonitor context config

                match! Preflight.initializeFileSystem config with
                | Error e -> return Error e
                | Ok() ->
                    match! Preflight.runAllChecks context session breaker config with
                    | Error e -> return Error e
                    | Ok() ->
                        match! Metadata.collect context session breaker config with
                        | Error e -> return Error e
                        | Ok metadata ->
                            let finalFilename =
                                Configuration.generateMetadataFilename config.OutputDirectory metadata

                            Log.info (sprintf "Export filename: %s" (System.IO.Path.GetFileName(finalFilename)))
                            Log.info "Collecting detailed statistics for export manifest"
                            return! performExport context session config metadata
            | Choice2Of2 ex -> return Error(ConnectionError("Failed to connect to Neo4j", Some ex))
        }
