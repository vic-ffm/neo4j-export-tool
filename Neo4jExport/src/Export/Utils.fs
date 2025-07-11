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

module Neo4jExport.ExportUtils

open System
open System.Text
open System.Runtime.CompilerServices
open System.Collections.Generic
open Neo4jExport
open Neo4jExport.ExportTypes

module StringOps =
    // AggressiveInlining hints the JIT to inline this method at call sites
    // Critical for hot path string operations to avoid function call overhead
    [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
    let truncateSpan (maxLen: int) (span: ReadOnlySpan<char>) =
        // ReadOnlySpan<char> provides zero-copy string slicing
        // Avoids substring allocation when only checking length
        if span.Length <= maxLen then
            span.ToString()
        else
            String.Concat(span.Slice(0, maxLen - 3).ToString(), "...")

let computeSha256 (data: byte[]) =
    use sha256 =
        Security.Cryptography.SHA256.Create()

    let hash = sha256.ComputeHash data
    Convert.ToBase64String hash

let createKeyTracker () = HashSet<string>(StringComparer.Ordinal)

let ensureUniqueKey (key: string) (tracker: HashSet<string>) =
    let truncatedKey =
        if key.Length > 1000 then
            StringOps.truncateSpan 997 (key.AsSpan())
        else
            key

    // HashSet.Add returns true if the item was added (not a duplicate)
    if tracker.Add truncatedKey then
        truncatedKey
    else
        // Tail-recursive function finds next available key by appending counter
        // F# compiler optimizes tail recursion into a loop
        let rec findUniqueKey counter =
            let candidateKey =
                sprintf "%s_%d" truncatedKey counter

            if tracker.Add candidateKey then
                candidateKey
            else
                findUniqueKey (counter + 1)

        findUniqueKey 1


let validateLabel (label: string) (elementId: string) =
    match label with
    | null -> Error(sprintf "Null label found on node %s" elementId)
    | l when l.Length > 1000 -> Error(sprintf "Label truncated on node %s" elementId)
    | l -> Ok l

let validateRelType (relType: string) (elementId: string) =
    match relType with
    | null -> Error(sprintf "Null relationship type on relationship %s" elementId)
    | t when t.Length > 1000 -> Error(sprintf "Relationship type truncated on relationship %s" elementId)
    | t -> Ok t
