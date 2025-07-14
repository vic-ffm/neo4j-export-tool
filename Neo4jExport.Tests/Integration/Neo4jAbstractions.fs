module Neo4jExport.Tests.Integration.Neo4jAbstractions

open System
open System.Collections.Generic
open Neo4j.Driver

// Minimal interfaces that represent only what we actually use from Neo4j types
// This follows the Interface Segregation Principle and "Don't mock what you don't own"

/// Minimal view of INode containing only the properties we use
type INodeView =
    abstract ElementId: string
    abstract Labels: IReadOnlyList<string>
    abstract Properties: IReadOnlyDictionary<string, obj>

/// Minimal view of IRelationship containing only the properties we use
type IRelationshipView =
    abstract ElementId: string
    abstract Type: string
    abstract StartNodeElementId: string
    abstract EndNodeElementId: string
    abstract Properties: IReadOnlyDictionary<string, obj>

/// Minimal view of IPath containing only the properties we use
type IPathView =
    abstract Nodes: IReadOnlyList<INodeView>
    abstract Relationships: IReadOnlyList<IRelationshipView>

/// Minimal view of IRecord containing only what we use
type IRecordView =
    abstract Keys: IReadOnlyList<string>
    abstract Item: string -> obj with get
    abstract TryGetValue: string * byref<obj> -> bool

/// Minimal view of IResultCursor containing only what we use
type IResultCursorView =
    abstract Current: IRecordView
    abstract FetchAsync: unit -> System.Threading.Tasks.Task<bool>
    abstract ConsumeAsync: unit -> System.Threading.Tasks.Task<IResultSummary>

// Adapter functions to convert from Neo4j types to our views
// These would normally go in the production code, but for testing we'll keep them here

let nodeToView (node: INode) : INodeView =
    { new INodeView with
        member _.ElementId = node.ElementId
        member _.Labels = node.Labels
        member _.Properties = node.Properties }

let relationshipToView (rel: IRelationship) : IRelationshipView =
    { new IRelationshipView with
        member _.ElementId = rel.ElementId
        member _.Type = rel.Type

        member _.StartNodeElementId =
            rel.StartNodeElementId

        member _.EndNodeElementId =
            rel.EndNodeElementId

        member _.Properties = rel.Properties }

let pathToView (path: IPath) : IPathView =
    { new IPathView with
        member _.Nodes =
            path.Nodes |> Seq.map nodeToView |> Seq.toList :> IReadOnlyList<INodeView>

        member _.Relationships =
            path.Relationships
            |> Seq.map relationshipToView
            |> Seq.toList
            :> IReadOnlyList<IRelationshipView> }

let recordToView (record: IRecord) : IRecordView =
    { new IRecordView with
        member _.Keys = record.Keys

        member _.Item
            with get key = record.[key]

        member _.TryGetValue(key, value) =
            try
                value <- record.[key]
                true
            with _ ->
                false }

// Test-specific adapter for IResultCursor
type ResultCursorAdapter(cursor: IResultCursor) =
    interface IResultCursorView with
        member _.Current = recordToView cursor.Current
        member _.FetchAsync() = cursor.FetchAsync()
        member _.ConsumeAsync() = cursor.ConsumeAsync()
