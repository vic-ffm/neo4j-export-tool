module Neo4jExport.Tests.Integration.GraphSerializationTests

open System
open System.Collections.Generic
open System.Buffers
open System.Text.Json
open Expecto
open Swensen.Unquote
open Neo4j.Driver
open Neo4jExport
open Neo4jExport.ErrorTracking
open Neo4jExport.SerializationEngine
open Neo4jExport.SerializationGraphElements
open Neo4jExport.SerializationPath
open Neo4jExport.SerializationContext
open Neo4jExport.Tests.Helpers.TestHelpers
open Neo4jExport.Tests.Integration.TestDoubles
open Neo4jExport.Tests.Integration.Neo4jAbstractions
open Neo4jExport.Tests.Integration.SerializationAdapters

[<Tests>]
let tests =
    testList
        "Graph Element Serialization Integration"
        [

          testCase "serialize node with test double"
          <| fun () ->
              // Create test node
              let props = Dictionary<string, obj>()
              props["name"] <- box "Test Person"
              props["age"] <- box 42
              props["scores"] <- box [ 1; 2; 3 ]

              let node =
                  TestNode("node:123", [ "Person"; "Employee" ], props)

              // Serialize using actual serialization engine through adapter
              let json =
                  serializeToJsonWithContext (fun writer ctx depth -> serializeTestNode writer ctx depth node)

              // Verify JSON structure
              withValidatedJson json (fun doc ->
                  let value = getJsonValue doc

                  let elementId =
                      value.GetProperty("element_id").GetString()

                  let labelsCount =
                      value.GetProperty("labels").GetArrayLength()

                  let name =
                      value.GetProperty("properties").GetProperty("name").GetString()

                  test <@ elementId = "node:123" @>
                  test <@ labelsCount = 2 @>
                  test <@ name = "Test Person" @>)

          testCase "serialize relationship with test double"
          <| fun () ->
              let props = Dictionary<string, obj>()
              props["since"] <- box 2020

              let rel =
                  TestRelationship("rel:456", "KNOWS", "node:123", "node:789", props)

              let json =
                  serializeToJsonWithContext (fun writer ctx depth -> serializeTestRelationship writer ctx depth rel)

              withValidatedJson json (fun doc ->
                  let value = getJsonValue doc

                  let elementId =
                      value.GetProperty("element_id").GetString()

                  let relType =
                      value.GetProperty("type").GetString()

                  let since =
                      value.GetProperty("properties").GetProperty("since").GetInt32()

                  test <@ elementId = "rel:456" @>
                  test <@ relType = "KNOWS" @>
                  test <@ since = 2020 @>)

          testCase "serialize path with all modes"
          <| fun () ->
              // Create nodes
              let node1Props = Dictionary<string, obj>()
              node1Props["name"] <- box "A"

              let node1 =
                  TestNode("node:1", [ "Start" ], node1Props) :> INodeView

              let node2Props = Dictionary<string, obj>()
              node2Props["name"] <- box "B"

              let node2 =
                  TestNode("node:2", [ "End" ], node2Props) :> INodeView

              // Create relationship
              let relProps = Dictionary<string, obj>()
              relProps["weight"] <- box 1.5

              let rel =
                  TestRelationship("rel:10", "CONNECTED", "node:1", "node:2", relProps) :> IRelationshipView

              // Create path
              let path =
                  TestPath([ node1; node2 ], [ rel ])

              // Test Full mode
              let configFull =
                  { createTestConfig () with
                      PathFullModeLimit = 10L }

              let jsonFull =
                  serializeToJsonWithConfig (fun writer config ->
                      let errorFuncs =
                          createErrorTrackingSystem (Guid.NewGuid())

                      let ctx =
                          SerializationContext.createWriterContext config errorFuncs (Guid.NewGuid())

                      serializeTestPath writer ctx path)

              withValidatedJson jsonFull (fun doc ->
                  let value = getJsonValue doc
                  let nodes = value.GetProperty("nodes")
                  let nodesCount = nodes.GetArrayLength()
                  test <@ nodesCount = 2 @>
                  // In full mode, nodes should have properties
                  let mutable propsElement =
                      Unchecked.defaultof<JsonElement>

                  let hasProps =
                      nodes[0].TryGetProperty("properties", &propsElement)

                  test <@ hasProps = true @>)

          testCase "nested graph elements in properties"
          <| fun () ->
              // Test serializing nodes that contain other graph elements in properties
              let innerNode =
                  TestNode("node:999", [ "Inner" ], Dictionary<string, obj>())

              let innerAdapter =
                  NodeAdapter(innerNode) :> INode

              let props = Dictionary<string, obj>()
              props["simple"] <- box "value"
              props["nested"] <- box innerAdapter

              let outerNode =
                  TestNode("node:1", [ "Outer" ], props)

              let json =
                  serializeToJsonWithContext (fun writer ctx depth -> serializeTestNode writer ctx depth outerNode)

              withValidatedJson json (fun doc ->
                  let value = getJsonValue doc

                  let nestedProp =
                      value.GetProperty("properties").GetProperty("nested")
                  // Should serialize as reference in nested context
                  let mutable elementIdProp =
                      Unchecked.defaultof<JsonElement>

                  let hasElementId =
                      nestedProp.TryGetProperty("element_id", &elementIdProp)

                  test <@ hasElementId = true @>) ]
