#r "nuget: Neo4j.Driver, 5.28.1"
#r "nuget: System.Text.Json, 9.0.6"

open System
open System.IO
open System.Text.Json
open System.Diagnostics
open Neo4j.Driver

// Test configuration
let neo4jUri = Environment.GetEnvironmentVariable("NEO4J_URI") |> Option.ofObj |> Option.defaultValue "bolt://localhost:7687"
let neo4jUser = Environment.GetEnvironmentVariable("NEO4J_USER") |> Option.ofObj |> Option.defaultValue "neo4j"
let neo4jPassword = Environment.GetEnvironmentVariable("NEO4J_PASSWORD") |> Option.ofObj |> Option.defaultValue "password"

printfn "Connecting to Neo4j at %s..." neo4jUri

// Connect to Neo4j
let driver = GraphDatabase.Driver(neo4jUri, AuthTokens.Basic(neo4jUser, neo4jPassword))

// First, let's check how many nodes we have
let getNodeCount() = 
    async {
        use session = driver.AsyncSession()
        let! result = session.RunAsync("MATCH (n) RETURN count(n) as count") |> Async.AwaitTask
        let! record = result.SingleAsync() |> Async.AwaitTask
        return record.["count"].As<int64>()
    }

// Test SKIP/LIMIT pagination
let testSkipLimitPagination(batchSize: int, maxBatches: int) =
    async {
        printfn "\n=== Testing SKIP/LIMIT Pagination ==="
        use session = driver.AsyncSession()
        let stopwatch = Stopwatch()
        let mutable batchTimes = []
        
        for batch in 0 .. maxBatches - 1 do
            let skip = batch * batchSize
            stopwatch.Restart()
            
            let query = sprintf "MATCH (n) RETURN n, labels(n) as labels ORDER BY id(n) SKIP %d LIMIT %d" skip batchSize
            let! result = session.RunAsync(query) |> Async.AwaitTask
            let! records = result.ToListAsync() |> Async.AwaitTask
            
            let elapsed = stopwatch.Elapsed.TotalMilliseconds
            batchTimes <- (batch + 1, elapsed) :: batchTimes
            
            printfn "Batch %d: %d records in %.2fms (SKIP %d)" (batch + 1) records.Count elapsed skip
            
            if records.Count = 0 then
                return List.rev batchTimes
        
        return List.rev batchTimes
    }

// Test Keyset pagination (simulated - using WHERE clause with ID)
let testKeysetPagination(batchSize: int, maxBatches: int) =
    async {
        printfn "\n=== Testing Keyset Pagination ==="
        use session = driver.AsyncSession()
        let stopwatch = Stopwatch()
        let mutable lastId = -1L
        let mutable batchTimes = []
        
        for batch in 0 .. maxBatches - 1 do
            stopwatch.Restart()
            
            let query = 
                if lastId < 0L then
                    sprintf "MATCH (n) RETURN n, labels(n) as labels, id(n) as nodeId ORDER BY id(n) LIMIT %d" batchSize
                else
                    sprintf "MATCH (n) WHERE id(n) > %d RETURN n, labels(n) as labels, id(n) as nodeId ORDER BY id(n) LIMIT %d" lastId batchSize
            
            let! result = session.RunAsync(query) |> Async.AwaitTask
            let! records = result.ToListAsync() |> Async.AwaitTask
            
            let elapsed = stopwatch.Elapsed.TotalMilliseconds
            batchTimes <- (batch + 1, elapsed) :: batchTimes
            
            if records.Count > 0 then
                lastId <- records.[records.Count - 1].["nodeId"].As<int64>()
                printfn "Batch %d: %d records in %.2fms (after ID %d)" (batch + 1) records.Count elapsed lastId
            else
                printfn "Batch %d: 0 records (no more data)"
                return List.rev batchTimes
        
        return List.rev batchTimes
    }

// Analyze performance trends
let analyzePerformance(timings: (int * float) list, method: string) =
    if timings.Length < 3 then
        printfn "\n%s: Insufficient data for trend analysis" method
    else
        let times = timings |> List.map snd
        let avgTime = times |> List.average
        let firstBatch = List.head times
        let lastBatch = List.last times
        let midBatch = times.[times.Length / 2]
        
        // Calculate ratios to detect trend
        let firstToMidRatio = midBatch / firstBatch
        let midToLastRatio = lastBatch / midBatch
        
        let trend = 
            if abs(firstToMidRatio - 1.0) < 0.2 && abs(midToLastRatio - 1.0) < 0.2 then
                "CONSTANT (O(log n))"
            elif firstToMidRatio > 1.3 && midToLastRatio > 1.3 then
                "EXPONENTIAL (O(n²))"
            else
                "LINEAR (O(n))"
        
        printfn "\n%s Performance Analysis:" method
        printfn "- Average batch time: %.2fms" avgTime
        printfn "- First batch: %.2fms" firstBatch
        printfn "- Last batch: %.2fms" lastBatch
        printfn "- Performance trend: %s" trend
        printfn "- Degradation: %.1fx slower (last vs first)" (lastBatch / firstBatch)

// Main test execution
let runPerformanceTest() =
    async {
        try
            // Get total node count
            let! nodeCount = getNodeCount()
            printfn "Total nodes in database: %d" nodeCount
            
            let batchSize = 1000
            let maxBatches = min 20 (int (nodeCount / int64 batchSize + 1L))
            
            printfn "Testing with batch size: %d, max batches: %d" batchSize maxBatches
            
            // Test both methods
            let! skipLimitTimes = testSkipLimitPagination(batchSize, maxBatches)
            let! keysetTimes = testKeysetPagination(batchSize, maxBatches)
            
            // Analyze results
            analyzePerformance(skipLimitTimes, "SKIP/LIMIT")
            analyzePerformance(keysetTimes, "KEYSET")
            
            // Direct comparison
            printfn "\n=== PERFORMANCE COMPARISON ==="
            let skipAvg = skipLimitTimes |> List.map snd |> List.average
            let keysetAvg = keysetTimes |> List.map snd |> List.average
            let improvement = skipAvg / keysetAvg
            
            printfn "Average batch times:"
            printfn "- SKIP/LIMIT: %.2fms" skipAvg
            printfn "- Keyset: %.2fms" keysetAvg
            printfn "- Keyset is %.1fx faster on average" improvement
            
            // Show batch-by-batch comparison
            printfn "\nBatch-by-batch comparison:"
            List.zip skipLimitTimes keysetTimes
            |> List.iter (fun ((b1, t1), (b2, t2)) ->
                printfn "Batch %d: SKIP/LIMIT=%.2fms, Keyset=%.2fms (%.1fx faster)" b1 t1 t2 (t1/t2))
            
        finally
            driver.Dispose()
    }

// Run the test
runPerformanceTest() |> Async.RunSynchronously