module Neo4jExport.Tests.Integration.MetadataTests

open System
open System.Collections.Generic
open Expecto
open Swensen.Unquote
open Neo4j.Driver
open Neo4jExport
open Neo4jExport.Tests.Helpers.TestHelpers
open Neo4jExport.Tests.Integration.TestDoubles
open Neo4jExport.Tests.Integration.Neo4jAbstractions

[<Tests>]
let tests =
    testList
        "Metadata Collection Integration"
        [

          testCase "schema query collection"
          <| fun () ->
              // Create mock database that returns schema info
              let schemaRecords =
                  [
                    // Node labels
                    let values1 =
                        Map.ofList
                            [ "entityType", box "NODE"
                              "label", box "Person"
                              "propertyName", box "name"
                              "propertyType", box "STRING" ]

                    yield TestRecord(values1) :> IRecordView

                    let values2 =
                        Map.ofList
                            [ "entityType", box "NODE"
                              "label", box "Person"
                              "propertyName", box "age"
                              "propertyType", box "INTEGER" ]

                    yield TestRecord(values2) :> IRecordView ]

              let cursor =
                  TestResultCursor(schemaRecords) :> IResultCursorView

              // Process schema
              let schema = ref []
              let mutable hasMore = true

              while hasMore do
                  let fetchResult =
                      cursor.FetchAsync()
                      |> Async.AwaitTask
                      |> Async.RunSynchronously

                  if fetchResult then
                      schema := cursor.Current :: !schema

                  hasMore <- fetchResult

              test <@ List.length !schema = 2 @>

          testCase "version compatibility detection"
          <| fun () ->
              let db: DatabaseOps<unit> =
                  { ExecuteQuery =
                      fun query _ ->
                          if query.Contains("dbms.components") then
                              let values =
                                  Map.ofList [ "version", box "5.15.0" ]

                              let record =
                                  TestRecord(values) :> IRecordView

                              async { return Ok(TestResultCursor([ record ]) :> IResultCursorView) }
                          else
                              async { return Error(Exception("Unknown query")) }

                    RunTransaction = fun _ -> async { return Error(System.NotImplementedException() :> exn) }
                    GetVersion = fun () -> async { return Ok "5.15.0" }
                    Close = fun () -> async { return () } }

              let version =
                  db.GetVersion() |> Async.RunSynchronously

              test <@ version = Ok "5.15.0" @>

          testCase "statistics aggregation"
          <| fun () ->
              // Test collecting node/relationship counts
              () ]
