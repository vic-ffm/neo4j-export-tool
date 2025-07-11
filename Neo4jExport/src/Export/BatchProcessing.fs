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
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.Json
open System.Buffers
open System.Diagnostics
open Neo4j.Driver
open Neo4jExport
open Neo4jExport.ExportTypes
open Neo4jExport.SerializationEngine
open Neo4jExport.ErrorDeduplication
open Neo4jExport.Neo4jExportToolId
open ErrorTracking

let private isValidStableId (id: string) =
    let isHexChar = function
        | c when c >= '0' && c <= '9' -> true
        | c when c >= 'a' && c <= 'f' -> true
        | _ -> false
    
    id.Length = 64 && id |> Seq.forall isHexChar

let reportProgress
    (stats: ExportProgress)
    (startTime: DateTime)
    (progressInterval: TimeSpan)
    (lastProgress: DateTime)
    (totalOpt: int64 option)
    (entityType: string)
    =
    ProgressOperations.report stats startTime progressInterval lastProgress totalOpt entityType

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
        // struct tuples are value types, avoiding heap allocation in hot paths
        // The (value, success) pattern avoids exceptions for control flow
        struct (node.ElementId, true)
    with _ ->
        struct ("", false)

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

let private incrementStats (stats: ExportProgress) =
    { stats with
        RecordsProcessed = stats.RecordsProcessed + 1L }

/// Truncate and convert Neo4j temporal values to .NET types to avoid ValueTruncationException
/// 
/// Neo4j stores temporal values with nanosecond precision (e.g., 2024-08-15T21:40:08.623060588Z)
/// but .NET DateTime only supports 100-nanosecond ticks. When the Neo4j driver tries to
/// convert a value with precision that would be lost (e.g., 88 nanoseconds), it throws
/// a ValueTruncationException to prevent silent data loss.
/// 
/// This function truncates Neo4j temporal values to .NET-compatible precision and converts
/// them to the appropriate .NET temporal types. The serialization engine will then handle
/// these typed values correctly, producing proper ISO format strings in the output.
let private truncateAndConvertTemporal (value: obj) : obj =
    match value with
    // Already .NET types - pass through unchanged
    | :? DateTime 
    | :? DateTimeOffset -> value
    
    // Neo4j temporal types - convert with truncation
    | :? Neo4j.Driver.LocalDate -> 
        // LocalDate has no time component and doesn't need truncation
        // Return unchanged to preserve date-only semantics
        value
    
    | :? Neo4j.Driver.LocalTime as lt -> 
        // Keep as LocalTime but truncate nanoseconds to 100ns precision
        let truncatedNanos = (lt.Nanosecond / 100) * 100
        box (LocalTime(lt.Hour, lt.Minute, lt.Second, truncatedNanos))
    
    | :? Neo4j.Driver.LocalDateTime as ldt -> 
        // Keep as LocalDateTime but truncate nanoseconds to 100ns precision
        let truncatedNanos = (ldt.Nanosecond / 100) * 100
        box (LocalDateTime(ldt.Year, ldt.Month, ldt.Day, ldt.Hour, ldt.Minute, ldt.Second, truncatedNanos))
    
    | :? Neo4j.Driver.OffsetTime as ot ->
        // Keep as OffsetTime but truncate nanoseconds to 100ns precision
        let truncatedNanos = (ot.Nanosecond / 100) * 100
        box (OffsetTime(ot.Hour, ot.Minute, ot.Second, truncatedNanos, ot.OffsetSeconds))
    
    | :? Neo4j.Driver.ZonedDateTime as zdt ->
        // Keep as ZonedDateTime but truncate nanoseconds to 100ns precision
        // This preserves timezone information (both offset and zone name)
        let truncatedNanos = (zdt.Nanosecond / 100) * 100
        // Use the UtcSeconds constructor to avoid deprecated constructor
        box (ZonedDateTime(zdt.UtcSeconds, truncatedNanos, zdt.Zone))
    
    | :? Neo4j.Driver.Duration as d ->
        // Duration remains as-is - the serialization engine handles it
        value
    
    | _ -> value

