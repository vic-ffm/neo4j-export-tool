module Neo4jExport.Tests.Helpers.TestLog

open System
open System.Threading

/// Test-specific logging that mimics production log levels and formatting
/// while integrating with Expecto test output
type private LogLevel =
    | Debug
    | Info
    | Warn
    | Error
    | Fatal

/// Thread-safe console operations
let private consoleLock = obj ()

/// Current minimum log level (mutable for runtime configuration)
let mutable private minLevel = LogLevel.Info

/// Test name prefix for better context
let mutable private currentTestName = ""

/// Set the current test name for context
let setCurrentTest name =
    Interlocked.Exchange(&currentTestName, name)
    |> ignore

/// Clear the current test name
let clearCurrentTest () =
    Interlocked.Exchange(&currentTestName, "")
    |> ignore

/// Set minimum log level from string
let setMinLevel levelStr =
    minLevel <-
        match levelStr with
        | "Debug"
        | "debug"
        | "DEBUG" -> LogLevel.Debug
        | "Info"
        | "info"
        | "INFO" -> LogLevel.Info
        | "Warn"
        | "warn"
        | "WARN" -> LogLevel.Warn
        | "Error"
        | "error"
        | "ERROR" -> LogLevel.Error
        | "Fatal"
        | "fatal"
        | "FATAL" -> LogLevel.Fatal
        | _ -> LogLevel.Info

/// Initialize from environment variable
let private initializeLogLevel () =
    match Environment.GetEnvironmentVariable(TestConstants.Env.TestLogLevel) with
    | null
    | "" -> setMinLevel TestConstants.Defaults.TestLogLevel
    | level -> setMinLevel level

// Initialize on module load
do initializeLogLevel ()

/// Check if a log level should be output
let private shouldLog level = level >= minLevel

/// Internal logging function with formatting
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
                | LogLevel.Info -> ConsoleColor.Cyan // Different from prod for test visibility
                | LogLevel.Warn -> ConsoleColor.Yellow
                | LogLevel.Error -> ConsoleColor.Red
                | LogLevel.Fatal -> ConsoleColor.Magenta

            // Include test context if available
            let context =
                if String.IsNullOrEmpty(currentTestName) then
                    "[TEST]"
                else
                    sprintf "[TEST:%s]" currentTestName

            Console.ForegroundColor <- color
            // Use eprintfn to match production behavior (stderr)
            eprintfn "[%s] [%s] %s %s" timestamp levelStr context message
            Console.ResetColor())

/// Public logging functions
let debug message = logInternal LogLevel.Debug message
let info message = logInternal LogLevel.Info message
let warn message = logInternal LogLevel.Warn message
let error message = logInternal LogLevel.Error message
let fatal message = logInternal LogLevel.Fatal message

/// Log with format string (F# idiomatic)
let debugf fmt = Printf.kprintf debug fmt
let infof fmt = Printf.kprintf info fmt
let warnf fmt = Printf.kprintf warn fmt
let errorf fmt = Printf.kprintf error fmt
let fatalf fmt = Printf.kprintf fatal fmt

/// Test execution helpers
let testStart name =
    setCurrentTest name
    infof "Starting: %s" name

let testEnd name (result: Result<unit, string>) =
    match result with
    | Result.Ok() -> debugf "Completed: %s" name
    | Result.Error msg -> errorf "Failed: %s - %s" name msg

    clearCurrentTest ()

/// Container operation logging
let containerOperation operation containerName =
    debugf "Container [%s]: %s" containerName operation

/// Data seeding progress
let dataSeeding operation count =
    debugf "Data: %s (%d records)" operation count

/// Performance metrics logging
let perfMetric metric value unit =
    infof "Performance: %s = %.2f %s" metric value unit

/// Expecto integration - wrap a test with logging
let withLogging name (test: Async<unit>) : Async<unit> =
    async {
        testStart name

        try
            do! test
            testEnd name (Result.Ok())
        with ex ->
            testEnd name (Result.Error ex.Message)
            raise ex
    }

/// Log test list execution
let testListStart name count =
    infof "Test List: %s (%d tests)" name count

let testListEnd name passed failed =
    if failed = 0 then
        infof "Test List Complete: %s - All %d tests passed" name passed
    else
        errorf "Test List Complete: %s - %d passed, %d failed" name passed failed
