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
open Capabilities

/// Main workflow orchestration for the export process
/// Factory functions for workflow contexts and operations
module WorkflowFactories =
    /// Create monitoring context from app context and config
    let createMonitoringContext (appContext: ApplicationContext) (config: ExportConfig) =
        { AppContext = appContext
          OutputDirectory = config.OutputDirectory
          MinDiskGb = config.MinDiskGb }

    /// Create workflow context from all components
    let createWorkflowContext app config session metadata errorFuncs version : WorkflowContext<SafeSession> =
        let errorContext =
            ErrorContext.create metadata.ExportMetadata.ExportId errorFuncs

        let initialStats =
            { RecordsProcessed = 0L
              RecordsSkipped = 0L
              BytesWritten = 0L
              StartTime = DateTime.UtcNow }

        let progressContext =
            { Stats = initialStats
              LineState = LineTracking.create () }

        // NEW: Create operations
        let workflowOps =
            WorkflowOperations.create app config

        let progressOps =
            ProgressOperations.create initialStats.StartTime (TimeSpan.FromSeconds 30.0) initialStats

        let exportContext =
            { Error = errorContext
              Progress = progressContext
              Config = config
              AppContext = app
              Workflow = workflowOps // NEW
              Reporting = Some progressOps } // NEW

        { App = app
          Export = exportContext
          Session = session
          Metadata = metadata
          Neo4jVersion = version } // ADD THIS LINE

