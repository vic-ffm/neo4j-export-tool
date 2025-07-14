module Neo4jExport.Tests.Integration.SerializationAdapters

open System
open System.Text.Json
open Neo4j.Driver
open Neo4jExport
open Neo4jExport.ExportTypes
open Neo4jExport.SerializationGraphElements
open Neo4jExport.SerializationPath
open Neo4jExport.Tests.Integration.Neo4jAbstractions

// These adapters allow us to test the actual serialization functions
// by creating minimal Neo4j objects that implement just what we need

/// Creates a minimal INode implementation from INodeView for testing
type NodeAdapter(nodeView: INodeView) =
    interface INode with
        member _.ElementId = nodeView.ElementId
        member _.Labels = nodeView.Labels
        member _.Properties = nodeView.Properties
        // These are required by INode but not used in serialization
        member _.Id = 0L

        member _.Item
            with get k = nodeView.Properties.[k]

        member _.Get<'T>(key: string) = nodeView.Properties.[key] :?> 'T

        member _.TryGet<'T>(key: string, value: byref<'T>) =
            match nodeView.Properties.TryGetValue(key) with
            | true, v ->
                value <- v :?> 'T
                true
            | _ -> false

        member _.Equals(other: INode) =
            other <> null
            && other.ElementId = nodeView.ElementId

/// Creates a minimal IRelationship implementation from IRelationshipView for testing
type RelationshipAdapter(relView: IRelationshipView) =
    interface IRelationship with
        member _.ElementId = relView.ElementId
        member _.Type = relView.Type

        member _.StartNodeElementId =
            relView.StartNodeElementId

        member _.EndNodeElementId =
            relView.EndNodeElementId

        member _.Properties = relView.Properties
        // These are required by IRelationship but not used in serialization
        member _.Id = 0L
        member _.StartNodeId = 0L
        member _.EndNodeId = 0L

        member _.Item
            with get k = relView.Properties.[k]

        member _.Get<'T>(key: string) = relView.Properties.[key] :?> 'T

        member _.TryGet<'T>(key: string, value: byref<'T>) =
            match relView.Properties.TryGetValue(key) with
            | true, v ->
                value <- v :?> 'T
                true
            | _ -> false

        member _.Equals(other: IRelationship) =
            other <> null
            && other.ElementId = relView.ElementId

/// Creates a minimal IPath implementation from IPathView for testing
type PathAdapter(pathView: IPathView) =
    let nodes =
        pathView.Nodes
        |> Seq.map (fun n -> NodeAdapter(n) :> INode)
        |> Seq.toList

    let relationships =
        pathView.Relationships
        |> Seq.map (fun r -> RelationshipAdapter(r) :> IRelationship)
        |> Seq.toList

    interface IPath with
        member _.Nodes =
            nodes :> System.Collections.Generic.IReadOnlyList<INode>

        member _.Relationships =
            relationships :> System.Collections.Generic.IReadOnlyList<IRelationship>

        member _.Start =
            if nodes.IsEmpty then null else nodes.Head

        member _.End =
            if nodes.IsEmpty then null else List.last nodes

        member _.Equals(other: IPath) =
            other <> null
            && other.Nodes.Count = nodes.Length
            && other.Relationships.Count = relationships.Length

// Test helper functions that use the adapters

/// Serialize a test node using the actual serialization function
let serializeTestNode (writer: Utf8JsonWriter) (ctx: WriterContext) (depth: SerializationDepth) (nodeView: INodeView) =
    let node = NodeAdapter(nodeView) :> INode
    serializeNode writer ctx depth node

/// Serialize a test relationship using the actual serialization function
let serializeTestRelationship
    (writer: Utf8JsonWriter)
    (ctx: WriterContext)
    (depth: SerializationDepth)
    (relView: IRelationshipView)
    =
    let rel =
        RelationshipAdapter(relView) :> IRelationship

    serializeRelationship writer ctx depth rel

/// Serialize a test path using the actual serialization function
let serializeTestPath (writer: Utf8JsonWriter) (ctx: WriterContext) (pathView: IPathView) =
    let path = PathAdapter(pathView) :> IPath
    serializePath writer ctx path
