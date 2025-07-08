#r "nuget: System.Text.Json, 9.0.6"

open System
open System.IO
open System.Text.Json

// Read metadata from JSONL files
let readMetadata (filename: string) =
    use file = File.OpenRead(filename)
    use reader = new StreamReader(file)
    let metadataLine = reader.ReadLine()
    JsonDocument.Parse(metadataLine)

// Extract performance metrics
let extractPerformance (doc: JsonDocument) =
    try
        let perf = doc.RootElement.GetProperty("pagination_performance")
        Some {|
            Strategy = perf.GetProperty("strategy").GetString()
            TotalBatches = perf.GetProperty("total_batches").GetInt32()
            AverageBatchTime = perf.GetProperty("average_batch_time_ms").GetDouble()
            FirstBatchTime = perf.GetProperty("first_batch_time_ms").GetDouble()
            LastBatchTime = perf.GetProperty("last_batch_time_ms").GetDouble()
            PerformanceTrend = perf.GetProperty("performance_trend").GetString()
            SampleTimings = 
                perf.GetProperty("sample_timings").EnumerateArray()
                |> Seq.map (fun t -> 
                    {| BatchNumber = t.GetProperty("BatchNumber").GetInt32()
                       TimeMs = t.GetProperty("TimeMs").GetDouble() |})
                |> Seq.toList
        |}
    with _ -> None

// Compare the exports
printfn "=== Neo4j Export Performance Comparison ==="
printfn ""

// Read SKIP/LIMIT export
use skipLimitDoc = readMetadata "skip_limit_export.jsonl"
let skipLimitPerf = extractPerformance skipLimitDoc

// Read Keyset export
use keysetDoc = readMetadata "keyset_export.jsonl"
let keysetPerf = extractPerformance keysetDoc

match skipLimitPerf, keysetPerf with
| Some sl, Some ks ->
    printfn "SKIP/LIMIT Pagination:"
    printfn "----------------------"
    printfn "Strategy: %s" sl.Strategy
    printfn "Total Batches: %d" sl.TotalBatches
    printfn "Average Batch Time: %.2fms" sl.AverageBatchTime
    printfn "First Batch Time: %.2fms" sl.FirstBatchTime
    printfn "Last Batch Time: %.2fms" sl.LastBatchTime
    printfn "Performance Trend: %s" sl.PerformanceTrend
    
    printfn ""
    printfn "Keyset Pagination:"
    printfn "-----------------"
    printfn "Strategy: %s" ks.Strategy
    printfn "Total Batches: %d" ks.TotalBatches
    printfn "Average Batch Time: %.2fms" ks.AverageBatchTime
    printfn "First Batch Time: %.2fms" ks.FirstBatchTime
    printfn "Last Batch Time: %.2fms" ks.LastBatchTime
    printfn "Performance Trend: %s" ks.PerformanceTrend
    
    printfn ""
    printfn "=== PERFORMANCE COMPARISON ==="
    printfn ""
    
    // Performance improvement
    let avgImprovement = sl.AverageBatchTime / ks.AverageBatchTime
    let firstBatchImprovement = sl.FirstBatchTime / ks.FirstBatchTime
    let lastBatchImprovement = sl.LastBatchTime / ks.LastBatchTime
    
    printfn "Average Batch Time:"
    printfn "  SKIP/LIMIT: %.2fms" sl.AverageBatchTime
    printfn "  Keyset: %.2fms" ks.AverageBatchTime
    printfn "  Keyset is %.1fx faster on average" avgImprovement
    
    printfn ""
    printfn "First vs Last Batch Degradation:"
    printfn "  SKIP/LIMIT: %.1fx slower (%.2fms -> %.2fms)" 
        (sl.LastBatchTime / sl.FirstBatchTime) sl.FirstBatchTime sl.LastBatchTime
    printfn "  Keyset: %.1fx slower (%.2fms -> %.2fms)" 
        (ks.LastBatchTime / ks.FirstBatchTime) ks.FirstBatchTime ks.LastBatchTime
    
    printfn ""
    printfn "Performance Characteristics:"
    printfn "  SKIP/LIMIT: %s (typical of O(n²) behavior)" sl.PerformanceTrend
    printfn "  Keyset: %s (typical of O(log n) behavior)" ks.PerformanceTrend
    
    // Show sample timings if available
    if sl.SampleTimings.Length > 0 && ks.SampleTimings.Length > 0 then
        printfn ""
        printfn "Sample Batch Timings:"
        printfn "Batch | SKIP/LIMIT (ms) | Keyset (ms) | Improvement"
        printfn "------|-----------------|-------------|------------"
        
        List.zip sl.SampleTimings ks.SampleTimings
        |> List.iter (fun (sl, ks) ->
            let improvement = sl.TimeMs / ks.TimeMs
            printfn "%5d | %14.2f | %11.2f | %9.1fx" 
                sl.BatchNumber sl.TimeMs ks.TimeMs improvement)
    
    printfn ""
    printfn "Summary:"
    printfn "--------"
    printfn "For this dataset with %d nodes and %d relationships:" 
        (skipLimitDoc.RootElement.GetProperty("database_statistics").GetProperty("node_count").GetInt64())
        (skipLimitDoc.RootElement.GetProperty("database_statistics").GetProperty("relationship_count").GetInt64())
    
    printfn "- Keyset pagination is %.1fx faster on average" avgImprovement
    printfn "- SKIP/LIMIT shows %s performance degradation" sl.PerformanceTrend
    printfn "- Keyset maintains %s performance" ks.PerformanceTrend
    
    if sl.TotalBatches = 1 && ks.TotalBatches = 1 then
        printfn ""
        printfn "Note: With only 1 batch, the dataset is too small to show"
        printfn "the full performance difference. Try with a larger dataset"
        printfn "to see O(n²) vs O(log n) behavior more clearly."
    
| _ ->
    printfn "Error: Could not extract performance metrics from one or both exports"