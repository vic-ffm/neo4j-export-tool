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

/// FsCheck generators for property-based testing of domain types
module Neo4jExport.Tests.Helpers.Generators

open System
open System.Collections.Generic
open FsCheck
open Neo4jExport
open Neo4jExport.ExportTypes

/// Generator for valid Neo4j labels (non-empty strings without special chars)
let labelGen: Gen<string> =
    gen {
        let! chars =
            Gen.arrayOfLength
                10
                (Gen.elements [ 'A' .. 'Z' ]
                 |> Gen.map Char.ToLower)

        let label = String(chars)
        return if String.IsNullOrWhiteSpace(label) then "Label" else label
    }

/// Generator for sets of Neo4j labels
let labelSetGen: Gen<Set<string>> =
    gen {
        let! count = Gen.choose (0, 5)
        let! labels = Gen.listOfLength count labelGen
        return Set.ofList labels
    }

/// Generator for valid Neo4j property keys
let propertyKeyGen: Gen<string> =
    gen {
        let! prefix = Gen.elements [ "prop"; "attr"; "field"; "value" ]
        let! suffix = Gen.choose (1, 100)
        return $"{prefix}{suffix}"
    }

/// Generator for Neo4j primitive values
let primitiveValueGen: Gen<obj> =
    Gen.oneof
        [ Gen.constant (box null)
          Gen.map box Arb.generate<bool>
          Gen.map box Arb.generate<int64>
          Gen.map box (Gen.filter Double.IsFinite Arb.generate<float>) // Only finite values
          Gen.map box (Gen.filter (fun s -> s <> null) Arb.generate<string>) ]

/// Generator for Neo4j property dictionaries
let propertyDictGen: Gen<IDictionary<string, obj>> =
    gen {
        let! count = Gen.choose (0, 10)
        let! keys = Gen.listOfLength count propertyKeyGen
        let! values = Gen.listOfLength count primitiveValueGen
        let dict = Dictionary<string, obj>()

        List.zip keys values
        |> List.iter (fun (k, v) -> dict.[k] <- v)

        return dict :> IDictionary<string, obj>
    }

/// Generator for ExportConfig
let exportConfigGen: Gen<ExportConfig> =
    gen {
        let! batchSize = Gen.choose (100, 50_000)
        let! maxMemory = Gen.choose (128, 4096) |> Gen.map int64
        let! minDisk = Gen.choose (1, 100) |> Gen.map int64
        let! enableHash = Arb.generate<bool>

        return
            { Uri = Uri("bolt://localhost:7687")
              User = "neo4j"
              Password = "testpass"
              OutputDirectory = "/tmp/test"
              MinDiskGb = minDisk
              MaxMemoryMb = maxMemory
              SkipSchemaCollection = false
              MaxRetries = 3
              RetryDelayMs = 1000
              MaxRetryDelayMs = 30000
              QueryTimeoutSeconds = 300
              EnableDebugLogging = false
              ValidateJsonOutput = false
              AllowInsecure = false
              BatchSize = batchSize
              JsonBufferSizeKb = 64
              MaxPathLength = 100_000L
              PathFullModeLimit = 10L
              PathCompactModeLimit = 100L
              PathPropertyDepth = 2
              MaxNestedDepth = 10
              NestedShallowModeDepth = 2
              NestedReferenceModeDepth = 4
              MaxCollectionItems = 1000
              MaxLabelsPerNode = 10
              MaxLabelsInReferenceMode = 3
              MaxLabelsInPathCompact = 5
              EnableHashedIds = enableHash }
    }

/// Coded error message types for safe testing
type TestErrorMessage =
    | StandardError
    | ValidationError
    | ProcessingError
    | ResourceError
    | SystemError
    | DataError
    | OperationError
    | ServiceError
    | ConfigurationError
    | RuntimeError

/// Convert coded error type to string
let testMessageToString =
    function
    | StandardError -> "Standard error occurred"
    | ValidationError -> "Validation failed"
    | ProcessingError -> "Processing error"
    | ResourceError -> "Resource unavailable"
    | SystemError -> "System error"
    | DataError -> "Data error encountered"
    | OperationError -> "Operation failed"
    | ServiceError -> "Service error"
    | ConfigurationError -> "Configuration issue"
    | RuntimeError -> "Runtime error"

/// Generator for coded error messages
let testErrorMessageGen: Gen<TestErrorMessage> =
    Gen.elements
        [ StandardError
          ValidationError
          ProcessingError
          ResourceError
          SystemError
          DataError
          OperationError
          ServiceError
          ConfigurationError
          RuntimeError ]

/// Generator for safe error messages that won't trigger content filters
let safeStringGen: Gen<string> =
    Gen.map testMessageToString testErrorMessageGen

/// Generator for test paths
let testPathGen: Gen<string> =
    Gen.elements
        [ "/tmp/test"
          "/var/tmp/export"
          "/tmp/data"
          "/home/test/export"
          "/opt/app/data" ]

/// Generator for entity types
let entityTypeGen: Gen<string> =
    Gen.elements
        [ "Node"
          "Relationship"
          "Property"
          "Label"
          "Type" ]

/// Generator for query patterns
let queryPatternGen: Gen<string> =
    Gen.elements
        [ "MATCH (n)"
          "MATCH (n:Label)"
          "MATCH ()-[r]->()"
          "RETURN n"
          "WITH n" ]

