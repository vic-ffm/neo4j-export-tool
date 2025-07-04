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
type SerializationState =
    { Depth: SerializationDepth
      BytesWritten: int64
      RecordCount: int64 }

type WriterContext =
    { Config: ExportConfig
      ErrorTracker: ErrorTracker
      ExportId: Guid }

/// Generic batch processor configuration
type BatchProcessor =
    { Query: string
      GetTotalQuery: string option
      ProcessRecord:
          ArrayBufferWriter<byte>
              -> IRecord
              -> Guid
              -> ExportProgress
              -> ErrorTracker
              -> ExportConfig
              -> (int64 * ExportProgress)
      EntityName: string }

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
