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

/// Helper functions for working with JsonValue type
module JsonHelpers =
    /// Convert arbitrary objects to type-safe JsonValue with proper error handling
    let rec toJsonValue (obj: obj) : Result<JsonValue, string> =
        match obj with
        | null -> Ok JNull
        | :? string as s -> Ok(JString s)
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
            d
            |> Seq.map (fun kvp ->
                match toJsonValue kvp.Value with
                | Ok v -> Ok(kvp.Key, v)
                | Error e -> Error e)
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

    /// Add a convenience function for backward compatibility
    /// WARNING: This will throw an exception for unsupported types
    let toJsonValueUnsafe (obj: obj) : JsonValue =
        match toJsonValue obj with
        | Ok value -> value
        | Error msg -> failwith (sprintf "JSON conversion error: %s" msg)

    /// Extract string value from JsonValue - Result version
    let tryGetString (value: JsonValue) : Result<string, string> =
        match value with
        | JString s -> Ok s
        | _ -> Error "Value is not a string"


    /// Extract int64 value from JsonValue - Result version
    let tryGetInt64 (value: JsonValue) : Result<int64, string> =
        match value with
        | JNumber n ->
            try
                Ok(int64 n)
            with _ ->
                Error "Cannot convert number to int64"
        | _ -> Error "Value is not a number"

    /// Convert JsonValue back to obj for JSON serialization
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

    /// Write JsonValue directly to Utf8JsonWriter for optimal performance
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
