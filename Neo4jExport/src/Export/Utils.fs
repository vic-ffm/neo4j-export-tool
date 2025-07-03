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
open System.Buffers
open System.Runtime.CompilerServices
open System.Collections.Generic
open Neo4jExport.ExportTypes

module StringOps =
    [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
    let truncateSpan (maxLen: int) (span: ReadOnlySpan<char>) =
        if span.Length <= maxLen then
            span.ToString()
        else
            String.Concat(span.Slice(0, maxLen - 3).ToString(), "...")

    [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
    let isNullOrWhiteSpace (s: string) = String.IsNullOrWhiteSpace(s)

module BufferPool =
    let private pool = ArrayPool<byte>.Shared

    [<Struct>]
    type PooledBuffer =
        { Buffer: byte[]
          Length: int }

        interface IDisposable with
            member this.Dispose() =
                pool.Return(this.Buffer, clearArray = true)

    let rent minLength =
        let buffer = pool.Rent(minLength)

        { Buffer = buffer
          Length = buffer.Length }

module VOpt =
    [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
    let inline ofOption opt =
        match opt with
        | Some v -> ValueSome v
        | None -> ValueNone

    [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
    let inline defaultValue def vopt =
        match vopt with
        | ValueSome v -> v
        | ValueNone -> def

let computeSha256 (data: byte[]) =
    use sha256 =
        Security.Cryptography.SHA256.Create()

    let hash = sha256.ComputeHash data
    Convert.ToBase64String hash

/// Track unique keys within a JSON object to prevent duplicates
let createKeyTracker () = HashSet<string>(StringComparer.Ordinal)

/// Ensures property keys are unique by adding suffix if needed
let ensureUniqueKey (key: string) (tracker: HashSet<string>) =
    let truncatedKey =
        if key.Length > 1000 then
            StringOps.truncateSpan 997 (key.AsSpan())
        else
            key

    if tracker.Add truncatedKey then
        truncatedKey
    else
        let rec findUniqueKey counter =
            let candidateKey =
                sprintf "%s_%d" truncatedKey counter

            if tracker.Add candidateKey then
                candidateKey
            else
                findUniqueKey (counter + 1)

        findUniqueKey 1


[<MethodImpl(MethodImplOptions.AggressiveInlining)>]
let validateDepth currentDepth maxDepth =
    if currentDepth >= maxDepth then
        ValueSome(DepthExceeded(currentDepth, maxDepth))
    else
        ValueNone

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
