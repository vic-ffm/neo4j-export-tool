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
open System.Collections
open System.Collections.Generic
open System.Text.Json
open Neo4j.Driver

/// Helper functions for working with JsonValue type - FOR METADATA COLLECTION ONLY!
/// WARNING: These functions are designed for metadata serialization where JSON fidelity
/// matters more than .NET type fidelity. All numbers are converted to decimal.
/// DO NOT use these for actual Neo4j data export - use Utf8JsonWriter directly instead.
module JsonHelpers =
    /// Convert arbitrary objects to JsonValue for METADATA ONLY.
    /// NOTE: All numeric types are converted to decimal for JSON compatibility.
    /// This intentionally loses .NET type information but preserves JSON numeric fidelity.
    let rec toJsonValue (obj: obj) : Result<JsonValue, string> =
        match obj with
        | null -> Ok JNull
        | :? string as s -> Ok(JString s)
        // Converting all numeric types to decimal ensures consistent JSON number representation
        // JSON doesn't distinguish between int/float/decimal, so this unification prevents
        // type-related serialization issues while maintaining numeric precision
        | :? int as i -> Ok(JNumber(decimal i))
        | :? int64 as l -> Ok(JNumber(decimal l))
        | :? decimal as d -> Ok(JNumber d)
        | :? float as f -> Ok(JNumber(decimal f))
        | :? bool as b -> Ok(JBool b)
        | :? DateTime as dt -> Ok(JString(dt.ToString("O")))
        | :? DateTimeOffset as dto -> Ok(JString(dto.ToString("O")))
        | :? TimeSpan as ts -> Ok(JString(ts.ToString()))
        | :? Guid as g -> Ok(JString(g.ToString()))
        | :? Point as p ->
            Ok(
                JObject(
                    dict
                        [ "type", JString "Point"
                          "coordinates",
                          JArray
                              [ JNumber(decimal p.X)
                                JNumber(decimal p.Y) ]
                          "srid", JNumber(decimal p.SrId) ]
                )
            )
        | :? LocalDate as ld -> Ok(JString(ld.ToString()))
        | :? LocalTime as lt -> Ok(JString(lt.ToString()))
        | :? LocalDateTime as ldt -> Ok(JString(ldt.ToString()))
        | :? ZonedDateTime as zdt -> Ok(JString(zdt.ToString()))
        | :? Duration as dur -> Ok(JString(dur.ToString()))
        | :? OffsetTime as ot -> Ok(JString(ot.ToString()))
        | :? IDictionary<string, obj> as d ->
            // Transform dictionary entries while preserving error information
            // Each value conversion can fail, so we collect Results
            d
            |> Seq.map (fun kvp ->
                match toJsonValue kvp.Value with
                | Ok v -> Ok(kvp.Key, v)
                | Error e -> Error e)
            // fold accumulates Results, short-circuiting on first error
            // This ensures we either convert all values or report the first failure
            |> Seq.fold
                (fun acc curr ->
                    match acc, curr with
                    | Ok items, Ok item -> Ok(item :: items)
                    | Error e, _ -> Error e
                    | _, Error e -> Error e)
                (Ok [])
            |> Result.map (fun items -> JObject(dict (List.rev items)))
        | :? IEnumerable as list ->
            list
            |> Seq.cast<obj>
            |> Seq.map toJsonValue
            |> Seq.fold
                (fun acc curr ->
                    match acc, curr with
                    | Ok items, Ok item -> Ok(item :: items)
                    | Error e, _ -> Error e
                    | _, Error e -> Error e)
                (Ok [])
            |> Result.map (List.rev >> JArray)
        | _ -> Error(sprintf "Unsupported type for JSON conversion: %s" (obj.GetType().FullName))

    /// Convert arbitrary objects to type-safe JsonValue with a default fallback.
    /// This avoids exceptions for non-critical conversions while providing error visibility.
    let toJsonValueWithDefault (defaultValue: JsonValue) (logWarning: string -> unit) (obj: obj) : JsonValue =
        match toJsonValue obj with
        | Ok value -> value
        | Error msg ->
            logWarning (
                sprintf
                    "JSON conversion failed for type '%s', using default value. Error: %s"
                    (obj.GetType().FullName)
                    msg
            )

            defaultValue

    let tryGetString (value: JsonValue) : Result<string, string> =
        match value with
        | JString s -> Ok s
        | _ -> Error "Value is not a string"


    let tryGetInt64 (value: JsonValue) : Result<int64, string> =
        match value with
        | JNumber n ->
            try
                Ok(int64 (Math.Round(float n)))
            with _ ->
                Error "Cannot convert number to int64"
        | _ -> Error "Value is not a number"

    /// Convert JsonValue back to obj for JSON serialization - FOR METADATA ONLY!
    /// WARNING: All JNumber values return as decimal, regardless of original type.
    /// This is intentional for metadata serialization but unsuitable for type-safe data export.
    let rec fromJsonValue (value: JsonValue) : obj =
        match value with
        | JNull -> null
        | JString s -> box s
        | JNumber n -> box n
        | JBool b -> box b
        | JObject d ->
            d
            |> Seq.map (fun kvp -> kvp.Key, fromJsonValue kvp.Value)
            |> dict
            |> box
        | JArray list ->
            list
            |> List.map fromJsonValue
            |> List.toArray
            |> box

    // Recursively serializes JsonValue tree structure to Utf8JsonWriter
    // The 'rec' keyword enables self-referential calls for nested objects/arrays
    let rec writeJsonValue (writer: Utf8JsonWriter) (value: JsonValue) =
        match value with
        | JNull -> writer.WriteNullValue()
        | JString s -> writer.WriteStringValue(s)
        | JNumber n -> writer.WriteNumberValue(n)
        | JBool b -> writer.WriteBooleanValue(b)
        | JObject dict ->
            writer.WriteStartObject()

            for kvp in dict do
                writer.WritePropertyName(kvp.Key)
                writeJsonValue writer kvp.Value

            writer.WriteEndObject()
        | JArray list ->
            writer.WriteStartArray()

            for item in list do
                writeJsonValue writer item

            writer.WriteEndArray()
