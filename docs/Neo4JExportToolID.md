# Neo4j Export Tool ID Specification

## Purpose

The Neo4j Export Tool (NET) generates stable, deterministic identifiers for nodes and relationships that remain consistent across:
- Multiple exports of the same database
- Different Neo4j versions (4.x, 5.x, 6.x+)
- Changes to Neo4j's internal ID formats

This enables downstream analytics systems (e.g., ClickHouse, PowerBI) to detect changes and track data snapshots across exports.

## Guiding Principles

### Identity is Content
The identifiers are **content-addressable**. Their purpose is to generate unique, deterministic signatures:
- **For nodes (`NET_node_content_hash`)**: Based solely on labels and properties
- **For relationships (`NET_rel_topology_hash`)**: Based on type, connected nodes, and properties
- If the relevant content is identical, the ID will be identical
- If the content differs in any way, the ID will be different

### Version Independence
The generation logic is completely decoupled from Neo4j's internal ID systems (`id`, `elementId`). This ensures that the ID for an element with identical content will be the same regardless of whether it is exported from Neo4j 4.x, 5.x, or 6.x+.

## Field Definitions

### For Nodes
- **Field Name**: `NET_node_content_hash`
- **Type**: String (64 characters)
- **Format**: Lowercase hexadecimal representation of SHA-256 hash
- **Example**: `a7b9c2d4e6f8901234567890abcdef1234567890abcdef1234567890abcdef12`

### For Relationships

#### Primary Identity Field
- **Field Name**: `NET_rel_identity_hash`
- **Type**: String (64 characters)
- **Format**: Lowercase hexadecimal representation of SHA-256 hash
- **Purpose**: Stable identity that only changes when the relationship itself changes (type, endpoints, or properties)
- **Example**: `b8c9d3e5f7a9012345678901bcdef2345678901bcdef2345678901bcdef234567`

#### Additional Fields
- **`start_element_id`**: Neo4j element ID of the start node
- **`end_element_id`**: Neo4j element ID of the end node
- **`start_node_content_hash`**: The `NET_node_content_hash` of the start node
- **`end_node_content_hash`**: The `NET_node_content_hash` of the end node

## Generation Rules

### For Nodes

The ID is generated using SHA-256 hash of concatenated components. This rule applies unconditionally to all nodes:

```
SHA256("node:" + sorted_labels + ":" + canonicalized_properties)
```

Where:
- `sorted_labels`: Labels sorted alphabetically and joined with "+". Empty label set results in empty string
- `canonicalized_properties`: JSON representation of properties with keys sorted alphabetically. If no properties exist, this is an empty string ("")

#### Node Examples

1. **Node with labels and properties**:
   ```
   Input: Node with labels ["Person", "Employee"] and properties {name: "John", age: 30}
   Hash Input: "node:Employee+Person:{\"age\":30,\"name\":\"John\"}"
   ```

2. **Node with only labels**:
   ```
   Input: Node with labels ["Config"] and no properties
   Hash Input: "node:Config:"
   ```

3. **Node with only properties**:
   ```
   Input: Node with no labels and properties {value: 1}
   Hash Input: "node::{\"value\":1}"
   ```

4. **Node without labels or properties**:
   ```
   Input: Node with no labels and no properties
   Hash Input: "node::"
   ```

### For Relationships

#### NET_rel_identity_hash Generation

The identity hash is generated using SHA-256 hash of the relationship's type, its properties, and the **Neo4j element IDs** of its start and end nodes:

```
SHA256("rel:" + type + ":" + start_element_id + ":" + end_element_id + ":" + canonicalized_properties)
```

Where:
- `type`: The relationship type
- `start_element_id`: The Neo4j element ID of the start node (e.g., "4:abc:123")
- `end_element_id`: The Neo4j element ID of the end node (e.g., "4:def:456")
- `canonicalized_properties`: JSON representation of properties with keys sorted alphabetically. If no properties exist, this is an empty string ("")

**Key Difference**: This hash uses Neo4j element IDs instead of content hashes, making it stable even when connected nodes' properties change.

#### Relationship Examples

1. **Relationship with properties**:
   ```
   Input: KNOWS relationship with {since: 2020} connecting:
   - Start node with element ID: "4:abc:123"
   - End node with element ID: "4:def:456"
   Hash Input: "rel:KNOWS:4:abc:123:4:def:456:{\"since\":2020}"

   Export includes:
   - NET_rel_identity_hash: (hash of above)
   - start_element_id: "4:abc:123"
   - end_element_id: "4:def:456"
   - start_node_content_hash: (content hash of start node)
   - end_node_content_hash: (content hash of end node)
   ```

2. **Relationship without properties**:
   ```
   Input: FOLLOWS relationship with no properties connecting:
   - Start node with element ID: "4:abc:789"
   - End node with element ID: "4:def:012"
   Hash Input: "rel:FOLLOWS:4:abc:789:4:def:012:"
   ```

## Canonicalization Rules

To ensure consistent hashing across exports:

### Property Canonicalization

