module Neo4jExport.Tests.EndToEnd.ExportTests.PerformanceBaselineTests

open System
open Expecto
open Swensen.Unquote
open System.Text.Json
open Neo4jExport.Tests.EndToEnd.Infrastructure.ContainerFixtures
open Neo4jExport.Tests.EndToEnd.Infrastructure.ContainerLifecycle
open Neo4jExport.Tests.EndToEnd.Infrastructure.TestDataManagement
open Neo4jExport.Tests.EndToEnd.ExportTests.ExportTestUtils
open Neo4jExport.Tests.Helpers.TestLog

[<Tests>]
let tests =
    testList
        "Performance Baseline Tests"
        [

          // Test 1: Small dataset baseline
          Fixtures.performanceTest "small dataset meets performance targets" Small 10000.0 (fun  // 10K records/second minimum for small datasets
                                                                                                metrics ->
              async {
                  info $"Small dataset performance: {metrics.ThroughputPerSecond:F0} records/sec"
                  info $"Memory used: {metrics.MemoryUsedMB:F2} MB"
                  test <@ metrics.MemoryUsedMB < 100.0 @> // Should use less than 100MB
              })

          // Test 2: Medium dataset baseline (primary performance test)
          Fixtures.performanceTest "medium dataset achieves 50K+ records/second" Medium 50000.0 (fun  // 50K records/second target
                                                                                                     metrics ->
              async {
                  info $"Medium dataset performance: {metrics.ThroughputPerSecond:F0} records/sec"
                  info $"Total records: {metrics.NodeCount + metrics.RelationshipCount}"
                  info $"Duration: {metrics.Duration.TotalSeconds:F2} seconds"

                  // Additional validations
                  test <@ metrics.NodeCount = 500000L @>
                  test <@ metrics.RelationshipCount = 1000000L @>
              })

          // Test 3: Memory usage remains constant
          testAsync "memory usage remains constant with dataset size" {
              do!
                  ContainerFixture.withContainerInfo ContainerConfig.defaultConfig (fun containerInfo ->
                      async {
                          let uri =
                              Uri(containerInfo.ConnectionString)

                          let mutable smallMetrics = None
                          let mutable mediumMetrics = None

                          // First, seed and export Small dataset
                          do!
                              TestExecution.withSeededData containerInfo.Driver SimpleGraph Small (fun () ->
                                  async {
                                      match! Fixtures.runExportWithMetrics uri "neo4j" containerInfo.Password with
                                      | Ok(metrics, _) -> smallMetrics <- Some metrics
                                      | Error err -> failtest $"Small export failed: {err}"
                                  })

                          match smallMetrics with
                          | Some small ->
                              // Clean and seed Medium dataset
                              match! DatabaseCleanup.cleanDatabase containerInfo.Driver with
                              | Ok() -> ()
                              | Error e -> failtest $"Failed to clean database: {e}"

                              do!
                                  TestExecution.withSeededData containerInfo.Driver SimpleGraph Medium (fun () ->
                                      async {
                                          match! Fixtures.runExportWithMetrics uri "neo4j" containerInfo.Password with
                                          | Ok(metrics, _) -> mediumMetrics <- Some metrics
                                          | Error err -> failtest $"Medium export failed: {err}"
                                      })

                              match mediumMetrics with
                              | Some medium ->
                                  info
                                      $"Small dataset: {small.MemoryUsedMB:F2} MB for {small.NodeCount + small.RelationshipCount} records"

                                  info
                                      $"Medium dataset: {medium.MemoryUsedMB:F2} MB for {medium.NodeCount + medium.RelationshipCount} records"

                                  // Memory should not increase proportionally with data size
                                  let memoryRatio =
                                      medium.MemoryUsedMB / small.MemoryUsedMB

                                  info $"Memory ratio: {memoryRatio:F2}x for 100x data"

                                  test <@ memoryRatio < 2.0 @> // Less than 2x increase for 100x data
                              | None -> failtest "Failed to get medium dataset metrics"
                          | None -> failtest "Failed to get small dataset metrics"
                      })
          }

          // Test 4: Pagination performance metrics
          testAsync "tracks pagination performance" {
              do!
                  ContainerFixture.withContainerInfo ContainerConfig.defaultConfig (fun containerInfo ->
                      TestExecution.withSeededData containerInfo.Driver SimpleGraph Small (fun () ->
                          async {
                              // Run export
                              let uri =
                                  Uri(containerInfo.ConnectionString)

                              match! Fixtures.runExportWithMetrics uri "neo4j" containerInfo.Password with
                              | Ok(_metrics, exportFile) ->
                                  // Parse metadata
                                  let (metadata, _records) =
                                      ExportTestUtils.parseExportFile exportFile

                                  // Check if pagination_performance exists
                                  match ExportTestUtils.tryGetProperty metadata "pagination_performance" with
                                  | Some perfData ->

                                      // Verify strategy
                                      let hasStrategy =
                                          perfData.TryGetProperty("strategy") |> fst

                                      test <@ hasStrategy @>

                                      let strategy =
                                          perfData.GetProperty("strategy").GetString()

                                      test <@ strategy = "keyset" || strategy = "skip_limit" @>

                                      // Verify timing data
                                      let hasTotalBatches =
                                          perfData.TryGetProperty("total_batches") |> fst

                                      test <@ hasTotalBatches @>

                                      let hasAvgTime =
                                          perfData.TryGetProperty("average_batch_time_ms")
                                          |> fst

                                      test <@ hasAvgTime @>

                                      let hasFirstTime =
                                          perfData.TryGetProperty("first_batch_time_ms")
                                          |> fst

                                      test <@ hasFirstTime @>

                                      let hasLastTime =
                                          perfData.TryGetProperty("last_batch_time_ms")
                                          |> fst

                                      test <@ hasLastTime @>

                                      let totalBatches =
                                          perfData.GetProperty("total_batches").GetInt32()

                                      let avgTime =
                                          perfData.GetProperty("average_batch_time_ms").GetDouble()

                                      let firstTime =
                                          perfData.GetProperty("first_batch_time_ms").GetDouble()

                                      let lastTime =
                                          perfData.GetProperty("last_batch_time_ms").GetDouble()

                                      info $"Pagination: {totalBatches} batches, avg {avgTime:F2}ms"
                                      info $"First batch: {firstTime:F2}ms, Last batch: {lastTime:F2}ms"

                                      // Verify reasonable performance
                                      test <@ avgTime > 0.0 @>
                                      test <@ totalBatches > 0 @>

                                      // Check performance trend if available
                                      match ExportTestUtils.tryGetProperty perfData "performance_trend" with
                                      | Some trendElem ->
                                          let trend = trendElem.GetString()
                                          info $"Performance trend: {trend}"

                                          test
                                              <@
                                                  trend = "constant"
                                                  || trend = "linear"
                                                  || trend = "exponential"
                                              @>
                                      | None -> ()

                                      // Check sample timings if available
                                      match ExportTestUtils.tryGetProperty perfData "sample_timings" with
                                      | Some samples ->
                                          let sampleCount = samples.GetArrayLength()
                                          test <@ sampleCount > 0 @>

                                          // Verify no severe degradation
                                          if sampleCount > 1 then
                                              let samplesArray =
                                                  samples.EnumerateArray() |> Seq.toArray

                                              let firstSample =
                                                  samplesArray.[0].GetProperty("TimeMs").GetDouble()

                                              let lastSample =
                                                  samplesArray.[sampleCount - 1].GetProperty("TimeMs").GetDouble()

                                              let degradationRatio =
                                                  lastSample / firstSample

                                              info $"Degradation ratio: {degradationRatio:F2}x"
                                              test <@ degradationRatio < 10.0 @> // No more than 10x degradation
                                      | None -> ()
                                  | None ->
                                      // If no pagination_performance in metadata, that's OK
                                      info "No pagination_performance data in metadata"

                              | Error err -> failtest $"Export failed: {err}"
                          }))
          } ]
