namespace Neo4jExport

open System
open System.Collections.Generic
open System.IO
open Neo4j.Driver

/// Metadata collection for export manifest
module Metadata =
    let private collectDatabaseInfo (session: SafeSession) (breaker: Neo4j.CircuitBreaker) (config: ExportConfig) =
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
                        1 // Max 1 result expected

                match result with
                | Ok records ->
                    match records with
                    | [ (version, edition) ] ->
                        info.["version"] <- JString version
                        info.["edition"] <- JString edition
                    | [] ->
                        info.["version"] <- JString "unknown"
                        info.["edition"] <- JString "unknown"
                    | _ ->
                        info.["version"] <- JString "unknown"
                        info.["edition"] <- JString "unknown"
                | Error e ->
                    Log.warn (sprintf "Failed to retrieve database version info: %A" e)
                    info.["version"] <- JString "unknown"
                    info.["edition"] <- JString "unknown"
            with ex ->
                Log.warn (sprintf "Exception while collecting database info: %s" ex.Message)
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
                        1 // Max 1 result expected

                match dbNameResult with
                | Ok records ->
                    match records with
                    | [ name ] -> info.["database_name"] <- JString name
                    | [] ->
                        Log.warn "Could not retrieve database name, using default"
                        info.["database_name"] <- JString "neo4j"
                    | _ -> info.["database_name"] <- JString "neo4j"
                | Error e ->
                    Log.warn (sprintf "Failed to retrieve database name: %A" e)
                    info.["database_name"] <- JString "neo4j"
            with ex ->
                Log.warn (sprintf "Exception while retrieving database name: %s" ex.Message)
                info.["database_name"] <- JString "neo4j"

            try
                let dbName =
                    match info.TryGetValue("database_name") with
                    | true, value ->
                        JsonHelpers.tryGetString value
                        |> Option.defaultValue "neo4j"
                    | _ -> "neo4j"

                let! creationResult =
                    Neo4j.executeQueryList
                        session
                        breaker
                        config
                        (sprintf "SHOW DATABASE `%s` YIELD createdAt RETURN createdAt" dbName)
                        (fun record ->
                            let createdAt = record.["createdAt"]

                            if isNull createdAt then
                                None
                            else
                                Some(createdAt.As<DateTime>().ToString("O")))
                        1 // Max 1 result expected

                match creationResult with
                | Ok records ->
                    match records with
                    | [ Some createdAt ] -> info.["creation_date"] <- JString createdAt
                    | _ -> () // No creation date available
                | Error _ -> ()
            with _ ->
                ()

            try
                let dbName =
                    match info.TryGetValue("database_name") with
                    | true, value ->
                        JsonHelpers.tryGetString value
                        |> Option.defaultValue "neo4j"
                    | _ -> "neo4j"

                let! storeSizeResult =
                    Neo4j.executeQueryList
                        session
                        breaker
                        config
                        "CALL db.stats.store.size() YIELD value RETURN value"
                        (fun record -> record.["value"].As<int64>())
                        1 // Max 1 result expected

                match storeSizeResult with
                | Ok records ->
                    match records with
                    | [ sizeBytes ] -> info.["size_bytes"] <- JNumber(decimal sizeBytes)
                    | _ -> () // Size not available
                | Error _ -> ()
            with _ ->
                ()

            return Ok(info :> IDictionary<string, JsonValue>)
        }

    let private collectStatistics (session: SafeSession) (breaker: Neo4j.CircuitBreaker) (config: ExportConfig) =
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
                        1 // Exactly 1 result expected

                match result with
                | Ok records ->
                    match records with
                    | [ (nodeCount, relCount) ] ->
                        stats.["nodeCount"] <- JNumber(decimal nodeCount)
                        stats.["relCount"] <- JNumber(decimal relCount)
                    | [] ->
                        Log.warn "No results from statistics query"
                        stats.["nodeCount"] <- JNumber 0M
                        stats.["relCount"] <- JNumber 0M
                    | _ ->
                        stats.["nodeCount"] <- JNumber 0M
                        stats.["relCount"] <- JNumber 0M
                | Error e ->
                    Log.warn (sprintf "Failed to collect statistics: %A" e)
                    stats.["nodeCount"] <- JNumber 0M
                    stats.["relCount"] <- JNumber 0M
            with ex ->
                Log.warn (sprintf "Exception while collecting statistics: %s" ex.Message)
                stats.["nodeCount"] <- JNumber 0M
                stats.["relCount"] <- JNumber 0M

            stats.["labelCount"] <- JNumber 0M
            stats.["relTypeCount"] <- JNumber 0M
            return Ok(stats :> IDictionary<string, JsonValue>)
        }

    let private collectSchema (session: SafeSession) (breaker: Neo4j.CircuitBreaker) (config: ExportConfig) =
        async {
            if config.SkipSchemaCollection then
                Log.info "Skipping schema collection (disabled by configuration)"
                return Ok(dict [])
            else
                Log.info "Collecting basic schema information..."
                let schema = Dictionary<string, JsonValue>()

                try
                    let! result =
                        Neo4j.executeQueryList
                            session
                            breaker
                            config
                            "CALL db.labels() YIELD label RETURN collect(label) as labels"
                            (fun record -> record.["labels"].As<List<obj>>())
                            1 // Returns exactly 1 row with collected labels

                    match result with
                    | Ok records ->
                        match records with
                        | [ labels ] -> schema.["labels"] <- JsonHelpers.toJsonValueUnsafe labels
                        | _ -> Log.warn "Failed to collect database labels: unexpected result count"
                    | Error e -> Log.warn (sprintf "Failed to collect database labels: %A" e)
                with ex ->
                    Log.warn (sprintf "Exception while collecting labels: %s" ex.Message)

                try
                    let! result =
                        Neo4j.executeQueryList
                            session
                            breaker
                            config
                            "CALL db.relationshipTypes() YIELD relationshipType RETURN collect(relationshipType) as types"
                            (fun record -> record.["types"].As<List<obj>>())
                            1 // Returns exactly 1 row with collected types

                    match result with
                    | Ok records ->
                        match records with
                        | [ types ] -> schema.["relationshipTypes"] <- JsonHelpers.toJsonValueUnsafe types
                        | _ -> Log.warn "Failed to collect relationship types: unexpected result count"
                    | Error e -> Log.warn (sprintf "Failed to collect relationship types: %A" e)
                with ex ->
                    Log.warn (sprintf "Exception while collecting relationship types: %s" ex.Message)

                return Ok(schema :> IDictionary<string, JsonValue>)
        }

    /// Enhances metadata with export manifest details after completion
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

    let collect
        (context: ApplicationContext)
        (session: SafeSession)
        (breaker: Neo4j.CircuitBreaker)
        (config: ExportConfig)
        =
        async {
            Log.info "Collecting metadata..."
            let! dbInfo = collectDatabaseInfo session breaker config
            let! stats = collectStatistics session breaker config
            let! schema = collectSchema session breaker config

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

                let! scriptChecksum = Utils.getScriptChecksum context |> Async.AwaitTask

                let metadata =
                    { ExportMetadata =
                        { ExportId = Guid.NewGuid()
                          ExportTimestampUtc = DateTime.UtcNow
                          ExportScript =
                            { Name = Path.GetFileName(System.Reflection.Assembly.GetExecutingAssembly().Location)
                              Version = Constants.getVersion ()
                              Checksum = scriptChecksum
                              RuntimeVersion = Environment.Version.ToString() }
                          ExportMode = "native_driver_streaming" }
                      SourceSystem =
                        { Type = "neo4j"
                          Version =
                            match db.TryGetValue("version") with
                            | true, value ->
                                JsonHelpers.tryGetString value
                                |> Option.defaultValue "unknown"
                            | _ -> "unknown"
                          Edition =
                            match db.TryGetValue("edition") with
                            | true, value ->
                                JsonHelpers.tryGetString value
                                |> Option.defaultValue "unknown"
                            | _ -> "unknown"
                          Database =
                            { Name =
                                match db.TryGetValue("database_name") with
                                | true, value ->
                                    JsonHelpers.tryGetString value
                                    |> Option.defaultValue "neo4j"
                                | _ -> "neo4j"
                              CreationDate =
                                match db.TryGetValue("creation_date") with
                                | true, value ->
                                    match JsonHelpers.tryGetString value with
                                    | Some dateStr ->
                                        try
                                            Some(DateTime.Parse(dateStr))
                                        with _ ->
                                            None
                                    | None -> None
                                | _ -> None
                              SizeBytes =
                                match db.TryGetValue("size_bytes") with
                                | true, value -> JsonHelpers.tryGetInt64 value
                                | _ -> None } }
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
                      ExportManifest = None }

                Log.info "Metadata collection completed"
                return Ok metadata
            | Error e, _, _ -> return Error e
            | _, Error e, _ -> return Error e
            | _, _, Error e -> return Error e
        }


    /// Estimates the maximum possible metadata size for placeholder allocation
    /// This runs BEFORE export when statistics are unknown
    let estimateMaxMetadataSize (config: ExportConfig) (baseMetadata: FullMetadata) : int =
        let actualLabelCount =
            match baseMetadata.DatabaseSchema.TryGetValue("labels") with
            | true, value ->
                match value with
                | JArray labels -> labels.Length
                | _ -> 20 // Reasonable default
            | _ -> 20 // Reasonable default

        let jsonOptions =
            JsonConfig.createDataExportJsonOptions ()

        let currentMetadataBytes =
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                JsonConfig.toSerializableMetadata baseMetadata,
                jsonOptions
            )

        let perLabelOverhead = 500
        let generalBuffer = 4096

        let estimatedSize =
            currentMetadataBytes.Length
            + (actualLabelCount * perLabelOverhead)
            + generalBuffer
            + 1024

        let withMargin =
            int (float estimatedSize * 1.2)

        match withMargin with
        | s when s < 16384 -> 16384
        | s when s < 32768 -> 32768
        | s when s < 65536 -> 65536
        | s -> ((s / 32768) + 1) * 32768
