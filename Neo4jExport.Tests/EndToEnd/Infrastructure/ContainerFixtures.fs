module Neo4jExport.Tests.EndToEnd.Infrastructure.ContainerFixtures

open System
open System.IO
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
        : Async<Result<ExportMetrics * string, AppError>> = // Return metrics AND file path
        async {
            // Create temp output directory
            let outputDir =
                Path.Combine(Path.GetTempPath(), $"neo4j-export-test-{Guid.NewGuid()}")

            Directory.CreateDirectory(outputDir) |> ignore

            try
                let baseConfig =
                    Neo4jExport.Tests.Helpers.TestHelpers.createTestConfig ()

                let config =
                    { baseConfig with
                        Uri = connectionUri
                        User = user
                        Password = password
                        OutputDirectory = outputDir }

                // Create application context
                let context =
                    { CancellationTokenSource = new System.Threading.CancellationTokenSource()
                      TempFiles = System.Collections.Concurrent.ConcurrentBag<string>()
                      ActiveProcesses = System.Collections.Concurrent.ConcurrentBag<System.Diagnostics.Process>() }

                let! (memoryUsed, (duration, result)) =
                    MemoryHelpers.measureMemoryUsage (fun () ->
                        PerformanceHelpers.measureDuration (fun () -> Workflow.runExport context config))

                match result with
                | Ok() ->
                    // Find the exported file
                    let exportedFiles =
                        Directory.GetFiles(outputDir, "*.jsonl")

                    if Array.isEmpty exportedFiles then
                        return Error(FileSystemError(outputDir, "No export file found", None))
                    else
                        let exportFile = exportedFiles.[0]

                        // Copy to a temp file that won't be deleted
                        let safeFile =
                            Path.Combine(Path.GetTempPath(), Path.GetFileName(exportFile))

                        File.Copy(exportFile, safeFile, true)

                        // Read metadata from first line of SAFE FILE (not original!)
                        let firstLine =
                            File.ReadLines(safeFile) |> Seq.head

                        use metadataDoc =
                            System.Text.Json.JsonDocument.Parse(firstLine)

                        let metadata = metadataDoc.RootElement

                        // Extract statistics
                        let nodeCount =
                            metadata.GetProperty("database_statistics").GetProperty("nodeCount").GetInt64()

                        let relCount =
                            metadata.GetProperty("database_statistics").GetProperty("relCount").GetInt64()

                        let totalRecords = nodeCount + relCount

                        let metrics =
                            { NodeCount = nodeCount
                              RelationshipCount = relCount
                              Duration = duration
                              MemoryUsedMB = MemoryHelpers.bytesToMB memoryUsed
                              ThroughputPerSecond = PerformanceHelpers.calculateThroughput totalRecords duration }

                        return Ok(metrics, safeFile)
                | Error err -> return Error err
            finally
                // Clean up temp directory
                if Directory.Exists(outputDir) then
                    Directory.Delete(outputDir, true)
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
                                    | Ok(metrics, _exportFile) ->
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
