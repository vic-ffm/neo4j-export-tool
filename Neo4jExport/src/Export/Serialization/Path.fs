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

module Neo4jExport.SerializationPath

open System
open System.Text.Json
open Neo4j.Driver
open Neo4jExport
open Neo4jExport.ExportTypes
open Neo4jExport.SerializationContext
open Neo4jExport.SerializationCollections
open Neo4jExport.JsonHelpers
open ErrorTracking

/// Generate the alternating sequence of nodes and relationships for a path
/// Neo4j paths always follow the pattern: Node -> Relationship -> Node -> ... -> Node
/// Therefore: relationships = nodes - 1 for valid paths
let private generatePathSequence nodeCount relCount =
    // Validate Neo4j path invariant
    if relCount <> nodeCount - 1 && nodeCount > 0 then
        // Log warning but continue - defensive against malformed data
        Log.warn (
            sprintf
                "Invalid path structure: %d nodes and %d relationships (expected %d relationships)"
                nodeCount
                relCount
                (nodeCount - 1)
        )

    // Simple approach: generate indices for the alternating pattern
    // Total elements = nodes + relationships
    let totalElements = nodeCount + relCount

    // Handle edge cases
    if totalElements = 0 then
        []
    else
        [ 0 .. totalElements - 1 ]
        |> List.map (fun i ->
            if i % 2 = 0 then
                // Even positions are nodes: 0, 2, 4, ...
                {| Type = "node"; Index = i / 2 |}
            else
                // Odd positions are relationships: 1, 3, 5, ...
                {| Type = "relationship"
                   Index = i / 2 |})
        |> List.filter (fun item ->
            // Defensive: ensure we don't exceed actual counts
            match item.Type with
            | "node" -> item.Index < nodeCount
            | "relationship" -> item.Index < relCount
            | _ -> false)

let private serializePathFull (writer: Utf8JsonWriter) (ctx: WriterContext) (path: IPath) =
    writer.WritePropertyName("nodes")
    writer.WriteStartArray()

    path.Nodes
    |> Seq.iter (fun node ->
        writer.WriteStartObject()
        writer.WriteString("element_id", node.ElementId)
        writer.WritePropertyName("labels")
        writer.WriteStartArray()
        node.Labels |> Seq.iter writer.WriteStringValue
        writer.WriteEndArray()
        writer.WritePropertyName("properties")
        writer.WriteStartObject()
        serializeProperties writer ctx SerializationDepth.zero node.Properties
        writer.WriteEndObject()
        writer.WriteEndObject())

    writer.WriteEndArray()

    writer.WritePropertyName("relationships")
    writer.WriteStartArray()

    path.Relationships
    |> Seq.iter (fun rel ->
        writer.WriteStartObject()
        writer.WriteString("element_id", rel.ElementId)
        writer.WriteString("type", rel.Type)
        writer.WriteString("start_element_id", rel.StartNodeElementId)
        writer.WriteString("end_element_id", rel.EndNodeElementId)
        writer.WritePropertyName("properties")
        writer.WriteStartObject()
        serializeProperties writer ctx SerializationDepth.zero rel.Properties
        writer.WriteEndObject()
        writer.WriteEndObject())

    writer.WriteEndArray()

let private serializePathCompact (writer: Utf8JsonWriter) (ctx: WriterContext) (path: IPath) =
    writer.WritePropertyName("nodes")
    writer.WriteStartArray()

    path.Nodes
    |> Seq.iter (fun node ->
        writer.WriteStartObject()
        writer.WriteString("element_id", node.ElementId)
        writer.WritePropertyName("labels")
        writer.WriteStartArray()

        node.Labels
        |> Seq.truncate ctx.Config.MaxLabelsInPathCompact
        |> Seq.iter writer.WriteStringValue

        writer.WriteEndArray()
        writer.WriteEndObject())

    writer.WriteEndArray()

    writer.WritePropertyName("relationships")
    writer.WriteStartArray()

    path.Relationships
    |> Seq.iter (fun rel ->
        writer.WriteStartObject()
        writer.WriteString("element_id", rel.ElementId)
        writer.WriteString("type", rel.Type)
        writer.WriteEndObject())

    writer.WriteEndArray()

let private serializePathIdsOnly (writer: Utf8JsonWriter) (path: IPath) =
    writer.WritePropertyName("node_element_ids")
    writer.WriteStartArray()

    path.Nodes
    |> Seq.iter (fun n -> writer.WriteStringValue(n.ElementId))

    writer.WriteEndArray()

    writer.WritePropertyName("relationship_element_ids")
    writer.WriteStartArray()

    path.Relationships
    |> Seq.iter (fun r -> writer.WriteStringValue(r.ElementId))

    writer.WriteEndArray()

let serializePath (writer: Utf8JsonWriter) (ctx: WriterContext) (path: IPath) =
    if int64 path.Nodes.Count > ctx.Config.MaxPathLength then
        ctx.ErrorFuncs.TrackError
            (sprintf "Path too long: %d nodes exceeds maximum %d" path.Nodes.Count ctx.Config.MaxPathLength)
            None
            (createErrorContext None [ "path_segment_count", box path.Nodes.Count ])

        writer.WriteStartObject()
        writer.WriteString("_type", "path")
        writer.WriteString("_error", "path_too_long")
        writer.WriteEndObject()
    else
        let level =
            determinePathLevel path.Nodes.Count ctx.Config

        match level with
        | Compact ->
            ctx.ErrorFuncs.TrackWarning
                (sprintf "Path length %d exceeds full threshold, automatically using Compact mode" path.Nodes.Count)
                None
                None
        | IdsOnly ->
            ctx.ErrorFuncs.TrackWarning
                (sprintf "Path length %d exceeds compact threshold, automatically using IdsOnly mode" path.Nodes.Count)
                None
                None
        | Full -> ()

        writer.WriteStartObject()
        writer.WriteString("_type", "path")
        writer.WriteNumber("length", path.Nodes.Count)
        writer.WriteString("_serialization_level", level.ToString())

        match level with
        | Full -> serializePathFull writer ctx path
        | Compact -> serializePathCompact writer ctx path
        | IdsOnly -> serializePathIdsOnly writer path

        writer.WritePropertyName("sequence")
        writer.WriteStartArray()

        generatePathSequence path.Nodes.Count path.Relationships.Count
        |> List.iter (fun item ->
            writer.WriteStartObject()
            writer.WriteString("type", item.Type)
            writer.WriteNumber("index", item.Index)
            writer.WriteEndObject())

        writer.WriteEndArray()
        writer.WriteEndObject()