module Workflow =
    open Neo4jExport.ExportTypes
    
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

    /// Prepare export file with metadata placeholder
    /// Now accepts WorkflowContext for access to all operations
    let private prepareExportFile (workflowCtx: WorkflowContext<'T>) =
        async {
            let config = workflowCtx.Export.Config
            let metadata = workflowCtx.Metadata

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

            // Use WorkflowOperations instead of direct call
            workflowCtx.Export.Workflow.RegisterTempFile finalTempFile

            let finalStream =
                new FileStream(
                    finalTempFile,
                    FileMode.Create,
                    FileAccess.ReadWrite,
                    FileShare.Read,
                    bufferSize = 65536,
                    options = FileOptions.RandomAccess
                )

            try
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
                return Ok(finalTempFile, finalStream, placeholderSize, dataStartPosition)
            with ex ->
                finalStream.Dispose()
                return Error(FileSystemError(finalTempFile, "Failed to prepare export file", Some ex))
        }

    /// Execute node and relationship export
    let private executeExport
        (workflowCtx: WorkflowContext<SafeSession>)
        (fileStream: FileStream)
        (tempFilePath: string)
        (exportStartTime: DateTime)
        : Async<Result<ExportResult<LabelStatsTracker.Tracker>, AppError>> =
        async {
            Log.debug (sprintf "Creating export processors for Neo4j version: %A" workflowCtx.Neo4jVersion)
            
            // Create processors
            let nodeProcessor =
                ExportCore.ExportProcessors.createNodeProcessor workflowCtx.Neo4jVersion

            let relProcessor =
                ExportCore.ExportProcessors.createRelationshipProcessor workflowCtx.Neo4jVersion

            // Add version validation warning
            match workflowCtx.Neo4jVersion with
            | Unknown ->
                Log.warn "Neo4j version could not be detected - using SKIP/LIMIT pagination (O(n²) performance degradation for large datasets)"
                // Track this as a warning in error tracking
                workflowCtx.Export.Error.Funcs.TrackWarning 
                    "Unknown Neo4j version - degraded pagination performance" 
                    None 
                    (Some (dict ["impact", JString "performance"; "fallback", JString "SKIP/LIMIT"]))
            | v ->
                Log.info (sprintf "Neo4j %A detected - using optimized keyset pagination (O(log n) performance)" v)
            
            // Create ExportState for stable ID tracking
            let exportState = ExportState.Create(workflowCtx.Neo4jVersion)
            
            Log.debug (sprintf "ExportState initialized - Version: %A, NodeIdMapping capacity: %d" 
                exportState.Version 
                exportState.NodeIdMapping.Count)

            // Export nodes
            let! nodeResult =
                ExportCore.exportNodesUnified workflowCtx.Session fileStream workflowCtx.Export exportState nodeProcessor

            match nodeResult with
            | Error e -> return Error e
            | Ok(nodeStats, labelStats, lineStateAfterNodes) ->
                // Update export context with new progress state
                let updatedExportContext =
                    { workflowCtx.Export with
                        Progress =
                            { Stats = nodeStats
                              LineState = lineStateAfterNodes } }

                // Export relationships with updated context
                match!
                    ExportCore.exportRelationships workflowCtx.Session fileStream updatedExportContext exportState relProcessor
                with
                | Error e -> return Error e
                | Ok(finalStats, lineStateAfterRels) ->
                    // Export any error/warning records if they exist
                    let mutable finalStatsWithErrors =
                        finalStats

                    let mutable finalLineState =
                        lineStateAfterRels

                    if workflowCtx.Export.Error.Funcs.Queries.HasErrors() then
                        let! (errorCount, lineStateAfterErrors) =
                            ExportCore.exportErrors
                                fileStream
                                workflowCtx.Export.Error.Funcs
                                workflowCtx.Export.Error.ExportId
                                lineStateAfterRels

                        Log.warn (sprintf "Exported %d error/warning records" errorCount)
                        finalLineState <- lineStateAfterErrors

                        finalStatsWithErrors <-
                            { finalStats with
                                RecordsProcessed = finalStats.RecordsProcessed + errorCount }

                    do! fileStream.FlushAsync() |> Async.AwaitTask

                    let completedStats =
                        ExportStats.complete finalStatsWithErrors DateTime.UtcNow

                    let exportDuration =
                        (DateTime.UtcNow - exportStartTime).TotalSeconds
                    
                    // Extract performance metrics from ExportState
                    let nodePerfMetrics = 
                        let nodeStrategy = if exportState.Version = Unknown then SkipLimit 0 else Keyset(None, exportState.Version)
                        exportState.NodePerfTracker.GetMetrics(nodeStrategy)
                    
                    let relPerfMetrics = 
                        let relStrategy = if exportState.Version = Unknown then SkipLimit 0 else Keyset(None, exportState.Version)
                        exportState.RelPerfTracker.GetMetrics(relStrategy)
                    
                    // Combine metrics (preferring node metrics as primary indicator)
                    let combinedPerfMetrics = 
                        if nodePerfMetrics.TotalBatches > relPerfMetrics.TotalBatches then
                            Some nodePerfMetrics
                        else
                            Some relPerfMetrics

                    let enhancedMetadata =
                        workflowCtx.Metadata
                        |> fun m -> Metadata.enhanceWithManifest m labelStats exportDuration
                        |> fun m -> Metadata.addErrorSummary m workflowCtx.Export.Error.Funcs
                        |> fun m -> Metadata.addFormatInfo m finalLineState
                        |> fun m -> { m with PaginationPerformance = combinedPerfMetrics }

                    return
                        Ok
                            { Stats = completedStats
                              LabelStats = labelStats
                              EnhancedMetadata = enhancedMetadata
                              FinalLineState = finalLineState
                              PaginationPerformance = combinedPerfMetrics }
        }

    /// Write final metadata and finalize file
    let private finalizeExportFile
        (workflowCtx: WorkflowContext<'TSession>)
        (fileStream: FileStream)
        (tempFilePath: string)
        (result: ExportResult<'TTracker>)
        placeholderSize
        finalLineState
        =
        async {
            let newlineBytes =
                Encoding.UTF8.GetBytes(Environment.NewLine)

            // Seek to beginning to write metadata
            fileStream.Seek(0L, SeekOrigin.Begin) |> ignore

            match
                MetadataWriter.writeMetadataDirectly
                    fileStream
                    result.EnhancedMetadata
                    placeholderSize
                    (JsonConfig.createWriterOptions ())
                    finalLineState
            with
            | Error msg -> return Error(ExportError(sprintf "Failed to write metadata: %s" msg, None))
            | Ok() ->
                do!
                    fileStream.WriteAsync(newlineBytes, 0, newlineBytes.Length)
                    |> Async.AwaitTask

                match!
                    ExportCore.finalizeExport
                        workflowCtx.App
                        workflowCtx.Export.Config
                        result.EnhancedMetadata
                        tempFilePath
                        result.Stats
                with
                | Error e -> return Error e
                | Ok() -> return Ok()
        }

    /// Performs export with efficient single-pass data writing and statistics collection
    let private performExport (workflowCtx: WorkflowContext<SafeSession>) : Async<Result<unit, AppError>> =
        async {
            let exportStartTime = DateTime.UtcNow

            // Prepare export file
            match! prepareExportFile workflowCtx with
            | Error e -> return Error e
            | Ok(tempFile, fileStream, placeholderSize, dataStartPosition) ->
                try
                    // Execute export
                    match! executeExport workflowCtx fileStream tempFile exportStartTime with
                    | Error e ->
                        fileStream.Dispose()
                        return Error e
                    | Ok exportResult ->
                        // Finalize file
                        match!
                            finalizeExportFile
                                workflowCtx
                                fileStream
                                tempFile
                                exportResult
                                placeholderSize
                                exportResult.FinalLineState
                        with
                        | Error e ->
                            fileStream.Dispose()
                            return Error e
                        | Ok() ->
                            fileStream.Dispose()
                            return Ok()
                with
                | :? OperationCanceledException ->
                    fileStream.Dispose()
                    return Error(ExportError("Export cancelled by user", None))
                | ex ->
                    fileStream.Dispose()
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

                // Create monitoring context and start monitor
                let monitoringCtx =
                    WorkflowFactories.createMonitoringContext context config

                use monitor =
                    Monitoring.startResourceMonitor monitoringCtx

                // Generate exportId early for consistent tracking
                let exportId = Guid.NewGuid()

                // Create complete error tracking system with proper disposal
                let errorFuncs =
                    ErrorTracking.createErrorTrackingSystem exportId

                // Manual disposal tracking since F# records can't use 'use' binding
                try
                    match! Preflight.initializeFileSystem config with
                    | Error e -> return Error e
                    | Ok() ->
                        match! Preflight.runAllChecks context session breaker config with
                        | Error e -> return Error e
                        | Ok() ->
                            // Create contexts for new parameter-reduced function
                            let queryExecutors =
                                Neo4j.createQueryExecutors session breaker config

                            let errorContext =
                                ErrorContext.create exportId errorFuncs

                            match! Metadata.collect context queryExecutors errorContext config with
                            | Error e -> return Error e
                            | Ok(metadata, version) ->
                                Log.info (sprintf "Detected Neo4j version: %A" version)
                                
                                let finalFilename =
                                    Configuration.generateMetadataFilename config.OutputDirectory metadata

                                Log.info (sprintf "Export filename: %s" (System.IO.Path.GetFileName(finalFilename)))
                                Log.info "Collecting detailed statistics for export manifest"

                                // Create workflow context and execute export
                                let workflowCtx =
                                    WorkflowFactories.createWorkflowContext
                                        context
                                        config
                                        session
                                        metadata
                                        errorFuncs
                                        version

                                return! performExport workflowCtx
                finally
                    errorFuncs.Dispose()
            | Choice2Of2 ex -> return Error(ConnectionError("Failed to connect to Neo4j", Some ex))
        }
