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

open System.IO
open System.Diagnostics

module Cleanup =
    let registerTempFile (context: ApplicationContext) (path: string) = AppContext.addTempFile context path

    let performCleanup (context: ApplicationContext) (reason: string) =
        Log.info (sprintf "Performing cleanup: %s" reason)

        if not (AppContext.isCancellationRequested context) then
            AppContext.cancel context

        for proc in context.ActiveProcesses do
            try
                if not proc.HasExited then
                    let mutable exited = false

                    if proc.MainWindowHandle <> System.IntPtr.Zero then
                        proc.CloseMainWindow() |> ignore
                        exited <- proc.WaitForExit(5000)

                    if not exited && not proc.HasExited then
                        proc.Kill()
                        Log.debug (sprintf "Forcefully terminated process: %d" proc.Id)
                    else
                        Log.debug (sprintf "Gracefully terminated process: %d" proc.Id)
            with ex ->
                let procId =
                    try
                        proc.Id.ToString()
                    with _ ->
                        "unknown"

                Log.warn (sprintf "Failed to terminate process %s: %s" procId (ErrorAccumulation.exceptionToString ex))

        for file in context.TempFiles do
            try
                if File.Exists(file) then
                    File.Delete(file)
                    Log.debug (sprintf "Deleted temp file: %s" file)
            with ex ->
                Log.warn (sprintf "Failed to delete temp file %s: %s" file (ErrorAccumulation.exceptionToString ex))

        Log.info "Cleanup completed"
