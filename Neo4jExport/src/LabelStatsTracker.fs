namespace Neo4jExport

open System

/// Module for tracking export statistics per label
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
