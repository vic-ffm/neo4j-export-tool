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

module Neo4jExport.Tests.Properties.IdGenerationProperties

open System
open System.Collections.Generic
open Expecto
open FsCheck
open Swensen.Unquote
open Neo4jExport
open Neo4jExport.Neo4jExportToolId
open Neo4jExport.Tests.Helpers.Generators
open Neo4jExport.Tests.Helpers.TestHelpers
open System.Text.Json

[<Tests>]
let tests =
    testList
        "ID Generation Properties"
        [
          // 1. Determinism Property
          testPropertyWithConfig fsCheckConfig "generateNodeId is deterministic"
          <| fun (labels: Set<string>) (props: IDictionary<string, obj>) ->
              let id1 =
                  Neo4jExportToolId.generateNodeId (labels |> Set.toSeq) props

              let id2 =
                  Neo4jExportToolId.generateNodeId (labels |> Set.toSeq) props

              id1 = id2

          // 2. Format Property
          testPropertyWithConfig fsCheckConfig "generateNodeId produces valid 64-char hex string"
          <| fun (labels: Set<string>) (props: IDictionary<string, obj>) ->
              let id =
                  Neo4jExportToolId.generateNodeId (labels |> Set.toSeq) props

              id.Length = 64
              && id
                 |> Seq.forall (fun c -> (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))

          // 3. Canonicalization Property - Label Order Independence
          testPropertyWithConfig fsCheckConfig "label order doesn't affect node ID"
          <| fun (labelList: string list) (props: IDictionary<string, obj>) ->
              match labelList with
              | [] -> true // Skip empty case
              | labels ->
                  let set1 = Set.ofList labels
                  let set2 = labels |> List.rev |> Set.ofList

                  let id1 =
                      Neo4jExportToolId.generateNodeId (set1 |> Set.toSeq) props

                  let id2 =
                      Neo4jExportToolId.generateNodeId (set2 |> Set.toSeq) props

                  id1 = id2

          // 4. Canonicalization Property - Verify Key Sorting
          testPropertyWithConfig fsCheckConfig "canonicalizeProperties sorts keys alphabetically"
          <| fun (props: (string * obj) list) ->
              match props with
              | [] -> true // Empty case is trivially sorted
              | _ ->
                  // Since we can't easily test the private canonicalizeProperties function,
                  // we'll test the behavior indirectly by verifying that identical content
                  // with different key orders produces the same ID
                  let keys =
                      props |> List.map fst |> List.distinct

                  let values =
                      props
                      |> List.take (List.length keys)
                      |> List.map snd

                  // Create multiple dictionaries with same content but different key insertion order
                  let dict1 = Dictionary<string, obj>()
                  let dict2 = Dictionary<string, obj>()

                  // Add in forward order
                  List.zip keys values
                  |> List.iter (fun (k, v) -> dict1.[k] <- v)

                  // Add in reverse order
                  List.zip (List.rev keys) (List.rev values)
                  |> List.iter (fun (k, v) -> dict2.[k] <- v)

                  // Generate IDs - they should be the same due to canonicalization
                  let id1 =
                      Neo4jExportToolId.generateNodeId Seq.empty dict1

                  let id2 =
                      Neo4jExportToolId.generateNodeId Seq.empty dict2

                  id1 = id2

          // 5. Distinctness Property
          testPropertyWithConfig
              { fsCheckConfig with maxTest = 500 }
              "different inputs produce different IDs with high probability"
          <| fun () ->
              let gen =
                  Gen.map2
                      (fun labels props -> Neo4jExportToolId.generateNodeId (labels |> Set.toSeq) props)
                      labelSetGen
                      richPropertyDictGen

              let ids = Gen.sample 100 100 gen

              let uniqueIds =
                  ids |> List.distinct |> List.length
              // Allow for small collision probability in random data
              float uniqueIds / float ids.Length > 0.95

          // 6. Relationship ID Properties
          testPropertyWithConfig fsCheckConfig "generateRelationshipId is deterministic"
          <| fun (relType: string) (startId: string) (endId: string) (props: IDictionary<string, obj>) ->
              // Generate valid node IDs for start/end
              let validStartId = String.replicate 64 "a"
              let validEndId = String.replicate 64 "b"

              let safeRelType =
                  if String.IsNullOrEmpty relType then
                      "RELATES_TO"
                  else
                      relType

              let id1 =
                  Neo4jExportToolId.generateRelationshipId safeRelType validStartId validEndId props

              let id2 =
                  Neo4jExportToolId.generateRelationshipId safeRelType validStartId validEndId props

              id1 = id2

          // 7. Null/Empty Safety
          testPropertyWithConfig fsCheckConfig "handles null labels gracefully"
          <| fun (props: IDictionary<string, obj>) ->
              let id =
                  Neo4jExportToolId.generateNodeId null props

              Neo4jExportToolId.isValidStableId id

          testPropertyWithConfig fsCheckConfig "handles empty properties"
          <| fun (labels: Set<string>) ->
              let emptyProps =
                  Dictionary<string, obj>() :> IDictionary<string, obj>

              let id =
                  Neo4jExportToolId.generateNodeId (labels |> Set.toSeq) emptyProps

              Neo4jExportToolId.isValidStableId id ]
