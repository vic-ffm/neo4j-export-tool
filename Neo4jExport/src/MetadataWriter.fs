namespace Neo4jExport

open System
open System.Text
open System.Text.Json
open System.IO

module MetadataWriter =

    /// Write metadata directly using JsonValue writer for optimal performance
    let writeMetadataDirectly
        (stream: Stream)
        (metadata: FullMetadata)
        (targetSize: int)
        (writerOptions: JsonWriterOptions)
        (lineState: LineTrackingState)
        : Result<unit, string> =

        use memoryStream = new MemoryStream()

        use writer =
            new Utf8JsonWriter(memoryStream, writerOptions)

        writer.WriteStartObject()

        // Write format_version at root level
        writer.WriteString("format_version", metadata.FormatVersion)

        // Export metadata
        writer.WritePropertyName("export_metadata")
        writer.WriteStartObject()
        writer.WriteString("export_id", metadata.ExportMetadata.ExportId.ToString())
        writer.WriteString("export_timestamp_utc", metadata.ExportMetadata.ExportTimestampUtc.ToString("O"))
        writer.WriteString("export_mode", metadata.ExportMetadata.ExportMode)

        // Format info
        match metadata.ExportMetadata.Format with
        | Some format ->
            writer.WritePropertyName("format")
            writer.WriteStartObject()
            writer.WriteString("type", format.Type)
            writer.WriteNumber("metadata_line", format.MetadataLine)

            // Dynamically write all record type start lines
            for KeyValue(recordType, startLine) in lineState.RecordTypeStartLines do
                writer.WriteNumber(sprintf "%s_start_line" recordType, startLine)

            writer.WriteEndObject()
        | None -> ()

        writer.WriteEndObject()

        // Producer (renamed from export_script)
        writer.WritePropertyName("producer")
        JsonSerializer.Serialize(writer, metadata.Producer)

        // Source system
        writer.WritePropertyName("source_system")
        writer.WriteStartObject()
        writer.WriteString("type", metadata.SourceSystem.Type)
        writer.WriteString("version", metadata.SourceSystem.Version)
        writer.WriteString("edition", metadata.SourceSystem.Edition)

        // Database
        writer.WritePropertyName("database")
        writer.WriteStartObject()
        writer.WriteString("name", metadata.SourceSystem.Database.Name)
        writer.WriteEndObject()
        writer.WriteEndObject()

        // Error summary
        match metadata.ErrorSummary with
        | Some errorSummary ->
            writer.WritePropertyName("error_summary")
            writer.WriteStartObject()
            writer.WriteNumber("error_count", errorSummary.ErrorCount)
            writer.WriteNumber("warning_count", errorSummary.WarningCount)
            writer.WriteBoolean("has_errors", errorSummary.HasErrors)
            writer.WriteEndObject()
        | None -> ()

        // Database statistics
        writer.WritePropertyName("database_statistics")
        JsonHelpers.writeJsonValue writer (JObject metadata.DatabaseStatistics)

        // Record types
        writer.WritePropertyName("supported_record_types")
        writer.WriteStartArray()

        for recordType in metadata.RecordTypes do
            writer.WriteStartObject()
            writer.WriteString("type_name", recordType.TypeName)
            writer.WriteString("description", recordType.Description)
            writer.WritePropertyName("required_fields")
            writer.WriteStartArray()

            for field in recordType.RequiredFields do
                writer.WriteStringValue(field)

            writer.WriteEndArray()

            match recordType.OptionalFields with
            | Some fields ->
                writer.WritePropertyName("optional_fields")
                writer.WriteStartArray()

                for field in fields do
                    writer.WriteStringValue(field)

                writer.WriteEndArray()
            | None -> ()

            writer.WriteEndObject()

        writer.WriteEndArray()

        // Environment
        writer.WritePropertyName("environment")
        JsonSerializer.Serialize(writer, metadata.Environment)

        // Security
        writer.WritePropertyName("security")
        JsonSerializer.Serialize(writer, metadata.Security)

        // Compatibility
        writer.WritePropertyName("compatibility")
        writer.WriteStartObject()
        writer.WriteString("minimum_reader_version", metadata.Compatibility.MinimumReaderVersion)
        writer.WritePropertyName("deprecated_fields")
        writer.WriteStartArray()

        for field in metadata.Compatibility.DeprecatedFields do
            writer.WriteStringValue(field)

        writer.WriteEndArray()
        writer.WriteString("breaking_change_version", metadata.Compatibility.BreakingChangeVersion)
        writer.WriteEndObject()

        // Compression hints
        writer.WritePropertyName("compression")
        writer.WriteStartObject()
        writer.WriteString("recommended", metadata.Compression.Recommended)
        writer.WritePropertyName("compatible")
        writer.WriteStartArray()

        for format in metadata.Compression.Compatible do
            writer.WriteStringValue(format)

        writer.WriteEndArray()

        // Always write expected_ratio (null if None)
        writer.WritePropertyName("expected_ratio")

        match metadata.Compression.ExpectedRatio with
        | Some ratio -> writer.WriteNumberValue(ratio)
        | None -> writer.WriteNullValue()

        writer.WriteString("suffix", metadata.Compression.Suffix)
        writer.WriteEndObject()

        // Database schema
        writer.WritePropertyName("database_schema")
        JsonHelpers.writeJsonValue writer (JObject metadata.DatabaseSchema)

        // Export manifest
        writer.WritePropertyName("export_manifest")
        JsonSerializer.Serialize(writer, metadata.ExportManifest)

        // Update _reserved without version
        match metadata.Reserved with
        | Some reserved ->
            writer.WritePropertyName("_reserved")
            writer.WriteStartObject()
            writer.WriteString("purpose", reserved.Purpose)
            writer.WriteEndObject()
        | None -> ()

        writer.WriteEndObject()
        writer.Flush()

        let baseBytes = memoryStream.ToArray()

        match JsonConfig.calculateDirectPaddingBytes baseBytes.Length targetSize with
        | Error msg -> Error msg
        | Ok 0 ->
            stream.Write(baseBytes, 0, baseBytes.Length)
            Ok()
        | Ok paddingBytesNeeded ->
            let baseJsonLength = baseBytes.Length - 1
            stream.Write(baseBytes, 0, baseJsonLength)

            let paddingPrefix =
                Encoding.UTF8.GetBytes(",\"padding\":\"")

            stream.Write(paddingPrefix, 0, paddingPrefix.Length)

            let paddingBytes =
                Array.create paddingBytesNeeded (byte 32uy)

            stream.Write(paddingBytes, 0, paddingBytes.Length)

            let closingBytes =
                Encoding.UTF8.GetBytes("\"}")

            stream.Write(closingBytes, 0, closingBytes.Length)

            let totalWritten =
                baseJsonLength
                + paddingPrefix.Length
                + paddingBytesNeeded
                + closingBytes.Length

            if totalWritten <> targetSize then
                Error(sprintf "Byte count mismatch: wrote %d, expected %d" totalWritten targetSize)
            else
                Ok()
