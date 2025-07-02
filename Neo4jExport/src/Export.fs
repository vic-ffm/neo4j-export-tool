namespace Neo4jExport

open System
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.Json
open System.Buffers
open Neo4j.Driver
open ErrorTracking

/// Core export functionality for streaming Neo4j data to JSONL format
/// This module uses JavaScriptEncoder.UnsafeRelaxedJsonEscaping throughout.
/// This is the CORRECT choice for a data export tool because:
/// - We must preserve data exactly as stored in Neo4j
/// - The output is JSONL files for data processing, not web rendering
/// - Escaping HTML chars would transform data, violating our core purpose
/// - Security is the responsibility of downstream consumers
///
/// Example: If Neo4j contains "A & B <test>", we export exactly that,
/// not "A \u0026 B \u003Ctest\u003E". Data fidelity is paramount.
module Export =
    let createLabelStatsTracker () = LabelStatsTracker.create ()

    let getLabelStatsList tracker =
        LabelStatsTracker.finalizeAndGetAllStats tracker
        |> List.sortBy (fun s -> s.Label)


    /// Safe serialization limits to prevent memory exhaustion
    [<Literal>]
    let private MaxStringLength = 10_000_000

    [<Literal>]
    let private MaxBinaryLength = 50_000_000

    [<Literal>]
    let private MaxDepth = 50

    [<Literal>]
    let private MaxCollectionItems = 10_000

    let private computeSha256 (data: byte[]) =
        use sha256 =
            Security.Cryptography.SHA256.Create()

        let hash = sha256.ComputeHash data
        Convert.ToBase64String hash

    /// Helper to write structured error when even error serialization fails
    let private writeSerializationError (writer: Utf8JsonWriter) (errorType: string) (context: string) =
        writer.WriteStartObject()
        writer.WriteString("_error", "unrecoverable_serialization_failure")
        writer.WriteString("_error_type", errorType)
        writer.WriteString("_context", context)
        writer.WriteEndObject()

    /// Track unique keys within a JSON object to prevent duplicates
    let private createKeyTracker () = Collections.Generic.HashSet<string>()

    /// Ensures property keys are unique by adding suffix if needed
    let private ensureUniqueKey (key: string) (tracker: Collections.Generic.HashSet<string>) =
        let truncatedKey =
            if key.Length > 1000 then
                key.Substring(0, 994) + "..."
            else
                key

        if tracker.Add truncatedKey then
            truncatedKey
        else
            // Key already exists, add counter suffix
            let mutable counter = 1

            let mutable uniqueKey =
                sprintf "%s_%d" truncatedKey counter

            while not (tracker.Add uniqueKey) do
                counter <- counter + 1
                uniqueKey <- sprintf "%s_%d" truncatedKey counter

            uniqueKey

    /// Recursively serializes Neo4j values with safety guards against malicious data
    let rec private serializeValue (writer: Utf8JsonWriter) (value: obj) (depth: int) =
        if depth > MaxDepth then
            writer.WriteStartObject()
            writer.WriteString("_truncated", "depth_limit")
            writer.WriteNumber("_depth", depth)

            writer.WriteString(
                "_type",
                try
                    if value = null then "null" else value.GetType().FullName
                with _ ->
                    "unknown"
            )

            writer.WriteEndObject()
            ()
        else
            try
                match value with
                | null -> writer.WriteNullValue()
                | :? string as s ->
                    if s.Length > MaxStringLength then
                        writer.WriteStartObject()
                        writer.WriteString("_truncated", "string_too_large")
                        writer.WriteNumber("_length", s.Length)
                        writer.WriteString("_prefix", s.Substring(0, min 1000 s.Length))

                        writer.WriteString(
                            "_sha256",
                            try
                                computeSha256 (Encoding.UTF8.GetBytes s)
                            with _ ->
                                "hash_failed"
                        )

                        writer.WriteEndObject()
                    else
                        writer.WriteStringValue s
                | :? bool as b -> writer.WriteBooleanValue b
                | :? int64 as i -> writer.WriteNumberValue i
                | :? int32 as i -> writer.WriteNumberValue i
                | :? int16 as i -> writer.WriteNumberValue i
                | :? uint64 as i -> writer.WriteNumberValue(decimal i)
                | :? uint32 as i -> writer.WriteNumberValue i
                | :? uint16 as i -> writer.WriteNumberValue i
                | :? byte as b -> writer.WriteNumberValue b
                | :? sbyte as b -> writer.WriteNumberValue b
                | :? decimal as d -> writer.WriteNumberValue d
                | :? double as d ->
                    if Double.IsNaN d then
                        writer.WriteStringValue "NaN"
                    elif Double.IsPositiveInfinity d then
                        writer.WriteStringValue "Infinity"
                    elif Double.IsNegativeInfinity d then
                        writer.WriteStringValue "-Infinity"
                    else
                        writer.WriteNumberValue d
                | :? float32 as f ->
                    if Single.IsNaN f then
                        writer.WriteStringValue "NaN"
                    elif Single.IsPositiveInfinity f then
                        writer.WriteStringValue "Infinity"
                    elif Single.IsNegativeInfinity f then
                        writer.WriteStringValue "-Infinity"
                    else
                        writer.WriteNumberValue(float f)
                | :? DateTime as dt -> writer.WriteStringValue(dt.ToString "O")
                | :? DateTimeOffset as dto -> writer.WriteStringValue(dto.ToString "O")
                | :? LocalDate as ld -> writer.WriteStringValue(ld.ToString())
                | :? LocalTime as lt -> writer.WriteStringValue(lt.ToString())
                | :? LocalDateTime as ldt -> writer.WriteStringValue(ldt.ToString())
                | :? ZonedDateTime as zdt -> writer.WriteStringValue(zdt.ToString())
                | :? Duration as dur -> writer.WriteStringValue(dur.ToString())
                | :? Point as pt ->
                    writer.WriteStartObject()
                    writer.WriteString("type", "Point")
                    writer.WriteNumber("srid", pt.SrId)
                    writer.WriteNumber("x", pt.X)
                    writer.WriteNumber("y", pt.Y)

                    if not (Double.IsNaN pt.Z) then
                        writer.WriteNumber("z", pt.Z)

                    writer.WriteEndObject()
                | :? (byte[]) as bytes ->
                    if bytes.Length > MaxBinaryLength then
                        writer.WriteStartObject()
                        writer.WriteString("_truncated", "binary_too_large")
                        writer.WriteNumber("_length", bytes.Length)

                        writer.WriteString(
                            "_sha256",
                            try
                                computeSha256 bytes
                            with _ ->
                                "hash_failed"
                        )

                        writer.WriteEndObject()
                    else
                        try
                            writer.WriteStringValue(Convert.ToBase64String bytes)
                        with _ ->
                            writer.WriteStartObject()
                            writer.WriteString("_type", "byte_array")
                            writer.WriteNumber("_length", bytes.Length)
                            writer.WriteString("_error", "base64_failed")
                            writer.WriteEndObject()
                | :? Collections.IList as list ->
                    writer.WriteStartArray()

                    let items =
                        list
                        |> Seq.cast<obj>
                        |> Seq.truncate MaxCollectionItems
                        |> Seq.toList

                    items
                    |> List.iter (fun item -> serializeValue writer item (depth + 1))

                    if list.Count > MaxCollectionItems then
                        writer.WriteStartObject()
                        writer.WriteString("_truncated", "list_too_large")
                        writer.WriteNumber("_total_items", list.Count)
                        writer.WriteNumber("_shown_items", MaxCollectionItems)
                        writer.WriteEndObject()

                    writer.WriteEndArray()
                | :? Collections.IDictionary as dict ->
                    writer.WriteStartObject()
                    let keyTracker = createKeyTracker ()

                    let entries =
                        dict.Keys
                        |> Seq.cast<obj>
                        |> Seq.truncate MaxCollectionItems
                        |> Seq.toList

                    entries
                    |> List.iter (fun key ->
                        let keyStr =
                            try
                                if key = null then
                                    "null"
                                else
                                    ensureUniqueKey (key.ToString()) keyTracker
                            with _ ->
                                "_key_error"

                        writer.WritePropertyName keyStr
                        serializeValue writer dict.[key] (depth + 1))

                    if dict.Count > MaxCollectionItems then
                        writer.WriteString("_truncated", "map_too_large")
                        writer.WriteNumber("_total_entries", dict.Count)
                        writer.WriteNumber("_shown_entries", MaxCollectionItems)

                    writer.WriteEndObject()
                | :? INode ->
                    writer.WriteStartObject()
                    writer.WriteString("_type", "node_reference")
                    writer.WriteString("_error", "nested_node_not_supported")
                    writer.WriteEndObject()
                | :? IRelationship ->
                    writer.WriteStartObject()
                    writer.WriteString("_type", "relationship_reference")
                    writer.WriteString("_error", "nested_relationship_not_supported")
                    writer.WriteEndObject()
                | _ ->
                    writer.WriteStartObject()

                    writer.WriteString(
                        "_type",
                        try
                            value.GetType().FullName
                        with _ ->
                            "unknown"
                    )

                    writer.WriteString(
                        "_assembly",
                        try
                            value.GetType().Assembly.GetName().Name
                        with _ ->
                            "unknown"
                    )

                    writer.WriteString("_note", "unserializable_type")
                    writer.WriteEndObject()
            with ex ->
                try
                    writer.WriteStartObject()
                    writer.WriteString("_serialization_error", ex.GetType().Name)
                    writer.WriteString("_at_depth", string depth)
                    writer.WriteEndObject()
                with _ ->
                    // Even error serialization failed, write minimal error info
                    writeSerializationError writer "catastrophic" (sprintf "depth_%d" depth)

    /// Creates error context details for error tracking
    let private createErrorContext (nodeId: int64 option) (relId: int64 option) (additionalInfo: (string * obj) list) =
        let details =
            Dictionary<string, JsonValue>()

        additionalInfo
        |> List.iter (fun (key, value) ->
            match JsonHelpers.toJsonValue value with
            | Ok jsonValue -> details.[key] <- jsonValue
            | Error _ -> details.[key] <- JString(value.ToString()))

        if details.Count > 0 then
            Some(details :> IDictionary<string, JsonValue>)
        else
            None

    /// Tracks serialization errors with rich context
    let private trackSerializationError
        (errorTracker: ErrorTracker)
        (message: string)
        (id: int64)
        (entityType: string)
        (exceptionType: string)
        =
        let context =
            [ "entity_type", box entityType
              "exception_type", box exceptionType
              "serialization_phase", box "write" ]

        let details =
            createErrorContext
                (if entityType = "node" then Some id else None)
                (if entityType = "relationship" then Some id else None)
                context

        errorTracker.AddError(message, ?details = details)

    /// Writes a node directly to the Utf8JsonWriter for optimal performance
    let private writeNode
        (writer: Utf8JsonWriter)
        (node: INode)
        (nodeId: int64)
        (exportId: Guid)
        (errorTracker: ErrorTracker)
        =
        writer.WriteStartObject()
        writer.WriteString("type", "node")
        writer.WriteNumber("id", nodeId)
        writer.WriteString("export_id", exportId.ToString())
        writer.WriteStartArray("labels")

        try
            let labels =
                node.Labels |> Seq.truncate 100 |> Seq.toList

            labels
            |> List.iter (fun label ->
                let safeLabel =
                    if label = null then
                        errorTracker.AddWarning(sprintf "Null label found on node %d" nodeId, nodeId = nodeId)
                        "null"
                    elif label.Length > 1000 then
                        errorTracker.AddWarning(
                            sprintf "Label truncated on node %d: %s..." nodeId (label.Substring(0, 50)),
                            nodeId = nodeId
                        )

                        label.Substring(0, 1000) + "..."
                    else
                        label

                writer.WriteStringValue safeLabel)

            if node.Labels.Count > 100 then
                errorTracker.AddWarning(
                    sprintf "Too many labels on node %d: %d total, showing first 100" nodeId node.Labels.Count,
                    nodeId = nodeId
                )

                writer.WriteStringValue "_too_many_labels"
        with ex ->
            let context =
                createErrorContext (Some nodeId) None [ ("exception_type", box (ex.GetType().Name)) ]

            match context with
            | Some details ->
                errorTracker.AddError(
                    sprintf "Failed to serialize labels for node %d: %s" nodeId ex.Message,
                    nodeId = nodeId,
                    details = details
                )
            | None ->
                errorTracker.AddError(
                    sprintf "Failed to serialize labels for node %d: %s" nodeId ex.Message,
                    nodeId = nodeId
                )

            writer.WriteStringValue(sprintf "_label_error: %s" (ex.GetType().Name))

        writer.WriteEndArray()
        writer.WriteStartObject "properties"

        try
            let keyTracker = createKeyTracker ()

            let properties =
                node.Properties
                |> Seq.truncate MaxCollectionItems
                |> Seq.toList

            properties
            |> List.iteri (fun i kvp ->
                let safePropName =
                    try
                        if kvp.Key = null then
                            errorTracker.AddWarning(
                                sprintf "Null property key on node %d at index %d" nodeId i,
                                nodeId = nodeId
                            )

                            "_null_key"
                        else
                            ensureUniqueKey kvp.Key keyTracker
                    with ex ->
                        errorTracker.AddWarning(
                            sprintf "Property key error on node %d at index %d: %s" nodeId i ex.Message,
                            nodeId = nodeId
                        )

                        sprintf "_key_error_%d" i

                writer.WritePropertyName safePropName
                serializeValue writer kvp.Value 0)

            if node.Properties.Count > MaxCollectionItems then
                errorTracker.AddWarning(
                    sprintf
                        "Too many properties on node %d: %d total, showing first %d"
                        nodeId
                        node.Properties.Count
                        MaxCollectionItems,
                    nodeId = nodeId
                )

                writer.WritePropertyName "_truncated"
                writer.WriteStringValue(sprintf "too_many_properties: %d total" node.Properties.Count)
        with ex ->
            let context =
                createErrorContext (Some nodeId) None [ ("exception_type", box (ex.GetType().Name)) ]

            match context with
            | Some details ->
                errorTracker.AddError(
                    sprintf "Failed to serialize properties for node %d: %s" nodeId ex.Message,
                    nodeId = nodeId,
                    details = details
                )
            | None ->
                errorTracker.AddError(
                    sprintf "Failed to serialize properties for node %d: %s" nodeId ex.Message,
                    nodeId = nodeId
                )

            writer.WritePropertyName "_properties_error"
            writer.WriteStringValue(ex.GetType().Name)

        writer.WriteEndObject()
        writer.WriteEndObject()

    /// Writes a relationship directly to the Utf8JsonWriter for optimal performance
    let private writeRelationship
        (writer: Utf8JsonWriter)
        (rel: IRelationship)
        (relId: int64)
        (startId: int64)
        (endId: int64)
        (exportId: Guid)
        (errorTracker: ErrorTracker)
        =
        writer.WriteStartObject()
        writer.WriteString("type", "relationship")
        writer.WriteNumber("id", relId)
        writer.WriteString("export_id", exportId.ToString())

        let safeType =
            try
                if rel.Type = null then
                    errorTracker.AddWarning(sprintf "Null relationship type on relationship %d" relId, relId = relId)
                    "_null_type"
                elif rel.Type.Length > 1000 then
                    errorTracker.AddWarning(
                        sprintf
                            "Relationship type truncated on relationship %d: %s..."
                            relId
                            (rel.Type.Substring(0, 50)),
                        relId = relId
                    )

                    rel.Type.Substring(0, 1000) + "..."
                else
                    rel.Type
            with ex ->
                errorTracker.AddError(
                    sprintf "Failed to read relationship type on relationship %d: %s" relId ex.Message,
                    relId = relId
                )

                "_type_error"

        writer.WriteString("label", safeType)
        writer.WriteNumber("start", startId)
        writer.WriteNumber("end", endId)
        writer.WriteStartObject("properties")

        try
            let keyTracker = createKeyTracker ()

            let properties =
                rel.Properties
                |> Seq.truncate MaxCollectionItems
                |> Seq.toList

            properties
            |> List.iteri (fun i kvp ->
                let safePropName =
                    try
                        if kvp.Key = null then
                            errorTracker.AddWarning(
                                sprintf "Null property key on relationship %d at index %d" relId i,
                                relId = relId
                            )

                            "_null_key"
                        else
                            ensureUniqueKey kvp.Key keyTracker
                    with ex ->
                        errorTracker.AddWarning(
                            sprintf "Property key error on relationship %d at index %d: %s" relId i ex.Message,
                            relId = relId
                        )

                        sprintf "_key_error_%d" i

                writer.WritePropertyName safePropName
                serializeValue writer kvp.Value 0)

            if rel.Properties.Count > MaxCollectionItems then
                errorTracker.AddWarning(
                    sprintf
                        "Too many properties on relationship %d: %d total, showing first %d"
                        relId
                        rel.Properties.Count
                        MaxCollectionItems,
                    relId = relId
                )

                writer.WritePropertyName "_truncated"
                writer.WriteStringValue(sprintf "too_many_properties: %d total" rel.Properties.Count)
        with ex ->
            let context =
                createErrorContext None (Some relId) [ ("exception_type", box (ex.GetType().Name)) ]

            match context with
            | Some details ->
                errorTracker.AddError(
                    sprintf "Failed to serialize properties for relationship %d: %s" relId ex.Message,
                    relId = relId,
                    details = details
                )
            | None ->
                errorTracker.AddError(
                    sprintf "Failed to serialize properties for relationship %d: %s" relId ex.Message,
                    relId = relId
                )

            writer.WritePropertyName "_properties_error"
            writer.WriteStringValue(ex.GetType().Name)

        writer.WriteEndObject()
        writer.WriteEndObject()

    /// Common progress reporting logic
    let private reportProgress
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

    /// Generic batch processor configuration
    type BatchProcessor =
        { Query: string
          GetTotalQuery: string option
          ProcessRecord:
              ArrayBufferWriter<byte> -> IRecord -> Guid -> ExportProgress -> ErrorTracker -> (int64 * ExportProgress)
          EntityName: string }

    /// Record handler that can maintain state
    type RecordHandler<'state> = 'state -> IRecord -> int64 -> 'state

    /// Generic batch processing function following functional composition
    let private processBatchedQuery<'state>
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
            // Create a single reusable buffer for optimal performance
            let bufferSize =
                config.JsonBufferSizeKb * 1024

            let buffer =
                new ArrayBufferWriter<byte>(bufferSize)

            let newlineBytes =
                Encoding.UTF8.GetBytes Environment.NewLine

            let progressInterval =
                TimeSpan.FromSeconds 30.0

            let batchSize = config.BatchSize

            // Get total count if query provided
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

                                // Clear buffer for reuse - resets position, not memory
                                buffer.Clear()

                                let dataBytes, newStats =
                                    processor.ProcessRecord buffer record exportId batchStats errorTracker

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

                        // Flush the file stream buffer periodically
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

    /// Common node processing logic
    let private processNodeRecord
        (buffer: ArrayBufferWriter<byte>)
        (record: IRecord)
        (exportId: Guid)
        (stats: ExportProgress)
        (errorTracker: ErrorTracker)
        =
        let nodeId =
            try
                record.["nodeId"].As<int64>()
            with ex ->
                let context =
                    createErrorContext None None [ ("exception", box ex.Message) ]

                match context with
                | Some details -> errorTracker.AddError("Failed to extract node ID from record", details = details)
                | None -> errorTracker.AddError("Failed to extract node ID from record")

                -1L

        // Create a new writer for each record, pointed at the reusable buffer
        use writer =
            new Utf8JsonWriter(buffer, JsonConfig.createWriterOptions ())

        try
            let node = record.["n"].As<INode>()
            writeNode writer node nodeId exportId errorTracker
        with ex ->
            trackSerializationError
                errorTracker
                (sprintf "Node serialization failed for ID %d: %s" nodeId ex.Message)
                nodeId
                "node"
                (ex.GetType().Name)

            // Still write fallback JSON
            writer.WriteStartObject()
            writer.WriteString("type", "node")
            writer.WriteNumber("id", nodeId)
            writer.WriteString("export_id", exportId.ToString())
            writer.WriteStartArray "labels"
            writer.WriteEndArray()
            writer.WriteStartObject "properties"
            writer.WriteString("_export_error", "serialization_failed")
            writer.WriteNumber("_original_id", nodeId)
            writer.WriteEndObject()
            writer.WriteEndObject()

        writer.Flush()
        let dataBytes = int64 buffer.WrittenCount

        let newStats =
            { stats with
                RecordsProcessed = stats.RecordsProcessed + 1L }

        dataBytes, newStats

    /// Common relationship processing logic
    let private processRelationshipRecord
        (buffer: ArrayBufferWriter<byte>)
        (record: IRecord)
        (exportId: Guid)
        (stats: ExportProgress)
        (errorTracker: ErrorTracker)
        =
        let relId =
            try
                record.["relId"].As<int64>()
            with ex ->
                let context =
                    createErrorContext None None [ ("exception", box ex.Message) ]

                match context with
                | Some details ->
                    errorTracker.AddError("Failed to extract relationship ID from record", details = details)
                | None -> errorTracker.AddError("Failed to extract relationship ID from record")

                -1L

        let startId =
            try
                record.["startId"].As<int64>()
            with ex ->
                errorTracker.AddWarning(sprintf "Failed to extract start ID for relationship %d" relId, relId = relId)
                -1L

        let endId =
            try
                record.["endId"].As<int64>()
            with ex ->
                errorTracker.AddWarning(sprintf "Failed to extract end ID for relationship %d" relId, relId = relId)
                -1L

        // Create a new writer for each record, pointed at the reusable buffer
        use writer =
            new Utf8JsonWriter(buffer, JsonConfig.createWriterOptions ())

        try
            let rel = record.["r"].As<IRelationship>()
            writeRelationship writer rel relId startId endId exportId errorTracker
        with ex ->
            trackSerializationError
                errorTracker
                (sprintf "Relationship serialization failed for ID %d: %s" relId ex.Message)
                relId
                "relationship"
                (ex.GetType().Name)

            // Still write fallback JSON
            writer.WriteStartObject()
            writer.WriteString("type", "relationship")
            writer.WriteNumber("id", relId)
            writer.WriteString("export_id", exportId.ToString())
            writer.WriteString("label", "_UNKNOWN")
            writer.WriteNumber("start", startId)
            writer.WriteNumber("end", endId)
            writer.WriteStartObject "properties"
            writer.WriteString("_export_error", "relationship_access_failed")
            writer.WriteEndObject()
            writer.WriteEndObject()

        writer.Flush()
        let dataBytes = int64 buffer.WrittenCount

        let newStats =
            { stats with
                RecordsProcessed = stats.RecordsProcessed + 1L }

        dataBytes, newStats


    /// Unified node export with statistics
    let internal exportNodesUnified
        (context: ApplicationContext)
        (session: SafeSession)
        (config: ExportConfig)
        (fileStream: FileStream)
        (stats: ExportProgress)
        (exportId: Guid)
        (errorTracker: ErrorTracker)
        (lineState: LineTrackingState)
        : Async<Result<(ExportProgress * LabelStatsTracker.Tracker * LineTrackingState), AppError>> =
        async {
            Log.info "Exporting nodes with label statistics..."

            let initialLabelTracker =
                createLabelStatsTracker ()

            // Track node start line
            let lineStateWithNodeStart =
                lineState |> LineTracking.recordTypeStart "node"

            let processor =
                { Query = "MATCH (n) RETURN n, id(n) as nodeId, labels(n) as labels SKIP $skip LIMIT $limit"
                  GetTotalQuery = Some "MATCH (n) RETURN count(n) as count"
                  ProcessRecord = processNodeRecord
                  EntityName = "Nodes" }

            // We need a new processBatchedQuery that handles line tracking
            // For now, let's use the existing one and thread line state manually
            let mutable currentLineState =
                lineStateWithNodeStart

            match!
                processBatchedQuery
                    processor
                    context
                    session
                    config
                    fileStream
                    stats
                    exportId
                    errorTracker
                    initialLabelTracker
                    (fun tracker record bytesWritten ->
                        errorTracker.IncrementLine()
                        currentLineState <- currentLineState |> LineTracking.incrementLine

                        let labels =
                            try
                                record.["labels"].As<List<obj>>()
                                |> Seq.map (fun o -> o.ToString())
                                |> Seq.toList
                            with _ ->
                                []

                        let bytesPerLabel =
                            if List.isEmpty labels then
                                bytesWritten
                            else
                                bytesWritten / int64 labels.Length

                        let newTracker =
                            labels
                            |> List.fold
                                (fun tracker label ->
                                    tracker
                                    |> LabelStatsTracker.startLabel label
                                    |> LabelStatsTracker.updateLabel label 1L bytesPerLabel)
                                tracker

                        newTracker)
            with
            | Error e -> return Error e
            | Ok(finalStats, finalLabelTracker) -> return Ok(finalStats, finalLabelTracker, currentLineState)
        }

    let internal exportRelationships
        (context: ApplicationContext)
        (session: SafeSession)
        (config: ExportConfig)
        (fileStream: FileStream)
        stats
        exportId
        (errorTracker: ErrorTracker)
        (lineState: LineTrackingState)
        : Async<Result<(ExportProgress * LineTrackingState), AppError>> =
        async {
            Log.info "Exporting relationships..."

            // Track relationship start line
            let lineStateWithRelStart =
                lineState
                |> LineTracking.recordTypeStart "relationship"

            // Create relationship processor
            let processor =
                { Query =
                    "MATCH (s)-[r]->(t) RETURN r, id(r) as relId, id(s) as startId, id(t) as endId SKIP $skip LIMIT $limit"
                  GetTotalQuery = Some "MATCH ()-[r]->() RETURN count(r) as count"
                  ProcessRecord = processRelationshipRecord
                  EntityName = "Relationships" }

            // Thread line state through relationship processing
            let mutable currentLineState =
                lineStateWithRelStart

            // Handler that just counts lines - returns unit since no state is tracked
            let relationshipHandler () (record: IRecord) (bytesWritten: int64) =
                errorTracker.IncrementLine()
                currentLineState <- currentLineState |> LineTracking.incrementLine
                ()

            // Use generic processor with error tracker
            match!
                processBatchedQuery
                    processor
                    context
                    session
                    config
                    fileStream
                    stats
                    exportId
                    errorTracker
                    ()
                    relationshipHandler
            with
            | Error e -> return Error e
            | Ok(finalStats, _) -> return Ok(finalStats, currentLineState)
        }

    /// Export error and warning records from the error tracker
    let internal exportErrors
        (fileStream: FileStream)
        (errorTracker: ErrorTracker)
        (exportId: Guid)
        (lineState: LineTrackingState)
        : Async<int64 * LineTrackingState> =
        async {
            let errors = errorTracker.GetErrors()
            let mutable count = 0L
            let mutable currentLineState = lineState

            let newlineBytes =
                Encoding.UTF8.GetBytes Environment.NewLine

            for error in errors do
                // Track first error/warning line
                currentLineState <-
                    currentLineState
                    |> LineTracking.recordTypeStart error.Type
                    |> LineTracking.incrementLine

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

                match error.NodeId with
                | Some id -> writer.WriteNumber("node_id", id)
                | None -> ()

                match error.RelationshipId with
                | Some id -> writer.WriteNumber("relationship_id", id)
                | None -> ()

                match error.Details with
                | Some details ->
                    writer.WritePropertyName("details")
                    // Serialize details dictionary
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

    /// Common export completion logging
    let private logExportCompletion (_stats: CompletedExportStats) = ()

    /// Validates and moves temporary export file to final destination with metadata-based naming
    let finalizeExport
        _
        (config: ExportConfig)
        (metadata: FullMetadata)
        (tempFile: string)
        (stats: CompletedExportStats)
        =
        async {
            try
                Log.info "Finalizing export..."
                let tempInfo = FileInfo tempFile

                if not tempInfo.Exists then
                    return Error(FileSystemError(tempFile, "Temporary file not found", None))
                elif tempInfo.Length = 0L then
                    return Error(ExportError("Export produced empty file", None))
                else
                    // Generate final filename using metadata
                    let finalPath =
                        Configuration.generateMetadataFilename config.OutputDirectory metadata

                    Log.info (sprintf "Moving %s -> %s" (Path.GetFileName tempFile) (Path.GetFileName finalPath))
                    File.Move(tempFile, finalPath, true)
                    // File.Move throws if it fails, no need for redundant check

                    Log.info (sprintf "Export successful: %s" finalPath)
                    Log.info (sprintf "File size: %s" (Utils.formatBytes (FileInfo(finalPath).Length)))

                    Log.info (sprintf "Records: %d exported, %d skipped" stats.RecordsProcessed stats.RecordsSkipped)

                    return Ok()
            with ex ->
                return Error(FileSystemError(tempFile, "Failed to finalize export", Some ex))
        }
