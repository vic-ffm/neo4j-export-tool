namespace Neo4jExport

open System

/// Structured logging with thread-safe console output and level filtering
module Log =
    type private LogLevel =
        | Debug
        | Info
        | Warn
        | Error
        | Fatal

    let private scriptName =
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Name

    let mutable private minLevel = LogLevel.Info
    let private consoleLock = obj ()

    /// Sets the minimum log level from a string value
    let setMinLevel levelStr =
        minLevel <-
            match levelStr with
            | "Debug"
            | "debug" -> LogLevel.Debug
            | "Info"
            | "info" -> LogLevel.Info
            | "Warn"
            | "warn" -> LogLevel.Warn
            | "Error"
            | "error" -> LogLevel.Error
            | "Fatal"
            | "fatal" -> LogLevel.Fatal
            | _ -> LogLevel.Info

    let private shouldLog level = level >= minLevel

    let private logInternal level message =
        if shouldLog level then
            lock consoleLock (fun () ->
                let timestamp =
                    DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'")

                let levelStr =
                    match level with
                    | LogLevel.Debug -> "DEBUG"
                    | LogLevel.Info -> "INFO"
                    | LogLevel.Warn -> "WARN"
                    | LogLevel.Error -> "ERROR"
                    | LogLevel.Fatal -> "FATAL"

                let color =
                    match level with
                    | LogLevel.Debug -> ConsoleColor.Gray
                    | LogLevel.Info -> ConsoleColor.White
                    | LogLevel.Warn -> ConsoleColor.Yellow
                    | LogLevel.Error -> ConsoleColor.Red
                    | LogLevel.Fatal -> ConsoleColor.Magenta

                Console.ForegroundColor <- color
                eprintfn "[%s] [%s] [%s] %s" timestamp levelStr scriptName message
                Console.ResetColor())

    let debug message = logInternal LogLevel.Debug message
    let info message = logInternal LogLevel.Info message
    let warn message = logInternal LogLevel.Warn message
    let error message = logInternal LogLevel.Error message
    let fatal message = logInternal LogLevel.Fatal message

    /// Logs an exception with optional stack trace based on current log level
    let logException (ex: exn) =
        error (sprintf "Exception: %s" ex.Message)

        if shouldLog LogLevel.Debug then
            error (sprintf "StackTrace: %s" ex.StackTrace)

            match ex.InnerException with
            | null -> ()
            | inner -> error (sprintf "InnerException: %s" inner.Message)
