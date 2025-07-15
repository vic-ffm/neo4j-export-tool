module Neo4jExport.Tests.EndToEnd.ExampleContainerTests

open Expecto
open Swensen.Unquote
open Neo4j.Driver
open Neo4jExport.Tests.EndToEnd.Infrastructure.ContainerFixtures
open Neo4jExport.Tests.Helpers.TestLog
open Neo4jExport.Tests.EndToEnd.Infrastructure.TestDataManagement

[<Tests>]
let tests =
    let testCount = 7 // Update this if adding/removing tests
    testListStart "Container Infrastructure Examples" testCount

    // Tag container tests so they can be easily filtered
    testList
        "Container Infrastructure Examples"
        [

          // Basic container test
          Fixtures.testWithCleanDb "can connect to Neo4j container" (fun driver ->
              async {
                  use session = driver.AsyncSession()

                  let! cursor =
                      session.RunAsync("RETURN 'Hello from Neo4j!' as message")
                      |> Async.AwaitTask

                  let! record = cursor.SingleAsync() |> Async.AwaitTask

                  let message =
                      record.["message"].As<string>()

                  test <@ message = "Hello from Neo4j!" @>
              })

          // Multi-version test
          Fixtures.testWithAllVersions "verify version detection" (fun driver ->
              async {
                  use session = driver.AsyncSession()

                  let! cursor =
                      session.RunAsync(
                          "CALL dbms.components() YIELD name, versions RETURN name, versions[0] as version"
                      )
                      |> Async.AwaitTask

                  let! records = cursor.ToListAsync() |> Async.AwaitTask

                  let neo4jRecord =
                      records
                      |> Seq.find (fun r -> r.["name"].As<string>() = "Neo4j Kernel")

                  let version =
                      neo4jRecord.["version"].As<string>()

                  test
                      <@
                          version.StartsWith("4.4")
                          || version.StartsWith("5.")
                      @>
              })

          // Data seeding test
          Fixtures.testWithData "verify simple graph seeding" SimpleGraph Small (fun driver ->
              async {
                  use session = driver.AsyncSession()

                  // Count nodes
                  let! cursor =
                      session.RunAsync("MATCH (n:TestNode) RETURN count(n) as count")
                      |> Async.AwaitTask

                  let! record = cursor.SingleAsync() |> Async.AwaitTask
                  let nodeCount = record.["count"].As<int64>()

                  // Count relationships
                  let! cursor =
                      session.RunAsync("MATCH ()-[r:CONNECTED]->() RETURN count(r) as count")
                      |> Async.AwaitTask

                  let! record = cursor.SingleAsync() |> Async.AwaitTask
                  let relCount = record.["count"].As<int64>()

                  test <@ nodeCount = 5000L @>
                  test <@ relCount = 10000L @>
              })

          // Complex types test
          Fixtures.testWithData "verify complex types seeding" ComplexTypes Small (fun driver ->
              async {
                  use session = driver.AsyncSession()

                  // Check temporal node
                  let! cursor =
                      session.RunAsync("MATCH (n:TemporalNode) RETURN n")
                      |> Async.AwaitTask

                  let! record = cursor.SingleAsync() |> Async.AwaitTask
                  let node = record.["n"].As<INode>()

                  test <@ node.Properties.ContainsKey("date") @>
                  test <@ node.Properties.ContainsKey("duration") @>
                  test <@ node.Properties.ContainsKey("epochDateTime") @>
                  test <@ node.Properties.ContainsKey("negativeDuration") @>

                  // Check spatial node
                  let! cursor =
                      session.RunAsync("MATCH (n:SpatialNode) RETURN n")
                      |> Async.AwaitTask

                  let! record = cursor.SingleAsync() |> Async.AwaitTask
                  let node = record.["n"].As<INode>()

                  test <@ node.Properties.ContainsKey("wgs84_2d") @>
                  test <@ node.Properties.ContainsKey("cartesian3d") @>
                  test <@ node.Properties.ContainsKey("highPrecision") @>
                  test <@ node.Properties.ContainsKey("pointList2d") @>
                  test <@ node.Properties.ContainsKey("pointListGeo") @>
                  test <@ node.Properties.ContainsKey("emptyPointList") @>

                  // Check all primitive type nodes exist
                  let primitiveLabels =
                      [ "PrimitiveNode"
                        "FloatNode"
                        "StringNode"
                        "CollectionNode"
                        "SerializationTestNode"
                        "MapDataNode"
                        "ByteArrayNode"
                        "LongStringNode" ]

                  for label in primitiveLabels do
                      let! cursor =
                          session.RunAsync($"MATCH (n:{label}) RETURN count(n) as cnt")
                          |> Async.AwaitTask

                      let! record = cursor.SingleAsync() |> Async.AwaitTask
                      let count = record.["cnt"].As<int64>()
                      test <@ count > 0L @>
              })

          // Edge cases test
          Fixtures.testWithData "verify edge cases seeding" EdgeCases Small (fun driver ->
              async {
                  use session = driver.AsyncSession()

                  // Check boundary node
                  let! cursor =
                      session.RunAsync("MATCH (n:BoundaryNode) RETURN n")
                      |> Async.AwaitTask

                  let! record = cursor.SingleAsync() |> Async.AwaitTask
                  let node = record.["n"].As<INode>()

                  test <@ node.Properties.ContainsKey("maxInt") @>
                  test <@ node.Properties.ContainsKey("кириллица") @> // Cyrillic property
                  test <@ node.Properties.ContainsKey("中文属性") @> // Chinese property

                  // Check self-referencing node
                  let! cursor =
                      session.RunAsync("""MATCH (n:SelfRefNode)-[r:SELF_REF]->(n) RETURN count(r) as selfRefs""")
                      |> Async.AwaitTask

                  let! record = cursor.SingleAsync() |> Async.AwaitTask

                  let selfRefs =
                      record.["selfRefs"].As<int64>()

                  test <@ selfRefs = 1L @>
              })

          // Truncation tests
          Fixtures.testWithData "verify truncation test data" TruncationTests Small (fun driver ->
              async {
                  use session = driver.AsyncSession()

                  // Check node with exactly 10 labels
                  let! cursor =
                      session.RunAsync(
                          """MATCH (n:L1:L2:L3:L4:L5:L6:L7:L8:L9:L10 {id: 'exactly-10-labels'}) RETURN size(labels(n)) as labelCount"""
                      )
                      |> Async.AwaitTask

                  let! record = cursor.SingleAsync() |> Async.AwaitTask

                  let labelCount =
                      record.["labelCount"].As<int64>()

                  test <@ labelCount = 10L @>

                  // Check truncation boundary node exists
                  let! cursor =
                      session.RunAsync("MATCH (n:TruncationBoundary) RETURN n")
                      |> Async.AwaitTask

                  let! record = cursor.SingleAsync() |> Async.AwaitTask
                  let node = record.["n"].As<INode>()

                  test <@ node.Properties.ContainsKey("exactLimit") @>
                  test <@ node.Properties.ContainsKey("overLimit") @>
              }) ]