let processNodeRecord (exportState: ExportState) (recordCtx: RecordContext<BatchErrorAccumulator>) (exportCtx: ExportContext) (record: IRecord) =
    let ctx =
        SerializationContext.createWriterContext exportCtx.Config exportCtx.Error.Funcs exportCtx.Error.ExportId

    use writer =
        new Utf8JsonWriter(recordCtx.Buffer, JsonConfig.createWriterOptions ())

    try
        let elementId = record.["elementId"].As<string>()
        let labels = record.["labels"].As<List<obj>>() |> Seq.cast<string> |> Seq.toList
        
        // Both Neo4j 4.x and 5.x+ now return raw nodes to minimize DB load
        // We handle temporal conversion here in the export tool rather than in the query
        let properties = 
            if record.Keys |> Seq.exists (fun k -> k = "node") then
                // Extract properties from the raw node
                let node = record.["node"].As<INode>()
                let d = Dictionary<string, obj>()
                // CRITICAL: Truncate and convert temporal values to .NET types
                // This prevents ValueTruncationException when accessing high-precision temporal values
                for kvp in node.Properties do
                    d.Add(kvp.Key, truncateAndConvertTemporal kvp.Value)
                d :> IReadOnlyDictionary<string, obj>
            else
                // Fallback for any future query formats that pre-process properties
                let propsList = record.["properties"].As<List<obj>>()
                let d = Dictionary<string, obj>()
                for prop in propsList do
                    let propMap = prop :?> IReadOnlyDictionary<string, obj>
                    d.Add(propMap.["key"].As<string>(), propMap.["value"])
                d :> IReadOnlyDictionary<string, obj>
        
        // The properties dictionary is now guaranteed to be safe
        let propsDict = 
            properties 
            |> Seq.map (fun kvp -> kvp.Key, kvp.Value)
            |> dict
        let stableId = Neo4jExportToolId.generateNodeId labels propsDict
        
        // Only validate in debug mode to avoid hot path overhead
        if exportCtx.Config.EnableDebugLogging && not (isValidStableId stableId) then
            Log.warn (sprintf "Invalid stable ID format for node %s: %s" elementId stableId)
        
        exportState.NodeIdMapping.TryAdd(elementId, stableId) |> ignore
        
        // Use direct serialization to avoid object allocation in hot path
        writeNodeDirect writer elementId stableId (labels :> IReadOnlyList<string>) properties ctx
    with ex ->
        let elementId = ""

        // Use deduplication instead of direct tracking
        trackSerializationErrorDedup recordCtx.ErrorAccumulator ex elementId "node" "SerializationError"

        writer.WriteStartObject()
        writer.WriteString("type", "node")
        writer.WriteString("element_id", elementId)
        writer.WriteString("NET_node_content_hash", "")
        writer.WriteString("export_id", ctx.ExportId.ToString())
        writer.WriteStartArray "labels"
        writer.WriteEndArray()
        writer.WriteStartObject "properties"
        writer.WriteString("_export_error", "serialization_failed")
        writer.WriteString("_original_element_id", elementId)
        writer.WriteEndObject()
        writer.WriteEndObject()

    writer.Flush()

    let dataBytes =
        int64 recordCtx.Buffer.WrittenCount

    dataBytes, incrementStats recordCtx.Stats


