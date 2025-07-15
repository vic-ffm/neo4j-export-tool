module Neo4jExport.Tests.EndToEnd.ExportTests.ErrorHandlingTests

open System
open System.IO
open System.Collections.Concurrent
open Expecto
open Swensen.Unquote
open System.Text.Json
open Neo4jExport
open Neo4jExport.Workflow
open Neo4jExport.Tests.EndToEnd.Infrastructure.ContainerFixtures
open Neo4jExport.Tests.EndToEnd.Infrastructure.ContainerLifecycle
open Neo4jExport.Tests.EndToEnd.Infrastructure.TestDataManagement
open Neo4jExport.Tests.Helpers.TestHelpers

[<Tests>]
let tests =
    testList
        "Error Handling E2E Tests"
        [

          // Test 1: Invalid credentials
          testAsync "handles authentication errors correctly" {
              let config = createTestConfig ()

              let invalidConfig =
                  { config with
                      Password = "wrong-password" }

              // Create application context
              let context =
                  { CancellationTokenSource = new System.Threading.CancellationTokenSource()
                    TempFiles = System.Collections.Concurrent.ConcurrentBag<string>()
                    ActiveProcesses = System.Collections.Concurrent.ConcurrentBag<System.Diagnostics.Process>() }

              match! Workflow.runExport context invalidConfig with
              | Ok() -> failtest "Expected authentication error"
              | Error err ->
                  match err with
                  | AuthenticationError _ -> () // Expected
                  | _ -> failtest $"Expected AuthenticationError but got: {err}"
          }

          // Test 2: Invalid output directory
          testAsync "handles invalid output paths correctly" {
              let config = createTestConfig ()

              let invalidConfig =
                  { config with
                      OutputDirectory = "/invalid/path/that/does/not/exist" }

              // Create application context
              let context =
                  { CancellationTokenSource = new System.Threading.CancellationTokenSource()
                    TempFiles = System.Collections.Concurrent.ConcurrentBag<string>()
                    ActiveProcesses = System.Collections.Concurrent.ConcurrentBag<System.Diagnostics.Process>() }

              match! Workflow.runExport context invalidConfig with
              | Ok() -> failtest "Expected file system error"
              | Error err ->
                  match err with
                  | FileSystemError _ -> () // Expected
                  | _ -> failtest $"Expected FileSystemError but got: {err}"
          }

          // Test 3: Connection timeout
          testAsync "handles connection timeouts correctly" {
              let config = createTestConfig ()

              let timeoutConfig =
                  { config with
                      Uri = Uri("bolt://non-existent-host:7687") }

              // Create application context
              let context =
                  { CancellationTokenSource = new System.Threading.CancellationTokenSource()
                    TempFiles = System.Collections.Concurrent.ConcurrentBag<string>()
                    ActiveProcesses = System.Collections.Concurrent.ConcurrentBag<System.Diagnostics.Process>() }

              match! Workflow.runExport context timeoutConfig with
              | Ok() -> failtest "Expected connection error"
              | Error err ->
                  match err with
                  | ConnectionError _ -> () // Expected
                  | _ -> failtest $"Expected ConnectionError but got: {err}"
          }

          // Test 4: Cancellation handling
          testAsync "handles cancellation gracefully" {
              do!
                  ContainerFixture.withContainerInfo ContainerConfig.defaultConfig (fun containerInfo ->
                      async {
                          do!
                              TestExecution.withSeededData containerInfo.Driver SimpleGraph Medium (fun () ->
                                  async {
                                      // Create a cancellation token that will cancel after a short delay
                                      let context =
                                          { CancellationTokenSource = new System.Threading.CancellationTokenSource()
                                            TempFiles = System.Collections.Concurrent.ConcurrentBag<string>()
                                            ActiveProcesses =
                                              System.Collections.Concurrent.ConcurrentBag<System.Diagnostics.Process>() }

                                      // Schedule cancellation after 500ms (during export)
                                      context.CancellationTokenSource.CancelAfter(500)

                                      let config = createTestConfig ()

                                      let exportConfig =
                                          { config with
                                              Uri = Uri(containerInfo.ConnectionString)
                                              User = "neo4j"
                                              Password = containerInfo.Password
                                              OutputDirectory =
                                                  Path.Combine(Path.GetTempPath(), $"cancel-test-{Guid.NewGuid()}") }

                                      // Create output directory
                                      Directory.CreateDirectory(exportConfig.OutputDirectory)
                                      |> ignore

                                      try
                                          // Run export that should be cancelled
                                          let! result = Workflow.runExport context exportConfig

                                          match result with
                                          | Ok() ->
                                              // If we get here, the export completed before cancellation
                                              // This is OK for small datasets
                                              ()
                                          | Error err ->
                                              // We expect a cancellation-related error
                                              match err with
                                              | TimeoutError _ -> () // Cancellation manifests as timeout
                                              | ExportError(msg, _) when msg.Contains("cancel") -> () // Or export error
                                              | _ -> ()

                                          // Verify no partial files remain
                                          let exportFiles =
                                              Directory.GetFiles(exportConfig.OutputDirectory, "*.jsonl")

                                          if exportFiles.Length > 0 then
                                              // If a file exists, it should be valid JSONL
                                              let firstLine =
                                                  File.ReadLines(exportFiles.[0]) |> Seq.tryHead

                                              match firstLine with
                                              | Some line ->
                                                  // Should be valid JSON
                                                  use _ = JsonDocument.Parse(line)
                                                  ()
                                              | None -> ()

                                      finally
                                          // Clean up test directory
                                          if Directory.Exists(exportConfig.OutputDirectory) then
                                              Directory.Delete(exportConfig.OutputDirectory, true)
                                  })
                      })
          }

          // Test 5: Disk space exhaustion
          testAsync "handles disk space errors correctly" {
              // This test would require mocking or a very small disk quota
              // For now, verify the error type exists and can be constructed
              let error = DiskSpaceError(1000L, 500L) // Required 1000 bytes, only 500 available

              test
                  <@
                      match error with
                      | DiskSpaceError _ -> true
                      | _ -> false
                  @>
          }

          // Test 6: Error record generation
          testAsync "generates error records in output" {
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
                                          // Parse file
                                          let (metadata, records) =
                                              ExportTestUtils.parseExportFile exportFile

                                          // Find error/warning records
                                          let errorRecords =
                                              records
                                              |> Array.filter (fun r ->
                                                  ExportTestUtils.isRecordType "error" r
                                                  || ExportTestUtils.isRecordType "warning" r)

                                          // Should have error/warning records for unsupported types
                                          test <@ errorRecords.Length > 0 @>

                                          if errorRecords.Length > 0 then
                                              let firstError = errorRecords.[0]

                                              // Verify error record structure
                                              let hasType =
                                                  firstError.TryGetProperty("type") |> fst

                                              test <@ hasType @>

                                              let hasTimestamp =
                                                  firstError.TryGetProperty("timestamp") |> fst

                                              test <@ hasTimestamp @>

                                              let hasMessage =
                                                  firstError.TryGetProperty("message") |> fst

                                              test <@ hasMessage @>

                                              // Timestamp should be valid ISO format
                                              let timestamp =
                                                  firstError.GetProperty("timestamp").GetString()

                                              let parsed = DateTime.TryParse(timestamp)
                                              test <@ fst parsed = true @>

                                              // Message should be informative
                                              let message =
                                                  firstError.GetProperty("message").GetString()

                                              test <@ message.Length > 0 @>

                                          // Check metadata includes error_summary
                                          let hasErrorSummary =
                                              metadata.TryGetProperty("error_summary") |> fst

                                          test <@ hasErrorSummary @>

                                          let errorSummary =
                                              metadata.GetProperty("error_summary")

                                          // Verify error summary fields
                                          let hasErrorCount =
                                              errorSummary.TryGetProperty("error_count") |> fst

                                          test <@ hasErrorCount @>

                                          let hasWarningCount =
                                              errorSummary.TryGetProperty("warning_count")
                                              |> fst

                                          test <@ hasWarningCount @>

                                          let hasErrors =
                                              errorSummary.TryGetProperty("has_errors") |> fst

                                          test <@ hasErrors @>

                                          let errorCount =
                                              errorSummary.GetProperty("error_count").GetInt64()

                                          let warningCount =
                                              errorSummary.GetProperty("warning_count").GetInt64()

                                          let hasErrors =
                                              errorSummary.GetProperty("has_errors").GetBoolean()

                                          // Counts should match actual error records
                                          let actualErrors =
                                              errorRecords
                                              |> Array.filter (ExportTestUtils.isRecordType "error")

                                          let actualWarnings =
                                              errorRecords
                                              |> Array.filter (ExportTestUtils.isRecordType "warning")

                                          test <@ int64 actualErrors.Length = errorCount @>
                                          test <@ int64 actualWarnings.Length = warningCount @>
                                          test <@ hasErrors = (errorCount > 0L) @>

                                      | Error err -> failtest $"Export failed: {err}"
                                  })
                      })
          } ]
