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

/// Core progress reporting operations
module ProgressOperations =
    let report
        (stats: ExportProgress)
        (startTime: DateTime)
        (interval: TimeSpan)
        (lastProgress: DateTime)
        (totalOpt: int64 option)
        (entityType: string)
        =
        let now = DateTime.UtcNow

        // Throttle progress reporting to avoid spamming logs during fast exports
        // Only report if the specified interval has elapsed since last report
        if now - lastProgress > interval then
            let rate =
                float stats.RecordsProcessed
                / (now - startTime).TotalSeconds

            let message =
                match totalOpt with
                | Some total ->
                    sprintf
                        "%s: %d/%d exported (%.0f records/sec, %s written)"
                        entityType
                        stats.RecordsProcessed
                        total
                        rate
                        (Utils.formatBytes stats.BytesWritten)
                | None ->
                    sprintf
                        "%s: %d exported (%.0f records/sec, %s written)"
                        entityType
                        stats.RecordsProcessed
                        rate
                        (Utils.formatBytes stats.BytesWritten)

            Log.info message
            now
        else
            lastProgress
