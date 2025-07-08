// MIT License
//
// Copyright (c) 2025-present State Government of Victoria
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

module Neo4jExport.ExportTypes

open System
open System.Collections.Generic
open System.Buffers
open Neo4j.Driver
open Neo4jExport
open ErrorTracking

[<Struct>]
type EntityIds =
    { ElementId: string
      StartElementId: string
      EndElementId: string }

[<Struct>]
type EntityIdsWithStable =
    { ElementId: string
      StableId: string  // This will be the identity hash
      StartElementId: string
      StartStableId: string  // This will be the start node content hash
      EndElementId: string
      EndStableId: string }  // This will be the end node content hash

[<Struct>]
type SerializationState =
    { Depth: SerializationDepth
      BytesWritten: int64
      RecordCount: int64 }

type WriterContext =
    { Config: ExportConfig
      ErrorFuncs: ErrorTracking.ErrorTrackingFunctions
      ExportId: Guid }


// Thread-safe mapping for stable IDs
type NodeIdMapping = System.Collections.Concurrent.ConcurrentDictionary<string, string>

/// Record handler that can maintain state
type RecordHandler<'state> = 'state -> IRecord -> int64 -> 'state

/// Combined state for node export that tracks both lines and labels
type NodeExportState =
    { LineState: LineTrackingState
      LabelTracker: LabelStatsTracker.Tracker }

/// State for relationship export that only tracks lines
type RelationshipExportState = LineTrackingState

let (|TemporalType|_|) (value: obj) =
    match value with
    | :? LocalDate as ld -> Some(ld.ToString())
    | :? LocalTime as lt -> Some(lt.ToString())
    | :? LocalDateTime as ldt -> Some(ldt.ToString())
    | :? ZonedDateTime as zdt -> Some(zdt.ToString())
    | :? Duration as dur -> Some(dur.ToString())
    | :? OffsetTime as ot -> Some(ot.ToString())
    | _ -> None

let (|NumericType|_|) (value: obj) =
    match value with
    | :? int64 -> Some value
    | :? int32 -> Some value
    | :? int16 -> Some value
    | :? uint64 -> Some value
    | :? uint32 -> Some value
    | :? uint16 -> Some value
    | :? byte -> Some value
    | :? sbyte -> Some value
    | :? decimal -> Some value
    | :? double as d when not (Double.IsNaN d || Double.IsInfinity d) -> Some value
    | :? float32 as f when not (Single.IsNaN f || Single.IsInfinity f) -> Some value
    | _ -> None

let (|GraphElement|_|) (value: obj) =
    match value with
    | :? INode as n -> Some(GraphElement.Node n)
    | :? IRelationship as r -> Some(GraphElement.Relationship r)
    | :? IPath as p -> Some(GraphElement.Path p)
    | _ -> None

// Keyset pagination ID types - cannot use struct for unions with different field types
type KeysetId =
    | NumericId of int64 // For Neo4j 4.x using id() function
    | ElementId of string // For Neo4j 5.x+ using elementId() function

// Helper functions for KeysetId
module KeysetId =
    let defaultForVersion =
        function
        | V4x -> NumericId -1L
        | V5x
        | V6x
        | Unknown -> ElementId ""

    let compare id1 id2 =
        match id1, id2 with
        | NumericId n1, NumericId n2 -> compare n1 n2
        | ElementId e1, ElementId e2 -> compare e1 e2
        | NumericId _, ElementId _ -> failwith "Cannot compare different ID types"
        | ElementId _, NumericId _ -> failwith "Cannot compare different ID types"

    // For use in query parameter building
    let toParameter =
        function
        | NumericId n -> box n
        | ElementId s -> box s

/// Pagination strategy for batch processing
type PaginationStrategy =
    | SkipLimit of skip: int
    | Keyset of lastId: KeysetId option * version: Neo4jVersion

