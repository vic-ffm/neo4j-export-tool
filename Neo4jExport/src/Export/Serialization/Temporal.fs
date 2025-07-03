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

module Neo4jExport.SerializationTemporal

open System
open System.Text.Json
open Neo4j.Driver

let serializeTemporal (writer: Utf8JsonWriter) (value: obj) =
    let str =
        match value with
        | :? LocalDate as ld -> ld.ToString()
        | :? LocalTime as lt -> lt.ToString()
        | :? LocalDateTime as ldt -> ldt.ToString()
        | :? ZonedDateTime as zdt -> zdt.ToString()
        | :? Duration as dur -> dur.ToString()
        | :? OffsetTime as ot -> ot.ToString()
        | _ -> failwith "Not a temporal type"

    writer.WriteStringValue str

let serializeDateTime (writer: Utf8JsonWriter) (dt: DateTime) =
    writer.WriteStringValue(dt.ToString "O")

let serializeDateTimeOffset (writer: Utf8JsonWriter) (dto: DateTimeOffset) =
    writer.WriteStringValue(dto.ToString "O")
