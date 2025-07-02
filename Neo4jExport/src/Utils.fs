namespace Neo4jExport

open System
open System.IO
open System.Security.Cryptography
open System.Reflection
open System.Threading.Tasks

/// Common utility functions
module Utils =
    let getScriptChecksum (context: ApplicationContext) : Task<string> =
        task {
            try
                let assembly =
                    Assembly.GetExecutingAssembly()

                use stream =
                    File.OpenRead(assembly.Location)

                let! hash = SHA256.HashDataAsync(stream, AppContext.getCancellationToken context)
                return BitConverter.ToString(hash).Replace("-", "").ToLower()
            with
            | :? IOException as ex ->
                Log.warn (sprintf "Could not compute script checksum due to I/O error: %s" ex.Message)
                return "unknown"
            | :? UnauthorizedAccessException as ex ->
                Log.warn (sprintf "Could not compute script checksum due to permissions error: %s" ex.Message)
                return "unknown"
            | :? OperationCanceledException ->
                Log.debug "Script checksum computation was cancelled"
                return "unknown"
            | ex ->
                Log.error (
                    sprintf
                        "An unexpected error occurred while computing script checksum: %s (%s)"
                        ex.Message
                        (ex.GetType().Name)
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
            let dir = Path.GetDirectoryName(path)

            if not (String.IsNullOrWhiteSpace(dir)) then
                Directory.CreateDirectory(dir) |> ignore

            Result.Ok()
        with
        | :? ArgumentException as ex ->
            Result.Error(FileSystemError(path, sprintf "Invalid path format: %s" ex.Message, Some ex))
        | :? PathTooLongException as ex -> Result.Error(FileSystemError(path, "Path exceeds system limits", Some ex))
        | :? DirectoryNotFoundException as ex ->
            Result.Error(FileSystemError(path, "Parent directory not found", Some ex))
        | :? IOException as ex ->
            Result.Error(FileSystemError(path, sprintf "I/O error creating directory: %s" ex.Message, Some ex))
        | :? UnauthorizedAccessException as ex ->
            Result.Error(FileSystemError(path, "Insufficient permissions to create directory", Some ex))
        | :? NotSupportedException as ex ->
            Result.Error(FileSystemError(path, sprintf "Path format not supported: %s" ex.Message, Some ex))
