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

let private generatePathSequenceTailRec nodeCount relCount =
    let rec loop acc nodeIdx relIdx isNode =
        match nodeIdx < nodeCount, relIdx < relCount, isNode with
        | true, _, true ->
            let item =
                {| Type = "node"; Index = nodeIdx |}

            loop (item :: acc) (nodeIdx + 1) relIdx false
        | _, true, false ->
            let item =
                {| Type = "relationship"
                   Index = relIdx |}

            loop (item :: acc) nodeIdx (relIdx + 1) true
        | false, false, _ -> List.rev acc
        | _ -> loop acc nodeIdx relIdx (not isNode)

    loop [] 0 0 true

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

        generatePathSequenceTailRec path.Nodes.Count path.Relationships.Count
        |> List.iter (fun item ->
            writer.WriteStartObject()
            writer.WriteString("type", item.Type)
            writer.WriteNumber("index", item.Index)
            writer.WriteEndObject())

        writer.WriteEndArray()
        writer.WriteEndObject()