let processRelationshipRecord
    (exportState: ExportState)
    (recordCtx: RecordContext<BatchErrorAccumulator>)
    (exportCtx: ExportContext)
    (record: IRecord)
    =
    let ctx =
        SerializationContext.createWriterContext exportCtx.Config exportCtx.Error.Funcs exportCtx.Error.ExportId

    use writer =
        new Utf8JsonWriter(recordCtx.Buffer, JsonConfig.createWriterOptions ())

    try
        let elementId = record.["elementId"].As<string>()
        let relType = record.["type"].As<string>()
        let startNodeId = record.["startNodeElementId"].As<string>()
        let endNodeId = record.["endNodeElementId"].As<string>()
        
        // Both Neo4j 4.x and 5.x+ now return raw relationships to minimize DB load
        // We handle temporal conversion here in the export tool rather than in the query
        let properties = 
            if record.Keys |> Seq.exists (fun k -> k = "relationship") then
                // Extract properties from the raw relationship
                let rel = record.["relationship"].As<IRelationship>()
                let d = Dictionary<string, obj>()
                // CRITICAL: Truncate and convert temporal values to .NET types
                // This prevents ValueTruncationException when accessing high-precision temporal values
                for kvp in rel.Properties do
                    d.Add(kvp.Key, truncateAndConvertTemporal kvp.Value)
                d :> IReadOnlyDictionary<string, obj>
            else
                // Fallback for any future query formats that pre-process properties
                let propsList = record.["properties"].As<List<obj>>()
                let d = Dictionary<string, obj>()
                for prop in propsList do
                    let propMap = prop :?> IReadOnlyDictionary<string, obj>
                    d.Add(propMap.["key"].As<string>(), propMap.["value"])
                d :> IReadOnlyDictionary<string, obj>
        
        // Lookup stable IDs for nodes
        let startStableId = 
            match exportState.NodeIdMapping.TryGetValue(startNodeId) with
            | true, id -> id
            | false, _ -> 
                // Fallback: use element ID if stable ID not found
                ctx.ErrorFuncs.TrackWarning 
                    (sprintf "Stable ID not found for start node %s" startNodeId) 
                    (Some elementId) 
                    None
                startNodeId
        
        let endStableId = 
            match exportState.NodeIdMapping.TryGetValue(endNodeId) with
            | true, id -> id
            | false, _ -> 
                // Fallback: use element ID if stable ID not found
                ctx.ErrorFuncs.TrackWarning 
                    (sprintf "Stable ID not found for end node %s" endNodeId) 
                    (Some elementId) 
                    None
                endNodeId
        
        // The properties dictionary is now guaranteed to be safe
        let propsDict = 
            properties 
            |> Seq.map (fun kvp -> kvp.Key, kvp.Value)
            |> dict
        // Generate identity hash using element IDs (stable within database)
        let identityHash = Neo4jExportToolId.generateRelationshipIdentityHash relType startNodeId endNodeId propsDict
        
        // Only validate in debug mode to avoid hot path overhead
        if exportCtx.Config.EnableDebugLogging && not (isValidStableId identityHash) then
            Log.warn (sprintf "Invalid identity hash format for relationship %s: %s" elementId identityHash)
        
        let ids =
            { ElementId = elementId
              StableId = identityHash  // The relationship identity hash
              StartElementId = startNodeId
              StartStableId = startStableId  // Start node content hash
              EndElementId = endNodeId
              EndStableId = endStableId }  // End node content hash

        // Use direct serialization to avoid object allocation in hot path
        writeRelationshipDirect writer relType properties ids ctx
    with ex ->
        // Failed to parse relationship from record
        trackSerializationErrorDedup recordCtx.ErrorAccumulator ex "" "relationship" "RecordAccessError"

        writer.WriteStartObject()
        writer.WriteString("type", "relationship")
        writer.WriteString("element_id", "")
        writer.WriteString("NET_rel_identity_hash", "")
        writer.WriteString("export_id", ctx.ExportId.ToString())
        writer.WriteString("label", "_UNKNOWN")
        writer.WriteString("start_element_id", "")
        writer.WriteString("end_element_id", "")
        writer.WriteString("start_node_content_hash", "")
        writer.WriteString("end_node_content_hash", "")
        writer.WriteStartObject "properties"
        writer.WriteString("_export_error", "relationship_access_failed")
        writer.WriteString("_error_message", (ErrorAccumulation.exceptionToString ex))
        writer.WriteEndObject()
        writer.WriteEndObject()

    writer.Flush()

    let dataBytes =
        int64 recordCtx.Buffer.WrittenCount

    dataBytes, incrementStats recordCtx.Stats


