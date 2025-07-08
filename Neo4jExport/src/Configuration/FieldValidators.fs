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
open Neo4jExport.ConfigurationValidationHelpers

module internal ConfigurationFieldValidators =

    let validateUri (uriStr: string) : Result<ValidatedField, string> =
        try
            let uri = Uri(uriStr)

            if
                uri.Scheme = "bolt"
                || uri.Scheme = "neo4j"
                || uri.Scheme = "bolt+s"
                || uri.Scheme = "neo4j+s"
            then
                Ok(VUri uri)
            else
                Error "URI scheme must be one of: bolt, neo4j, bolt+s, neo4j+s"
        with ex ->
            Error(sprintf "Invalid URI: %s" ex.Message)

    let validateOutputDirectory (path: string) : Result<ValidatedField, string> =
        match Security.validatePathSyntax path with
        | Error _ -> Error "Invalid path syntax"
        | Ok validPath ->
            match Utils.ensureDirectoryExists validPath with
            | Error _ -> Error "Cannot create or access output directory"
            | Ok() -> Ok(VString validPath)

    /// Generic integer validation with range
    let validateInt
        (name: string)
        (min: int option)
        (max: int option)
        (value: string)
        : Result<ValidatedField, string> =
        match Int32.TryParse(value) with
        | false, _ -> Error(sprintf "%s must be a valid integer (got: '%s')" name value)
        | true, parsed ->
            match min, max with
            | Some minVal, _ when parsed < minVal ->
                Error(sprintf "%s must be at least %d (got: %d)" name minVal parsed)
            | _, Some maxVal when parsed > maxVal -> Error(sprintf "%s must be at most %d (got: %d)" name maxVal parsed)
            | _ -> Ok(VInt parsed)

    let validateInt64
        (name: string)
        (min: int64 option)
        (max: int64 option)
        (value: string)
        : Result<ValidatedField, string> =
        match Int64.TryParse(value) with
        | false, _ -> Error(sprintf "%s must be a valid integer (got: '%s')" name value)
        | true, parsed ->
            match min, max with
            | Some minVal, _ when parsed < minVal ->
                Error(sprintf "%s must be at least %d (got: %d)" name minVal parsed)
            | _, Some maxVal when parsed > maxVal -> Error(sprintf "%s must be at most %d (got: %d)" name maxVal parsed)
            | _ -> Ok(VInt64 parsed)

    let validateBool (name: string) (value: string) : Result<ValidatedField, string> =
        match value.ToLowerInvariant() with
        | "true"
        | "yes"
        | "1" -> Ok(VBool true)
        | "false"
        | "no"
        | "0" -> Ok(VBool false)
        | _ -> Error(sprintf "%s must be a valid boolean (true/false, yes/no, 1/0) (got: '%s')" name value)

    /// Validates JSON buffer size with KB-specific constraints
    let validateJsonBufferSize (value: string) : Result<ValidatedField, string> =
        match validateInt "JSON_BUFFER_SIZE_KB" (Some 1) (Some 1024) value with
        | Error e -> Error e
        | Ok(VInt size) -> Ok(VInt size)
        | _ -> Error "Internal validation error"
