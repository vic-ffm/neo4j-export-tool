namespace Neo4jExport

open System.Text.Json
open System.Text.Json.Serialization
open System.Text.Encodings.Web

/// JSON configuration and helpers for the Neo4j export tool
module JsonConfig =

    /// Creates JSON options optimized for data export fidelity.
    /// Uses UnsafeRelaxedJsonEscaping because:
    /// 1. This is a data export tool, not a web application
    /// 2. Primary goal is to preserve data exactly as stored in Neo4j
    /// 3. Output is JSONL files for data processing, not HTML rendering
    /// 4. Downstream consumers are responsible for their own security needs
    /// 5. Escaping HTML characters would transform the data, violating the
    ///    tool's core purpose of faithful data export
    ///
    /// The "unsafe" designation only applies to direct HTML rendering contexts,
    /// which is not relevant for a data export tool. The JSON produced is
    /// valid and parseable by any standard JSON parser.
    let createDataExportJsonOptions () =
        let options = JsonSerializerOptions()
        options.WriteIndented <- false
        options.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull
        options.Encoder <- JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        options

    /// Creates Utf8JsonWriter options for high-performance streaming
    let createWriterOptions () =
        JsonWriterOptions(
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Indented = false,
            SkipValidation = false
        )

    /// Converts metadata to serializable format
    let toSerializableMetadata (metadata: FullMetadata) =
        {| export_metadata = metadata.ExportMetadata
           source_system = metadata.SourceSystem
           database_statistics =
            metadata.DatabaseStatistics
            |> Seq.map (fun kvp -> kvp.Key, JsonHelpers.fromJsonValue kvp.Value)
            |> dict
           database_schema =
            metadata.DatabaseSchema
            |> Seq.map (fun kvp -> kvp.Key, JsonHelpers.fromJsonValue kvp.Value)
            |> dict
           environment = metadata.Environment
           security = metadata.Security
           export_manifest = metadata.ExportManifest |}

    /// Create metadata with reserved field but no padding
    let toSerializableMetadataBase (metadata: FullMetadata) =
        {| export_metadata = metadata.ExportMetadata
           source_system = metadata.SourceSystem
           database_statistics =
            metadata.DatabaseStatistics
            |> Seq.map (fun kvp -> kvp.Key, JsonHelpers.fromJsonValue kvp.Value)
            |> dict
           database_schema =
            metadata.DatabaseSchema
            |> Seq.map (fun kvp -> kvp.Key, JsonHelpers.fromJsonValue kvp.Value)
            |> dict
           environment = metadata.Environment
           security = metadata.Security
           export_manifest = metadata.ExportManifest
           _reserved =
            {| purpose = "JSONL streaming compatibility - enables single-pass export"
               version = "1.0" |} |}

    /// Calculate exact bytes needed for direct padding
    let calculateDirectPaddingBytes (baseMetadataSize: int) (targetSize: int) : Result<int, string> =
        // Account for JSON structure: ,"padding":""}
        let jsonOverhead = 13 // Exact byte count for the JSON wrapper

        let availableSpace =
            targetSize - baseMetadataSize - jsonOverhead

        if availableSpace < 0 then
            Error(sprintf "Metadata too large: %d bytes, max %d bytes" baseMetadataSize targetSize)
        elif availableSpace = 0 then
            Ok(0) // Perfect fit, no padding needed
        else
            Ok(availableSpace)
