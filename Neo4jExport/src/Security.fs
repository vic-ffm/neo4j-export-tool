namespace Neo4jExport

open System
open System.IO
open System.Text
open System.Runtime.InteropServices

/// Security validation and sanitization
module Security =

    /// Sanitizes strings for safe logging by replacing ALL control characters
    let sanitizeForLogging (str: string) (maxLength: int) : string =
        if String.IsNullOrEmpty(str) then
            "<empty>"
        else
            let sb =
                StringBuilder(min str.Length maxLength)

            let mutable charCount = 0

            for c in str do
                if charCount >= maxLength then
                    ()
                elif Char.IsControl(c) then
                    match c with
                    | '\r' -> sb.Append("\\r") |> ignore
                    | '\n' -> sb.Append("\\n") |> ignore
                    | '\t' -> sb.Append("\\t") |> ignore
                    | '\000' -> sb.Append("\\0") |> ignore
                    | _ -> sb.AppendFormat("\\u{0:X4}", int c) |> ignore

                    charCount <- charCount + 1
                else
                    sb.Append(c) |> ignore
                    charCount <- charCount + 1

            if str.Length > maxLength then
                sb.Append("...").ToString()
            else
                sb.ToString()

    /// Platform-specific path validation
    let private validatePlatformSpecific (path: string) : Result<unit, AppError> =
        if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
            Ok()
        else if path.StartsWith("~") then
            Error(SecurityError "Path contains shell expansion character (~)")
        else
            Ok()

    /// Validates that a path is within the allowed base directory
    let validatePath (path: string) (allowedBaseDir: string) : Result<string, AppError> =
        try
            // Normalize both paths to prevent TOCTOU attacks
            let fullPath = Path.GetFullPath(path)

            let fullBaseDir =
                Path.GetFullPath(allowedBaseDir)

            match validatePlatformSpecific path with
            | Error e -> Error e
            | Ok() ->
                if fullPath.StartsWith(fullBaseDir, StringComparison.OrdinalIgnoreCase) then
                    Ok fullPath
                else
                    Error(SecurityError "Path is outside the allowed directory")
        with
        | :? ArgumentException
        | :? ArgumentNullException
        | :? NotSupportedException
        | :? PathTooLongException
        | :? System.Security.SecurityException as ex -> Error(SecurityError(sprintf "Invalid path: %s" ex.Message))

    /// Validates that a path is syntactically valid for the OS
    let validatePathSyntax (path: string) : Result<string, AppError> =
        if String.IsNullOrWhiteSpace(path) then
            Error(SecurityError "Path cannot be empty")
        else
            try
                Ok(Path.GetFullPath(path))
            with
            | :? ArgumentException
            | :? ArgumentNullException
            | :? NotSupportedException
            | :? PathTooLongException
            | :? System.Security.SecurityException as ex -> Error(SecurityError(sprintf "Invalid path: %s" ex.Message))

    /// Sanitizes a filename by removing invalid characters
    let sanitizeFilename (filename: string) : Result<string, AppError> =
        if String.IsNullOrWhiteSpace(filename) then
            Error(SecurityError "Filename cannot be empty")
        else
            try
                let invalidChars =
                    Path.GetInvalidFileNameChars()

                let sanitized =
                    filename.ToCharArray()
                    |> Array.map (fun c -> if Array.contains c invalidChars then '_' else c)
                    |> String

                let cleaned = sanitized.Trim([| '.'; ' ' |])

                if String.IsNullOrWhiteSpace(cleaned) then
                    Error(SecurityError "Filename contains only invalid characters")
                else
                    Ok cleaned
            with ex ->
                Error(SecurityError(sprintf "Failed to sanitize filename: %s" ex.Message))
