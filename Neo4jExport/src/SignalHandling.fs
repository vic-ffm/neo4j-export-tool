namespace Neo4jExport

open System
open System.Threading
#if NET6_0_OR_GREATER
open System.Runtime.InteropServices
#endif

module SignalHandling =
    // Signal handlers only signal cancellation, no cleanup
    let registerHandlers (context: ApplicationContext) =
        AppDomain.CurrentDomain.UnhandledException.Add(fun args ->
            let ex = args.ExceptionObject :?> Exception
            Log.fatal (sprintf "Unhandled exception: %s" ex.Message)
            Log.logException ex)

        Console.CancelKeyPress.Add(fun args ->
            if not (AppContext.isCancellationRequested context) then
                Log.warn "Received interrupt signal (SIGINT), requesting shutdown..."
                AppContext.cancel context
                args.Cancel <- true)

#if NET6_0_OR_GREATER
        PosixSignalRegistration.Create(
            PosixSignal.SIGTERM,
            Action<PosixSignalContext>(fun _ ->
                if not (AppContext.isCancellationRequested context) then
                    Log.warn "Received SIGTERM signal, requesting shutdown..."
                    AppContext.cancel context)
        )
        |> ignore
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

                        createMethod.Invoke(null, [| sigterm; box handler |])
                        |> ignore

                        Log.debug "SIGTERM handler registered via reflection"
                    else
                        Log.debug "PosixSignalRegistration.Create method not found"
                else
                    Log.debug "PosixSignalRegistration types not available"
            with ex ->
                Log.debug (sprintf "Could not register SIGTERM handler: %s" ex.Message)

        registerSigtermFallback ()
#endif
