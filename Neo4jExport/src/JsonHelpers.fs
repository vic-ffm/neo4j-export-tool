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

    /// Extract string value from JsonValue - strict version
    let tryGetString (value: JsonValue) : string option =
        match value with
        | JString s -> Some s
        | _ -> None

    /// Convert any JsonValue to string representation - explicit coercion
    let asString (value: JsonValue) : string =
        match value with
        | JString s -> s
        | JNumber n -> n.ToString()
        | JBool b -> if b then "true" else "false"
        | JNull -> ""
        | JObject _ -> "{...}"
        | JArray _ -> "[...]"

    /// Try to convert JsonValue to string with coercion
    let tryCoerceToString (value: JsonValue) : string option =
        match value with
        | JString s -> Some s
        | JNumber n -> Some(n.ToString())
        | JBool b -> Some(if b then "true" else "false")
        | JNull -> None
        | _ -> None

    /// Extract int64 value from JsonValue
    let tryGetInt64 (value: JsonValue) : int64 option =
        match value with
        | JNumber n ->
            try
                Some(Decimal.ToInt64 n)
            with :? OverflowException ->
                None
        | _ -> None

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
