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

module Neo4jExport.ExportCore

open System
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.Json
open Neo4j.Driver
open Neo4jExport
open Neo4jExport.ExportTypes
open Neo4jExport.SerializationEngine
open Neo4jExport.ExportBatchProcessing
open ErrorTracking

module QueryBuilders =
    // Version-specific query builders handle differences between Neo4j 4.x (numeric IDs)
    // and 5.x+ (element IDs). This abstraction allows the export logic to remain version-agnostic.
    let buildNodeQuery (version: Neo4jVersion) =
        match version with
        | V4x -> "MATCH (n) RETURN n, labels(n) as labels, id(n) as nodeId ORDER BY id(n) SKIP $skip LIMIT $limit"
        | V5x
        | V6x
        | Unknown ->
            "MATCH (n) RETURN n, labels(n) as labels, elementId(n) as elementId ORDER BY elementId(n) SKIP $skip LIMIT $limit"

    let buildNodeQueryKeyset (version: Neo4jVersion) (lastId: KeysetId option) =
        let whereClause =
            match version, lastId with
            | V4x, Some(NumericId id) -> sprintf "WHERE id(n) > %d" id
            | (V5x | V6x), Some(ElementId id) -> sprintf "WHERE elementId(n) > '%s'" id
            | _ -> ""

        match version with
        | V4x ->
            // For Neo4j 4.x: Return raw node to minimize DB load
            // We return the node object directly and handle temporal conversion in .NET
            // to avoid putting computational load on the production database
            sprintf
                """
            MATCH (n)
            %s
            RETURN 
                toString(id(n)) AS elementId,
                labels(n) AS labels,
                n AS node,
                id(n) AS nodeId
            ORDER BY id(n)
            LIMIT $limit
            """
                whereClause
        | V5x
        | V6x
        | Unknown ->
            // For Neo4j 5.x+: Also return raw node (same strategy as 4.x)
            // Although 5.x supports type operators like IS ::DATETIME, we chose to
            // use the same approach as 4.x to minimize database CPU usage.
            // Temporal conversion happens in BatchProcessing.fs instead.
            sprintf
                """
            MATCH (n)
            %s
            RETURN 
                elementId(n) AS elementId,
                labels(n) AS labels,
                n AS node
            ORDER BY elementId(n)
            LIMIT $limit
            """
                whereClause

    let buildRelationshipQuery (version: Neo4jVersion) =
        match version with
        | V4x -> "MATCH ()-[r]->() RETURN r, type(r) as type, id(r) as relId ORDER BY id(r) SKIP $skip LIMIT $limit"
        | V5x
        | V6x
        | Unknown ->
            "MATCH ()-[r]->() RETURN r, type(r) as type, elementId(r) as elementId ORDER BY elementId(r) SKIP $skip LIMIT $limit"

    let buildRelationshipQueryKeyset (version: Neo4jVersion) (lastId: KeysetId option) =
        let whereClause =
            match version, lastId with
            | V4x, Some(NumericId id) -> sprintf "WHERE id(r) > %d" id
            | (V5x | V6x), Some(ElementId id) -> sprintf "WHERE elementId(r) > '%s'" id
            | _ -> ""

        match version with
        | V4x ->
            // For Neo4j 4.x: Return raw relationship to minimize DB load
            // We return the relationship object directly and handle temporal conversion in .NET
            // to avoid putting computational load on the production database
            sprintf
                """
            MATCH (startNode)-[r]->(endNode)
            %s
            RETURN 
                toString(id(r)) AS elementId,
                type(r) AS type,
                toString(id(startNode)) AS startNodeElementId,
                toString(id(endNode)) AS endNodeElementId,
                r AS relationship,
                id(r) AS relId
            ORDER BY id(r)
            LIMIT $limit
            """
                whereClause
        | V5x
        | V6x
        | Unknown ->
            // For Neo4j 5.x+: Also return raw relationship (same strategy as 4.x)
            // Although 5.x supports type operators like IS ::DATETIME, we chose to
            // use the same approach as 4.x to minimize database CPU usage.
            // Temporal conversion happens in BatchProcessing.fs instead.
            sprintf
                """
            MATCH (startNode)-[r]->(endNode)
            %s
            RETURN 
                elementId(r) AS elementId,
                type(r) AS type,
                elementId(startNode) AS startNodeElementId,
                elementId(endNode) AS endNodeElementId,
                r AS relationship
            ORDER BY elementId(r)
            LIMIT $limit
            """
                whereClause

    let buildQueryParameters (lastId: KeysetId option) (batchSize: int) (skip: int option) =
        match lastId, skip with
        | None, None -> dict [ "limit", box batchSize ]
        | Some id, None ->
            dict
                [ "lastId", KeysetId.toParameter id
                  "limit", box batchSize ]
        | None, Some s ->
            dict
                [ "skip", box s
                  "limit", box batchSize ]
        | Some _, Some _ -> failwith "Cannot use both lastId and skip parameters"

    let buildNodeCountQuery (_: Neo4jVersion) = "MATCH (n) RETURN count(n) as count"

    let buildRelationshipCountQuery (_: Neo4jVersion) =
        "MATCH ()-[r]->() RETURN count(r) as count"

    /// Note: This function needs the version from the processor to build correct queries
    let buildNodeQueryWithParams
        (version: Neo4jVersion)
        (strategy: PaginationStrategy)
        (batchSize: int)
        : string * IDictionary<string, obj> =
        match strategy with
        | SkipLimit skip ->
            let query = buildNodeQuery version

            let parameters =
                dict
                    [ "skip", box skip
                      "limit", box batchSize ]

            query, parameters

        | Keyset(lastId, version) ->
            let query =
                buildNodeQueryKeyset version lastId

            let parameters =
                buildQueryParameters lastId batchSize None

            query, parameters

    let buildRelationshipQueryWithParams
        (version: Neo4jVersion)
        (strategy: PaginationStrategy)
        (batchSize: int)
        : string * IDictionary<string, obj> =
        match strategy with
        | SkipLimit skip ->
            let query = buildRelationshipQuery version

            let parameters =
                dict
                    [ "skip", box skip
                      "limit", box batchSize ]

            query, parameters

        | Keyset(lastId, version) ->
            let query =
                buildRelationshipQueryKeyset version lastId

            let parameters =
                buildQueryParameters lastId batchSize None

            query, parameters

