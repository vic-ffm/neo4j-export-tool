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

open System.Text.Json
open System.Text.Json.Serialization
open System.Text.Encodings.Web

module JsonConfig =

    /// Creates JSON options for data export with UnsafeRelaxedJsonEscaping
    /// to preserve data exactly as stored in Neo4j without HTML escaping.
    let createDataExportJsonOptions () =
        let options = JsonSerializerOptions()
        options.WriteIndented <- false
        options.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull
        options.Encoder <- JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        options

    let createWriterOptions () =
        JsonWriterOptions(
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Indented = false,
            SkipValidation = false
        )

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


    let calculateDirectPaddingBytes (baseMetadataSize: int) (targetSize: int) : Result<int, string> =
        let jsonOverhead = 13 // ,"padding":""

        let availableSpace =
            targetSize - baseMetadataSize - jsonOverhead

        if availableSpace < 0 then
            Error(sprintf "Metadata too large: %d bytes, max %d bytes" baseMetadataSize targetSize)
        elif availableSpace = 0 then
            Ok(0)
        else
            Ok(availableSpace)
