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
        : Result<unit, string> =

        use memoryStream = new MemoryStream()

        use writer =
            new Utf8JsonWriter(memoryStream, writerOptions)

        writer.WriteStartObject()

        writer.WritePropertyName("export_metadata")
        JsonSerializer.Serialize(writer, metadata.ExportMetadata)

        writer.WritePropertyName("source_system")
        JsonSerializer.Serialize(writer, metadata.SourceSystem)

        writer.WritePropertyName("database_statistics")
        JsonHelpers.writeJsonValue writer (JObject metadata.DatabaseStatistics)

        writer.WritePropertyName("database_schema")
        JsonHelpers.writeJsonValue writer (JObject metadata.DatabaseSchema)

        writer.WritePropertyName("environment")
        JsonSerializer.Serialize(writer, metadata.Environment)

        writer.WritePropertyName("security")
        JsonSerializer.Serialize(writer, metadata.Security)

        writer.WritePropertyName("export_manifest")
        JsonSerializer.Serialize(writer, metadata.ExportManifest)

        writer.WritePropertyName("_reserved")
        writer.WriteStartObject()
        writer.WriteString("purpose", "JSONL streaming compatibility - enables single-pass export")
        writer.WriteString("version", "1.0")
        writer.WriteEndObject()

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
