# Neo4j Export Tool ID Specification

## Purpose

Version 0.14.0 of the Neo4j Export Tool introduced the following deterministic identifiers for nodes and relationships to enable:
- Change detection across multiple exports of the same database
- Tracking of data lineage and history in downstream analytics systems
- Content-based addressing for deduplication and data quality

**Note**: Hash ID generation can be disabled by setting `N4JET_ENABLE_HASHED_IDS=false` for improved performance when these features are not needed.

## Overview of ID Types

The tool generates two distinct types of hashes with different purposes:

1. **`NET_node_content_hash`** - A content-based hash for nodes based on their labels and properties
2. **`NET_rel_identity_hash`** - An identity hash for relationships based on their type, endpoints, and properties

**Note**: While `NET_node_content_hash` is content-based and version independent, `NET_rel_identity_hash` depends on Neo4j's internal element IDs, making it version-dependent between Neo4j 4.x and 5.x+.

## NET_node_content_hash

### Purpose
Provides a stable, content-addressable identifier for nodes that remains consistent when:
- The same node (same labels and properties) is exported multiple times
- The node is exported from different Neo4j versions
- The node's internal ID changes (e.g., after database rebuild)

### Field Details
- **Field Name**: `NET_node_content_hash`
- **Type**: String (64 characters)
- **Format**: Lowercase hexadecimal SHA-256 hash
- **Example**: `a7b9c2d4e6f8901234567890abcdef1234567890abcdef1234567890abcdef12`

### Generation Algorithm

```
SHA256("node:" + sorted_labels + ":" + canonicalized_properties)
```

Where:
- `sorted_labels`: Labels sorted alphabetically and joined with "+". Empty label set results in empty string
- `canonicalized_properties`: JSON representation with keys sorted alphabetically. Empty object becomes empty string

### Examples

1. **Node with labels and properties**:
   ```
   Input: Node with labels ["Person", "Employee"] and properties {name: "John", age: 30}
   Hash Input: "node:Employee+Person:{"age":30,"name":"John"}"
   ```

2. **Node with only labels**:
   ```
   Input: Node with labels ["Config"] and no properties
   Hash Input: "node:Config:"
   ```

3. **Node without labels** (special case):
   ```
   Input: Node with no labels and properties {value: 1}
   Hash Input: "node::{"value":1}"
   ```

### Version Independence
✅ **Truly version-independent** - The same node will generate the same hash across Neo4j 4.x, 5.x, and future versions.

## NET_rel_identity_hash

### Purpose
Provides a stable identifier for relationships that remains consistent when:
- The same relationship is exported multiple times from the same database
- The relationship's properties remain unchanged
- The relationship connects the same two nodes (by element ID)

### Field Details
- **Field Name**: `NET_rel_identity_hash`
- **Type**: String (64 characters)
- **Format**: Lowercase hexadecimal SHA-256 hash
- **Example**: `b8c9d3e5f7a9012345678901bcdef2345678901bcdef2345678901bcdef234567`

### Generation Algorithm

```
SHA256("rel:" + type + ":" + start_element_id + ":" + end_element_id + ":" + canonicalized_properties)
```

Where:
- `type`: The relationship type (e.g., "KNOWS", "FOLLOWS")
- `start_element_id`: Neo4j's internal element ID of the start node
- `end_element_id`: Neo4j's internal element ID of the end node
- `canonicalized_properties`: JSON representation with keys sorted alphabetically

### Examples

1. **Neo4j 4.x relationship**:
   ```
   Input: KNOWS relationship with {since: 2020} connecting nodes with IDs 123 and 456
   Hash Input: "rel:KNOWS:123:456:{"since":2020}"
   ```

2. **Neo4j 5.x relationship** (same logical relationship):
   ```
   Input: KNOWS relationship with {since: 2020} connecting nodes with element IDs "4:abc:123" and "4:def:456"
   Hash Input: "rel:KNOWS:4:abc:123:4:def:456:{"since":2020}"
   ```

