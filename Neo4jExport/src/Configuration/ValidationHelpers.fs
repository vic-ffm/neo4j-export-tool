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

module internal ConfigurationValidationHelpers =
    /// Wrapper type to preserve type information through validation
    /// This discriminated union ensures validated values maintain their types
    /// throughout the validation pipeline rather than becoming generic objects
    type ValidatedField =
        | VUri of Uri
        | VString of string
        | VInt of int
        | VInt64 of int64
        | VBool of bool

    /// Run all validations and collect errors
    let validateAll
        (validations: (string * Result<ValidatedField, string>) list)
        : Result<Map<string, ValidatedField>, string list> =
        // First collect all errors using List.choose to filter out Ok results
        let errors =
            validations
            |> List.choose (fun (_, result) ->
                match result with
                | Error e -> Some e
                | _ -> None)

        // Only build the success map if there are no errors
        // This ensures all-or-nothing validation semantics
        if List.isEmpty errors then
            validations
            |> List.choose (fun (name, result) ->
                match result with
                | Ok value -> Some(name, value)
                | _ -> None)
            |> Map.ofList
            |> Ok
        else
            Error errors

module internal FieldExtractors =
    open ConfigurationValidationHelpers

    /// Extracts a Uri from the validated fields map, failing if the key is missing or has wrong type
    /// This pattern ensures compile-time type safety when building the configuration
    let getUri (fields: Map<string, ValidatedField>) (key: string) : Uri =
        // The failwith is intentional here - these should never fail in production
        // because validateAll ensures all required fields exist with correct types
        match fields.[key] with
        | VUri uri -> uri
        | _ -> failwith (sprintf "Invalid field type for %s" key)

    let getString (fields: Map<string, ValidatedField>) (key: string) : string =
        match fields.[key] with
        | VString s -> s
        | _ -> failwith (sprintf "Invalid field type for %s" key)

    let getInt (fields: Map<string, ValidatedField>) (key: string) : int =
        match fields.[key] with
        | VInt i -> i
        | _ -> failwith (sprintf "Invalid field type for %s" key)

    let getInt64 (fields: Map<string, ValidatedField>) (key: string) : int64 =
        match fields.[key] with
        | VInt64 i -> i
        | _ -> failwith (sprintf "Invalid field type for %s" key)

    let getBool (fields: Map<string, ValidatedField>) (key: string) : bool =
        match fields.[key] with
        | VBool b -> b
        | _ -> failwith (sprintf "Invalid field type for %s" key)
