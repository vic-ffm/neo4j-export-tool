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

    /// Warning types for deduplication
    type MetadataWarning =
        | ConnectionFailure of phase: string * error: string
        | QueryFailure of query: string * error: string
        | DataMissing of dataType: string
        | JsonSerializationFailure of dataType: string * error: string

    /// For semantic comparison
    let warningKey =
        function
        | ConnectionFailure(phase, _) -> sprintf "conn_%s" phase
        | QueryFailure(query, _) -> sprintf "query_%s" (query.Substring(0, min 50 query.Length))
        | DataMissing dataType -> sprintf "missing_%s" dataType
        | JsonSerializationFailure(dataType, _) -> sprintf "json_%s" dataType

    // New parameter-reduced functions for Phase 10
    let private collectDatabaseInfo (queryExecutors: Neo4j.QueryExecutors) =
        async {
            let info = Dictionary<string, JsonValue>()
            let mutable warnings = []

            try
                let! result =
                    queryExecutors.ListQuery
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
                    warnings <-
                        QueryFailure("dbms.components", (ErrorAccumulation.appErrorToString e))
                        :: warnings

                    info.["version"] <- JString "unknown"
                    info.["edition"] <- JString "unknown"
            with ex ->
                warnings <-
                    ConnectionFailure("database_info", (ErrorAccumulation.exceptionToString ex))
                    :: warnings

                info.["version"] <- JString "unknown"
                info.["edition"] <- JString "unknown"

            try
                let! dbNameResult =
                    queryExecutors.ListQuery
                        "CALL db.info() YIELD name RETURN name"
                        (fun record -> record.["name"].As<string>())
                        1

                match dbNameResult with
                | Ok records ->
                    match records with
                    | [ name ] -> info.["database_name"] <- JString name
                    | _ ->
                        warnings <- DataMissing("database_name") :: warnings
                        info.["database_name"] <- JString "neo4j"
                | Error e ->
                    warnings <-
                        QueryFailure("db.info", (ErrorAccumulation.appErrorToString e))
                        :: warnings

                    info.["database_name"] <- JString "neo4j"
            with ex ->
                warnings <-
                    ConnectionFailure("database_name", (ErrorAccumulation.exceptionToString ex))
                    :: warnings

                info.["database_name"] <- JString "neo4j"

            return Ok(info :> IDictionary<string, JsonValue>), warnings
        }

    let private collectStatistics (queryExecutors: Neo4j.QueryExecutors) =
        async {
            Log.info "Collecting database statistics..."
            let stats = Dictionary<string, JsonValue>()
            let mutable warnings = []

            try
                let query =
                    """
                    MATCH (n)
                    WITH count(n) as nodeCount
                    MATCH ()-[r]->()
                    RETURN nodeCount, count(r) as relCount
                """

                let! result =
                    queryExecutors.ListQuery
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
                        warnings <- DataMissing("statistics") :: warnings
                        stats.["nodeCount"] <- JNumber 0M
                        stats.["relCount"] <- JNumber 0M
                | Error e ->
                    warnings <-
                        QueryFailure("statistics_query", (ErrorAccumulation.appErrorToString e))
                        :: warnings

                    stats.["nodeCount"] <- JNumber 0M
                    stats.["relCount"] <- JNumber 0M
            with ex ->
                warnings <-
                    ConnectionFailure("statistics", (ErrorAccumulation.exceptionToString ex))
                    :: warnings

                stats.["nodeCount"] <- JNumber 0M
                stats.["relCount"] <- JNumber 0M

            stats.["labelCount"] <- JNumber 0M
            stats.["relTypeCount"] <- JNumber 0M
            return Ok(stats :> IDictionary<string, JsonValue>), warnings
        }

    let private collectSchema (queryExecutors: Neo4j.QueryExecutors) (skipSchemaCollection: bool) =
        async {
            if skipSchemaCollection then
                Log.info "Skipping schema collection (disabled by configuration)"
                return Ok(dict []), []
            else
                Log.info "Collecting basic schema information..."
                let schema = Dictionary<string, JsonValue>()
                let mutable warnings = []

                let jsonConversionError =
                    JString "serialization_error" // A sensible default

                // Helper to handle JSON conversion warnings
                let toJsonValueWithWarning obj =
                    match JsonHelpers.toJsonValue obj with
                    | Ok jsonValue -> jsonValue
                    | Error _ ->
                        warnings <-
                            JsonSerializationFailure("schema", "Failed to serialize schema data")
                            :: warnings

                        jsonConversionError

                try
                    let! result =
                        queryExecutors.ListQuery
                            "CALL db.labels() YIELD label RETURN collect(label) as labels"
                            (fun record -> record.["labels"].As<List<obj>>())
                            1

                    match result with
                    | Ok records ->
                        match records with
                        | [ labels ] -> schema.["labels"] <- toJsonValueWithWarning labels
                        | _ -> warnings <- DataMissing("labels") :: warnings
                    | Error e ->
                        warnings <-
                            QueryFailure("db.labels", (ErrorAccumulation.appErrorToString e))
                            :: warnings
                with ex ->
                    warnings <-
                        ConnectionFailure("labels", (ErrorAccumulation.exceptionToString ex))
                        :: warnings

                try
                    let! result =
                        queryExecutors.ListQuery
                            "CALL db.relationshipTypes() YIELD relationshipType RETURN collect(relationshipType) as types"
                            (fun record -> record.["types"].As<List<obj>>())
                            1

                    match result with
                    | Ok records ->
                        match records with
                        | [ types ] -> schema.["relationshipTypes"] <- toJsonValueWithWarning types
                        | _ -> warnings <- DataMissing("relationshipTypes") :: warnings
                    | Error e ->
                        warnings <-
                            QueryFailure("db.relationshipTypes", (ErrorAccumulation.appErrorToString e))
                            :: warnings
                with ex ->
                    warnings <-
                        ConnectionFailure("relationshipTypes", (ErrorAccumulation.exceptionToString ex))
                        :: warnings

                return Ok(schema :> IDictionary<string, JsonValue>), warnings
        }

    // Add version parsing function
    let parseNeo4jVersion (versionString: string) : Neo4jVersion =
        match versionString with
        | null
        | "" -> Unknown
        | v when v.StartsWith("4.4") -> V4x
        | v when v.StartsWith("4.") && not (v.StartsWith("4.4")) -> Unknown // Other 4.x not supported
        | v when v.StartsWith("5.") -> V5x
        | v when v.StartsWith("6.") -> V6x
        | _ -> Unknown

    // Add helper to extract major.minor version
    let extractVersionComponents (versionString: string) : (int * int) option =
        match versionString.Split('.') with
        | parts when parts.Length >= 2 ->
            match Int32.TryParse(parts.[0]), Int32.TryParse(parts.[1]) with
            | (true, major), (true, minor) -> Some(major, minor)
            | _ -> None
        | _ -> None

    /// Deduplicate and flush warnings to ErrorTracker
    let private deduplicateAndFlush
        (warnings: MetadataWarning list)
        (errorFuncs: ErrorTracking.ErrorTrackingFunctions)
        =
        warnings
        |> List.groupBy warningKey
        |> List.map (fun (_, group) ->
            match group with
            | [] -> None
            | first :: rest ->
                let count = 1 + List.length rest

                let message =
                    match first with
                    | ConnectionFailure(phase, err) ->
                        sprintf "Connection failed during %s (occurred %d times): %s" phase count err
                    | QueryFailure(query, err) -> sprintf "Query failed (occurred %d times): %s - %s" count query err
                    | DataMissing dataType -> sprintf "Data missing: %s (occurred %d times)" dataType count
                    | JsonSerializationFailure(dataType, err) ->
                        sprintf "JSON serialization failed for %s (occurred %d times): %s" dataType count err

                Some message)
        |> List.choose id
        |> List.iter (fun msg ->
            Log.warn msg
            errorFuncs.TrackWarning msg None None)

    /// New public collect function with reduced parameters
    let collect
        (appContext: ApplicationContext)
        (queryExecutors: Neo4j.QueryExecutors)
        (errorContext: ErrorContext)
        (config: ExportConfig)
        =
        async {
            Log.info "Collecting metadata..."

            // Collect with warnings
            let! dbInfoResult = collectDatabaseInfo queryExecutors
            let! statsResult = collectStatistics queryExecutors
            let! schemaResult = collectSchema queryExecutors config.SkipSchemaCollection

            // Extract results and warnings
            let dbInfo, dbWarnings = dbInfoResult
            let stats, statsWarnings = statsResult
            let schema, schemaWarnings = schemaResult

            // Accumulate all warnings
            let allWarnings =
                [ dbWarnings
                  statsWarnings
                  schemaWarnings ]
                |> List.concat

            // Single deduplication and flush
            deduplicateAndFlush allWarnings errorContext.Funcs

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
                    Utils.getScriptChecksum (AppContext.getCancellationToken appContext)
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

                // Extract version string for parsing
                let versionString =
                    match db.TryGetValue("version") with
                    | true, value ->
                        match JsonHelpers.tryGetString value with
                        | Ok s -> s
                        | Error _ -> "unknown"
                    | _ -> "unknown"

                let fullMetadata =
                    { FormatVersion = FORMAT_VERSION
                      ExportMetadata =
                        { ExportId = errorContext.ExportId // Use the exportId from context
                          ExportTimestampUtc = DateTime.UtcNow
                          ExportMode = "native_driver_streaming"
                          Format = None }
                      Producer = exportScript
                      SourceSystem =
                        { Type = "neo4j"
                          Version = versionString
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
                              Padding = None }
                      PaginationPerformance = None }

                // Parse version before returning
                let parsedVersion =
                    parseNeo4jVersion versionString

                Log.info "Metadata collection completed"
                return Ok(fullMetadata, parsedVersion) // Return tuple
            | Error e, _, _ -> return Error e
            | _, Error e, _ -> return Error e
            | _, _, Error e -> return Error e
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

    let addErrorSummary (metadata: FullMetadata) (errorFuncs: ErrorTracking.ErrorTrackingFunctions) : FullMetadata =
        let errorSummary =
            { ErrorCount = errorFuncs.Queries.GetErrorCount()
              WarningCount = errorFuncs.Queries.GetWarningCount()
              HasErrors = errorFuncs.Queries.HasErrors() }

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
