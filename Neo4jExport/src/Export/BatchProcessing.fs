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

module Neo4jExport.ExportBatchProcessing

open System
open System.IO
open System.Text
open System.Text.Json
open System.Buffers
open Neo4j.Driver
open Neo4jExport
open Neo4jExport.ExportTypes
open Neo4jExport.ExportUtils
open Neo4jExport.SerializationEngine
open ErrorTracking

/// Common progress reporting logic
let reportProgress
    (stats: ExportProgress)
    (startTime: DateTime)
    (progressInterval: TimeSpan)
    (lastProgress: DateTime)
    (totalOpt: int64 option)
    (entityType: string)
    =
    let now = DateTime.UtcNow

    if now - lastProgress > progressInterval then
        let rate =
            float stats.RecordsProcessed
            / (now - startTime).TotalSeconds

        let message =
            match totalOpt with
            | Some total ->
                sprintf
                    "%s: %d/%d exported (%.0f records/sec, %s written)"
                    entityType
                    stats.RecordsProcessed
                    total
                    rate
                    (Utils.formatBytes stats.BytesWritten)
            | None ->
                sprintf
                    "%s: %d exported (%.0f records/sec, %s written)"
                    entityType
                    stats.RecordsProcessed
                    rate
                    (Utils.formatBytes stats.BytesWritten)

        Log.info message
        now
    else
        lastProgress

let private tryGetNode (record: IRecord) =
    try
        Ok(record.["n"].As<INode>())
    with ex ->
        Error ex

let private tryGetRelationship (record: IRecord) =
    try
        Ok(record.["r"].As<IRelationship>())
    with ex ->
        Error ex

let private tryExtractElementId (node: INode) =
    try
        struct (node.ElementId, true)
    with _ ->
        struct ("", false)

let private tryExtractRelIds (rel: IRelationship) (startNode: INode) (endNode: INode) =
    try
        struct (rel.ElementId, startNode.ElementId, endNode.ElementId, true)
    with _ ->
        struct ("", "", "", false)

let private tryExtractRelationshipIdsDirectly (rel: IRelationship) =
    try
        // Extract IDs directly from the relationship object without needing node instances
        let relId = rel.ElementId
        let startId = rel.StartNodeElementId
        let endId = rel.EndNodeElementId
        let relType = rel.Type
        struct (relId, startId, endId, relType, true)
    with _ ->
        struct ("", "", "", "", false)

let private tryGetNodes (startVal: obj) (endVal: obj) =
    try
        let startNode = startVal.As<INode>()
        let endNode = endVal.As<INode>()
        Ok (startNode, endNode)
    with ex ->
        Error ex

let private incrementStats (stats: ExportProgress) =
    { stats with
        RecordsProcessed = stats.RecordsProcessed + 1L }

/// Common node processing logic
let processNodeRecord (buffer: ArrayBufferWriter<byte>) record exportId stats errorTracker config =
    let ctx =
        SerializationContext.createWriterContext config errorTracker exportId

    use writer =
        new Utf8JsonWriter(buffer, JsonConfig.createWriterOptions ())

    match tryGetNode record with
    | Ok node ->
        let elementId = node.ElementId
        writeNode writer node elementId ctx
    | Error ex ->
        let elementId = ""

        trackSerializationError
            ctx.ErrorTracker
            (sprintf "Node serialization failed: %s" ex.Message)
            elementId
            "node"
            (ex.GetType().Name)

        writer.WriteStartObject()
        writer.WriteString("type", "node")
        writer.WriteString("element_id", elementId)
        writer.WriteString("export_id", ctx.ExportId.ToString())
        writer.WriteStartArray "labels"
        writer.WriteEndArray()
        writer.WriteStartObject "properties"
        writer.WriteString("_export_error", "serialization_failed")
        writer.WriteString("_original_element_id", elementId)
        writer.WriteEndObject()
        writer.WriteEndObject()

    writer.Flush()
    let dataBytes = int64 buffer.WrittenCount

    dataBytes, incrementStats stats

