module Neo4jExport.Tests.EndToEnd.ExportTests.BasicExportTests

open System
open System.IO
open System.Collections.Concurrent
open Expecto
open Swensen.Unquote
open Neo4j.Driver
open System.Text.Json
open Neo4jExport
open Neo4jExport.Tests.EndToEnd.Infrastructure.ContainerFixtures
open Neo4jExport.Tests.EndToEnd.Infrastructure.ContainerLifecycle
open Neo4jExport.Tests.EndToEnd.Infrastructure.TestDataManagement
open Neo4jExport.Tests.Helpers.TestLog

[<Tests>]
let tests =
    testList
        "Basic Export E2E Tests"
        [

          // Test 1: Export empty database
          testAsync "exports empty database successfully" {
              do!
                  ContainerFixture.withContainerInfo ContainerConfig.defaultConfig (fun containerInfo ->
                      async {
                          // Clean database first
                          match! DatabaseCleanup.cleanDatabase containerInfo.Driver with
                          | Ok() -> ()
                          | Error e -> failtest $"Failed to clean database: {e}"

                          // Run export with container connection details
                          let uri =
                              Uri(containerInfo.ConnectionString)

                          match! Fixtures.runExportWithMetrics uri "neo4j" containerInfo.Password with
                          | Ok(metrics, exportFile) ->
                              // Verify empty database metrics
                              test <@ metrics.NodeCount = 0L @>
                              test <@ metrics.RelationshipCount = 0L @>

                              // Verify metadata structure
                              let firstLine =
                                  File.ReadLines(exportFile) |> Seq.head

                              use metadataDoc =
                                  JsonDocument.Parse(firstLine)

                              let metadata = metadataDoc.RootElement

                              // Check required metadata fields
                              let hasFormatVersion =
                                  metadata.TryGetProperty("format_version") |> fst

                              test <@ hasFormatVersion @>

                              let hasExportMetadata =
                                  metadata.TryGetProperty("export_metadata") |> fst

                              test <@ hasExportMetadata @>

                              let hasProducer =
                                  metadata.TryGetProperty("producer") |> fst

                              test <@ hasProducer @>

                              let hasSourceSystem =
                                  metadata.TryGetProperty("source_system") |> fst

                              test <@ hasSourceSystem @>

                              let hasDbStats =
                                  metadata.TryGetProperty("database_statistics")
                                  |> fst

                              test <@ hasDbStats @>

                              let hasManifest =
                                  metadata.TryGetProperty("export_manifest") |> fst

                              test <@ hasManifest @>

                              // Verify only one line (metadata only)
                              let lineCount =
                                  File.ReadLines(exportFile) |> Seq.length

                              test <@ lineCount = 1 @>

                          | Error err -> failtest $"Export failed: {err}"
                      })
          }

          // Test 2: Export small dataset
          testAsync "exports small dataset correctly" {
              do!
                  ContainerFixture.withContainerInfo ContainerConfig.defaultConfig (fun containerInfo ->
                      async {
                          // Seed test data and run export
                          do!
                              TestExecution.withSeededData containerInfo.Driver SimpleGraph Small (fun () ->
                                  async {
                                      // Run export
                                      let uri =
                                          Uri(containerInfo.ConnectionString)

                                      match! Fixtures.runExportWithMetrics uri "neo4j" containerInfo.Password with
                                      | Ok(metrics, exportFile) ->
                                          // Verify correct counts
                                          test <@ metrics.NodeCount = 5000L @>
                                          test <@ metrics.RelationshipCount = 10000L @>

                                          // Parse and validate records
                                          let lines = File.ReadAllLines(exportFile)

                                          let records =
                                              lines
                                              |> Array.skip 1
                                              |> Array.map (fun line ->
                                                  use doc = JsonDocument.Parse(line)
                                                  doc.RootElement.Clone())

                                          // Verify all records have _tool_id
                                          for record in records do
                                              let toolId =
                                                  record.GetProperty("_tool_id").GetString()

                                              test <@ toolId.Length = 64 @>

                                              test
                                                  <@
                                                      System.Text.RegularExpressions.Regex.IsMatch(
                                                          toolId,
                                                          "^[a-f0-9]{64}$"
                                                      )
                                                  @>
                                      | Error err -> failtest $"Export failed: {err}"
                                  })
                      })
          }

          // Test 3: Verify metadata completeness
          testAsync "generates complete metadata" {
              do!
                  ContainerFixture.withContainerInfo ContainerConfig.defaultConfig (fun containerInfo ->
                      async {
                          // Seed test data
                          do!
                              TestExecution.withSeededData containerInfo.Driver ComplexTypes Small (fun () ->
                                  async {
                                      // Run export
                                      let uri =
                                          Uri(containerInfo.ConnectionString)

                                      match! Fixtures.runExportWithMetrics uri "neo4j" containerInfo.Password with
                                      | Ok(_metrics, exportFile) ->
                                          // Parse metadata
                                          let firstLine =
                                              File.ReadLines(exportFile) |> Seq.head

                                          use metadataDoc =
                                              JsonDocument.Parse(firstLine)

                                          let metadata = metadataDoc.RootElement

                                          // Verify format_version
                                          let formatVersion =
                                              metadata.GetProperty("format_version").GetString()

                                          test <@ formatVersion = "1.0.0" @>

                                          // Verify export_metadata
                                          let exportMeta =
                                              metadata.GetProperty("export_metadata")

                                          let hasExportId =
                                              exportMeta.TryGetProperty("export_id") |> fst

                                          test <@ hasExportId @>

                                          let hasTimestamp =
                                              exportMeta.TryGetProperty("export_timestamp_utc")
                                              |> fst

                                          test <@ hasTimestamp @>

                                          let exportMode =
                                              exportMeta.GetProperty("export_mode").GetString()

                                          test <@ exportMode = "native_driver_streaming" @>

                                          // Verify producer
                                          let producer =
                                              metadata.GetProperty("producer")

                                          let producerName =
                                              producer.GetProperty("name").GetString()

                                          test <@ producerName.Contains("neo4j-export") @>

                                          let hasVersion =
                                              producer.TryGetProperty("version") |> fst

                                          test <@ hasVersion @>

                                          let hasChecksum =
                                              producer.TryGetProperty("checksum") |> fst

                                          test <@ hasChecksum @>

                                          // Verify source_system
                                          let sourceSystem =
                                              metadata.GetProperty("source_system")

                                          let sourceType =
                                              sourceSystem.GetProperty("type").GetString()

                                          test <@ sourceType = "neo4j" @>

                                          let hasSysVersion =
                                              sourceSystem.TryGetProperty("version") |> fst

                                          test <@ hasSysVersion @>

                                          let hasDbName =
                                              sourceSystem.GetProperty("database").TryGetProperty("name")
                                              |> fst

                                          test <@ hasDbName @>

                                          // Verify database_statistics
                                          let stats =
                                              metadata.GetProperty("database_statistics")

                                          let hasNodeCount =
                                              stats.TryGetProperty("nodeCount") |> fst

                                          test <@ hasNodeCount @>

                                          let hasRelCount =
                                              stats.TryGetProperty("relCount") |> fst

                                          test <@ hasRelCount @>

                                          let hasLabelCount =
                                              stats.TryGetProperty("labelCount") |> fst

                                          test <@ hasLabelCount @>

                                          let hasRelTypeCount =
                                              stats.TryGetProperty("relTypeCount") |> fst

                                          test <@ hasRelTypeCount @>

                                          // Verify database_schema exists
                                          let hasDbSchema =
                                              metadata.TryGetProperty("database_schema") |> fst

                                          test <@ hasDbSchema @>

                                          // Verify export_manifest
                                          let manifest =
                                              metadata.GetProperty("export_manifest")

                                          let hasDuration =
                                              manifest.TryGetProperty("total_export_duration_seconds")
                                              |> fst

                                          test <@ hasDuration @>

                                          let hasFileStats =
                                              manifest.TryGetProperty("file_statistics") |> fst

                                          test <@ hasFileStats @>

                                      | Error err -> failtest $"Export failed: {err}"
                                  })
                      })
          }

          // Test 4: Verify record structure
          testAsync "exports records with correct structure" {
              do!
                  ContainerFixture.withContainerInfo ContainerConfig.defaultConfig (fun containerInfo ->
                      async {
                          // Seed test data
                          do!
                              TestExecution.withSeededData containerInfo.Driver SimpleGraph Small (fun () ->
                                  async {
                                      // Run export
                                      let uri =
                                          Uri(containerInfo.ConnectionString)

                                      match! Fixtures.runExportWithMetrics uri "neo4j" containerInfo.Password with
                                      | Ok(_metrics, exportFile) ->
                                          // Parse records
                                          let (_metadata, records) =
                                              ExportTestUtils.parseExportFile exportFile

                                          // Separate nodes and relationships
                                          let recordCounts =
                                              ExportTestUtils.countRecordsByType records

                                          test <@ recordCounts.ContainsKey("node") @>
                                          test <@ recordCounts.ContainsKey("relationship") @>

                                          // Validate at least one node
                                          let nodes =
                                              records
                                              |> Array.filter (ExportTestUtils.isRecordType "node")

                                          test <@ nodes.Length > 0 @>
                                          let firstNode = nodes.[0]
                                          ExportTestUtils.validateNodeRecord firstNode

                                          // Check node has properties
                                          let hasNodeProps =
                                              firstNode.TryGetProperty("properties") |> fst

                                          test <@ hasNodeProps @>

                                          let nodePropsKind =
                                              firstNode.GetProperty("properties").ValueKind

                                          test <@ nodePropsKind = JsonValueKind.Object @>

                                          // Validate at least one relationship
                                          let relationships =
                                              records
                                              |> Array.filter (ExportTestUtils.isRecordType "relationship")

                                          test <@ relationships.Length > 0 @>
                                          let firstRel = relationships.[0]
                                          ExportTestUtils.validateRelationshipRecord firstRel

                                          // Check relationship has properties
                                          let hasRelProps =
                                              firstRel.TryGetProperty("properties") |> fst

                                          test <@ hasRelProps @>

                                          let relPropsKind =
                                              firstRel.GetProperty("properties").ValueKind

                                          test <@ relPropsKind = JsonValueKind.Object @>

                                      | Error err -> failtest $"Export failed: {err}"
                                  })
                      })
          }

          // Test 5: Verify export idempotency
          testAsync "produces identical exports for same data" {
              do!
                  ContainerFixture.withContainerInfo ContainerConfig.defaultConfig (fun containerInfo ->
                      async {
                          // Seed test data once
                          do!
                              TestExecution.withSeededData containerInfo.Driver SimpleGraph Small (fun () ->
                                  async {
                                      let uri =
                                          Uri(containerInfo.ConnectionString)

                                      // First export
                                      let! firstResult =
                                          Fixtures.runExportWithMetrics uri "neo4j" containerInfo.Password

                                      match firstResult with
                                      | Error err -> return failtest $"First export failed: {err}"
                                      | Ok(_metrics1, exportFile1) ->

                                          // Second export
                                          let! secondResult =
                                              Fixtures.runExportWithMetrics uri "neo4j" containerInfo.Password

                                          match secondResult with
                                          | Error err -> return failtest $"Second export failed: {err}"
                                          | Ok(_metrics2, exportFile2) ->

                                              // Parse both files
                                              let (_metadata1, records1) =
                                                  ExportTestUtils.parseExportFile exportFile1

                                              let (_metadata2, records2) =
                                                  ExportTestUtils.parseExportFile exportFile2

                                              // Same number of records
                                              test <@ records1.Length = records2.Length @>

                                              // Extract and compare tool IDs
                                              let toolIds1 =
                                                  records1
                                                  |> Array.map (fun r -> r.GetProperty("_tool_id").GetString())
                                                  |> Array.sort

                                              let toolIds2 =
                                                  records2
                                                  |> Array.map (fun r -> r.GetProperty("_tool_id").GetString())
                                                  |> Array.sort

                                              // Same tool IDs (order-independent)
                                              test <@ toolIds1 = toolIds2 @>

                                              // Compare records by tool ID
                                              let recordMap1 =
                                                  records1
                                                  |> Array.map (fun r -> r.GetProperty("_tool_id").GetString(), r)
                                                  |> Map.ofArray

                                              let recordMap2 =
                                                  records2
                                                  |> Array.map (fun r -> r.GetProperty("_tool_id").GetString(), r)
                                                  |> Map.ofArray

                                              // Verify each record matches (except timestamps in error/warning records)
                                              for toolId in toolIds1 do
                                                  let rec1 = recordMap1.[toolId]
                                                  let rec2 = recordMap2.[toolId]

                                                  // Compare type
                                                  let type1 =
                                                      rec1.GetProperty("type").GetString()

                                                  let type2 =
                                                      rec2.GetProperty("type").GetString()

                                                  test <@ type1 = type2 @>

                                                  // For nodes and relationships, all fields should match
                                                  let recordType =
                                                      rec1.GetProperty("type").GetString()

                                                  if recordType <> "error" && recordType <> "warning" then
                                                      let elemId1 =
                                                          rec1.GetProperty("_element_id").GetString()

                                                      let elemId2 =
                                                          rec2.GetProperty("_element_id").GetString()

                                                      test <@ elemId1 = elemId2 @>
                                  })
                      })
          }

          // Test 6: Verify file naming convention
          testAsync "generates correct filename" {
              do!
                  ContainerFixture.withContainerInfo ContainerConfig.defaultConfig (fun containerInfo ->
                      async {
                          // Clean database to get predictable counts
                          match! DatabaseCleanup.cleanDatabase containerInfo.Driver with
                          | Ok() -> ()
                          | Error e -> failtest $"Failed to clean database: {e}"

                          // Run export
                          let uri =
                              Uri(containerInfo.ConnectionString)

                          match! Fixtures.runExportWithMetrics uri "neo4j" containerInfo.Password with
                          | Ok(_metrics, exportFile) ->
                              // Extract just the filename
                              let filename = Path.GetFileName(exportFile)

                              // Pattern: [dbname]_[timestamp]_[nodes]n_[rels]r_[id].jsonl
                              let pattern =
                                  @"^(.+)_(\d{8}T\d{6}Z)_(\d+)n_(\d+)r_([a-f0-9]{8})\.jsonl$"

                              let regex =
                                  System.Text.RegularExpressions.Regex(pattern)

                              let matchResult = regex.Match(filename)

                              test <@ matchResult.Success @>

                              if matchResult.Success then
                                  let dbName = matchResult.Groups.[1].Value
                                  let timestamp = matchResult.Groups.[2].Value
                                  let nodeCount = matchResult.Groups.[3].Value
                                  let relCount = matchResult.Groups.[4].Value
                                  let shortId = matchResult.Groups.[5].Value

                                  // Validate components
                                  test <@ dbName.Length > 0 @>
                                  test <@ timestamp.Length = 16 @> // yyyyMMddTHHmmssZ
                                  test <@ nodeCount = "0" @> // Empty database
                                  test <@ relCount = "0" @> // Empty database
                                  test <@ shortId.Length = 8 @> // Short export ID

                          | Error err -> failtest $"Export failed: {err}"
                      })
          } ]
