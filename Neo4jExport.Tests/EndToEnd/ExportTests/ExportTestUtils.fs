module Neo4jExport.Tests.EndToEnd.ExportTests.ExportTestUtils

open System
open System.IO
open System.Text.Json
open Swensen.Unquote
open Neo4jExport.Tests.Helpers.TestHelpers

// Parse JSONL file into metadata and records
let parseExportFile (filePath: string) =
    let lines = File.ReadAllLines(filePath)

    if Array.isEmpty lines then
        failwith "Export file is empty"

    // Parse metadata from first line
    use metadataDoc =
        JsonDocument.Parse(lines.[0])

    let metadata =
        metadataDoc.RootElement.Clone()

    // Parse records from remaining lines
    let records =
        lines
        |> Array.skip 1
        |> Array.map (fun line ->
            use doc = JsonDocument.Parse(line)
            doc.RootElement.Clone())

    (metadata, records)

// Validate metadata structure
let validateMetadata (metadata: JsonElement) =
    let requiredFields =
        [ "format_version"
          "export_metadata"
          "producer"
          "source_system"
          "database_statistics"
          "database_schema"
          "export_manifest" ]

    for field in requiredFields do
        let hasProperty =
            metadata.TryGetProperty(field) |> fst

        test <@ hasProperty @>

// Validate node record structure
let validateNodeRecord (record: JsonElement) =
    let recordType =
        record.GetProperty("type").GetString()

    test <@ recordType = "node" @>

    let hasToolId =
        record.TryGetProperty("_tool_id") |> fst

    test <@ hasToolId @>

    let hasElementId =
        record.TryGetProperty("_element_id") |> fst

    test <@ hasElementId @>

    let hasLabels =
        record.TryGetProperty("_labels") |> fst

    test <@ hasLabels @>

    // Validate _tool_id is valid SHA-256
    let toolId =
        record.GetProperty("_tool_id").GetString()

    test <@ toolId.Length = 64 @>
    test <@ System.Text.RegularExpressions.Regex.IsMatch(toolId, "^[a-f0-9]{64}$") @>

// Validate relationship record structure
let validateRelationshipRecord (record: JsonElement) =
    let recordType =
        record.GetProperty("type").GetString()

    test <@ recordType = "relationship" @>

    let hasToolId =
        record.TryGetProperty("_tool_id") |> fst

    test <@ hasToolId @>

    let hasElementId =
        record.TryGetProperty("_element_id") |> fst

    test <@ hasElementId @>

    let hasType =
        record.TryGetProperty("_type") |> fst

    test <@ hasType @>

    let hasStartNode =
        record.TryGetProperty("_start_node_element_id")
        |> fst

    test <@ hasStartNode @>

    let hasEndNode =
        record.TryGetProperty("_end_node_element_id")
        |> fst

    test <@ hasEndNode @>

// Count records by type
let countRecordsByType (records: JsonElement[]) =
    records
    |> Array.groupBy (fun r -> r.GetProperty("type").GetString())
    |> Array.map (fun (typ, items) -> (typ, Array.length items))
    |> Map.ofArray

// Helper to check if property exists
let tryGetProperty (element: JsonElement) (propertyName: string) =
    match element.TryGetProperty(propertyName) with
    | true, value -> Some value
    | false, _ -> None

// Helper to check if array contains string
let arrayContainsString (element: JsonElement) (value: string) =
    element.EnumerateArray()
    |> Seq.exists (fun elem -> elem.GetString() = value)

// Helper to filter nodes by label safely
let isNodeWithLabel (labelName: string) (record: JsonElement) =
    let recordType =
        record.GetProperty("type").GetString()

    recordType = "node"
    && match tryGetProperty record "_labels" with
       | Some labels -> arrayContainsString labels labelName
       | None -> false

// Helper to filter by record type safely
let isRecordType (typeName: string) (record: JsonElement) =
    record.GetProperty("type").GetString() = typeName
