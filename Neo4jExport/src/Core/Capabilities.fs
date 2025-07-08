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

/// Operation types that group related functions following FP principles
/// These use closure to capture context, reducing parameter passing
module Capabilities =

    /// Groups workflow control operations
    /// Captures ApplicationContext and ExportConfig through closure
    type WorkflowOperations =
        { IsCancellationRequested: unit -> bool
          RegisterTempFile: string -> unit
          GetConfig: unit -> ExportConfig }

    module WorkflowOperations =
        /// Creates workflow operations with captured context
        let create (appContext: ApplicationContext) (config: ExportConfig) =
            { IsCancellationRequested = fun () -> CancellationOperations.check appContext
              RegisterTempFile = fun path -> TempFileOperations.register appContext path
              GetConfig = fun () -> config }

    /// Groups progress reporting operations
    /// Reduces reportProgress from 6 parameters to 3
    type ProgressOperations =
        { ReportProgress: string -> int64 -> int64 option -> DateTime
          GetStartTime: unit -> DateTime
          GetInterval: unit -> TimeSpan }

    module ProgressOperations =
        /// Creates progress operations capturing common parameters
        let create (startTime: DateTime) (interval: TimeSpan) (stats: ExportProgress) =
            let mutable currentStats = stats

            { ReportProgress =
                fun entityType current totalOpt ->
                    currentStats <-
                        { currentStats with
                            RecordsProcessed = current }

                    ProgressOperations.report currentStats startTime interval DateTime.UtcNow totalOpt entityType
              GetStartTime = fun () -> startTime
              GetInterval = fun () -> interval }