/// Generator for AppError variants (now includes all types with safe messages)
let appErrorGen: Gen<AppError> =
    Gen.oneof
        [ Gen.map ConfigError safeStringGen
          Gen.map2
              (fun msg exn -> ConnectionError(msg, Some exn))
              safeStringGen
              (Gen.constant (Exception("Test exception")))
          Gen.map AuthenticationError safeStringGen // Now included with safe message
          Gen.map3
              (fun query msg exn -> QueryError(query, msg, Some exn))
              queryPatternGen
              safeStringGen
              (Gen.constant (Exception("Query test")))
          Gen.map3
              (fun line msg sample -> DataCorruptionError(line, msg, Some sample))
              (Gen.choose (1, 1000))
              safeStringGen
              safeStringGen
          Gen.map2
              (fun req avail -> DiskSpaceError(req, avail))
              (Gen.choose (1000, 10000) |> Gen.map int64)
              (Gen.choose (100, 999) |> Gen.map int64)
          Gen.map MemoryError safeStringGen
          Gen.map2 (fun msg exn -> ExportError(msg, Some exn)) safeStringGen (Gen.constant (Exception("Export test")))
          Gen.map3
              (fun path msg exn -> FileSystemError(path, msg, Some exn))
              testPathGen
              safeStringGen
              (Gen.constant (Exception("File test")))
          Gen.map SecurityError safeStringGen // Now included with safe message
          Gen.map2 (fun op dur -> TimeoutError(op, TimeSpan.FromSeconds(float dur))) safeStringGen (Gen.choose (1, 300))
          Gen.map2 (fun entityType msg -> PaginationError(entityType, msg)) entityTypeGen safeStringGen ]

/// Generator for NonEmptyList of AppError (used for AggregateError)
/// Note: We create a separate generator first to avoid infinite recursion
let rec appErrorGenWithAggregate (depth: int) : Gen<AppError> =
    if depth > 2 then
        // At max depth, don't generate more AggregateErrors
        Gen.oneof
            [ Gen.map ConfigError (Gen.filter ((<>) null) Arb.generate<string>)
              Gen.map MemoryError (Gen.filter ((<>) null) Arb.generate<string>)
              Gen.map SecurityError (Gen.filter ((<>) null) Arb.generate<string>) ]
    else
        Gen.frequency
            [ (10, appErrorGen) // 10/11 chance of non-aggregate error
              (1,
               gen { // 1/11 chance of aggregate error
                   let! head = appErrorGenWithAggregate (depth + 1)
                   let! tailCount = Gen.choose (0, 2)
                   let! tail = Gen.listOfLength tailCount (appErrorGenWithAggregate (depth + 1))
                   return AggregateError(NonEmptyList(head, tail))
               }) ]

/// Container type for custom Arbitrary instances
type CustomGenerators =
    static member String() =
        Arb.Default.String() |> Arb.filter ((<>) null)

    static member ExportConfig() = Arb.fromGen exportConfigGen

    static member AppError() =
        Arb.fromGen (appErrorGenWithAggregate 0)

    static member PropertyDict() = Arb.fromGen propertyDictGen

/// Generator for valid Neo4j property values including all supported types
let neo4jValueGen: Gen<obj> =
    Gen.frequency
        [ (3, Gen.constant (box null))
          (2, Gen.map box Arb.generate<bool>)
          (3, Gen.map box Arb.generate<int64>)
          (3, Gen.map box (Gen.filter Double.IsFinite Arb.generate<float>)) // Only finite values
          (5, Gen.map box (Gen.filter (fun s -> s <> null) Arb.generate<string>))
          (1, Gen.map box Arb.generate<DateTime>) // Temporal types
          (1, Gen.map (fun arr -> box (arr: int64[])) (Gen.arrayOf Arb.generate<int64>)) ] // Arrays

/// Generator for richer property dictionaries with various Neo4j types
let richPropertyDictGen: Gen<IDictionary<string, obj>> =
    gen {
        let! count = Gen.choose (0, 20) // More properties for thorough testing
        let! keys = Gen.listOfLength count propertyKeyGen
        let distinctKeys = keys |> List.distinct
        let! values = Gen.listOfLength (List.length distinctKeys) neo4jValueGen
        let dict = Dictionary<string, obj>()

        List.zip distinctKeys values
        |> List.iter (fun (k, v) -> dict.[k] <- v)

        return dict :> IDictionary<string, obj>
    }

/// Generator for node data (labels + properties)
let nodeDataGen: Gen<Set<string> * IDictionary<string, obj>> =
    Gen.map2 (fun labels props -> (labels, props)) labelSetGen richPropertyDictGen

/// Generator for element IDs
let elementIdGen: Gen<string> =
    Gen.frequency
        [ (8, Gen.map (sprintf "element:%d") (Gen.choose (1, 10000)))
          (1, Gen.constant "") // Test empty element IDs
          (1, Gen.constant null) ] // Test null element IDs

/// Generator for error info tuples
let errorInfoGen: Gen<AppError * string> =
    Gen.map2 (fun error elemId -> (error, elemId)) appErrorGen elementIdGen

/// Generator for pagination strategies
let keysetIdGen: Gen<KeysetId> =
    Gen.oneof
        [ Gen.map NumericId (Gen.choose (1, 1000000) |> Gen.map int64)
          Gen.map ElementId (Gen.map (sprintf "element:%d") (Gen.choose (1, 1000000))) ]

let neo4jVersionGen: Gen<Neo4jVersion> =
    Gen.elements [ V4x; V5x; V6x; Unknown ]

let paginationStrategyGen: Gen<PaginationStrategy> =
    Gen.oneof
        [ Gen.map SkipLimit (Gen.choose (0, 10000))
          Gen.map2 (fun lastId version -> Keyset(Some lastId, version)) keysetIdGen neo4jVersionGen ]

/// Registers all custom generators with FsCheck
let registerGenerators () =
    Arb.register<CustomGenerators> () |> ignore
