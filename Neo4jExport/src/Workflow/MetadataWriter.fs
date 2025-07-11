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
open System.Text
open System.Text.Json
open System.IO

module MetadataWriter =

    let writeMetadataDirectly
        (stream: Stream)
        (metadata: FullMetadata)
        (targetSize: int)
        (writerOptions: JsonWriterOptions)
        (lineState: LineTrackingState)
        : Result<unit, string> =

        // Write to memory first to calculate exact size before padding
        // This allows accurate padding calculation without multiple passes
        use memoryStream = new MemoryStream()

        use writer =
            new Utf8JsonWriter(memoryStream, writerOptions)

        writer.WriteStartObject()

        writer.WriteString("format_version", metadata.FormatVersion)

        writer.WritePropertyName("export_metadata")
        writer.WriteStartObject()
        writer.WriteString("export_id", metadata.ExportMetadata.ExportId.ToString())
        writer.WriteString("export_timestamp_utc", metadata.ExportMetadata.ExportTimestampUtc.ToString("O"))
        writer.WriteString("export_mode", metadata.ExportMetadata.ExportMode)

        match metadata.ExportMetadata.Format with
        | Some format ->
            writer.WritePropertyName("format")
            writer.WriteStartObject()
            writer.WriteString("type", format.Type)
            writer.WriteNumber("metadata_line", format.MetadataLine)

            for KeyValue(recordType, startLine) in lineState.RecordTypeStartLines do
                writer.WriteNumber(sprintf "%s_start_line" recordType, startLine)

            writer.WriteEndObject()
        | None -> ()

        writer.WriteEndObject()

        writer.WritePropertyName("producer")
        JsonSerializer.Serialize(writer, metadata.Producer)

        writer.WritePropertyName("source_system")
        writer.WriteStartObject()
        writer.WriteString("type", metadata.SourceSystem.Type)
        writer.WriteString("version", metadata.SourceSystem.Version)
        writer.WriteString("edition", metadata.SourceSystem.Edition)

        writer.WritePropertyName("database")
        writer.WriteStartObject()
        writer.WriteString("name", metadata.SourceSystem.Database.Name)
        writer.WriteEndObject()
        writer.WriteEndObject()

        match metadata.ErrorSummary with
        | Some errorSummary ->
            writer.WritePropertyName("error_summary")
            writer.WriteStartObject()
            writer.WriteNumber("error_count", errorSummary.ErrorCount)
            writer.WriteNumber("warning_count", errorSummary.WarningCount)
            writer.WriteBoolean("has_errors", errorSummary.HasErrors)
            writer.WriteEndObject()
        | None -> ()

        writer.WritePropertyName("database_statistics")
        JsonHelpers.writeJsonValue writer (JObject metadata.DatabaseStatistics)

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

        writer.WritePropertyName("environment")
        JsonSerializer.Serialize(writer, metadata.Environment)

        writer.WritePropertyName("security")
        JsonSerializer.Serialize(writer, metadata.Security)

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

        writer.WritePropertyName("compression")
        writer.WriteStartObject()
        writer.WriteString("recommended", metadata.Compression.Recommended)
        writer.WritePropertyName("compatible")
        writer.WriteStartArray()

        for format in metadata.Compression.Compatible do
            writer.WriteStringValue(format)

        writer.WriteEndArray()

        writer.WritePropertyName("expected_ratio")

        match metadata.Compression.ExpectedRatio with
        | Some ratio -> writer.WriteNumberValue(ratio)
        | None -> writer.WriteNullValue()

        writer.WriteString("suffix", metadata.Compression.Suffix)
        writer.WriteEndObject()

        writer.WritePropertyName("database_schema")
        JsonHelpers.writeJsonValue writer (JObject metadata.DatabaseSchema)

        writer.WritePropertyName("export_manifest")
        JsonSerializer.Serialize(writer, metadata.ExportManifest)

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
            // Remove the closing brace from base JSON to insert padding field
            let baseJsonLength = baseBytes.Length - 1
            stream.Write(baseBytes, 0, baseJsonLength)

            let paddingPrefix =
                Encoding.UTF8.GetBytes(",\"padding\":\"")

            stream.Write(paddingPrefix, 0, paddingPrefix.Length)

            // Fill padding with spaces (0x20) for human readability
            // This creates a fixed-size metadata line for efficient seeking
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
