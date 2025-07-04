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
open System.Threading
#if NET6_0_OR_GREATER
open System.Runtime.InteropServices
#endif

module SignalHandling =
    let registerHandlers (context: ApplicationContext) : IDisposable option =
        AppDomain.CurrentDomain.UnhandledException.Add(fun args ->
            let ex = args.ExceptionObject :?> Exception
            Log.fatal (sprintf "Unhandled exception: %s" (ErrorAccumulation.exceptionToString ex))
            Log.logException ex)

        Console.CancelKeyPress.Add(fun args ->
            if not (AppContext.isCancellationRequested context) then
                Log.warn "Received interrupt signal (SIGINT), requesting shutdown..."
                AppContext.cancel context
                args.Cancel <- true)

#if NET6_0_OR_GREATER
        let sigtermRegistration =
            PosixSignalRegistration.Create(
                PosixSignal.SIGTERM,
                Action<PosixSignalContext>(fun _ ->
                    if not (AppContext.isCancellationRequested context) then
                        Log.warn "Received SIGTERM signal, requesting shutdown..."
                        AppContext.cancel context)
            )

        Some(sigtermRegistration :> IDisposable)
#else
        let registerSigtermFallback () =
            try
                let posixSignalType =
                    Type.GetType("System.Runtime.InteropServices.PosixSignalRegistration, System.Runtime")

                let posixSignalEnum =
                    Type.GetType("System.Runtime.InteropServices.PosixSignal, System.Runtime")

                if posixSignalType <> null && posixSignalEnum <> null then
                    let sigterm =
                        Enum.Parse(posixSignalEnum, "SIGTERM")

                    let createMethod =
                        posixSignalType.GetMethod(
                            "Create",
                            [| posixSignalEnum
                               typeof<Action<obj>> |]
                        )

                    if createMethod <> null then
                        let handler =
                            Action<obj>(fun _ ->
                                if not (AppContext.isCancellationRequested context) then
                                    Log.warn "Received SIGTERM signal, requesting shutdown..."
                                    AppContext.cancel context)

                        let registration =
                            createMethod.Invoke(null, [| sigterm; box handler |])

                        Log.debug "SIGTERM handler registered via reflection"

                        // Check if the result implements IDisposable
                        match registration with
                        | :? IDisposable as disposable -> Some disposable
                        | _ -> None
                    else
                        Log.debug "PosixSignalRegistration.Create method not found"
                        None
                else
                    Log.debug "PosixSignalRegistration types not available"
                    None
            with ex ->
                // Infrastructure failure - app works without SIGTERM handling
                // Log for diagnostics but don't create AppError (no caller to handle it)
                Log.debug (sprintf "Could not register SIGTERM handler: %s" (ErrorAccumulation.exceptionToString ex))
                None

        registerSigtermFallback ()
#endif
