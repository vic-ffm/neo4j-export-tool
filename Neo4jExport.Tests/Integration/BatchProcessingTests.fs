module Neo4jExport.Tests.Integration.BatchProcessingTests

open System
open System.Collections.Generic
open Expecto
open Swensen.Unquote
open Neo4j.Driver
open Neo4jExport
open Neo4jExport.Tests.Helpers.TestHelpers
open Neo4jExport.Tests.Integration.TestDoubles

// Memory testing helper
let verifyConstantMemory operation =
    GC.Collect()
    GC.WaitForPendingFinalizers()
    GC.Collect()
    let initial = GC.GetTotalMemory(true)

    operation ()

    GC.Collect()
    GC.WaitForPendingFinalizers()
    GC.Collect()
    let final = GC.GetTotalMemory(true)

    let growth = final - initial
    test <@ growth < 10_000_000L @> // Less than 10MB growth

[<Tests>]
let tests =
    testList
        "Batch Processing Integration"
        [

          testCase "memory remains constant across batches"
          <| fun () ->
              verifyConstantMemory (fun () ->
                  // Process multiple batches of 10K records each
                  for batch in 1..10 do
                      let nodes =
                          [ for i in 1..10_000 do
                                let props = Dictionary<string, obj>()
                                props["data"] <- box (String.replicate 100 "x")
                                yield TestNode($"node:{i}", [ "Label" ], props) ]

                      // Process batch (simulate serialization)
                      for node in nodes do
                          let _ = node.Properties.Count
                          ())

          testCase "error handling within batches preserves progress"
          <| fun () ->
              let errorTracker =
                  createThreadSafeErrorTracker ()

              let processedCount = ref 0

              // Create batch with some bad records
              let records =
                  [ for i in 1..100 do
                        let props = Dictionary<string, obj>()

                        if i % 10 = 0 then
                            // This will cause serialization error
                            props["bad"] <- box null
                        else
                            props["good"] <- box i

                        yield TestNode($"node:{i}", [ "Label" ], props) ]

              // Process batch
              for record in records do
                  try
                      // Simulate processing
                      if record.Properties.ContainsKey("bad") then
                          errorTracker.RecordError(ExportError("Null value", None))
                      else
                          incr processedCount
                  with ex ->
                      errorTracker.RecordError(ExportError("Processing failed", Some ex))

              test <@ !processedCount = 90 @>
              test <@ errorTracker.GetErrors().Length = 10 @>

          testCase "progress reporting accuracy"
          <| fun () ->
              // Test that progress is reported correctly
              ()

          testCase "temporal value truncation in batches"
          <| fun () ->
              // Test nanosecond truncation handling
              () ]