module BatchContext =
    let createFull processor session fileStream bufferSize =
        { Processor = processor
          Session = session
          FileStream = fileStream
          // ArrayBufferWriter provides efficient, reusable byte buffer for JSON serialization
          // Avoids allocating new arrays for each record
          Buffer = new ArrayBufferWriter<byte>(bufferSize)
          NewlineBytes = Encoding.UTF8.GetBytes Environment.NewLine
          // Error accumulator deduplicates errors within each batch to prevent log spam
          ErrorAccumulator = createAccumulator 100 }

    let createRecordContext buffer errorAccumulator stats =
        { RecordContext.Buffer = buffer
          ErrorAccumulator = errorAccumulator
          Stats = stats }

    let dispose (ctx: BatchContext<_>) = ctx.Buffer.Clear()

module private KeysetPagination =
    // Keyset pagination uses the last seen ID to fetch the next batch
    // This is O(1) per batch vs O(n) for SKIP, making it essential for large datasets
    // The approach differs between Neo4j versions due to ID system changes
    let extractId (version: Neo4jVersion) (entityType: string) (record: IRecord) : KeysetId option =
        try
            match version with
            | V4x ->
                // Neo4j 4.x uses different field names for nodes and relationships
                let fieldName =
                    match entityType with
                    | "Nodes" -> "nodeId"
                    | "Relationships" -> "relId"
                    | _ -> failwithf "Unknown entity type: %s" entityType
                let id = record.[fieldName].As<int64>()
                Some(NumericId id)
            | V5x | V6x ->
                // Neo4j 5.x+ uses elementId for both nodes and relationships
                let id = record.["elementId"].As<string>()
                Some(ElementId id)
            | Unknown -> None
        with ex ->
            Log.warn $"Failed to extract ID from {entityType}: {ex.Message}"
            None

    let updateHighest (current: KeysetId option) (newId: KeysetId option) : KeysetId option =
        match current, newId with
        | None, Some id -> Some id
        | Some curr, Some new' when KeysetId.compare new' curr > 0 -> Some new'
        | _ -> current

