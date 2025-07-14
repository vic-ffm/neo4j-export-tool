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

module Neo4jExport.Tests.Unit.Export.Serialization.GraphElementsTests

open System
open System.Collections.Generic
open System.Text.Json
open Expecto
open Swensen.Unquote
open Neo4jExport
open Neo4jExport.ExportTypes
open Neo4jExport.SerializationGraphElements
open Neo4jExport.Tests.Helpers.TestHelpers
open Neo4j.Driver
open Neo4jExport.SerializationEngine

// Ensure SerializationEngine module is initialized
let private ensureEngineInitialized =
    // Explicitly initialize the serialization engine
    initializeSerializationEngine ()

[<Tests>]
let tests =
    testList
        "Serialization - Graph Elements"
        [

          testList
              "Node serialization"
              [ testCase "serializes node with writeNodeDirect"
                <| fun () ->
                    let buffer, writer, context =
                        createTestWriterContext ()
                    // ArrayBufferWriter is not IDisposable
                    use _ = writer

                    let elementId = "element:123"

                    let stableId =
                        "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789" // 64-char hash

                    let labels =
                        [ "Person"; "Employee" ] :> IReadOnlyList<string>

                    let props = Dictionary<string, obj>()
                    props["name"] <- box "Alice"
                    props["age"] <- box 30L

                    let properties =
                        props :> IReadOnlyDictionary<string, obj>

                    writer.WriteStartObject()
                    writer.WritePropertyName("value")
                    writeNodeDirect writer elementId stableId labels properties context
                    writer.WriteEndObject()
                    writer.Flush()

                    let json =
                        System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan.ToArray())

                    match validateJson json with
                    | Ok doc ->
                        let obj = getJsonValue doc

                        let obj__type_str =
                            obj.GetProperty("type").GetString()

                        test <@ obj__type_str = "node" @>

                        let obj__element_id_str =
                            obj.GetProperty("element_id").GetString()

                        test <@ obj__element_id_str = "element:123" @>

                        let exportIdKind =
                            obj.GetProperty("export_id").ValueKind

                        test <@ exportIdKind <> JsonValueKind.Null @>

                        let labels = obj.GetProperty("labels")
                        let labelsLength = labels.GetArrayLength()
                        test <@ labelsLength = 2 @>

                        let properties =
                            obj.GetProperty("properties")

                        let properties__name_str =
                            properties.GetProperty("name").GetString()

                        test <@ properties__name_str = "Alice" @>

                        let age =
                            properties.GetProperty("age").GetInt64()

                        test <@ age = 30L @>

                        // When EnableHashedIds = true (default in test config)
                        let hashId =
                            obj.GetProperty("NET_node_content_hash").GetString()

                        test <@ hashId = stableId @>
                    | Error msg -> failtest msg

                testCase "serializes node without hash ID"
                <| fun () ->
                    let buffer, writer, context =
                        createTestWriterContext ()
                    // ArrayBufferWriter is not IDisposable
                    use _ = writer

                    // Disable hash IDs
                    let noHashContext =
                        { context with
                            Config =
                                { context.Config with
                                    EnableHashedIds = false } }

                    let elementId = "element:1"
                    let stableId = "" // Empty when hashing disabled

                    let labels =
                        [ "Entity" ] :> IReadOnlyList<string>

                    let props = Dictionary<string, obj>()
                    props["id"] <- box 1L

                    let properties =
                        props :> IReadOnlyDictionary<string, obj>

                    writer.WriteStartObject()
                    writer.WritePropertyName("value")
                    writeNodeDirect writer elementId stableId labels properties noHashContext
                    writer.WriteEndObject()
                    writer.Flush()

                    let json =
                        System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan.ToArray())

                    match validateJson json with
                    | Ok doc ->
                        let obj = getJsonValue doc

                        let mutable hashProp =
                            Unchecked.defaultof<JsonElement>

                        let hasHashProp =
                            obj.TryGetProperty("NET_node_content_hash", &hashProp)

                        test <@ hasHashProp = false @>
                    | Error msg -> failtest msg

                testCase "serializes node with empty labels"
                <| fun () ->
                    let buffer, writer, context =
                        createTestWriterContext ()
                    // ArrayBufferWriter is not IDisposable
                    use _ = writer

                    let elementId = "element:1"

                    let stableId =
                        "0000000000000000000000000000000000000000000000000000000000000000"

                    let labels = [] :> IReadOnlyList<string>

                    let properties =
                        Dictionary<string, obj>() :> IReadOnlyDictionary<string, obj>

                    writer.WriteStartObject()
                    writer.WritePropertyName("value")
                    writeNodeDirect writer elementId stableId labels properties context
                    writer.WriteEndObject()
                    writer.Flush()

                    let json =
                        System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan.ToArray())

                    match validateJson json with
                    | Ok doc ->
                        let obj = getJsonValue doc
                        let labels = obj.GetProperty("labels")
                        let labelsLength = labels.GetArrayLength()
                        test <@ labelsLength = 0 @>
                    | Error msg -> failtest msg

                testCase "serializes node with many labels truncated"
                <| fun () ->
                    let buffer, writer, context =
                        createTestWriterContext ()
                    // ArrayBufferWriter is not IDisposable
                    use _ = writer

                    // Create more labels than the limit
                    let labels =
                        [ for i in 1..20 do
                              yield $"Label{i}" ]
                        :> IReadOnlyList<string>

                    let elementId = "element:1"

                    let stableId =
                        "0000000000000000000000000000000000000000000000000000000000000000"

                    let properties =
                        Dictionary<string, obj>() :> IReadOnlyDictionary<string, obj>

                    writer.WriteStartObject()
                    writer.WritePropertyName("value")
                    writeNodeDirect writer elementId stableId labels properties context
                    writer.WriteEndObject()
                    writer.Flush()

                    let json =
                        System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan.ToArray())

                    match validateJson json with
                    | Ok doc ->
                        let obj = getJsonValue doc
                        let labelsArray = obj.GetProperty("labels")
                        // Should be truncated to MaxLabelsPerNode (10 by default)
                        let labelsArrayLength =
                            labelsArray.GetArrayLength()

                        test <@ labelsArrayLength = 10 @>
                    | Error msg -> failtest msg

                testCase "serializes node with special characters in properties"
                <| fun () ->
                    let buffer, writer, context =
                        createTestWriterContext ()
                    // ArrayBufferWriter is not IDisposable
                    use _ = writer

                    let elementId = "element:1"

                    let stableId =
                        "0000000000000000000000000000000000000000000000000000000000000000"

                    let labels =
                        [ "Unicode" ] :> IReadOnlyList<string>

                    let props = Dictionary<string, obj>()
                    props["name"] <- box "名前"
                    props["emoji"] <- box "🌍"
                    props["special"] <- box "line1\nline2\ttab"

                    let properties =
                        props :> IReadOnlyDictionary<string, obj>

                    writer.WriteStartObject()
                    writer.WritePropertyName("value")
                    writeNodeDirect writer elementId stableId labels properties context
                    writer.WriteEndObject()
                    writer.Flush()

                    let json =
                        System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan.ToArray())

                    match validateJson json with
                    | Ok doc ->
                        let obj = getJsonValue doc
                        let props = obj.GetProperty("properties")

                        let props__name_str =
                            props.GetProperty("name").GetString()

                        test <@ props__name_str = "名前" @>

                        let props__emoji_str =
                            props.GetProperty("emoji").GetString()

                        test <@ props__emoji_str = "🌍" @>

                        let props__special_str =
                            props.GetProperty("special").GetString()

                        test <@ props__special_str = "line1\nline2\ttab" @>
                    | Error msg -> failtest msg ]

          testList
              "Relationship serialization"
              [ testCase "serializes simple relationship"
                <| fun () ->
                    let buffer, writer, context =
                        createTestWriterContext ()
                    // ArrayBufferWriter is not IDisposable
                    use _ = writer

                    let props = Dictionary<string, obj>()
                    props["since"] <- box 2020L
                    props["strength"] <- box 0.8

                    let properties =
                        props :> IReadOnlyDictionary<string, obj>

                    let ids =
                        { ElementId = "element:456"
                          StableId = "rel_hash_123"
                          StartElementId = "element:123"
                          StartStableId = "start_hash_123"
                          EndElementId = "element:789"
                          EndStableId = "end_hash_789" }

                    writer.WriteStartObject()
                    writer.WritePropertyName("value")
                    writeRelationshipDirect writer "KNOWS" properties ids context
                    writer.WriteEndObject()
                    writer.Flush()

                    let json =
                        System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan.ToArray())

                    match validateJson json with
                    | Ok doc ->
                        let obj = getJsonValue doc

                        let obj__type_str =
                            obj.GetProperty("type").GetString()

                        test <@ obj__type_str = "relationship" @>

                        let obj__element_id_str =
                            obj.GetProperty("element_id").GetString()

                        test <@ obj__element_id_str = "element:456" @>

                        let obj__label_str =
                            obj.GetProperty("label").GetString()

                        test <@ obj__label_str = "KNOWS" @>

                        let obj__start_element_id_str =
                            obj.GetProperty("start_element_id").GetString()

                        test <@ obj__start_element_id_str = "element:123" @>

                        let obj__end_element_id_str =
                            obj.GetProperty("end_element_id").GetString()

                        test <@ obj__end_element_id_str = "element:789" @>

                        let properties =
                            obj.GetProperty("properties")

                        let since =
                            properties.GetProperty("since").GetInt64()

                        test <@ since = 2020L @>

                        let properties__strength_dbl =
                            properties.GetProperty("strength").GetDouble()

                        test <@ properties__strength_dbl = 0.8 @>

                        // When EnableHashedIds = true
                        let obj__net_rel_identity_hash_str =
                            obj.GetProperty("NET_rel_identity_hash").GetString()

                        test <@ obj__net_rel_identity_hash_str = "rel_hash_123" @>

                        let obj__start_node_content_hash_str =
                            obj.GetProperty("start_node_content_hash").GetString()

                        test <@ obj__start_node_content_hash_str = "start_hash_123" @>

                        let obj__end_node_content_hash_str =
                            obj.GetProperty("end_node_content_hash").GetString()

                        test <@ obj__end_node_content_hash_str = "end_hash_789" @>
                    | Error msg -> failtest msg

                testCase "serializes relationship with empty properties"
                <| fun () ->
                    let buffer, writer, context =
                        createTestWriterContext ()
                    // ArrayBufferWriter is not IDisposable
                    use _ = writer

                    let properties =
                        Dictionary<string, obj>() :> IReadOnlyDictionary<string, obj>

                    let ids =
                        { ElementId = "element:1"
                          StableId = ""
                          StartElementId = "element:1"
                          StartStableId = ""
                          EndElementId = "element:2"
                          EndStableId = "" }

                    writer.WriteStartObject()
                    writer.WritePropertyName("value")
                    writeRelationshipDirect writer "RELATES_TO" properties ids context
                    writer.WriteEndObject()
                    writer.Flush()

                    let json =
                        System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan.ToArray())

                    match validateJson json with
                    | Ok doc ->
                        let obj = getJsonValue doc

                        let properties =
                            obj.GetProperty("properties")

                        let propertiesIsEmpty =
                            properties.EnumerateObject() |> Seq.isEmpty

                        test <@ propertiesIsEmpty @>
                    | Error msg -> failtest msg

                testCase "serializes relationship without hash IDs"
                <| fun () ->
                    let buffer, writer, context =
                        createTestWriterContext ()
                    // ArrayBufferWriter is not IDisposable
                    use _ = writer

                    // Disable hash IDs
                    let noHashContext =
                        { context with
                            Config =
                                { context.Config with
                                    EnableHashedIds = false } }

                    let properties =
                        Dictionary<string, obj>() :> IReadOnlyDictionary<string, obj>

                    let ids =
                        { ElementId = "element:1"
                          StableId = ""
                          StartElementId = "element:1"
                          StartStableId = ""
                          EndElementId = "element:2"
                          EndStableId = "" }

                    writer.WriteStartObject()
                    writer.WritePropertyName("value")
                    writeRelationshipDirect writer "CONNECTS" properties ids noHashContext
                    writer.WriteEndObject()
                    writer.Flush()

                    let json =
                        System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan.ToArray())

                    match validateJson json with
                    | Ok doc ->
                        let obj = getJsonValue doc

                        let mutable relHashProp =
                            Unchecked.defaultof<JsonElement>

                        let hasRelHashProp =
                            obj.TryGetProperty("NET_rel_identity_hash", &relHashProp)

                        test <@ hasRelHashProp = false @>

                        let mutable startHashProp =
                            Unchecked.defaultof<JsonElement>

                        let hasStartHashProp =
                            obj.TryGetProperty("start_node_content_hash", &startHashProp)

                        test <@ hasStartHashProp = false @>

                        let mutable endHashProp =
                            Unchecked.defaultof<JsonElement>

                        let hasEndHashProp =
                            obj.TryGetProperty("end_node_content_hash", &endHashProp)

                        test <@ hasEndHashProp = false @>
                    | Error msg -> failtest msg ] ]
