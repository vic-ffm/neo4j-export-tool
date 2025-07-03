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

/// Tracks export statistics per label during the export process
module LabelStatsTracker =
    type private LabelState =
        | NotStarted
        | InProgress of startTime: DateTime * recordCount: int64 * bytesWritten: int64
        | Completed of stats: FileLevelStatistics

    type Tracker =
        private
            { states: Map<string, LabelState> }

    let create () = { states = Map.empty }

    let startLabel (label: string) (tracker: Tracker) : Tracker =
        match Map.tryFind label tracker.states with
        | None
        | Some NotStarted ->
            { tracker with
                states =
                    tracker.states
                    |> Map.add label (InProgress(DateTime.UtcNow, 0L, 0L)) }
        | _ -> tracker

    let updateLabel (label: string) (recordCount: int64) (bytesWritten: int64) (tracker: Tracker) : Tracker =
        match Map.tryFind label tracker.states with
        | Some(InProgress(startTime, currentCount, currentBytes)) ->
            { tracker with
                states =
                    tracker.states
                    |> Map.add label (InProgress(startTime, currentCount + recordCount, currentBytes + bytesWritten)) }
        | _ ->
            Log.warn (sprintf "Attempted to update label '%s' that was not started or already completed" label)
            tracker

    let completeLabel (label: string) (tracker: Tracker) : Tracker * FileLevelStatistics option =
        match Map.tryFind label tracker.states with
        | Some(InProgress(startTime, recordCount, bytesWritten)) ->
            let duration =
                (DateTime.UtcNow - startTime).TotalMilliseconds
                |> int64

            let stats =
                { Label = label
                  RecordCount = recordCount
                  BytesWritten = bytesWritten
                  ExportDurationMs = duration }

            let newTracker =
                { tracker with
                    states = tracker.states |> Map.add label (Completed stats) }

            (newTracker, Some stats)
        | _ -> (tracker, None)

    let getCompletedStats (tracker: Tracker) : FileLevelStatistics list =
        tracker.states
        |> Map.toList
        |> List.choose (fun (label, state) ->
            match state with
            | Completed stats -> Some stats
            | _ -> None)

    let completeAllInProgress (tracker: Tracker) : Tracker =
        tracker.states
        |> Map.toList
        |> List.fold
            (fun currentTracker (label, state) ->
                match state with
                | InProgress _ ->
                    let newTracker, _ =
                        completeLabel label currentTracker

                    newTracker
                | _ -> currentTracker)
            tracker

    let finalizeAndGetAllStats (tracker: Tracker) : FileLevelStatistics list =
        let finalizedTracker =
            completeAllInProgress tracker

        getCompletedStats finalizedTracker
