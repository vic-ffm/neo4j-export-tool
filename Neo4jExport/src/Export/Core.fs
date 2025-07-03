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
open Neo4jExport.ExportUtils
open Neo4jExport.SerializationEngine
open Neo4jExport.ExportBatchProcessing
open ErrorTracking

/// Unified node export with statistics
let exportNodesUnified
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
            LabelStatsTracker.create ()

        let lineStateWithNodeStart =
            lineState |> LineTracking.recordTypeStart "node"

        let processor =
            { Query = "MATCH (n) RETURN n, labels(n) as labels SKIP $skip LIMIT $limit"
              GetTotalQuery = Some "MATCH (n) RETURN count(n) as count"
              ProcessRecord = processNodeRecord
              EntityName = "Nodes" }

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

let exportRelationships
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

        let lineStateWithRelStart =
            lineState
            |> LineTracking.recordTypeStart "relationship"

        let processor =
            { Query = "MATCH (s)-[r]->(t) RETURN r, s, t SKIP $skip LIMIT $limit"
              GetTotalQuery = Some "MATCH ()-[r]->() RETURN count(r) as count"
              ProcessRecord = processRelationshipRecord
              EntityName = "Relationships" }

        let mutable currentLineState =
            lineStateWithRelStart

        let relationshipHandler () (record: IRecord) (bytesWritten: int64) =
            errorTracker.IncrementLine()
            currentLineState <- currentLineState |> LineTracking.incrementLine
            ()

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
let exportErrors
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

/// Common export completion logging
let private logExportCompletion (_stats: CompletedExportStats) = ()

/// Validates and moves temporary export file to final destination with metadata-based naming
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
