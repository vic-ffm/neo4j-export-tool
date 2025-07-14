module Neo4jExport.Tests.Integration.TestDoubles

open System
open System.Collections.Generic
open System.Collections.ObjectModel
open System.IO
open System.Threading
open Neo4j.Driver
open Neo4jExport
open Neo4jExport.ErrorTracking
open Neo4jExport.JsonHelpers
open Neo4jExport.Tests.Integration.Neo4jAbstractions

// 1. File System Operations Test Double
type FileSystemOps =
    { WriteAsync: string -> byte[] -> Async<Result<unit, exn>>
      Exists: string -> bool
      CreateDirectory: string -> Result<unit, exn>
      Delete: string -> Result<unit, exn>
      GetTempFileName: unit -> string
      Move: string -> string -> Result<unit, exn> }

// 2. Database Operations Test Double (using our minimal interfaces)
type DatabaseOps<'a> =
    { ExecuteQuery: string -> Map<string, obj> -> Async<Result<IResultCursorView, exn>>
      RunTransaction: (IAsyncTransaction -> Async<'a>) -> Async<Result<'a, exn>>
      GetVersion: unit -> Async<Result<string, exn>>
      Close: unit -> Async<unit> }

// 3. Error Tracking Test Double
type ErrorTracker =
    { RecordError: AppError -> unit
      RecordWarning: string -> unit
      GetErrors: unit -> AppError list
      GetWarnings: unit -> string list
      Reset: unit -> unit }

// 4. Neo4j Type Test Doubles using minimal interfaces
// These implement only what we actually need for testing

// 4a. Simple test node implementing INodeView
type TestNode(elementId: string, labels: string list, properties: IDictionary<string, obj>) =
    interface INodeView with
        member _.ElementId = elementId

        member _.Labels =
            labels :> IReadOnlyList<string>

        member _.Properties =
            ReadOnlyDictionary<string, obj>(properties) :> IReadOnlyDictionary<string, obj>

    member _.Properties = properties

// 4b. Simple test relationship implementing IRelationshipView
type TestRelationship
    (
        elementId: string,
        relType: string,
        startElementId: string,
        endElementId: string,
        properties: IDictionary<string, obj>
    ) =
    interface IRelationshipView with
        member _.ElementId = elementId
        member _.Type = relType
        member _.StartNodeElementId = startElementId
        member _.EndNodeElementId = endElementId

        member _.Properties =
            ReadOnlyDictionary<string, obj>(properties) :> IReadOnlyDictionary<string, obj>

    member _.Properties = properties

// 4c. Simple test path implementing IPathView
type TestPath(nodes: INodeView list, relationships: IRelationshipView list) =
    interface IPathView with
        member _.Nodes =
            nodes :> IReadOnlyList<INodeView>

        member _.Relationships =
            relationships :> IReadOnlyList<IRelationshipView>

// 4d. Simple test record implementing IRecordView
type TestRecord(values: Map<string, obj>) =
    interface IRecordView with
        member _.Keys =
            values |> Map.keys |> Seq.toList :> IReadOnlyList<string>

        member _.Item
            with get (key: string) = values.[key]

        member _.TryGetValue(key, value: byref<obj>) =
            match Map.tryFind key values with
            | Some v ->
                value <- v
                true
            | None -> false

    member _.Values = values

// 4e. Simple test cursor implementing IResultCursorView
type TestResultCursor(records: IRecordView list) =
    let mutable currentIndex = -1

    interface IResultCursorView with
        member _.Current =
            if currentIndex >= 0 && currentIndex < records.Length then
                records.[currentIndex]
            else
                raise (InvalidOperationException("No current record"))

        member _.FetchAsync() =
            async {
                currentIndex <- currentIndex + 1
                return currentIndex < records.Length
            }
            |> Async.StartAsTask

        member _.ConsumeAsync() =
            async {
                // Return null for IResultSummary as it's allowed
                return null :> IResultSummary
            }
            |> Async.StartAsTask

// Factory functions for creating test doubles
let createInMemoryFileSystem () =
    let files = ref Map.empty<string, byte[]>
    let directories = ref Set.empty<string>

    { WriteAsync =
        fun path content ->
            async {
                files := Map.add path content !files
                return Ok()
            }

      Exists =
        fun path ->
            Map.containsKey path !files
            || Set.contains path !directories

      CreateDirectory =
        fun path ->
            directories := Set.add path !directories
            Ok()

      Delete =
        fun path ->
            files := Map.remove path !files
            Ok()

      GetTempFileName = fun () -> $"/tmp/test_{Guid.NewGuid()}.tmp"

      Move =
        fun source dest ->
            match Map.tryFind source !files with
            | Some content ->
                files
                := !files
                   |> Map.remove source
                   |> Map.add dest content

                Ok()
            | None -> Error(System.IO.FileNotFoundException($"Source file not found: {source}") :> exn) }

let createThreadSafeErrorTracker () =
    let errors = ref []
    let warnings = ref []
    let lockObj = obj ()

    { RecordError = fun error -> lock lockObj (fun () -> errors := error :: !errors)

      RecordWarning = fun warning -> lock lockObj (fun () -> warnings := warning :: !warnings)

      GetErrors = fun () -> lock lockObj (fun () -> List.rev !errors)

      GetWarnings = fun () -> lock lockObj (fun () -> List.rev !warnings)

      Reset =
        fun () ->
            lock lockObj (fun () ->
                errors := []
                warnings := []) }