/// Mutable batch performance tracker for hot path efficiency
/// Note: Uses types from Neo4jExport namespace (Core/Types.fs)
[<Sealed>]
type BatchPerformanceTracker() =
    let mutable batchCount = 0
    let mutable totalTimeMs = 0.0
    let mutable firstBatchTimeMs = 0.0
    let mutable lastBatchTimeMs = 0.0
    let samples = ResizeArray<BatchTimingSample>(100) // Pre-size for efficiency
    
    member _.RecordBatch(durationMs: float) =
        batchCount <- batchCount + 1
        totalTimeMs <- totalTimeMs + durationMs
        lastBatchTimeMs <- durationMs
        
        if batchCount = 1 then
            firstBatchTimeMs <- durationMs
            
        // Sample every 10 batches for trend analysis
        if batchCount % 10 = 0 then
            samples.Add({ BatchNumber = batchCount; TimeMs = durationMs })
    
    member _.GetMetrics(strategy: PaginationStrategy) : PaginationPerformance =
        let strategyName = 
            match strategy with
            | Keyset _ -> "keyset"
            | SkipLimit _ -> "skip_limit"
            
        let avgTime = if batchCount > 0 then totalTimeMs / float batchCount else 0.0
        
        // Analyze performance trend
        let trend = 
            if samples.Count < 3 then "insufficient_data"
            elif samples.Count >= 3 then
                // Simple trend detection: compare first, middle, and last samples
                let first = samples.[0].TimeMs
                let middle = samples.[samples.Count / 2].TimeMs
                let last = samples.[samples.Count - 1].TimeMs
                
                let firstToMiddleRatio = middle / first
                let middleToLastRatio = last / middle
                
                if abs(firstToMiddleRatio - 1.0) < 0.2 && abs(middleToLastRatio - 1.0) < 0.2 then
                    "constant"  // O(log n) - keyset
                elif firstToMiddleRatio > 1.3 && middleToLastRatio > 1.3 then
                    "exponential"  // O(n²) - skip/limit
                else
                    "linear"
            else "unknown"
        
        {
            Strategy = strategyName
            TotalBatches = batchCount
            AverageBatchTimeMs = avgTime
            FirstBatchTimeMs = firstBatchTimeMs
            LastBatchTimeMs = lastBatchTimeMs
            PerformanceTrend = trend
            SampleTimings = samples |> Seq.toList
        }

/// Query builder function type
type QueryBuilder =
    Neo4jVersion -> PaginationStrategy -> int -> string * System.Collections.Generic.IDictionary<string, obj>

// Export state that flows through the pipeline
type ExportState =
    { Version: Neo4jVersion
      NodeIdMapping: NodeIdMapping 
      // Add performance tracking
      NodePerfTracker: BatchPerformanceTracker
      RelPerfTracker: BatchPerformanceTracker }

    static member Create(version: Neo4jVersion) =
        { Version = version
          NodeIdMapping = NodeIdMapping()
          NodePerfTracker = BatchPerformanceTracker()
          RelPerfTracker = BatchPerformanceTracker() }

/// Enhanced record handler that includes ExportState
type RecordHandlerWithExport<'state> = ExportState -> 'state -> IRecord -> int64 -> 'state

/// Enhanced batch processor supporting both static and dynamic queries
type BatchProcessor =
    { Query: string option // Static query for Unknown version fallback
      QueryBuilder: QueryBuilder option // Dynamic query builder
      GetTotalQuery: string option
      EntityName: string
      Version: Neo4jVersion } // Add version field for keyset pagination

    /// Create legacy processor with static query
    static member CreateLegacy(query: string, getTotalQuery: string option, entityName: string, version: Neo4jVersion) =
        { Query = Some query
          QueryBuilder = None
          GetTotalQuery = getTotalQuery
          EntityName = entityName
          Version = version }

    /// Create dynamic processor with query builder
    static member CreateDynamic
        (queryBuilder: QueryBuilder, getTotalQuery: string option, entityName: string, version: Neo4jVersion)
        =
        { Query = None
          QueryBuilder = Some queryBuilder
          GetTotalQuery = getTotalQuery
          EntityName = entityName
          Version = version }

/// Version-aware batch processor configuration
type VersionAwareBatchProcessor =
    { QueryBuilder: Neo4jVersion -> KeysetId option -> string
      ParameterBuilder: KeysetId option -> int -> int option -> IDictionary<string, obj>
      GetTotalQuery: string option
      EntityName: string
      Version: Neo4jVersion }

/// Context for individual record processing
type RecordContext<'TAccumulator> =
    { Buffer: ArrayBufferWriter<byte>
      ErrorAccumulator: 'TAccumulator
      Stats: ExportProgress }

/// Groups parameters for batch processing operations
type BatchContext<'TAccumulator> =
    { Processor: BatchProcessor
      Session: SafeSession
      FileStream: System.IO.FileStream
      Buffer: ArrayBufferWriter<byte>
      NewlineBytes: byte[]
      ErrorAccumulator: 'TAccumulator }
