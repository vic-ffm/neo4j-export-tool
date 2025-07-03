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
