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
open System.Text
open System.Runtime.InteropServices

module Security =

    /// Sanitizes strings for safe logging by replacing control characters
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


    /// Validates path syntax for the current operating system
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
            | :? System.Security.SecurityException as ex ->
                Error(SecurityError(sprintf "Invalid path: %s" (ErrorAccumulation.exceptionToString ex)))