module ExportProcessors =
    let createNodeProcessor (version: Neo4jVersion) =
        match version with
        | Unknown ->
            // Legacy SKIP/LIMIT for unknown versions
            BatchProcessor.CreateLegacy(
                QueryBuilders.buildNodeQuery version,
                Some(QueryBuilders.buildNodeCountQuery version),
                "Nodes",
                version
            )
        | _ ->
            // Use dynamic query builder for known versions
            BatchProcessor.CreateDynamic(
                QueryBuilders.buildNodeQueryWithParams,
                Some(QueryBuilders.buildNodeCountQuery version),
                "Nodes",
                version
            )

    let createRelationshipProcessor (version: Neo4jVersion) =
        match version with
        | Unknown ->
            // Legacy SKIP/LIMIT for unknown versions
            BatchProcessor.CreateLegacy(
                QueryBuilders.buildRelationshipQuery version,
                Some(QueryBuilders.buildRelationshipCountQuery version),
                "Relationships",
                version
            )
        | _ ->
            // Use dynamic query builder for known versions
            BatchProcessor.CreateDynamic(
                QueryBuilders.buildRelationshipQueryWithParams,
                Some(QueryBuilders.buildRelationshipCountQuery version),
                "Relationships",
                version
            )

let exportNodesUnified
    (session: SafeSession)
    (fileStream: FileStream)
    (exportCtx: ExportContext)
    (exportState: ExportState)
    (processor: BatchProcessor)
    : Async<Result<(ExportProgress * LabelStatsTracker.Tracker * LineTrackingState), AppError>> =
    async {
        Log.info "Exporting nodes with label statistics..."

        let initialState: NodeExportState =
            { LineState =
                exportCtx.Progress.LineState
                |> LineTracking.recordTypeStart "node"
              LabelTracker = LabelStatsTracker.create () }

        // Handler function is called for each record in the batch
        // It threads state through the entire export process, accumulating label statistics
        // The handler pattern enables different export scenarios without modifying core batch logic
        let nodeHandler (state: NodeExportState) (record: IRecord) (bytesWritten: int64) : NodeExportState =
            exportCtx.Error.Funcs.IncrementLine()

            let newLineState =
                state.LineState |> LineTracking.incrementLine

            let labels =
                try
                    record.["labels"].As<List<obj>>()
                    |> Seq.map (fun o -> o.ToString())
                    |> Seq.toList
                with _ ->
                    []

            // Handle both labeled and unlabeled nodes
            let newLabelTracker =
                if List.isEmpty labels then
                    // Track unlabeled nodes under a special category
                    state.LabelTracker
                    |> LabelStatsTracker.startLabel "_unlabeled"
                    |> LabelStatsTracker.updateLabel "_unlabeled" 1L bytesWritten
                else
                    // Distribute bytes evenly among labels
                    let bytesPerLabel =
                        bytesWritten / int64 labels.Length
                    // List.fold threads the tracker through each label update
                    // This is the functional equivalent of a foreach loop with mutable state
                    labels
                    |> List.fold
                        (fun tracker label ->
                            tracker
                            |> LabelStatsTracker.startLabel label
                            |> LabelStatsTracker.updateLabel label 1L bytesPerLabel)
                        state.LabelTracker

            { LineState = newLineState
              LabelTracker = newLabelTracker }

        let batchCtx =
            BatchContext.createFull processor session fileStream (exportCtx.Config.JsonBufferSizeKb * 1024)

        let versionOpt =
            match processor.QueryBuilder with
            | Some _ -> Some processor.Version
            | None -> None

        match! processBatchedQuery batchCtx exportCtx exportState initialState nodeHandler versionOpt with
        | Error e -> return Error e
        | Ok(finalStats, finalState) ->
            batchCtx.Buffer.Clear()

            // Log ID mapping statistics
            Log.info (sprintf "Node export complete. Stable ID mappings created: %d" exportState.NodeIdMapping.Count)

            return Ok(finalStats, finalState.LabelTracker, finalState.LineState)
    }

