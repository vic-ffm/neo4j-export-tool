module Neo4jExport.Tests.EndToEnd.ExportTests.DataTypeTests

open System
open System.IO
open System.Text.Json
open Expecto
open Swensen.Unquote
open Neo4jExport.Tests.EndToEnd.Infrastructure.ContainerFixtures
open Neo4jExport.Tests.EndToEnd.Infrastructure.ContainerLifecycle
open Neo4jExport.Tests.EndToEnd.Infrastructure.TestDataManagement
open Neo4jExport.Tests.EndToEnd.ExportTests.ExportTestUtils

[<Tests>]
let tests =
    testList
        "Data Type Export Tests"
        [

          // Test 1: Primitive types
          testAsync "exports all primitive types correctly" {
              do!
                  ContainerFixture.withContainerInfo ContainerConfig.defaultConfig (fun containerInfo ->
                      async {
                          do!
                              TestExecution.withSeededData containerInfo.Driver ComplexTypes Small (fun () ->
                                  async {
                                      // Run export
                                      let uri =
                                          Uri(containerInfo.ConnectionString)

                                      match! Fixtures.runExportWithMetrics uri "neo4j" containerInfo.Password with
                                      | Ok(_metrics, exportFile) ->
                                          // Parse records
                                          let (_metadata, records) =
                                              parseExportFile exportFile

                                          // Find nodes with primitive properties
                                          let primitiveNodes =
                                              records
                                              |> Array.filter (fun r ->
                                                  r.GetProperty("type").GetString() = "node"
                                                  && match tryGetProperty r "_labels" with
                                                     | Some labels -> arrayContainsString labels "PrimitiveTypes"
                                                     | None -> false)

                                          test <@ primitiveNodes.Length > 0 @>

                                          let props =
                                              primitiveNodes.[0].GetProperty("properties")

                                          // Test integer types
                                          let hasprops =
                                              props.TryGetProperty("byteValue") |> fst

                                          test <@ hasprops @>

                                          let hasprops =
                                              props.TryGetProperty("shortValue") |> fst

                                          test <@ hasprops @>

                                          let hasprops =
                                              props.TryGetProperty("intValue") |> fst

                                          test <@ hasprops @>

                                          let hasprops =
                                              props.TryGetProperty("longValue") |> fst

                                          test <@ hasprops @>

                                          // Test float types
                                          let hasprops =
                                              props.TryGetProperty("floatValue") |> fst

                                          test <@ hasprops @>

                                          let hasprops =
                                              props.TryGetProperty("doubleValue") |> fst

                                          test <@ hasprops @>

                                          // Test special float values
                                          match tryGetProperty props "nanFloat" with
                                          | Some nanFloat ->
                                              let nanValue = nanFloat.GetString()
                                              test <@ nanValue = "NaN" @>
                                          | None -> ()

                                          match tryGetProperty props "infinityFloat" with
                                          | Some infFloat ->
                                              let infValue = infFloat.GetString()
                                              test <@ infValue = "Infinity" @>
                                          | None -> ()

                                          // Test boolean
                                          let hasprops =
                                              props.TryGetProperty("boolTrue") |> fst

                                          test <@ hasprops @>

                                          match tryGetProperty props "boolTrue" with
                                          | Some boolVal ->
                                              let boolValue = boolVal.GetBoolean()
                                              test <@ boolValue = true @>
                                          | None -> ()

                                          // Test string (including Unicode)
                                          let hasprops =
                                              props.TryGetProperty("stringValue") |> fst

                                          test <@ hasprops @>

                                          match tryGetProperty props "unicodeString" with
                                          | Some unicode ->
                                              let str = unicode.GetString()
                                              test <@ str.Contains("Hello 世界") || str.Contains("Привет") @>
                                          | None -> ()

                                      | Error err -> failtest $"Export failed: {err}"
                                  })
                      })
          }

          // Test 2: Temporal types with nanosecond truncation
          testAsync "exports temporal types with correct precision" {
              do!
                  ContainerFixture.withContainerInfo ContainerConfig.defaultConfig (fun containerInfo ->
                      async {
                          do!
                              TestExecution.withSeededData containerInfo.Driver ComplexTypes Small (fun () ->
                                  async {
                                      let uri =
                                          Uri(containerInfo.ConnectionString)

                                      match! Fixtures.runExportWithMetrics uri "neo4j" containerInfo.Password with
                                      | Ok(_metrics, exportFile) ->
                                          let (_metadata, records) =
                                              parseExportFile exportFile

                                          // Find nodes with temporal properties
                                          let temporalNodes =
                                              records
                                              |> Array.filter (fun r ->
                                                  r.GetProperty("type").GetString() = "node"
                                                  && match tryGetProperty r "_labels" with
                                                     | Some labels -> arrayContainsString labels "TemporalTypes"
                                                     | None -> false)

                                          test <@ temporalNodes.Length > 0 @>

                                          let props =
                                              temporalNodes.[0].GetProperty("properties")

                                          // Test LocalDateTime - verify nanosecond truncation
                                          match tryGetProperty props "localDateTimeValue" with
                                          | Some ldt ->
                                              let str = ldt.GetString()
                                              // Should be truncated to 100ns precision (7 decimal places max)
                                              let dotIndex = str.LastIndexOf('.')

                                              if dotIndex > 0 then
                                                  let fractionalPart =
                                                      str.Substring(dotIndex + 1).TrimEnd('Z')

                                                  test <@ fractionalPart.Length <= 7 @>
                                          | None -> ()

                                          // Test ZonedDateTime
                                          let hasZonedDateTime =
                                              props.TryGetProperty("zonedDateTimeValue") |> fst

                                          test <@ hasZonedDateTime @>

                                          match tryGetProperty props "zonedDateTimeValue" with
                                          | Some zdt ->
                                              let str = zdt.GetString()

                                              test
                                                  <@
                                                      str.Contains("[")
                                                      || str.Contains("+")
                                                      || str.Contains("-")
                                                  @> // Has timezone
                                          | None -> ()

                                          // Test LocalDate
                                          match tryGetProperty props "localDateValue" with
                                          | Some ld ->
                                              let str = ld.GetString()

                                              test
                                                  <@
                                                      System.Text.RegularExpressions.Regex.IsMatch(
                                                          str,
                                                          @"^\d{4}-\d{2}-\d{2}$"
                                                      )
                                                  @>
                                          | None -> ()

                                          // Test LocalTime
                                          match tryGetProperty props "localTimeValue" with
                                          | Some lt ->
                                              let str = lt.GetString()
                                              test <@ str.Contains(":") @>
                                          | None -> ()

                                          // Test Duration
                                          match tryGetProperty props "durationValue" with
                                          | Some dur ->
                                              let str = dur.GetString()
                                              test <@ str.StartsWith("P") || str.StartsWith("-P") @>
                                          | None -> ()

                                      | Error err -> failtest $"Export failed: {err}"
                                  })
                      })
          }

          // Test 3: Spatial types
          testAsync "exports spatial types correctly" {
              do!
                  ContainerFixture.withContainerInfo ContainerConfig.defaultConfig (fun containerInfo ->
                      async {
                          do!
                              TestExecution.withSeededData containerInfo.Driver ComplexTypes Small (fun () ->
                                  async {
                                      let uri =
                                          Uri(containerInfo.ConnectionString)

                                      match! Fixtures.runExportWithMetrics uri "neo4j" containerInfo.Password with
                                      | Ok(_metrics, exportFile) ->
                                          let (_metadata, records) =
                                              parseExportFile exportFile

                                          // Find nodes with spatial properties
                                          let spatialNodes =
                                              records
                                              |> Array.filter (fun r ->
                                                  r.GetProperty("type").GetString() = "node"
                                                  && match tryGetProperty r "_labels" with
                                                     | Some labels -> arrayContainsString labels "SpatialTypes"
                                                     | None -> false)

                                          test <@ spatialNodes.Length > 0 @>

                                          let props =
                                              spatialNodes.[0].GetProperty("properties")

                                          // Test WGS84 2D point
                                          match tryGetProperty props "wgs84Point" with
                                          | Some point ->
                                              let haspoint =
                                                  point.TryGetProperty("srid") |> fst

                                              test <@ haspoint @>

                                              let srid =
                                                  point.GetProperty("srid").GetInt32()

                                              test <@ srid = 4326 @>

                                              let haspoint =
                                                  point.TryGetProperty("x") |> fst

                                              test <@ haspoint @>

                                              let haspoint =
                                                  point.TryGetProperty("y") |> fst

                                              test <@ haspoint @>
                                          | None -> ()

                                          // Test Cartesian 2D point
                                          match tryGetProperty props "cartesian2DPoint" with
                                          | Some point ->
                                              let srid =
                                                  point.GetProperty("srid").GetInt32()

                                              test <@ srid = 7203 @>

                                              let haspoint =
                                                  point.TryGetProperty("x") |> fst

                                              test <@ haspoint @>

                                              let haspoint =
                                                  point.TryGetProperty("y") |> fst

                                              test <@ haspoint @>
                                          | None -> ()

                                          // Test Cartesian 3D point
                                          match tryGetProperty props "cartesian3DPoint" with
                                          | Some point ->
                                              let srid =
                                                  point.GetProperty("srid").GetInt32()

                                              test <@ srid = 9157 @>

                                              let haspoint =
                                                  point.TryGetProperty("x") |> fst

                                              test <@ haspoint @>

                                              let haspoint =
                                                  point.TryGetProperty("y") |> fst

                                              test <@ haspoint @>

                                              let haspoint =
                                                  point.TryGetProperty("z") |> fst

                                              test <@ haspoint @>
                                          | None -> ()

                                          // Test point array (LIST<POINT>)
                                          match tryGetProperty props "pointList" with
                                          | Some pointArray ->
                                              let count = pointArray.GetArrayLength()
                                              test <@ count > 0 @>

                                              if count > 0 then
                                                  let firstPoint =
                                                      pointArray.EnumerateArray() |> Seq.head

                                                  let hasSrid =
                                                      firstPoint.TryGetProperty("srid") |> fst

                                                  test <@ hasSrid @>

                                                  let hasX =
                                                      firstPoint.TryGetProperty("x") |> fst

                                                  test <@ hasX @>

                                                  let hasY =
                                                      firstPoint.TryGetProperty("y") |> fst

                                                  test <@ hasY @>
                                          | None -> ()

                                      | Error err -> failtest $"Export failed: {err}"
                                  })
                      })
          }

          // Test 4: Collections
          testAsync "exports collections correctly" {
              do!
                  ContainerFixture.withContainerInfo ContainerConfig.defaultConfig (fun containerInfo ->
                      async {
                          do!
                              TestExecution.withSeededData containerInfo.Driver ComplexTypes Small (fun () ->
                                  async {
                                      let uri =
                                          Uri(containerInfo.ConnectionString)

                                      match! Fixtures.runExportWithMetrics uri "neo4j" containerInfo.Password with
                                      | Ok(_metrics, exportFile) ->
                                          let (_metadata, records) =
                                              parseExportFile exportFile

                                          // Find nodes with collection properties
                                          let collectionNodes =
                                              records
                                              |> Array.filter (fun r ->
                                                  r.GetProperty("type").GetString() = "node"
                                                  && match tryGetProperty r "_labels" with
                                                     | Some labels -> arrayContainsString labels "CollectionTypes"
                                                     | None -> false)

                                          test <@ collectionNodes.Length > 0 @>

                                          let props =
                                              collectionNodes.[0].GetProperty("properties")

                                          // Test homogeneous arrays
                                          match tryGetProperty props "intList" with
                                          | Some intArray ->
                                              let arrayLength = intArray.GetArrayLength()
                                              test <@ arrayLength > 0 @>

                                              let allNumbers =
                                                  intArray.EnumerateArray()
                                                  |> Seq.forall (fun v -> v.ValueKind = JsonValueKind.Number)

                                              test <@ allNumbers @>
                                          | None -> ()

                                          match tryGetProperty props "stringList" with
                                          | Some strArray ->
                                              let arrayLength = strArray.GetArrayLength()
                                              test <@ arrayLength > 0 @>

                                              let allStrings =
                                                  strArray.EnumerateArray()
                                                  |> Seq.forall (fun v -> v.ValueKind = JsonValueKind.String)

                                              test <@ allStrings @>
                                          | None -> ()

                                          // Test empty arrays
                                          match tryGetProperty props "emptyList" with
                                          | Some emptyArray ->
                                              let arrayLength =
                                                  emptyArray.GetArrayLength()

                                              test <@ arrayLength = 0 @>
                                          | None -> ()

                                          // Test nested arrays
                                          match tryGetProperty props "nestedList" with
                                          | Some nestedArray ->
                                              let nestedLength =
                                                  nestedArray.GetArrayLength()

                                              test <@ nestedLength > 0 @>

                                              let firstNested =
                                                  nestedArray.EnumerateArray() |> Seq.tryHead

                                              match firstNested with
                                              | Some nested ->
                                                  let nestedLength = nested.GetArrayLength()
                                                  test <@ nestedLength > 0 @>
                                              | None -> ()
                                          | None -> ()

                                          // Verify unsupported types are omitted
                                          let hasMixedTypeList =
                                              props.TryGetProperty("mixedTypeList") |> fst

                                          test <@ not hasMixedTypeList @>

                                          let hasListWithNulls =
                                              props.TryGetProperty("listWithNulls") |> fst

                                          test <@ not hasListWithNulls @>

                                      | Error err -> failtest $"Export failed: {err}"
                                  })
                      })
          }

          // Test 5: Special characters and Unicode
          testAsync "handles special characters correctly" {
              do!
                  ContainerFixture.withContainerInfo ContainerConfig.defaultConfig (fun containerInfo ->
                      async {
                          do!
                              TestExecution.withSeededData containerInfo.Driver EdgeCases Small (fun () ->
                                  async {
                                      let uri =
                                          Uri(containerInfo.ConnectionString)

                                      match! Fixtures.runExportWithMetrics uri "neo4j" containerInfo.Password with
                                      | Ok(_metrics, exportFile) ->
                                          let (_metadata, records) =
                                              parseExportFile exportFile

                                          // Find nodes with Unicode properties
                                          let unicodeNodes =
                                              records
                                              |> Array.filter (fun r ->
                                                  r.GetProperty("type").GetString() = "node"
                                                  && match tryGetProperty r "_labels" with
                                                     | Some labels -> arrayContainsString labels "UnicodeTest"
                                                     | None -> false)

                                          test <@ unicodeNodes.Length > 0 @>

                                          let props =
                                              unicodeNodes.[0].GetProperty("properties")

                                          // Test Chinese characters
                                          match tryGetProperty props "chinese" with
                                          | Some chinese ->
                                              let str = chinese.GetString()
                                              test <@ str.Contains("世界") || str.Contains("中文") @>
                                          | None -> ()

                                          // Test Cyrillic
                                          match tryGetProperty props "cyrillic" with
                                          | Some cyrillic ->
                                              let str = cyrillic.GetString()
                                              test <@ str.Contains("Привет") || str.Contains("мир") @>
                                          | None -> ()

                                          // Test emoji
                                          match tryGetProperty props "emoji" with
                                          | Some emoji ->
                                              let str = emoji.GetString()
                                              test <@ str.Contains("🚀") || str.Contains("😀") @>
                                          | None -> ()

                                          // Test JSON special characters (should be escaped)
                                          match tryGetProperty props "jsonSpecial" with
                                          | Some special ->
                                              let str = special.GetString()
                                              // The value should be properly escaped in JSON
                                              test <@ str.Length > 0 @>
                                          | None -> ()

                                      | Error err -> failtest $"Export failed: {err}"
                                  })
                      })
          }

          // Test 6: Large values and truncation
          testAsync "handles truncation correctly" {
              do!
                  ContainerFixture.withContainerInfo ContainerConfig.defaultConfig (fun containerInfo ->
                      async {
                          do!
                              TestExecution.withSeededData containerInfo.Driver TruncationTests Small (fun () ->
                                  async {
                                      let uri =
                                          Uri(containerInfo.ConnectionString)

                                      match! Fixtures.runExportWithMetrics uri "neo4j" containerInfo.Password with
                                      | Ok(_metrics, exportFile) ->
                                          let (_metadata, records) =
                                              parseExportFile exportFile

                                          // Find nodes with truncation test data
                                          let truncNodes =
                                              records
                                              |> Array.filter (fun r ->
                                                  r.GetProperty("type").GetString() = "node"
                                                  && match tryGetProperty r "_labels" with
                                                     | Some labels -> arrayContainsString labels "TruncationTest"
                                                     | None -> false)

                                          test <@ truncNodes.Length > 0 @>

                                          let props =
                                              truncNodes.[0].GetProperty("properties")

                                          // Test large string truncation
                                          match tryGetProperty props "veryLongString" with
                                          | Some longStr ->
                                              let str = longStr.GetString()
                                              // Should be truncated with marker
                                              test
                                                  <@
                                                      str.Contains("...TRUNCATED")
                                                      || str.Length < 100000
                                                  @>
                                          | None -> ()

                                          // Test array truncation
                                          match tryGetProperty props "veryLargeList" with
                                          | Some largeArray ->
                                              // Should be truncated at element limit
                                              let largeLength =
                                                  largeArray.GetArrayLength()

                                              test <@ largeLength <= 10000 @> // Default MAX_COLLECTION_ITEMS
                                          | None -> ()

                                          // Test byte arrays
                                          match tryGetProperty props "byteArray" with
                                          | Some byteArray ->
                                              let byteLength = byteArray.GetArrayLength()
                                              test <@ byteLength > 0 @>

                                              let allNumbers =
                                                  byteArray.EnumerateArray()
                                                  |> Seq.forall (fun v -> v.ValueKind = JsonValueKind.Number)

                                              test <@ allNumbers @>
                                          | None -> ()

                                      | Error err -> failtest $"Export failed: {err}"
                                  })
                      })
          }

          // Test 7: Graph elements
          testAsync "exports graph elements correctly" {
              do!
                  ContainerFixture.withContainerInfo ContainerConfig.defaultConfig (fun containerInfo ->
                      async {
                          do!
                              TestExecution.withSeededData containerInfo.Driver ComplexTypes Small (fun () ->
                                  async {
                                      let uri =
                                          Uri(containerInfo.ConnectionString)

                                      match! Fixtures.runExportWithMetrics uri "neo4j" containerInfo.Password with
                                      | Ok(_metrics, exportFile) ->
                                          let (_metadata, records) =
                                              parseExportFile exportFile

                                          // Test multiple labels on nodes
                                          let multiLabelNodes =
                                              records
                                              |> Array.filter (fun r ->
                                                  isRecordType "node" r
                                                  && match tryGetProperty r "_labels" with
                                                     | Some labels -> labels.GetArrayLength() > 1
                                                     | None -> false)

                                          test <@ multiLabelNodes.Length > 0 @>

                                          // Test self-referencing relationships
                                          let selfRefs =
                                              records
                                              |> Array.filter (fun r ->
                                                  isRecordType "relationship" r
                                                  && r.GetProperty("_start_node_element_id").GetString() = r
                                                      .GetProperty("_end_node_element_id")
                                                      .GetString())

                                          test <@ selfRefs.Length > 0 @>

                                          // Test properties on relationships
                                          let relsWithProps =
                                              records
                                              |> Array.filter (fun r ->
                                                  isRecordType "relationship" r
                                                  && match tryGetProperty r "properties" with
                                                     | Some props -> props.EnumerateObject() |> Seq.length > 0
                                                     | None -> false)

                                          test <@ relsWithProps.Length > 0 @>

                                          // Test empty property maps
                                          let emptyProps =
                                              records
                                              |> Array.filter (fun r ->
                                                  (isRecordType "node" r
                                                   || isRecordType "relationship" r)
                                                  && match tryGetProperty r "properties" with
                                                     | Some props -> props.EnumerateObject() |> Seq.length = 0
                                                     | None -> false)

                                          test <@ emptyProps.Length > 0 @>

                                      | Error err -> failtest $"Export failed: {err}"
                                  })
                      })
          }

          // Test 8: Unsupported type handling
          testAsync "handles unsupported types gracefully" {
              do!
                  ContainerFixture.withContainerInfo ContainerConfig.defaultConfig (fun containerInfo ->
                      async {
                          do!
                              TestExecution.withSeededData containerInfo.Driver ComplexTypes Small (fun () ->
                                  async {
                                      let uri =
                                          Uri(containerInfo.ConnectionString)

                                      match! Fixtures.runExportWithMetrics uri "neo4j" containerInfo.Password with
                                      | Ok(_metrics, exportFile) ->
                                          let (_metadata, records) =
                                              parseExportFile exportFile

                                          // Find nodes that should have unsupported properties
                                          let complexNodes =
                                              records
                                              |> Array.filter (isNodeWithLabel "ComplexTypes")

                                          if complexNodes.Length > 0 then
                                              let props =
                                                  complexNodes.[0].GetProperty("properties")

                                              // Maps as properties should be omitted
                                              let hasMapProperty =
                                                  props.TryGetProperty("mapProperty") |> fst

                                              test <@ not hasMapProperty @>

                                              // Mixed-type arrays should be omitted
                                              let hasMixedTypeList =
                                                  props.TryGetProperty("mixedTypeList") |> fst

                                              test <@ not hasMixedTypeList @>

                                              // Arrays with nulls should be omitted
                                              let hasListWithNulls =
                                                  props.TryGetProperty("listWithNulls") |> fst

                                              test <@ not hasListWithNulls @>

                                          // Check for error/warning records
                                          let errorRecords =
                                              records
                                              |> Array.filter (fun r ->
                                                  isRecordType "error" r || isRecordType "warning" r)

                                          // Should have some warnings for unsupported types
                                          test <@ errorRecords.Length > 0 @>

                                          // Verify error record structure
                                          if errorRecords.Length > 0 then
                                              let firstError = errorRecords.[0]

                                              let hasfirstError =
                                                  firstError.TryGetProperty("timestamp") |> fst

                                              test <@ hasfirstError @>

                                              let hasfirstError =
                                                  firstError.TryGetProperty("message") |> fst

                                              test <@ hasfirstError @>

                                      | Error err -> failtest $"Export failed: {err}"
                                  })
                      })
          } ]