### Version Dependence
❌ **Version-dependent** - The same logical relationship will generate different hashes between Neo4j versions due to:
- Neo4j 4.x uses numeric IDs (e.g., `123`)
- Neo4j 5.x+ uses UUID-based element IDs (e.g., `4:abc:123`)

### Additional Relationship Fields

To provide complete tracking capability, relationships also include:

- **`start_element_id`**: Neo4j element ID of the start node
- **`end_element_id`**: Neo4j element ID of the end node
- **`start_node_content_hash`**: The `NET_node_content_hash` of the start node
- **`end_node_content_hash`**: The `NET_node_content_hash` of the end node

This allows downstream systems to:
- Track when connected nodes change (content hashes change)
- Identify the same logical relationship across exports (when feasible)
- Build relationship graphs using content-based node identities

## Property Canonicalization

Both hash types use the same canonicalization rules to ensure consistency:

1. **Key Ordering**: Property keys sorted alphabetically
2. **JSON Format**: Compact JSON with no whitespace
3. **Number Format**:
   - Integers: No decimal point (30, not 30.0)
   - Floats: Minimal representation (3.14, not 3.140)
4. **Null Handling**: Properties with null values are omitted
5. **Special Types**:
   - Temporal values are pre-processed to handle precision differences
   - Neo4j-specific types (Point, Duration) are converted appropriately

## Implementation Details

### Two-Pass Export Process

The tool uses a two-pass approach to generate all identifiers:

1. **Pass 1: Export Nodes**
   - Generate `NET_node_content_hash` for each node
   - Store mapping of `elementId → content_hash` in memory
   - Write nodes to JSONL with their content hashes

2. **Pass 2: Export Relationships**
   - Look up content hashes for start/end nodes from Pass 1 mapping
   - Generate `NET_rel_identity_hash` using element IDs
   - Write relationships with both identity and node content hashes

## Downstream Usage Patterns

### Change Detection

```sql
-- Detect changed nodes between exports
SELECT
    old.element_id,
    old.NET_node_content_hash as old_hash,
    new.NET_node_content_hash as new_hash
FROM export_2024_01 old
JOIN export_2024_02 new ON old.element_id = new.element_id
WHERE old.NET_node_content_hash != new.NET_node_content_hash
```

### Content-Based Joins

```sql
-- Join nodes by content across different databases
SELECT *
FROM database_a.nodes a
JOIN database_b.nodes b ON a.NET_node_content_hash = b.NET_node_content_hash
```

### Relationship Stability Analysis

```sql
-- Find relationships where nodes changed but relationship didn't
SELECT
    r1.NET_rel_identity_hash,
    r1.start_node_content_hash != r2.start_node_content_hash as start_changed,
    r1.end_node_content_hash != r2.end_node_content_hash as end_changed
FROM export_jan r1
JOIN export_feb r2 ON r1.NET_rel_identity_hash = r2.NET_rel_identity_hash
```

## Limitations and Considerations

### Version Migration
When migrating from Neo4j 4.x to 5.x:
- All `NET_node_content_hash` values remain stable ✅
- All `NET_rel_identity_hash` values will change ❌
- Downstream systems must handle this one-time relationship hash change

### Collision Probability
- SHA-256 collision probability: ~1 in 2^256
- No collision detection implemented (computational cost outweighs risk)
- If collision occurs, entities would share the same ID

### Performance Impact
- SHA-256 computation: ~1μs per entity on modern hardware
- No caching (hashes computed on-demand)
- Typically adds <5% to total export time

## Future Ideas

### Potential Future Enhancements
1. Optional content-based relationship hashing (using node content hashes instead of element IDs)
2. Configurable hash algorithms for different security/performance requirements
3. Bloom filter generation for efficient existence checks

### Backward Compatibility
- These are additional fields; existing consumers can ignore them
- Original `element_id` field remains unchanged
- No breaking changes to JSONL format
