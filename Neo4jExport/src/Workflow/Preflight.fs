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
open System.IO
open System.Globalization
open Neo4j.Driver

// Computation expression builder combines async and Result workflows
// This enables writing async code that can fail without nested match expressions
type AsyncResultBuilder() =
    member _.Return(x) = async { return Ok x }
    member _.ReturnFrom(x) = x

    member _.Bind(x, f) =
        async {
            match! x with
            | Ok v -> return! f v
            | Error e -> return Error e // Short-circuit on first error
        }

    member _.Zero() = async { return Ok() }

/// Memory estimation configuration
type MemoryEstimationConfig =
    { AverageRecordSize: int64
      ProcessingOverheadMultiplier: float
      MinimumReservation: int64 }

module Preflight =
    let private asyncResult =
        AsyncResultBuilder()

    let private getLinuxAvailableMemory () =
        try
            let lines =
                System.IO.File.ReadAllLines("/proc/meminfo")

            let availableLine =
                lines
                |> Array.tryFind (fun line -> line.StartsWith("MemAvailable:"))

            match availableLine with
            | Some line ->
                let parts =
                    line.Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries)

                if parts.Length >= 2 then
                    let kbValue = int64 parts.[1]
                    kbValue * 1024L
                else
                    Constants.Defaults.ConservativeMemoryFallback
            | None ->
                Log.warn "MemAvailable not found in /proc/meminfo"
                Constants.Defaults.ConservativeMemoryFallback
        with ex ->
            Log.warn (sprintf "Failed to parse /proc/meminfo: %s" (ErrorAccumulation.exceptionToString ex))
            Constants.Defaults.ConservativeMemoryFallback

    let private getPlatformSpecificMemory () =
        try
            // Platform-specific memory detection as fallback when GC info unavailable
            // Linux has reliable /proc/meminfo, other platforms use conservative defaults
            if
                System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Linux
                )
            then
                getLinuxAvailableMemory ()
            elif
                System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Windows
                )
            then
                Log.warn "Memory detection not implemented for Windows. Please monitor memory usage manually."
                0L
            else
                Log.warn "Platform-specific memory detection not available, using conservative estimate"
                Constants.Defaults.ConservativeMemoryFallback
        with ex ->
            Log.warn (sprintf "Platform detection failed: %s" (ErrorAccumulation.exceptionToString ex))
            Constants.Defaults.ConservativeMemoryFallback

    /// Gets available system memory using GC info or platform-specific methods
    let private getAvailableSystemMemory () =
        try
            let memInfo = GC.GetGCMemoryInfo()

            let availableMemory =
                memInfo.TotalAvailableMemoryBytes

            Log.debug (sprintf "System memory info - Total available: %s" (Utils.formatBytes availableMemory))

            availableMemory
        with
        | :? NotSupportedException as ex ->
            Log.warn (
                sprintf "GC memory info not supported on this platform: %s" (ErrorAccumulation.exceptionToString ex)
            )

            getPlatformSpecificMemory ()
        | ex ->
            Log.warn (sprintf "Failed to get system memory info: %s" (ErrorAccumulation.exceptionToString ex))
            Constants.Defaults.ConservativeMemoryFallback

    let private defaultMemoryEstimationConfig =
        { AverageRecordSize = Constants.Defaults.AverageRecordSizeBytes
          ProcessingOverheadMultiplier = Constants.Defaults.ProcessingOverheadMultiplier
          MinimumReservation = Constants.Defaults.MinimumMemoryReservation }

    let private estimateExportMemoryUsage (config: ExportConfig) =
        let estimationConfig =
            let getEnvInt64 name defaultValue =
                match Environment.GetEnvironmentVariable(name) with
                | null
                | "" -> defaultValue
                | value ->
                    match Int64.TryParse(value) with
                    | true, v -> v
                    | false, _ -> defaultValue

            let getEnvFloat name defaultValue =
                match Environment.GetEnvironmentVariable(name) with
                | null
                | "" -> defaultValue
                | value ->
                    match Double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture) with
                    | true, v -> v
                    | false, _ -> defaultValue

            { AverageRecordSize =
                getEnvInt64 Constants.Env.AverageRecordSize defaultMemoryEstimationConfig.AverageRecordSize
              ProcessingOverheadMultiplier =
                getEnvFloat Constants.Env.OverheadMultiplier defaultMemoryEstimationConfig.ProcessingOverheadMultiplier
              MinimumReservation =
                getEnvInt64 Constants.Env.MinMemoryReservation defaultMemoryEstimationConfig.MinimumReservation }

        let batchMemory =
            int64 config.BatchSize
            * estimationConfig.AverageRecordSize

        let withOverhead =
            int64 (
                float batchMemory
                * estimationConfig.ProcessingOverheadMultiplier
            )

        let estimated =
            max withOverhead estimationConfig.MinimumReservation

        Log.debug (
            sprintf
                "Memory estimation - Batch size: %d, Avg record: %s, Overhead: %.1fx, Estimated: %s"
                config.BatchSize
                (Utils.formatBytes estimationConfig.AverageRecordSize)
                estimationConfig.ProcessingOverheadMultiplier
                (Utils.formatBytes estimated)
        )

        estimated

    let private checkDiskSpace (config: ExportConfig) =
        async {
            Log.info "Checking disk space..."

            try
                let root =
                    Path.GetPathRoot(config.OutputDirectory)

                if String.IsNullOrWhiteSpace(root) then
                    return
                        Error(
                            FileSystemError(
                                config.OutputDirectory,
                                "Could not determine drive root from output path",
                                None
                            )
                        )
                elif root.StartsWith(@"\\") then
                    return
                        Error(
                            FileSystemError(
                                config.OutputDirectory,
                                "UNC paths are not supported for disk space checking",
                                None
                            )
                        )
                else
                    // NOTE: By the time this check runs, the output directory has already been created
                    // during configuration validation (Configuration.validateOutputDirectory). We can
                    // safely assume it exists and just check the disk space of its root drive.
                    let drive = DriveInfo(root)

                    let availableBytes =
                        drive.AvailableFreeSpace

                    let requiredBytes =
                        config.MinDiskGb * 1024L * 1024L * 1024L

                    if availableBytes < requiredBytes then
                        // Business logic failure - insufficient disk space prevents export
                        // Return AppError so caller (Workflow.fs) can handle appropriately
                        return Error(DiskSpaceError(requiredBytes, availableBytes))
                    else
                        Log.info (sprintf "Disk space OK: %s available on %s" (Utils.formatBytes availableBytes) root)

                        return Ok()
            with ex ->
                return
                    Error(
                        FileSystemError(
                            config.OutputDirectory,
                            sprintf "Failed to check disk space: %s" (ErrorAccumulation.exceptionToString ex),
                            Some ex
                        )
                    )
        }

    let private checkMemory (config: ExportConfig) =
        async {
            Log.info "Checking memory constraints..."

            try
                let currentProcess =
                    System.Diagnostics.Process.GetCurrentProcess()

                let workingSetBytes =
                    currentProcess.WorkingSet64

                let gcMemory = GC.GetTotalMemory(false)

                let maxMemoryBytes =
                    config.MaxMemoryMb * 1024L * 1024L

                Log.info (
                    sprintf
                        "Memory usage - Managed heap: %s, Process total: %s"
                        (Utils.formatBytes gcMemory)
                        (Utils.formatBytes workingSetBytes)
                )

                // Only use working set for memory limit check since it includes all process memory
                // (managed heap + runtime + native libs + thread stacks + etc.)
                // We still log GC memory for diagnostic purposes to help identify memory issues
                if workingSetBytes > maxMemoryBytes then
                    return
                        Error(
                            MemoryError(
                                sprintf
                                    "Current memory usage exceeds limit. Current: %s, Max: %s"
                                    (Utils.formatBytes workingSetBytes)
                                    (Utils.formatBytes maxMemoryBytes)
                            )
                        )
                else
                    let availableMemory =
                        getAvailableSystemMemory ()

                    let estimatedNeed =
                        estimateExportMemoryUsage config

                    if availableMemory < estimatedNeed then
                        return
                            Error(
                                MemoryError(
                                    sprintf
                                        "Insufficient memory for export. Available: %s, Estimated need: %s"
                                        (Utils.formatBytes availableMemory)
                                        (Utils.formatBytes estimatedNeed)
                                )
                            )
                    else
                        Log.info (sprintf "Memory OK: %s available for export" (Utils.formatBytes availableMemory))
                        return Ok()
            with ex ->
                Log.error (sprintf "Failed to check memory: %s" (ErrorAccumulation.exceptionToString ex))

                return
                    Error(
                        MemoryError(
                            sprintf "Failed to perform memory check: %s" (ErrorAccumulation.exceptionToString ex)
                        )
                    )
        }

    let private checkDatabaseConnection (session: SafeSession) (breaker: Neo4j.CircuitBreaker) (config: ExportConfig) =
        async {
            Log.info "Verifying database connection..."

            let mutable testResult = None

            let processRecord (record: IRecord) =
                async { testResult <- Some(record.["test"].As<int>()) }

            let! result = Neo4j.executeQueryStreaming session breaker config "RETURN 1 as test" processRecord

            match result with
            | Ok _ ->
                if testResult = Some 1 then
                    Log.info "Database connection verified"
                    return Ok()
                else
                    Log.warn "Unexpected: test query returned unexpected result"
                    return Ok()
            | Error e -> return Error e
        }

    /// Creates output directory if it doesn't exist
    /// NOTE: This is called even though Configuration.validateOutputDirectory already created the directory.
    /// This redundancy is intentional, it is to ensure the directory exists
    /// even if the directory was deleted between configuration and export.
    /// Directory.CreateDirectory is idempotent, so this is safe.
    let initializeFileSystem (config: ExportConfig) =
        async {
            try
                if not (Directory.Exists(config.OutputDirectory)) then
                    Log.info (sprintf "Creating output directory: %s" config.OutputDirectory)

                    Directory.CreateDirectory(config.OutputDirectory)
                    |> ignore

                return Ok()
            with ex ->
                return
                    Error(
                        FileSystemError(
                            config.OutputDirectory,
                            sprintf "Failed to create output directory: %s" (ErrorAccumulation.exceptionToString ex),
                            Some ex
                        )
                    )
        }

    /// Runs all preflight checks
    let runAllChecks
        (context: ApplicationContext)
        (session: SafeSession)
        (breaker: Neo4j.CircuitBreaker)
        (config: ExportConfig)
        =
        // Computation expression syntax: 'do!' waits for async operations that return Result
        // If any check returns Error, the entire computation stops and returns that Error
        asyncResult {
            Log.info "Running preflight checks..."

            do! checkDiskSpace config
            do! checkMemory config
            do! checkDatabaseConnection session breaker config

            Log.info "All preflight checks passed"
            return ()
        }