/// Enhanced batch processing supporting both SKIP/LIMIT and keyset pagination
/// Generic over handler state ('state) to allow different export scenarios to maintain
/// their own state through the pagination process (e.g., label statistics tracking)
let processBatchedQuery<'state>
    (batchCtx: BatchContext<BatchErrorAccumulator>)
    (exportCtx: ExportContext)
    (exportState: ExportState)
    (handlerState: 'state)
    (handler: RecordHandler<'state>)
    (version: Neo4jVersion option) // NEW: Optional parameter for keyset pagination (None = use legacy SKIP/LIMIT)
    : Async<Result<ExportProgress * 'state, AppError>> =
    async {
        // Determine which performance tracker to use based on entity type
        let perfTracker = 
            match batchCtx.Processor.EntityName with
            | "Nodes" -> exportState.NodePerfTracker
            | "Relationships" -> exportState.RelPerfTracker
            | _ -> failwithf "Unknown entity type: %s" batchCtx.Processor.EntityName
        
        let stopwatch = Stopwatch()
        
        let progressInterval =
            TimeSpan.FromSeconds 30.0

        let batchSize = exportCtx.Config.BatchSize

        // Get total count if available
        let! totalOpt =
            match batchCtx.Processor.GetTotalQuery with
            | Some query ->
                async {
                    let! countResult = batchCtx.Session.RunAsync(query)
                    let! hasCount = countResult.FetchAsync() |> Async.AwaitTask

                    let total =
                        if hasCount then
                            countResult.Current.["count"].As<int64>()
                        else
                            0L

                    Log.info (sprintf "Total %s to export: %d" (batchCtx.Processor.EntityName.ToLower()) total)
                    return Some total
                }
            | None -> async { return None }

        // Determine initial pagination strategy
        let initialStrategy =
            match version, batchCtx.Processor.QueryBuilder with
            | Some v, Some _ -> 
                Log.info (sprintf "Using keyset pagination for %s (Neo4j %A)" 
                    (batchCtx.Processor.EntityName.ToLower()) v)
                Keyset(None, v) // Use keyset if version provided and processor supports it
            | _ -> 
                Log.warn (sprintf "Using SKIP/LIMIT pagination for %s (legacy mode)" 
                    (batchCtx.Processor.EntityName.ToLower()))
                
                // Track this as a performance warning
                exportCtx.Error.Funcs.TrackWarning 
                    "Fallback to SKIP/LIMIT pagination due to unknown version or missing query builder" 
                    None 
                    (Some (dict ["entity", JString batchCtx.Processor.EntityName; 
                                 "impact", JString "O(n²) performance"]))
                
                SkipLimit 0 // Otherwise use legacy SKIP/LIMIT

        // Recursive function implements the pagination loop
        // Each call processes one batch and decides whether to continue
        // The 'rec' keyword enables self-recursion
        let rec processBatch
            (currentStats: ExportProgress)
            (lastProgress: DateTime)
            (paginationState: PaginationStrategy)
            (currentHandlerState: 'state)
            =
            async {
                stopwatch.Restart()
                
                // Shadow the parameter with a mutable local for accumulation within the batch
                // This avoids threading state through every record processing call
                let mutable currentHandlerState =
                    currentHandlerState

                if exportCtx.Workflow.IsCancellationRequested() then
                    return Ok(currentStats, currentHandlerState)
                else
                    // Build query and parameters based on strategy
                    let query, parameters =
                        match batchCtx.Processor.QueryBuilder, paginationState with
                        | Some builder, _ ->
                            // Use dynamic query builder with version from processor
                            let q, p = builder batchCtx.Processor.Version paginationState batchSize
                            Log.debug (sprintf "Executing %s query with %A" 
                                batchCtx.Processor.EntityName paginationState)
                            q, p
                        | None, SkipLimit skip ->
                            // Use legacy static query
                            match batchCtx.Processor.Query with
                            | Some q ->
                                let queryParams =
                                    dict
                                        [ "skip", box skip
                                          "limit", box batchSize ]
                                Log.debug (sprintf "Executing legacy %s query with skip=%d limit=%d" 
                                    batchCtx.Processor.EntityName skip batchSize)
                                q, queryParams
                            | None -> failwith "BatchProcessor must have either Query or QueryBuilder"
                        | None, Keyset _ -> failwith "Cannot use keyset pagination with static query processor"

                    // Check if we should skip this batch (for SKIP/LIMIT with total count)
                    let shouldSkipBatch =
                        match paginationState, totalOpt with
                        | SkipLimit skip, Some total when int64 skip >= total -> true
                        | _ -> false

                    if shouldSkipBatch then
                        return Ok(currentStats, currentHandlerState)
                    else
                        let! cursor = batchCtx.Session.RunAsync(query, parameters)

                        let mutable batchStats = currentStats
                        let mutable recordCount = 0
                        let mutable hasMore = true

                        let mutable highestId: KeysetId option =
                            match paginationState with
                            | Keyset(lastId, _) -> lastId
                            | _ -> None

                        clearAccumulator batchCtx.ErrorAccumulator

                        while hasMore do
                            let! fetchResult = cursor.FetchAsync() |> Async.AwaitTask

                            if fetchResult then
                                recordCount <- recordCount + 1
                                let record = cursor.Current

                                batchCtx.Buffer.Clear()

                                // Create record context for processing
                                let recordCtx =
                                    BatchContext.createRecordContext
                                        batchCtx.Buffer
                                        batchCtx.ErrorAccumulator
                                        batchStats

                                let dataBytes, newStats =
                                    match batchCtx.Processor.EntityName with
                                    | "Nodes" -> processNodeRecord exportState recordCtx exportCtx record
                                    | "Relationships" -> processRelationshipRecord exportState recordCtx exportCtx record
                                    | _ -> failwithf "Unsupported entity type: %s" batchCtx.Processor.EntityName

                                // Write to file
                                batchCtx.FileStream.Write batchCtx.Buffer.WrittenSpan
                                batchCtx.FileStream.Write(batchCtx.NewlineBytes, 0, batchCtx.NewlineBytes.Length)

                                batchStats <-
                                    { newStats with
                                        BytesWritten =
                                            newStats.BytesWritten
                                            + dataBytes
                                            + int64 batchCtx.NewlineBytes.Length }

                                let newHandlerState =
                                    handler currentHandlerState record dataBytes

                                currentHandlerState <- newHandlerState

                                // Extract ID for keyset pagination if applicable
                                match paginationState with
                                | Keyset(_, version) ->
                                    let extractedId =
                                        KeysetPagination.extractId version batchCtx.Processor.EntityName record

                                    highestId <- KeysetPagination.updateHighest highestId extractedId
                                | _ -> ()
                            else
                                hasMore <- false

                        batchCtx.FileStream.Flush()
                        
                        // Record batch performance
                        let batchDurationMs = stopwatch.Elapsed.TotalMilliseconds
                        perfTracker.RecordBatch(batchDurationMs)
                        
                        // Log performance periodically
                        if recordCount > 0 && batchStats.RecordsProcessed % 10000L = 0L then
                            let metrics = perfTracker.GetMetrics(paginationState)
                            Log.info (sprintf "Batch performance - Strategy: %s, Avg time: %.2fms, Trend: %s" 
                                metrics.Strategy metrics.AverageBatchTimeMs metrics.PerformanceTrend)

                        flushErrors batchCtx.ErrorAccumulator exportCtx.Error.Funcs (int64 recordCount)

                        if recordCount = 0 then
                            return Ok(batchStats, currentHandlerState)
                        else
                            let newLastProgress =
                                match exportCtx.Reporting with
                                | Some ops ->
                                    ops.ReportProgress
                                        batchCtx.Processor.EntityName
                                        batchStats.RecordsProcessed
                                        totalOpt
                                | None -> lastProgress

                            // Determine next pagination state
                            let nextPaginationState =
                                match paginationState with
                                | SkipLimit skip -> 
                                    let next = SkipLimit(skip + batchSize)
                                    Log.debug (sprintf "Next batch: skip=%d" (skip + batchSize))
                                    next
                                | Keyset(_, version) ->
                                    if recordCount = batchSize && highestId.IsSome then
                                        let next = Keyset(highestId, version)
                                        Log.debug (sprintf "Next batch: using keyset ID %A" highestId.Value)
                                        next
                                    else
                                        // No more records
                                        Log.debug "No more records to process"
                                        paginationState

                            // Continue if we got a full batch
                            if recordCount = batchSize then
                                // Critical check: Prevent infinite loop when using keyset pagination
                                match paginationState, nextPaginationState with
                                | Keyset(prevId, _), Keyset(nextId, _) when prevId = nextId ->
                                    // We processed a full batch but couldn't advance the pagination cursor
                                    // This means ID extraction failed for all records in the batch
                                    return Error(PaginationError(
                                        batchCtx.Processor.EntityName,
                                        sprintf "Unable to advance pagination after processing %d records. This typically occurs when the ID field (nodeId/relId) cannot be extracted from query results. Check that your Neo4j query returns the expected fields." recordCount))
                                | _ ->
                                    return! processBatch batchStats newLastProgress nextPaginationState currentHandlerState
                            else
                                return Ok(batchStats, currentHandlerState)
            }

        let! result = processBatch exportCtx.Progress.Stats DateTime.UtcNow initialStrategy handlerState

        match result with
        | Ok(stats, state) -> return Ok(stats, state)
        | Error e -> return Error e
    }