1. **Key Ordering**: All property keys must be sorted alphabetically
2. **JSON Format**: Use compact JSON with no extra whitespace
3. **Number Format**:
   - Integers: No decimal point (30, not 30.0)
   - Floats: Minimal representation (3.14, not 3.140)
4. **String Values**: UTF-8 encoded with JSON escaping
5. **Null Values**: Omit properties with null values
6. **Arrays**: Elements maintain their order
7. **Nested Objects**: Apply same rules recursively

### Label Handling

1. Sort labels alphabetically
2. Join with "+" character
3. Empty label set represented as empty string

### Special Characters

- All strings use standard JSON escaping
- Unicode characters preserved as-is (not escaped to \uXXXX)

## Implementation Guidelines

A two-pass export process is required to implement this specification correctly:

### Two-Pass Process

1. **Pass 1: Generate Node IDs**
   - Iterate through every node in the database
   - For each node, generate its `NET_node_content_hash` using the node generation rule
   - Store a mapping of `[Native Neo4j ID] -> [Generated Node Content Hash]` in memory
   - Write the node data (including the new content hash) to the JSONL file

2. **Pass 2: Generate Relationship IDs**
   - Iterate through every relationship in the database
   - For each relationship, get the native IDs of its start and end nodes
   - Look up their generated `NET_node_content_hash` values from the mapping created in Pass 1
   - Generate the relationship's `NET_rel_identity_hash` using the Neo4j element IDs
   - Include both the element IDs and node content hashes in the output
   - Write the relationship data to the JSONL file

### F# Implementation Pattern

```fsharp
module Neo4jExportToolId

open System
open System.Security.Cryptography
open System.Text
open Newtonsoft.Json

// Configure JSON settings for canonical output
let private jsonSettings =
    let settings = JsonSerializerSettings()
    settings.NullValueHandling <- NullValueHandling.Ignore // Omits nulls
    settings

let private computeSha256 (input: string) =
    use sha256 = SHA256.Create()
    let bytes = Encoding.UTF8.GetBytes(input)
    let hash = sha256.ComputeHash(bytes)
    BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant()

let private canonicalizeProperties (properties: IDictionary<string, obj>) =
    if properties.Count = 0 then
        "" // Use empty string for nodes without properties
    else
        // Sort keys alphabetically before serializing
        let sorted = properties |> Seq.sortBy (fun kvp -> kvp.Key) |> dict
        JsonConvert.SerializeObject(sorted, Formatting.None, jsonSettings)

let generateNodeId (labels: string seq) (properties: IDictionary<string, obj>) =
    let sortedLabels = labels |> Seq.sort |> String.concat "+"
    let propsJson = canonicalizeProperties properties
    let hashInput = sprintf "node:%s:%s" sortedLabels propsJson
    computeSha256 hashInput

// Generate identity hash using element IDs (stable within database)
let generateRelationshipIdentityHash (relType: string) (startElementId: string) (endElementId: string) (properties: IDictionary<string, obj>) =
    let propsJson = canonicalizeProperties properties
    let hashInput = sprintf "rel:%s:%s:%s:%s" relType startElementId endElementId propsJson
    computeSha256 hashInput
```

## Collision Handling

- **No collision detection**: The probability of SHA-256 collision is astronomically low (1 in 2^256)
- **No special handling**: If a collision theoretically occurs, both entities would have the same ID
- **Rationale**: The computational cost of collision detection outweighs the infinitesimal risk

## Performance Considerations

1. **Hashing overhead**: SHA-256 is fast; typical overhead < 1μs per entity
2. **Memory usage**: No additional memory needed beyond string allocation
3. **Caching**: Hash results are not cached; computed on-the-fly during export
4. **Parallelization**: Hash computation is thread-safe and can be parallelized

## Backward Compatibility

- This is a new field; existing JSONL consumers can ignore it
- The original `element_id` field remains unchanged
- No breaking changes to existing format

## Downstream Implications for Analytics (ClickHouse/Power BI)

### For Nodes
- **Change Detection**: `NET_node_content_hash` changes when any property or label changes
- **Content-Based Identity**: Same content always produces the same hash across exports

### For Relationships
- **Stable Identity**: `NET_rel_identity_hash` remains constant unless the relationship itself changes (type, endpoints, or properties)
- **Node Change Detection**: Compare `start_node_content_hash` and `end_node_content_hash` across exports to detect when connected nodes change
- **Relationship Tracking**: Can track the same relationship across exports even when connected nodes' properties change
- **Complete Picture**: The combination of fields allows distinguishing between:
  - Relationship changes (identity hash changes)
  - Node changes (content hashes change but identity hash stays same)
  - Both changing (all hashes change)

## Future Considerations

1. **Version tolerance**: The scheme handles future Neo4j versions without modification since it never relies on internal IDs
2. **Property changes**: Any property change creates a new ID (by design) - this is a content-addressable system
3. **Label changes**: Adding/removing labels creates a new ID (by design)
4. **Relationship repointing**: Changing start/end nodes creates a new ID (by design)