/// Common relationship processing logic
let processRelationshipRecord (buffer: ArrayBufferWriter<byte>) record exportId stats errorTracker config =
    let ctx =
        SerializationContext.createWriterContext config errorTracker exportId

    use writer =
        new Utf8JsonWriter(buffer, JsonConfig.createWriterOptions ())

    match tryGetRelationship record, record.TryGetValue("s"), record.TryGetValue("t") with
    | Ok rel, (true, startVal), (true, endVal) ->
        // Phase 1 & 2: Extract IDs directly from relationship first (safe operation)
        let struct (relId, startId, endId, relType, idsExtracted) = tryExtractRelationshipIdsDirectly rel
        
        // Phase 3: Try to get nodes for full serialization
        match tryGetNodes startVal endVal with
        | Ok (startNode, endNode) ->
            // We have nodes, use them for full serialization
            let ids =
                { ElementId = relId
                  StartElementId = startId
                  EndElementId = endId }
            
            writeRelationship writer rel ids ctx
        | Error ex ->
            // Phase 4: Node casting failed, but we already have IDs from the relationship
            trackSerializationError
                ctx.ErrorTracker
                (sprintf "Failed to cast nodes for relationship serialization: %s" ex.Message)
                relId
                "relationship"
                (ex.GetType().Name)

            // Write error record with the IDs we successfully extracted
            writer.WriteStartObject()
            writer.WriteString("type", "relationship")
            writer.WriteString("element_id", relId)
            writer.WriteString("export_id", ctx.ExportId.ToString())
            writer.WriteString("label", if String.IsNullOrEmpty(relType) then "_UNKNOWN" else relType)
            writer.WriteString("start_element_id", startId)
            writer.WriteString("end_element_id", endId)
            writer.WriteStartObject "properties"
            writer.WriteString("_export_error", "node_cast_failed")
            writer.WriteString("_error_message", ex.Message)
            writer.WriteEndObject()
            writer.WriteEndObject()
    | _ ->
        trackSerializationError
            ctx.ErrorTracker
            "Failed to extract relationship or nodes from record"
            ""
            "relationship"
            "RecordAccessError"

        writer.WriteStartObject()
        writer.WriteString("type", "relationship")
        writer.WriteString("element_id", "")
        writer.WriteString("export_id", ctx.ExportId.ToString())
        writer.WriteString("label", "_UNKNOWN")
        writer.WriteString("start_element_id", "")
        writer.WriteString("end_element_id", "")
        writer.WriteStartObject "properties"
        writer.WriteString("_export_error", "relationship_access_failed")
        writer.WriteEndObject()
        writer.WriteEndObject()

    writer.Flush()
    let dataBytes = int64 buffer.WrittenCount

    dataBytes, incrementStats stats

/// Generic batch processing function following functional composition
let processBatchedQuery<'state>
    (processor: BatchProcessor)
    (context: ApplicationContext)
    (session: SafeSession)
    (config: ExportConfig)
    (fileStream: FileStream)
    (initialStats: ExportProgress)
    (exportId: Guid)
    (errorTracker: ErrorTracker)
    (handlerState: 'state)
    (handler: RecordHandler<'state>)
    : Async<Result<ExportProgress * 'state, AppError>> =
    async {
        let bufferSize =
            config.JsonBufferSizeKb * 1024

        let buffer =
            new ArrayBufferWriter<byte>(bufferSize)

        let newlineBytes =
            Encoding.UTF8.GetBytes Environment.NewLine

        let progressInterval =
            TimeSpan.FromSeconds 30.0

        let batchSize = config.BatchSize

        let! totalOpt =
            match processor.GetTotalQuery with
            | Some query ->
                async {
                    let! countResult = session.RunAsync(query)
                    let! hasCount = countResult.FetchAsync() |> Async.AwaitTask

                    let total =
                        if hasCount then
                            countResult.Current.["count"].As<int64>()
                        else
                            0L

                    Log.info (sprintf "Total %s to export: %d" (processor.EntityName.ToLower()) total)
                    return Some total
                }
            | None -> async { return None }

        let rec processBatch
            (currentStats: ExportProgress)
            (lastProgress: DateTime)
            (skip: int)
            (currentHandlerState: 'state)
            =
            async {
                let mutable currentHandlerState =
                    currentHandlerState

                if AppContext.isCancellationRequested context then
                    return Ok(currentStats, currentHandlerState)
                elif totalOpt.IsSome && int64 skip >= totalOpt.Value then
                    return Ok(currentStats, currentHandlerState)
                else
                    let parameters =
                        dict
                            [ "skip", box skip
                              "limit", box batchSize ]

                    let! cursor = session.RunAsync(processor.Query, parameters)

                    let mutable batchStats = currentStats
                    let mutable recordCount = 0
                    let mutable hasMore = true

                    while hasMore do
                        let! fetchResult = cursor.FetchAsync() |> Async.AwaitTask

                        if fetchResult then
                            recordCount <- recordCount + 1
                            let record = cursor.Current

                            buffer.Clear()

                            let dataBytes, newStats =
                                processor.ProcessRecord buffer record exportId batchStats errorTracker config

                            fileStream.Write buffer.WrittenSpan
                            fileStream.Write(newlineBytes, 0, newlineBytes.Length)

                            batchStats <-
                                { newStats with
                                    BytesWritten =
                                        newStats.BytesWritten
                                        + dataBytes
                                        + int64 newlineBytes.Length }

                            let newHandlerState =
                                handler currentHandlerState record dataBytes

                            currentHandlerState <- newHandlerState
                        else
                            hasMore <- false

                    fileStream.Flush()

                    if recordCount = 0 then
                        return Ok(batchStats, currentHandlerState)
                    else
                        let newLastProgress =
                            reportProgress
                                batchStats
                                initialStats.StartTime
                                progressInterval
                                lastProgress
                                totalOpt
                                processor.EntityName

                        return! processBatch batchStats newLastProgress (skip + batchSize) currentHandlerState
            }

        let! result = processBatch initialStats DateTime.UtcNow 0 handlerState

        match result with
        | Ok(stats, state) -> return Ok(stats, state)
        | Error e -> return Error e
    }
