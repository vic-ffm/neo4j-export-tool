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
