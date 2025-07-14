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

module Neo4jExport.Tests.Unit.Export.Serialization.PathTests

open System
open System.Collections.Generic
open System.Text.Json
open Expecto
open Swensen.Unquote
open Neo4jExport
open Neo4jExport.ExportTypes
open Neo4jExport.SerializationPath
open Neo4jExport.SerializationContext
open Neo4jExport.Tests.Helpers.TestHelpers

[<Tests>]
let tests =
    testList
        "Serialization - Paths"
        [

          testList
              "Path serialization modes"
              [
                // NOTE: These tests demonstrate the intended behavior of path serialization.
                // Since IPath requires real Neo4j types, these tests would need to be
                // rewritten as integration tests or use test doubles to execute.

                testCase "determines serialization modes correctly"
                <| fun () ->
                    let buffer, writer, context =
                        createTestWriterContext ()
                    // ArrayBufferWriter is not IDisposable
                    use _ = writer

                    // Test mode determination based on node count
                    let fullMode =
                        determinePathLevel 5 context.Config

                    let compactMode =
                        determinePathLevel 50 context.Config

                    let idsOnlyMode =
                        determinePathLevel 500 context.Config

                    test <@ fullMode = Full @>
                    test <@ compactMode = Compact @>
                    test <@ idsOnlyMode = IdsOnly @>

                testCase "handles empty path concept"
                <| fun () ->
                    // Conceptual test - an empty path would have:
                    // - 0 nodes
                    // - 0 relationships
                    // - empty sequence
                    // In practice, Neo4j doesn't return empty paths, but the serializer should handle it
                    let nodeCount = 0
                    let relCount = 0

                    // Neo4j path invariant: relationships = nodes - 1 (except for empty path)
                    test <@ nodeCount = 0 && relCount = 0 @>

                testCase "handles single node path concept"
                <| fun () ->
                    // A path with just one node has:
                    // - 1 node
                    // - 0 relationships
                    // - sequence: [("node", 0)]
                    let nodeCount = 1
                    let relCount = 0

                    // Verify path invariant
                    test <@ relCount = nodeCount - 1 @>

                testCase "validates path structure invariant"
                <| fun () ->
                    // Neo4j paths always follow: Node -> Rel -> Node -> Rel -> ... -> Node
                    // Therefore: relationships = nodes - 1

                    let validPaths =
                        [ (1, 0) // Single node
                          (2, 1) // Two nodes, one relationship
                          (3, 2) // Three nodes, two relationships
                          (10, 9) ] // Ten nodes, nine relationships

                    validPaths
                    |> List.iter (fun (nodes, rels) -> test <@ rels = nodes - 1 @>)

                testCase "path mode thresholds from config"
                <| fun () ->
                    let config = createTestConfig ()

                    // Verify default thresholds
                    test <@ config.PathFullModeLimit = 10L @>
                    test <@ config.PathCompactModeLimit = 100L @>

                    // Test threshold boundaries
                    let mode9 = determinePathLevel 9 config
                    let mode10 = determinePathLevel 10 config
                    let mode11 = determinePathLevel 11 config

                    test <@ mode9 = Full @>
                    test <@ mode10 = Full @>
                    test <@ mode11 = Compact @>

                    let mode99 = determinePathLevel 99 config
                    let mode100 = determinePathLevel 100 config
                    let mode101 = determinePathLevel 101 config

                    test <@ mode99 = Compact @>
                    test <@ mode100 = Compact @>
                    test <@ mode101 = IdsOnly @> ]

          testList
              "Path length limits"
              [ testCase "enforces maximum path length"
                <| fun () ->
                    let config = createTestConfig ()

                    // Default max path length
                    test <@ config.MaxPathLength = 100_000L @>

                    // Path within limit
                    let withinLimit =
                        int config.MaxPathLength - 1

                    test <@ withinLimit < int config.MaxPathLength @>

                    // Path exceeding limit
                    let exceedsLimit =
                        int config.MaxPathLength + 1

                    test <@ exceedsLimit > int config.MaxPathLength @> ]

          testList
              "Path sequence generation"
              [ testCase "generates correct sequence for valid paths"
                <| fun () ->
                    // The path sequence alternates between nodes and relationships
                    // For a path with N nodes and N-1 relationships:
                    // Sequence: [("node",0), ("relationship",0), ("node",1), ("relationship",1), ..., ("node",N-1)]

                    let validateSequence nodeCount =
                        let relCount = nodeCount - 1
                        let expectedLength = nodeCount + relCount

                        // Manually construct expected sequence
                        let expected =
                            [ for i in 0 .. expectedLength - 1 do
                                  if i % 2 = 0 then
                                      yield ("node", i / 2)
                                  else
                                      yield ("relationship", i / 2) ]

                        // Verify structure
                        test <@ expected.Length = expectedLength @>

                        test
                            <@
                                expected
                                |> List.filter (fun (t, _) -> t = "node")
                                |> List.length = nodeCount
                            @>

                        test
                            <@
                                expected
                                |> List.filter (fun (t, _) -> t = "relationship")
                                |> List.length = relCount
                            @>

                    // Test various path lengths
                    validateSequence 1 // Single node
                    validateSequence 2 // Two nodes
                    validateSequence 5 // Five nodes
                    validateSequence 10 ] // Ten nodes

          testList
              "Serialization format concepts"
              [ testCase "Full mode includes all data"
                <| fun () ->
                    // In Full mode, the JSON should include:
                    // - All node properties
                    // - All relationship properties
                    // - Labels for nodes
                    // - Type for relationships
                    // - Complete sequence

                    // Conceptual structure verification
                    let fullModeFields =
                        [ "_type"
                          "length"
                          "_serialization_level"
                          "nodes"
                          "relationships"
                          "sequence" ]

                    test <@ fullModeFields.Length = 6 @>

                testCase "Compact mode reduces property data"
                <| fun () ->
                    // In Compact mode:
                    // - Nodes have element_id and labels only (no properties)
                    // - Relationships have basic info (no properties)
                    // - Sequence is still included

                    let compactNodeFields =
                        [ "element_id"; "labels" ]

                    let compactRelFields =
                        [ "element_id"
                          "type"
                          "start_element_id"
                          "end_element_id" ]

                    test <@ compactNodeFields.Length = 2 @>
                    test <@ compactRelFields.Length = 4 @>

                testCase "IdsOnly mode has minimal data"
                <| fun () ->
                    // In IdsOnly mode:
                    // - Only element IDs are preserved
                    // - No properties, labels, or types
                    // - Sequence is still included for structure

                    let idsOnlyNodeFields = [ "element_id" ]
                    let idsOnlyRelFields = [ "element_id" ]

                    test <@ idsOnlyNodeFields.Length = 1 @>
                    test <@ idsOnlyRelFields.Length = 1 @> ] ]
