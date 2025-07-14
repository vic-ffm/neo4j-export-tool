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

module Neo4jExport.SerializationEngine

open System
open System.Text.Json
open Neo4j.Driver
open Neo4jExport
open Neo4jExport.ExportTypes

open Neo4jExport.SerializationContext
open Neo4jExport.SerializationPrimitives
open Neo4jExport.SerializationTemporal
open Neo4jExport.SerializationSpatial
open Neo4jExport.SerializationCollections
open Neo4jExport.SerializationGraphElements
open Neo4jExport.SerializationPath
open ErrorTracking

let rec serializeValue (writer: Utf8JsonWriter) (ctx: WriterContext) (depth: SerializationDepth) (value: obj) =
    if SerializationDepth.exceedsLimit ctx.Config.MaxNestedDepth depth then
        writeDepthExceeded writer value depth
    else
        try
            match value with
            | null -> serializeNull writer
            | :? string as s -> serializeString writer s ctx.Config
            | :? bool as b -> serializeBoolean writer b
            | TemporalType _ -> serializeTemporal writer value
            | NumericType n -> serializeNumeric writer n
            | :? double as d when Double.IsNaN d || Double.IsInfinity d -> serializeSpecialFloat writer d
            | :? float32 as f when Single.IsNaN f || Single.IsInfinity f -> serializeSpecialFloat32 writer f
            | GraphElement elem -> serializeGraphElement writer ctx depth elem
            | :? DateTime as dt -> serializeDateTime writer dt
            | :? DateTimeOffset as dto -> serializeDateTimeOffset writer dto
            | :? Point as p -> serializePoint writer p
            | :? (byte[]) as bytes -> serializeBinary writer bytes ctx.Config
            | :? Collections.IList as list -> serializeList writer ctx depth list
            | :? Collections.IDictionary as dict -> serializeMap writer ctx depth dict
            | _ -> writeUnknownType writer value
        with ex ->
            handleSerializationError writer ex depth

/// Explicitly initialize the serialization engine
let initializeSerializationEngine () =
    SerializationCollections.serializeValueFunc <-
        Some(fun writer ctx depth value -> serializeValue writer ctx depth value)

    SerializationGraphElements.serializePathFunc <- Some(fun writer ctx path -> serializePath writer ctx path)

/// Initialize circular dependencies after serializeValue is defined
do initializeSerializationEngine ()

let writeNode =
    SerializationGraphElements.writeNode

let writeRelationship =
    SerializationGraphElements.writeRelationship

/// Re-export high-performance direct functions for hot path
let internal writeNodeDirect =
    SerializationGraphElements.writeNodeDirect

let internal writeRelationshipDirect =
    SerializationGraphElements.writeRelationshipDirect

let serializeProperties =
    SerializationCollections.serializeProperties

let createErrorContext =
    SerializationContext.createErrorContext

let trackSerializationError =
    SerializationContext.trackSerializationError
