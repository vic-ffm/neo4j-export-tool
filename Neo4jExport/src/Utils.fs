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
open System.Security.Cryptography
open System.Reflection
open System.Threading.Tasks

module Utils =
    let getScriptChecksum (cancellationToken: System.Threading.CancellationToken) : Task<string> =
        task {
            try
                let assembly =
                    Assembly.GetExecutingAssembly()

                use stream =
                    File.OpenRead(assembly.Location)

                let! hash = SHA256.HashDataAsync(stream, cancellationToken)
                return BitConverter.ToString(hash).Replace("-", "").ToLower()
            with
            | :? IOException as ex ->
                Log.warn (
                    sprintf
                        "Could not compute script checksum due to I/O error: %s"
                        (ErrorAccumulation.exceptionToString ex)
                )

                return "unknown"
            | :? UnauthorizedAccessException as ex ->
                Log.warn (
                    sprintf
                        "Could not compute script checksum due to permissions error: %s"
                        (ErrorAccumulation.exceptionToString ex)
                )

                return "unknown"
            | :? OperationCanceledException ->
                Log.debug "Script checksum computation was cancelled"
                return "unknown"
            | ex ->
                Log.error (
                    sprintf
                        "An unexpected error occurred while computing script checksum: %s"
                        (ErrorAccumulation.exceptionToString ex)
                )

                return "unknown"
        }

    let getEnvVar name defaultValue =
        match Environment.GetEnvironmentVariable(name) with
        | null
        | "" -> defaultValue
        | value -> value

    let formatBytes (bytes: int64) =
        let units =
            [| "B"; "KB"; "MB"; "GB"; "TB"; "PB" |]

        let rec findUnit (size: decimal) unitIndex =
            if size >= 1024.0m && unitIndex < units.Length - 1 then
                findUnit (size / 1024.0m) (unitIndex + 1)
            else
                let roundedSize = Decimal.Round(size, 2)
                sprintf "%M %s" roundedSize units.[unitIndex]

        if bytes < 0L then
            "Invalid (negative size)"
        else
            findUnit (decimal bytes) 0

    let ensureDirectoryExists (path: string) =
        try
            if not (String.IsNullOrWhiteSpace(path)) then
                Directory.CreateDirectory(path) |> ignore

            Result.Ok()
        with
        | :? ArgumentException as ex ->
            Result.Error(
                FileSystemError(
                    path,
                    sprintf "Invalid path format: %s" (ErrorAccumulation.exceptionToString ex),
                    Some ex
                )
            )
        | :? PathTooLongException as ex -> Result.Error(FileSystemError(path, "Path exceeds system limits", Some ex))
        | :? DirectoryNotFoundException as ex ->
            Result.Error(FileSystemError(path, "Parent directory not found", Some ex))
        | :? IOException as ex ->
            Result.Error(
                FileSystemError(
                    path,
                    sprintf "I/O error creating directory: %s" (ErrorAccumulation.exceptionToString ex),
                    Some ex
                )
            )
        | :? UnauthorizedAccessException as ex ->
            Result.Error(FileSystemError(path, "Insufficient permissions to create directory", Some ex))
        | :? NotSupportedException as ex ->
            Result.Error(
                FileSystemError(
                    path,
                    sprintf "Path format not supported: %s" (ErrorAccumulation.exceptionToString ex),
                    Some ex
                )
            )
