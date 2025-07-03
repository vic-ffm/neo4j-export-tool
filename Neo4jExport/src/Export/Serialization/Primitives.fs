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

module Neo4jExport.SerializationPrimitives

open System
open System.Text
open System.Text.Json
open System.Runtime.CompilerServices
open Neo4jExport
open Neo4jExport.ExportUtils

[<Literal>]
let private MaxStringLength = 10_000_000

[<Literal>]
let private MaxBinaryLength = 50_000_000

let serializeNull (writer: Utf8JsonWriter) = writer.WriteNullValue()

let serializeBoolean (writer: Utf8JsonWriter) (value: bool) = writer.WriteBooleanValue value

let serializeString (writer: Utf8JsonWriter) (value: string) (config: ExportConfig) =
    if value.Length > MaxStringLength then
        writer.WriteStartObject()
        writer.WriteString("_truncated", "string_too_large")
        writer.WriteNumber("_length", value.Length)
        writer.WriteString("_prefix", StringOps.truncateSpan 1000 (value.AsSpan()))

        writer.WriteString(
            "_sha256",
            try
                computeSha256 (Encoding.UTF8.GetBytes value)
            with _ ->
                "hash_failed"
        )

        writer.WriteEndObject()
    else
        writer.WriteStringValue value

[<MethodImpl(MethodImplOptions.AggressiveInlining)>]
let serializeNumeric (writer: Utf8JsonWriter) (value: obj) =
    match value with
    | :? int64 as i -> writer.WriteNumberValue i
    | :? int32 as i -> writer.WriteNumberValue i
    | :? int16 as i -> writer.WriteNumberValue i
    | :? uint64 as i -> writer.WriteNumberValue(decimal i)
    | :? uint32 as i -> writer.WriteNumberValue i
    | :? uint16 as i -> writer.WriteNumberValue i
    | :? byte as b -> writer.WriteNumberValue b
    | :? sbyte as b -> writer.WriteNumberValue b
    | :? decimal as d -> writer.WriteNumberValue d
    | :? double as d -> writer.WriteNumberValue d
    | :? float32 as f -> writer.WriteNumberValue(float f)
    | _ -> writer.WriteStringValue(value.ToString())

let serializeSpecialFloat (writer: Utf8JsonWriter) (value: float) =
    if Double.IsNaN value then
        writer.WriteStringValue "NaN"
    elif Double.IsPositiveInfinity value then
        writer.WriteStringValue "Infinity"
    else
        writer.WriteStringValue "-Infinity"

let serializeSpecialFloat32 (writer: Utf8JsonWriter) (value: float32) =
    if Single.IsNaN value then
        writer.WriteStringValue "NaN"
    elif Single.IsPositiveInfinity value then
        writer.WriteStringValue "Infinity"
    else
        writer.WriteStringValue "-Infinity"

let serializeBinary (writer: Utf8JsonWriter) (bytes: byte[]) (config: ExportConfig) =
    if bytes.Length > MaxBinaryLength then
        writer.WriteStartObject()
        writer.WriteString("_truncated", "binary_too_large")
        writer.WriteNumber("_length", bytes.Length)

        writer.WriteString(
            "_sha256",
            try
                computeSha256 bytes
            with _ ->
                "hash_failed"
        )

        writer.WriteEndObject()
    else
        try
            writer.WriteStringValue(Convert.ToBase64String bytes)
        with _ ->
            writer.WriteStartObject()
            writer.WriteString("_type", "byte_array")
            writer.WriteNumber("_length", bytes.Length)
            writer.WriteString("_error", "base64_failed")
            writer.WriteEndObject()
