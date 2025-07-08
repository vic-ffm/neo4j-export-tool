# Neo4j Export Tool - Data Type Support

The following data types are supported.

## Core Primitive Types

### null
- **Serialisation**: JSON `null`
- **Example**: `null`

### Boolean
- **Serialisation**: JSON boolean
- **Example**: `true`, `false`

### Integer
- **Supported Types**: All CLR integer types
  - Signed: `int16`, `int32`, `int64`, `sbyte`
  - Unsigned: `uint16`, `uint32`, `uint64`, `byte`
- **Serialisation**: JSON number
- **Example**: `42`, `-1000`, `9223372036854775807`

### Float
- **Supported Types**: `float32`, `double`, `decimal`
- **Special Values**:
  - `NaN` → `"NaN"` (string)
  - `Infinity` → `"Infinity"` (string)
  - `-Infinity` → `"-Infinity"` (string)
- **Serialisation**: JSON number (or string for special values)
- **Example**: `3.14`, `-0.001`, `"NaN"`

### String
- **Maximum Length**: 10,000,000 characters (10MB)
- **Truncation**: Large strings are truncated with SHA256 hash preserved
- **Serialisation**: JSON string
- **Truncated Format**:
  ```json
  {
    "_truncated": "string_too_large",
    "_length": 15000000,
    "_prefix": "first 1000 characters...",
    "_sha256": "base64_encoded_hash"
  }
  ```

### ByteArray
- **Maximum Size**: 50,000,000 bytes (50MB)
- **Serialisation**: Base64-encoded string
- **Truncation**: Large arrays are truncated with SHA256 hash preserved
- **Example**: `"SGVsbG8gV29ybGQ="`

## Collection Types

### List
- **Neo4j Type**: `List<T>`
- **Maximum Items**: 10,000 (configurable)
- **Serialisation**: JSON array
- **Supports**: Nested values of any supported type
- **Truncated Format**:
  ```json
  [
    "item1",
    "item2",
    ...,
    {
      "_truncated": "list_too_large",
      "_total_items": 15000,
      "_shown_items": 10000
    }
  ]
  ```

### Map
- **Neo4j Type**: `Map<String, T>`
- **Maximum Entries**: 10,000 (configurable)
- **Serialisation**: JSON object
- **Features**: Automatic key uniqueness (duplicates get numeric suffix)
- **Example**:
  ```json
  {
    "key1": "value1",
    "key2": 42,
    "key2_1": "duplicate key renamed"
  }
  ```

## Spatial Types

### Point
- **2D Points**: x, y coordinates with SRID
- **3D Points**: x, y, z coordinates with SRID
- **Serialisation**:
  ```json
  {
    "type": "Point",
    "srid": 4326,
    "x": -73.935242,
    "y": 40.730610,
    "z": 100.0  // Optional for 3D
  }
  ```

## Temporal Types

All temporal types are serialised using their string representation.

**Note**: Neo4j stores temporal values with nanosecond precision, but .NET supports only 100-nanosecond precision. Values are automatically truncated to the nearest 100 nanoseconds during export.

### Date
- **Neo4j Type**: `Date`
- **Example**: `"2024-01-15"`

### Time
- **Neo4j Type**: `Time` (with timezone offset)
- **Example**: `"14:30:15.123456700+02:00"`

### LocalTime
- **Neo4j Type**: `LocalTime` (no timezone)
- **Example**: `"14:30:15.123456700"`

### DateTime (ZonedDateTime)
- **Neo4j Type**: `DateTime` (with timezone)
- **Examples**:
  - With named timezone: `"2024-01-15T14:30:15.123456700+02:00[Europe/Berlin]"`
  - With offset only: `"2024-01-15T14:30:15.123456700+02:00"`
- **Note**: Preserves both UTC offset and timezone name when available

### LocalDateTime
- **Neo4j Type**: `LocalDateTime` (no timezone)
- **Example**: `"2024-01-15T14:30:15.123456700"`

### Duration
- **Neo4j Type**: `Duration`
- **Example**: `"P1Y2M3DT4H5M6S"`

## Graph Element Types

### Node
- **Serialisation**: Full node with labels and properties
- **Format**:
  ```json
  {
    "type": "node",
    "id": 123,
    "element_id": "4:abc:123",
    "labels": ["Person", "Employee"],
    "properties": {
      "name": "John Doe",
      "age": 30
    }
  }
  ```

### Relationship
- **Serialisation**: Full relationship with type and properties
- **Format**:
  ```json
  {
    "type": "relationship",
    "id": 456,
    "element_id": "5:def:456",
    "label": "KNOWS",
    "start": 123,
    "end": 789,
    "properties": {
      "since": "2020-01-01"
    }
  }
  ```

### Path
- **Serialisation**: Adaptive based on path length
- **Modes**:
  - **Full**: Complete node and relationship data (short paths)
  - **Compact**: IDs and labels only (medium paths)
  - **IdsOnly**: Just IDs (long paths)
- **Example (Full mode)**:
  ```json
  {
    "_type": "path",
    "length": 3,
    "_serialization_level": "Full",
    "nodes": [/* array of node objects */],
    "relationships": [/* array of relationship objects */],
    "sequence": [
      {"type": "node", "index": 0},
      {"type": "relationship", "index": 0},
      {"type": "node", "index": 1}
    ]
  }
  ```

## Additional CLR Types

### DateTime (.NET)
- **Serialisation**: ISO 8601 format
- **Example**: `"2024-01-15T14:30:15.1234567+02:00"`

### DateTimeOffset (.NET)
- **Serialisation**: ISO 8601 format with offset
- **Example**: `"2024-01-15T14:30:15.1234567+02:00"`

## Safety Features

### Depth Protection
- **Maximum Nesting**: Configurable (default 10 levels)
- **Behaviour**: Deep structures are truncated with metadata
- **Example**:
  ```json
  {
    "_truncated": "depth_limit",
    "_depth": 11,
    "_type": "System.Collections.Generic.Dictionary"
  }
  ```

### Property Limits
- **Maximum Properties per Object**: 10,000 (configurable)
- **Behaviour**: Additional properties are omitted with count preserved

### Nested Graph Elements
When nodes or relationships appear as property values (not common but possible in Neo4j):
- **Deep Mode**: Full serialisation (shallow nesting)
- **Shallow Mode**: Basic info only (medium nesting)
- **Reference Mode**: Minimal ID reference (deep nesting)

## Configuration

All limits can be configured via environment variables:
- `MAX_COLLECTION_ITEMS`: Maximum items in lists/maps
- `MAX_LABELS_PER_NODE`: Maximum labels to export per node
- `MAX_PATH_LENGTH`: Maximum path length before truncation
- `MAX_NESTED_DEPTH`: Maximum recursion depth

## Error Handling

The tool ensures valid JSON output even when encountering errors:
- Failed properties are replaced with error markers
- The export continues with remaining data
- Errors are logged and included in the export file

## Unsupported Types

Any types not explicitly listed above will be serialised as:
```json
{
  "_type": "TypeName",
  "_assembly": "AssemblyName",
  "_note": "unserializable_type"
}
```

This ensures the export completes successfully even when encountering unexpected types.
