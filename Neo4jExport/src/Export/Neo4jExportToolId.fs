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

namespace Neo4jExport

open System
open System.Collections.Generic
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.Json.Serialization

/// High-performance stable ID generation for Neo4j entities
module Neo4jExportToolId =

    // JSON options for canonical serialization - create once
    // Canonical form ensures identical objects always produce the same JSON string
    let private jsonOptions =
        let options = JsonSerializerOptions()
        options.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull
        options.WriteIndented <- false
        options

    // Convert byte to hex chars without allocation
    // inline expands this function at call sites for performance
    // CompiledName attribute controls the .NET method name for interop
    [<CompiledName("ByteToHexChars")>]
    let inline private byteToHexChars (b: byte) =
        // >>> is unsigned right shift, extracting high nibble (4 bits)
        let high = int b >>> 4
        // &&& is bitwise AND, masking to get low nibble
        let low = int b &&& 0xF

        // Convert to hex chars: 0-9 = ASCII 48-57, a-f = ASCII 97-102
        // This avoids string allocation compared to ToString("x2")
        let highChar =
            if high < 10 then char (high + 48) else char (high + 87)

        let lowChar =
            if low < 10 then char (low + 48) else char (low + 87)

        highChar, lowChar

    [<CompiledName("ComputeSha256Hex")>]
    let private computeSha256Hex (input: string) =
        let bytes = Encoding.UTF8.GetBytes(input)
        // Use static SHA256.HashData method - thread-safe and optimal for .NET 5+
        let hash = SHA256.HashData(bytes)

        // Pre-allocate exact size for hex string
        let chars = Array.zeroCreate<char> 64
        let mutable idx = 0

        for b in hash do
            let high, low = byteToHexChars b
            chars.[idx] <- high
            chars.[idx + 1] <- low
            idx <- idx + 2

        String(chars)

    // Recursively convert JsonValue discriminated union to objects for serialization
    // 'rec' enables self-recursion for nested structures
    // 'box' converts value types to obj (F#'s object type)
    let rec private convertToJsonElement (value: JsonValue) =
        match value with
        | JString s -> box s
        | JNumber n -> box n
        | JBool b -> box b
        | JNull -> null
        | JObject dictVal ->
            dictVal
            |> Seq.map (fun kvp -> kvp.Key, convertToJsonElement kvp.Value)
            |> dict
            |> box
        | JArray list ->
            list
            |> List.map convertToJsonElement
            |> List.toArray
            |> box

    [<CompiledName("CanonicalizeProperties")>]
    let private canonicalizeProperties (properties: IDictionary<string, obj>) =
        if properties.Count = 0 then
            ""
        else
            // Convert Neo4j properties to sortable format
            let converted = Dictionary<string, obj>()

            for kvp in properties do
                let value =
                    match kvp.Value with
                    | :? JsonValue as jv -> convertToJsonElement jv
                    | v -> v

                converted.[kvp.Key] <- value

            // Sort keys for deterministic output
            let sorted =
                converted
                |> Seq.sortBy (fun kvp -> kvp.Key)
                |> Seq.map (fun kvp -> kvp.Key, kvp.Value)
                |> dict

            JsonSerializer.Serialize(sorted, jsonOptions)

    [<CompiledName("GenerateNodeId")>]
    let generateNodeId (labels: string seq) (properties: IDictionary<string, obj>) =
        // Sort labels for deterministic output
        let sortedLabels =
            if isNull labels then
                ""
            else
                labels |> Seq.sort |> String.concat "+"

        let propsJson =
            canonicalizeProperties properties

        let hashInput =
            sprintf "node:%s:%s" sortedLabels propsJson

        computeSha256Hex hashInput

    [<CompiledName("GenerateRelationshipId")>]
    let generateRelationshipId
        (relType: string)
        (startNodeStableId: string)
        (endNodeStableId: string)
        (properties: IDictionary<string, obj>)
        =

        let propsJson =
            canonicalizeProperties properties

        let hashInput =
            sprintf "rel:%s:%s:%s:%s" relType startNodeStableId endNodeStableId propsJson

        computeSha256Hex hashInput

    /// Generate a stable relationship identity hash based on element IDs (not content hashes)
    let generateRelationshipIdentityHash
        (relType: string)
        (startElementId: string)
        (endElementId: string)
        (properties: IDictionary<string, obj>)
        =

        let propsJson =
            canonicalizeProperties properties

        let hashInput =
            sprintf "rel:%s:%s:%s:%s" relType startElementId endElementId propsJson

        computeSha256Hex hashInput

    let isValidStableId (id: string) =
        not (String.IsNullOrEmpty(id))
        && id.Length = 64
        && id
           |> Seq.forall (fun c -> (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))
