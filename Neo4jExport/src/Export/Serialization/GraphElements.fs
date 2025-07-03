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

module Neo4jExport.SerializationGraphElements

open System
open System.Text.Json
open Neo4j.Driver
open Neo4jExport
open Neo4jExport.ExportTypes
open Neo4jExport.ExportUtils
open Neo4jExport.SerializationContext
open Neo4jExport.SerializationCollections
open ErrorTracking

/// Forward declaration for serializePath
let mutable serializePathFunc: (Utf8JsonWriter -> IPath -> ExportConfig -> ErrorTracker -> unit) option =
    None

let serializeNode
    (writer: Utf8JsonWriter)
    (node: INode)
    (depth: SerializationDepth)
    (config: ExportConfig)
    (errorTracker: ErrorTracker)
    =
    let level =
        determineNestedLevel depth config

    match level with
    | Reference ->
        writer.WriteStartObject()
        writer.WriteString("_type", "node_reference")
        writer.WriteString("element_id", node.ElementId)
        writer.WritePropertyName("_labels")
        writer.WriteStartArray()

        node.Labels
        |> Seq.truncate config.MaxLabelsInReferenceMode
        |> Seq.iter writer.WriteStringValue

        writer.WriteEndArray()
        writer.WriteEndObject()

    | Shallow ->
        writer.WriteStartObject()
        writer.WriteString("_type", "embedded_node_shallow")
        writer.WriteString("element_id", node.ElementId)
        writer.WritePropertyName("labels")
        writer.WriteStartArray()
        node.Labels |> Seq.iter writer.WriteStringValue
        writer.WriteEndArray()
        writer.WriteNumber("_property_count", node.Properties.Count)
        writer.WriteEndObject()

    | Deep ->
        writer.WriteStartObject()
        writer.WriteString("_type", "embedded_node")
        writer.WriteString("element_id", node.ElementId)
        writer.WritePropertyName("labels")
        writer.WriteStartArray()
        node.Labels |> Seq.iter writer.WriteStringValue
        writer.WriteEndArray()

        writer.WritePropertyName("properties")
        writer.WriteStartObject()
        serializeProperties writer node.Properties (SerializationDepth.increment depth) config errorTracker
        writer.WriteEndObject()
        writer.WriteEndObject()

let serializeRelationship
    (writer: Utf8JsonWriter)
    (rel: IRelationship)
    (depth: SerializationDepth)
    (config: ExportConfig)
    (errorTracker: ErrorTracker)
    =
    let level =
        determineNestedLevel depth config

    match level with
    | Reference ->
        writer.WriteStartObject()
        writer.WriteString("_type", "relationship_reference")
        writer.WriteString("element_id", rel.ElementId)
        writer.WriteString("_type_name", rel.Type)
        writer.WriteEndObject()

    | Shallow ->
        writer.WriteStartObject()
        writer.WriteString("_type", "embedded_relationship_shallow")
        writer.WriteString("element_id", rel.ElementId)
        writer.WriteString("type", rel.Type)
        writer.WriteString("start_element_id", rel.StartNodeElementId)
        writer.WriteString("end_element_id", rel.EndNodeElementId)
        writer.WriteNumber("_property_count", rel.Properties.Count)
        writer.WriteEndObject()

    | Deep ->
        writer.WriteStartObject()
        writer.WriteString("_type", "embedded_relationship")
        writer.WriteString("element_id", rel.ElementId)
        writer.WriteString("type", rel.Type)
        writer.WriteString("start_element_id", rel.StartNodeElementId)
        writer.WriteString("end_element_id", rel.EndNodeElementId)

        writer.WritePropertyName("properties")
        writer.WriteStartObject()
        serializeProperties writer rel.Properties (SerializationDepth.increment depth) config errorTracker
        writer.WriteEndObject()
        writer.WriteEndObject()

let writeNode (writer: Utf8JsonWriter) (node: INode) (elementId: string) (ctx: WriterContext) =
    writer.WriteStartObject()
    writer.WriteString("type", "node")
    writer.WriteString("element_id", elementId)
    writer.WriteString("export_id", ctx.ExportId.ToString())
    writer.WriteStartArray("labels")

    node.Labels
    |> Seq.truncate ctx.Config.MaxLabelsPerNode
    |> Seq.iter (fun label ->
        match validateLabel label elementId with
        | Ok safeLabel -> writer.WriteStringValue safeLabel
        | Error msg ->
            ctx.ErrorTracker.AddWarning(msg, ?elementId = Some elementId)
            writer.WriteStringValue "_invalid_label")

    writer.WriteEndArray()
    writer.WriteStartObject "properties"

    serializeProperties writer node.Properties SerializationDepth.zero ctx.Config ctx.ErrorTracker

    writer.WriteEndObject()
    writer.WriteEndObject()

let writeRelationship (writer: Utf8JsonWriter) (rel: IRelationship) (ids: EntityIds) (ctx: WriterContext) =
    writer.WriteStartObject()
    writer.WriteString("type", "relationship")
    writer.WriteString("element_id", ids.ElementId)
    writer.WriteString("export_id", ctx.ExportId.ToString())

    let safeType =
        match validateRelType rel.Type ids.ElementId with
        | Ok t -> t
        | Error msg ->
            ctx.ErrorTracker.AddWarning(msg, ?elementId = Some ids.ElementId)
            "_invalid_type"

    writer.WriteString("label", safeType)
    writer.WriteString("start_element_id", ids.StartElementId)
    writer.WriteString("end_element_id", ids.EndElementId)
    writer.WriteStartObject("properties")

    serializeProperties writer rel.Properties SerializationDepth.zero ctx.Config ctx.ErrorTracker

    writer.WriteEndObject()
    writer.WriteEndObject()

let serializeGraphElement
    (writer: Utf8JsonWriter)
    (elem: GraphElement)
    (depth: SerializationDepth)
    (config: ExportConfig)
    (errorTracker: ErrorTracker)
    =
    match elem with
    | GraphElement.Node node -> serializeNode writer node depth config errorTracker
    | GraphElement.Relationship rel -> serializeRelationship writer rel depth config errorTracker
    | GraphElement.Path path -> serializePathFunc.Value writer path config errorTracker
