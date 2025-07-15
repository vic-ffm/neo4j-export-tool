module Neo4jExport.Tests.EndToEnd.Infrastructure.ContainerFixtures

open System
open Neo4j.Driver
open Expecto
open Neo4jExport
open Neo4jExport.Workflow
open Neo4jExport.Tests.EndToEnd.Infrastructure.ContainerLifecycle
open Neo4jExport.Tests.EndToEnd.Infrastructure.TestDataManagement

// Export metrics type for performance validation
type ExportMetrics =
    { NodeCount: int64
      RelationshipCount: int64
      Duration: TimeSpan
      MemoryUsedMB: float
      ThroughputPerSecond: float }

// Memory measurement helpers
module MemoryHelpers =

    // Measure memory usage before and after an operation
    let measureMemoryUsage (operation: unit -> Async<'a>) : Async<int64 * 'a> =
        async {
            // Force GC before measurement
            GC.Collect()
            GC.WaitForPendingFinalizers()
            GC.Collect()

            let beforeBytes = GC.GetTotalMemory(false)

            // Run operation
            let! result = operation ()

            // Force GC after operation
            GC.Collect()
            GC.WaitForPendingFinalizers()
            GC.Collect()

            let afterBytes = GC.GetTotalMemory(false)
            let usedBytes = afterBytes - beforeBytes

            return (usedBytes, result)
        }

    // Convert bytes to MB
    let bytesToMB bytes = float bytes / 1024.0 / 1024.0

// Performance measurement helpers
module PerformanceHelpers =

    // Measure operation duration
    let measureDuration (operation: unit -> Async<'a>) : Async<TimeSpan * 'a> =
        async {
            let stopwatch =
                System.Diagnostics.Stopwatch.StartNew()

            let! result = operation ()
            stopwatch.Stop()
            return (stopwatch.Elapsed, result)
        }

    // Calculate records per second
    let calculateThroughput (recordCount: int64) (duration: TimeSpan) =
        float recordCount / duration.TotalSeconds

// Complete test fixtures combining all infrastructure
module Fixtures =

    // Standard container configurations
    let neo4j4Container =
        ContainerConfig.neo4j4Config

    let neo4j5Container =
        ContainerConfig.neo4j5Config

    let defaultContainer =
        ContainerConfig.defaultConfig

    // Run export and measure performance
    let runExportWithMetrics
        (connectionUri: Uri)
        (user: string)
        (password: string)
        : Async<Result<ExportMetrics, AppError>> =
        async {
            // Create test config with container connection details
            let baseConfig =
                Neo4jExport.Tests.Helpers.TestHelpers.createTestConfig ()

            let config =
                { baseConfig with
                    Uri = connectionUri
                    User = user
                    Password = password }

            // Create application context for the export
            let context =
                { CancellationTokenSource = new System.Threading.CancellationTokenSource()
                  TempFiles = System.Collections.Concurrent.ConcurrentBag<string>()
                  ActiveProcesses = System.Collections.Concurrent.ConcurrentBag<System.Diagnostics.Process>() }

            let! (memoryUsed, (duration, result)) =
                MemoryHelpers.measureMemoryUsage (fun () ->
                    PerformanceHelpers.measureDuration (fun () -> Workflow.runExport context config))

            match result with
            | Ok() ->
                // Read export statistics from the output file
                // For Phase 6, we'll use placeholder metrics
                // Phase 7 will implement actual metric collection
                let metrics =
                    { NodeCount = 0L // TODO: Read from export metadata
                      RelationshipCount = 0L // TODO: Read from export metadata
                      Duration = duration
                      MemoryUsedMB = MemoryHelpers.bytesToMB memoryUsed
                      ThroughputPerSecond = 0.0 // TODO: Calculate from actual counts
                    }

                return Ok metrics
            | Error err -> return Error err
        }

    // Test with specific Neo4j version
    let testWithVersion version name testFunc =
        testAsync name { do! ContainerFixture.withContainer version testFunc }

    // Test with both Neo4j 4.4 and 5.x
    let testWithAllVersions name testFunc =
        testList
            name
            [ testWithVersion neo4j4Container $"{name} (Neo4j 4.4)" testFunc
              testWithVersion neo4j5Container $"{name} (Neo4j 5.x)" testFunc ]

    // Helper for default container tests
    let testWithDefaultContainer name testFunc =
        ContainerFixture.testWithContainer name ContainerConfig.defaultConfig testFunc

    // Test with clean database
    let testWithCleanDb name testFunc =
        testWithDefaultContainer name (fun driver -> TestExecution.withCleanDatabase driver (fun () -> testFunc driver))

    // Test with seeded data
    let testWithData name pattern scale testFunc =
        testWithDefaultContainer name (fun driver ->
            TestExecution.withSeededData driver pattern scale (fun () -> testFunc driver))

    // Performance test with metrics validation
    let performanceTest name scale expectedThroughput testFunc =
        testAsync name {
            do!
                ContainerFixture.withContainerInfo ContainerConfig.defaultConfig (fun containerInfo ->
                    async {
                        // Seed the data
                        do!
                            TestExecution.withSeededData containerInfo.Driver SimpleGraph scale (fun () ->
                                async {
                                    // Run export with metrics
                                    let uri =
                                        Uri(containerInfo.ConnectionString)

                                    match! runExportWithMetrics uri "neo4j" containerInfo.Password with
                                    | Ok metrics ->
                                        // Validate throughput
                                        if metrics.ThroughputPerSecond < expectedThroughput then
                                            failtest
                                                $"Throughput {metrics.ThroughputPerSecond:F0} records/sec is below expected {expectedThroughput:F0}"

                                        // Validate memory usage
                                        if metrics.MemoryUsedMB > 150.0 then
                                            failtest $"Memory usage {metrics.MemoryUsedMB:F2}MB exceeds 150MB limit"

                                        do! testFunc metrics

                                    | Error err -> failtest $"Export failed: {err}"
                                })
                    })
        }
