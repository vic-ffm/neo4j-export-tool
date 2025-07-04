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
open System.Collections.Generic
open System.IO
open Neo4j.Driver

module Metadata =
    [<Literal>]
    let private FORMAT_VERSION = "1.0.0"

    let private collectDatabaseInfo
        (session: SafeSession)
        (breaker: Neo4j.CircuitBreaker)
        (config: ExportConfig)
        (errorTracker: ErrorTracking.ErrorTracker)
        =
        async {
            let info = Dictionary<string, JsonValue>()

            try
                let! result =
                    Neo4j.executeQueryList
                        session
                        breaker
                        config
                        "CALL dbms.components() YIELD name, versions, edition WHERE name = 'Neo4j Kernel' RETURN versions[0] as version, edition"
                        (fun record -> record.["version"].As<string>(), record.["edition"].As<string>())
                        1

                match result with
                | Ok records ->
                    match records with
                    | [ (version, edition) ] ->
                        info.["version"] <- JString version
                        info.["edition"] <- JString edition
                    | _ ->
                        info.["version"] <- JString "unknown"
                        info.["edition"] <- JString "unknown"
                | Error e ->
                    let msg =
                        sprintf "Failed to retrieve database version info: %A" e

                    Log.warn msg
                    errorTracker.AddWarning(msg)
                    info.["version"] <- JString "unknown"
                    info.["edition"] <- JString "unknown"
            with ex ->
                let msg =
                    sprintf "Exception while collecting database info: %s" ex.Message

                Log.warn msg
                errorTracker.AddWarning(msg)
                info.["version"] <- JString "unknown"
                info.["edition"] <- JString "unknown"

            try
                let! dbNameResult =
                    Neo4j.executeQueryList
                        session
                        breaker
                        config
                        "CALL db.info() YIELD name RETURN name"
                        (fun record -> record.["name"].As<string>())
                        1

                match dbNameResult with
                | Ok records ->
                    match records with
                    | [ name ] -> info.["database_name"] <- JString name
                    | _ ->
                        let msg =
                            "Could not retrieve database name, using default"

                        Log.warn msg
                        errorTracker.AddWarning(msg)
                        info.["database_name"] <- JString "neo4j"
                | Error e ->
                    let msg =
                        sprintf "Failed to retrieve database name: %A" e

                    Log.warn msg
                    errorTracker.AddWarning(msg)
                    info.["database_name"] <- JString "neo4j"
            with ex ->
                let msg =
                    sprintf "Exception while retrieving database name: %s" ex.Message

                Log.warn msg
                errorTracker.AddWarning(msg)
                info.["database_name"] <- JString "neo4j"


            return Ok(info :> IDictionary<string, JsonValue>)
        }

    let private collectStatistics
        (session: SafeSession)
        (breaker: Neo4j.CircuitBreaker)
        (config: ExportConfig)
        (errorTracker: ErrorTracking.ErrorTracker)
        =
        async {
            Log.info "Collecting database statistics..."
            let stats = Dictionary<string, JsonValue>()

            try
                let query =
                    """
                    MATCH (n)
                    WITH count(n) as nodeCount
                    MATCH ()-[r]->()
                    RETURN nodeCount, count(r) as relCount
                """

                let! result =
                    Neo4j.executeQueryList
                        session
                        breaker
                        config
                        query
                        (fun record -> record.["nodeCount"].As<int64>(), record.["relCount"].As<int64>())
                        1

                match result with
                | Ok records ->
                    match records with
                    | [ (nodeCount, relCount) ] ->
                        stats.["nodeCount"] <- JNumber(decimal nodeCount)
                        stats.["relCount"] <- JNumber(decimal relCount)
                    | _ ->
                        let msg = "No results from statistics query"
                        Log.warn msg
                        errorTracker.AddWarning(msg)
                        stats.["nodeCount"] <- JNumber 0M
                        stats.["relCount"] <- JNumber 0M
                | Error e ->
                    let msg =
                        sprintf "Failed to collect statistics: %A" e

                    Log.warn msg
                    errorTracker.AddWarning(msg)
                    stats.["nodeCount"] <- JNumber 0M
                    stats.["relCount"] <- JNumber 0M
            with ex ->
                let msg =
                    sprintf "Exception while collecting statistics: %s" ex.Message

                Log.warn msg
                errorTracker.AddWarning(msg)
                stats.["nodeCount"] <- JNumber 0M
                stats.["relCount"] <- JNumber 0M

            stats.["labelCount"] <- JNumber 0M
            stats.["relTypeCount"] <- JNumber 0M
            return Ok(stats :> IDictionary<string, JsonValue>)
        }

    let private collectSchema
        (session: SafeSession)
        (breaker: Neo4j.CircuitBreaker)
        (config: ExportConfig)
        (errorTracker: ErrorTracking.ErrorTracker)
        =
        async {
            if config.SkipSchemaCollection then
                Log.info "Skipping schema collection (disabled by configuration)"
                return Ok(dict [])
            else
                Log.info "Collecting basic schema information..."
                let schema = Dictionary<string, JsonValue>()

                let jsonConversionError =
                    JString "serialization_error" // A sensible default

                try
                    let! result =
                        Neo4j.executeQueryList
                            session
                            breaker
                            config
                            "CALL db.labels() YIELD label RETURN collect(label) as labels"
                            (fun record -> record.["labels"].As<List<obj>>())
                            1

                    match result with
                    | Ok records ->
                        match records with
                        | [ labels ] ->
                            schema.["labels"] <- JsonHelpers.toJsonValueWithDefault jsonConversionError Log.warn labels
                        | _ ->
                            let msg =
                                "Failed to collect database labels: unexpected result count"

                            Log.warn msg
                            errorTracker.AddWarning(msg)
                    | Error e ->
                        let msg =
                            sprintf "Failed to collect database labels: %A" e

                        Log.warn msg
                        errorTracker.AddWarning(msg)
                with ex ->
                    let msg =
                        sprintf "Exception while collecting labels: %s" ex.Message

                    Log.warn msg
                    errorTracker.AddWarning(msg)

                try
                    let! result =
                        Neo4j.executeQueryList
                            session
                            breaker
                            config
                            "CALL db.relationshipTypes() YIELD relationshipType RETURN collect(relationshipType) as types"
                            (fun record -> record.["types"].As<List<obj>>())
                            1

                    match result with
                    | Ok records ->
                        match records with
                        | [ types ] ->
                            schema.["relationshipTypes"] <-
                                JsonHelpers.toJsonValueWithDefault jsonConversionError Log.warn types
                        | _ ->
                            let msg =
                                "Failed to collect relationship types: unexpected result count"

                            Log.warn msg
                            errorTracker.AddWarning(msg)
                    | Error e ->
                        let msg =
                            sprintf "Failed to collect relationship types: %A" e

                        Log.warn msg
                        errorTracker.AddWarning(msg)
                with ex ->
                    let msg =
                        sprintf "Exception while collecting relationship types: %s" ex.Message

                    Log.warn msg
                    errorTracker.AddWarning(msg)

                return Ok(schema :> IDictionary<string, JsonValue>)
        }

    let enhanceWithManifest
        (metadata: FullMetadata)
        (labelStatsTracker: LabelStatsTracker.Tracker)
        (exportDuration: float)
        =
        let manifestDetails =
            { TotalExportDurationSeconds = exportDuration
              FileStatistics =
                LabelStatsTracker.finalizeAndGetAllStats labelStatsTracker
                |> List.sortBy (fun s -> s.Label) }

        { metadata with
            ExportManifest = Some manifestDetails }

    let addErrorSummary (metadata: FullMetadata) (errorTracker: ErrorTracking.ErrorTracker) : FullMetadata =
        let errorSummary =
            { ErrorCount = errorTracker.GetErrorCount()
              WarningCount = errorTracker.GetWarningCount()
              HasErrors = errorTracker.HasErrors() }

        { metadata with
            ErrorSummary = Some errorSummary }

    let addFormatInfo (metadata: FullMetadata) (lineState: LineTrackingState) : FullMetadata =
        let formatInfo =
            { Type = "jsonl"; MetadataLine = 1 }

        let updatedExportMetadata =
            { metadata.ExportMetadata with
                Format = Some formatInfo }

        { metadata with
            ExportMetadata = updatedExportMetadata }

    let collect
        (context: ApplicationContext)
        (session: SafeSession)
        (breaker: Neo4j.CircuitBreaker)
        (config: ExportConfig)
        (errorTracker: ErrorTracking.ErrorTracker)
        =
        async {
            Log.info "Collecting metadata..."
            let! dbInfo = collectDatabaseInfo session breaker config errorTracker
            let! stats = collectStatistics session breaker config errorTracker
            let! schema = collectSchema session breaker config errorTracker

            match dbInfo, stats, schema with
            | Ok db, Ok st, Ok sc ->
                if sc.ContainsKey("labels") then
                    match sc.["labels"] with
                    | JArray arr -> st.["labelCount"] <- JNumber(decimal arr.Length)
                    | _ -> st.["labelCount"] <- JNumber 0M
                else
                    st.["labelCount"] <- JNumber 0M

                if sc.ContainsKey("relationshipTypes") then
                    match sc.["relationshipTypes"] with
                    | JArray arr -> st.["relTypeCount"] <- JNumber(decimal arr.Length)
                    | _ -> st.["relTypeCount"] <- JNumber 0M
                else
                    st.["relTypeCount"] <- JNumber 0M

                let! scriptChecksum =
                    Utils.getScriptChecksum (AppContext.getCancellationToken context)
                    |> Async.AwaitTask

                let exportScript =
                    { Name = Path.GetFileName(System.Reflection.Assembly.GetExecutingAssembly().Location)
                      Version = Constants.getVersion ()
                      Checksum = scriptChecksum
                      RuntimeVersion = Environment.Version.ToString() }

                let compatibility =
                    { MinimumReaderVersion = "1.0.0"
                      DeprecatedFields = []
                      BreakingChangeVersion = "2.0.0" }

                let compression =
                    { Recommended = "zstd"
                      Compatible = [ "zstd"; "gzip"; "brotli"; "none" ]
                      ExpectedRatio = Some 0.3
                      Suffix = ".jsonl.zst" }

                let metadata =
                    { FormatVersion = FORMAT_VERSION
                      ExportMetadata =
                        { ExportId = Guid.NewGuid()
                          ExportTimestampUtc = DateTime.UtcNow
                          ExportMode = "native_driver_streaming"
                          Format = None }
                      Producer = exportScript
                      SourceSystem =
                        { Type = "neo4j"
                          Version =
                            match db.TryGetValue("version") with
                            | true, value ->
                                match JsonHelpers.tryGetString value with
                                | Ok s -> s
                                | Error _ -> "unknown"
                            | _ -> "unknown"
                          Edition =
                            match db.TryGetValue("edition") with
                            | true, value ->
                                match JsonHelpers.tryGetString value with
                                | Ok s -> s
                                | Error _ -> "unknown"
                            | _ -> "unknown"
                          Database =
                            { Name =
                                match db.TryGetValue("database_name") with
                                | true, value ->
                                    match JsonHelpers.tryGetString value with
                                    | Ok s -> s
                                    | Error _ -> "neo4j"
                                | _ -> "neo4j" } }
                      DatabaseStatistics = st
                      DatabaseSchema = sc
                      Environment =
                        { Hostname = Environment.MachineName
                          OperatingSystem = Environment.OSVersion.ToString()
                          User = Environment.UserName
                          Runtime = sprintf ".NET %s" (Environment.Version.ToString())
                          Processors = Environment.ProcessorCount
                          MemoryGb = float (GC.GetTotalMemory(false)) / 1073741824.0 }
                      Security =
                        { EncryptionEnabled = config.Uri.Scheme.Contains("+s")
                          AuthMethod =
                            if String.IsNullOrEmpty(config.Password) then
                                "none"
                            else
                                "basic"
                          DataValidation = config.ValidateJsonOutput }
                      ExportManifest = None
                      ErrorSummary = None
                      RecordTypes = RecordTypes.getRecordTypes ()
                      Compatibility = compatibility
                      Compression = compression
                      Reserved =
                        Some
                            { Purpose = "JSONL streaming compatibility - enables single-pass export"
                              Padding = None } }

                Log.info "Metadata collection completed"
                return Ok metadata
            | Error e, _, _ -> return Error e
            | _, Error e, _ -> return Error e
            | _, _, Error e -> return Error e
        }


    /// Estimates maximum metadata size for placeholder allocation
    let estimateMaxMetadataSize (config: ExportConfig) (baseMetadata: FullMetadata) : int =
        let actualLabelCount =
            match baseMetadata.DatabaseSchema.TryGetValue("labels") with
            | true, value ->
                match value with
                | JArray labels -> labels.Length
                | _ -> 20
            | _ -> 20

        let jsonOptions =
            JsonConfig.createDataExportJsonOptions ()

        let currentMetadataBytes =
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                JsonConfig.toSerializableMetadata baseMetadata,
                jsonOptions
            )

        let perLabelOverhead = 500
        let generalBuffer = 4096
        let recordTypesSize = 2000
        let compressionSize = 500
        let compatibilitySize = 300

        let estimatedSize =
            currentMetadataBytes.Length
            + (actualLabelCount * perLabelOverhead)
            + generalBuffer
            + recordTypesSize
            + compressionSize
            + compatibilitySize
            + 1024

        let withMargin =
            int (float estimatedSize * 1.2)

        match withMargin with
        | s when s < 16384 -> 16384
        | s when s < 32768 -> 32768
        | s when s < 65536 -> 65536
        | s -> ((s / 32768) + 1) * 32768