let exportRelationships
    (session: SafeSession)
    (fileStream: FileStream)
    (exportCtx: ExportContext)
    (exportState: ExportState)
    (processor: BatchProcessor)
    : Async<Result<(ExportProgress * LineTrackingState), AppError>> =
    async {
        Log.info "Exporting relationships..."

        let initialState: RelationshipExportState =
            exportCtx.Progress.LineState
            |> LineTracking.recordTypeStart "relationship"

        let relationshipHandler
            (state: RelationshipExportState)
            (record: IRecord)
            (bytesWritten: int64)
            : RelationshipExportState =
            exportCtx.Error.Funcs.IncrementLine()
            state |> LineTracking.incrementLine

        let batchCtx =
            BatchContext.createFull processor session fileStream (exportCtx.Config.JsonBufferSizeKb * 1024)

        let versionOpt =
            match processor.QueryBuilder with
            | Some _ -> Some processor.Version
            | None -> None

        match! processBatchedQuery batchCtx exportCtx exportState initialState relationshipHandler versionOpt with
        | Error e -> return Error e
        | Ok(finalStats, finalState) ->
            batchCtx.Buffer.Clear()
            return Ok(finalStats, finalState)
    }

let exportErrors
    (fileStream: FileStream)
    (errorFuncs: ErrorTrackingFunctions)
    (exportId: Guid)
    (lineState: LineTrackingState)
    : Async<int64 * LineTrackingState> =
    async {
        let errors = errorFuncs.Queries.GetErrors()
        // Mutable state within async block is safe - async ensures sequential execution
        let mutable count = 0L
        let mutable currentLineState = lineState

        let newlineBytes =
            Encoding.UTF8.GetBytes Environment.NewLine

        for error in errors do
            currentLineState <-
                currentLineState
                |> LineTracking.recordTypeStart error.Type
                |> LineTracking.incrementLine

            // Manual JSON serialization provides fine control over field order and null handling
            // MemoryStream collects the JSON bytes before writing to file
            use memoryStream = new MemoryStream()

            use writer =
                new Utf8JsonWriter(memoryStream, JsonConfig.createWriterOptions ())

            writer.WriteStartObject()
            writer.WriteString("type", error.Type)
            writer.WriteString("export_id", exportId.ToString())
            writer.WriteString("timestamp", error.Timestamp.ToString("O"))

            match error.Line with
            | Some line -> writer.WriteNumber("line", line)
            | None -> ()

            writer.WriteString("message", error.Message)

            match error.ElementId with
            | Some id -> writer.WriteString("element_id", id)
            | None -> ()

            match error.Details with
            | Some details ->
                writer.WritePropertyName("details")
                writer.WriteStartObject()

                for kvp in details do
                    writer.WritePropertyName(kvp.Key)
                    JsonHelpers.writeJsonValue writer kvp.Value

                writer.WriteEndObject()
            | None -> ()

            writer.WriteEndObject()
            writer.Flush()

            let bytes = memoryStream.ToArray()
            fileStream.Write(bytes, 0, bytes.Length)
            fileStream.Write(newlineBytes, 0, newlineBytes.Length)
            count <- count + 1L

        return (count, currentLineState)
    }

let finalizeExport _ (config: ExportConfig) (metadata: FullMetadata) (tempFile: string) (stats: CompletedExportStats) =
    async {
        try
            Log.info "Finalizing export..."
            let tempInfo = FileInfo tempFile

            if not tempInfo.Exists then
                return Error(FileSystemError(tempFile, "Temporary file not found", None))
            elif tempInfo.Length = 0L then
                return Error(ExportError("Export produced empty file", None))
            else
                let finalPath =
                    Configuration.generateMetadataFilename config.OutputDirectory metadata

                Log.info (sprintf "Moving %s -> %s" (Path.GetFileName tempFile) (Path.GetFileName finalPath))
                File.Move(tempFile, finalPath, true)

                Log.info (sprintf "Export successful: %s" finalPath)
                Log.info (sprintf "File size: %s" (Utils.formatBytes (FileInfo(finalPath).Length)))

                Log.info (sprintf "Records: %d exported, %d skipped" stats.RecordsProcessed stats.RecordsSkipped)

                return Ok()
        with ex ->
            return Error(FileSystemError(tempFile, "Failed to finalize export", Some ex))
    }
