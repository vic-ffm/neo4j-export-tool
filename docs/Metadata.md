# Neo4j Export Tool - Metadata Structure

The first line of every JSONL export file contains comprehensive metadata about the export. This metadata provides everything needed to understand and process the exported data. The tool uses ElementIds exclusively for all node and relationship identification, ensuring compatibility with Neo4j 5.x and future Neo4j 6.0 releases.

Starting with version 0.14.0, the tool also generates stable content-based identifiers for nodes and relationships to enable change tracking and data snapshots across exports. See [Neo4JExportToolID.md](./Neo4JExportToolID.md) for detailed specification.

## Metadata Overview

The metadata is a single JSON object on line 1 that includes:
- Export identification and timestamps
- Source database information
- File structure details
- Schema information
- Performance metrics
- Compatibility guidelines

## Metadata Structure

### Root Level

```json
{
  "format_version": "1.0.0",
  "export_metadata": { ... },
  "producer": { ... },
  "source_system": { ... },
  ...
}
```

- **format_version**: Version of the metadata format specification

### Export Metadata

Information about the export process:

```json
"export_metadata": {
  "export_id": "77f72280-76e5-468b-8c6e-b32bed406e81",
  "export_timestamp_utc": "2025-07-02T12:37:00.0640070Z",
  "export_mode": "native_driver_streaming",
  "format": {
    "type": "jsonl",
    "metadata_line": 1,
    "node_start_line": 2,
    "relationship_start_line": 3612,
    "error_start_line": 8255,
    "warning_start_line": 8278
  }
}
```

- **export_id**: Unique GUID identifying this specific export
- **export_timestamp_utc**: ISO 8601 timestamp when export started
- **export_mode**: The export method (always "native_driver_streaming")
- **format**: Line numbers for different record types
  - **error_start_line**: Line where error records begin (if any)
  - **warning_start_line**: Line where warning records begin (if any)

### Producer Information

Details about the export tool:

```json
"producer": {
  "name": "neo4j-export.dll",
  "version": "0.14.0",
  "checksum": "018e11745ef7fb58e6d6e5a6af438f5f58ef2131e87b90e5ebe4be5bff57c305",
  "runtime_version": "9.0.6"
}
```

- **name**: Export tool executable name
- **version**: Export tool version
- **checksum**: SHA256 hash for binary verification
- **runtime_version**: .NET runtime version used

### Source System

Neo4j database information:

```json
"source_system": {
  "type": "neo4j",
  "version": "4.4.40",
  "edition": "community",
  "database": {
    "name": "neo4j"
  }
}
```

- **type**: Always "neo4j"
- **version**: Neo4j server version
- **edition**: Neo4j edition (community/enterprise)
- **database**: Database name and details

### Database Statistics

High-level content statistics:

```json
"database_statistics": {
  "nodeCount": 3610,
  "relCount": 4643,
  "labelCount": 68,
  "relTypeCount": 72
}
```

- **nodeCount**: Total number of nodes exported
- **relCount**: Total number of relationships exported
- **labelCount**: Number of unique node labels
- **relTypeCount**: Number of unique relationship types

### Database Schema

Complete schema information:

```json
"database_schema": {
  "labels": ["Person", "Organization", "Case", ...],
  "relationshipTypes": ["RELATED_TO", "ASSIGNED", "HAS_NOTE", ...]
}
```

- **labels**: All node labels in the database
- **relationshipTypes**: All relationship types in the database

### Error Summary

Export error statistics:

```json
"error_summary": {
  "error_count": 0,
  "warning_count": 0,
  "has_errors": false
}
```

- **error_count**: Number of errors during export
- **warning_count**: Number of warnings during export
- **has_errors**: Quick check for any errors

### Supported Record Types

Defines the structure of records in the file:

```json
"supported_record_types": [
  {
    "type_name": "node",
    "description": "A graph node with labels and properties",
    "required_fields": ["type", "element_id", "export_id", "labels", "properties"]
  },
  {
    "type_name": "relationship",
    "description": "A directed relationship between two nodes",
    "required_fields": ["type", "element_id", "export_id", "label", "start_element_id", "end_element_id", "properties"]
  },
  {
    "type_name": "error",
    "description": "An error that occurred during export",
    "required_fields": ["type", "timestamp", "message"],
    "optional_fields": ["line", "details", "element_id"]
  },
  {
    "type_name": "warning",
    "description": "A warning that occurred during export",
    "required_fields": ["type", "timestamp", "message"],
    "optional_fields": ["line", "details", "element_id"]
  }
]
```

### Environment Information

System environment details:

```json
"environment": {
  "hostname": "export-server",
  "operating_system": "Unix 15.4.1",
  "user": "export-user",
  "runtime": ".NET 9.0.6",
  "processors": 10,
  "memory_gb": 16.0
}
```

- **hostname**: Machine where export was performed
- **operating_system**: OS name and version
- **user**: User who performed the export
- **runtime**: Runtime environment
- **processors**: Number of CPU cores
- **memory_gb**: Available memory in GB

### Security Information

Security settings used:

```json
"security": {
  "encryption_enabled": false,
  "auth_method": "none",
  "data_validation": true
}
```

- **encryption_enabled**: Whether TLS was used
- **auth_method**: Authentication method used
- **data_validation**: Whether data integrity checks were performed

### Compatibility Information

Version compatibility for readers:

```json
"compatibility": {
  "minimum_reader_version": "1.0.0",
  "deprecated_fields": [],
  "breaking_change_version": "2.0.0"
}
```

- **minimum_reader_version**: Minimum reader version required
- **deprecated_fields**: Fields that will be removed in future
- **breaking_change_version**: Version where format will change

### Compression Hints

Recommendations for file compression:

```json
"compression": {
  "recommended": "zstd",
  "compatible": ["zstd", "gzip", "brotli", "none"],
  "expected_ratio": 0.3,
  "suffix": ".jsonl.zst"
}
```

- **recommended**: Optimal compression algorithm
- **compatible**: All supported compression formats
- **expected_ratio**: Expected compression ratio (0.3 = 70% reduction)
- **suffix**: Recommended file extension when compressed

### Export Manifest

Detailed performance metrics:

```json
"export_manifest": {
  "total_export_duration_seconds": 0.208669,
  "file_statistics": [
    {
      "label": "Person",
      "record_count": 7,
      "bytes_written": 1736,
      "export_duration_ms": 99
    }
  ]
}
```

Each label includes:
- **label**: Node label name
- **record_count**: Number of nodes exported
- **bytes_written**: Total bytes for this label
- **export_duration_ms**: Export time in milliseconds

## Using the Metadata

The metadata enables several important capabilities:

### Self-Documentation
Files contain all information needed to understand their content without external documentation.

### Version Compatibility
Readers can check the `format_version` and `compatibility` section to ensure they can process the file correctly.

### Performance Analysis
The `export_manifest` provides detailed timing information useful for troubleshooting slow exports.

### Data Validation
Compare `nodeCount` and `relCount` with actual records to verify complete exports.

### Schema Discovery
The `database_schema` section provides complete schema information without querying the database.

### Compression Guidance
The `compression` section helps choose optimal storage strategies for the exported files.

## Record Format Examples

### Node Record

```json
{
  "type": "node",
  "element_id": "4:68c06cde-1611-4b8e-a003-616fb97012a3:0",
  "NET_node_content_hash": "a7b9c2d4e6f8901234567890abcdef1234567890abcdef1234567890abcdef12",
  "export_id": "550e8400-e29b-41d4-a716-446655440000",
  "labels": ["Person", "Employee"],
  "properties": {
    "name": "John Doe",
    "age": 30,
    "department": "Engineering"
  }
}
```

### Relationship Record

```json
{
  "type": "relationship",
  "element_id": "5:12345678-abcd-ef01-2345-6789abcdef01:0",
  "NET_rel_identity_hash": "b8c9d3e5f7a9012345678901bcdef2345678901bcdef2345678901bcdef234567",
  "export_id": "550e8400-e29b-41d4-a716-446655440000",
  "label": "WORKS_FOR",
  "start_element_id": "4:68c06cde-1611-4b8e-a003-616fb97012a3:0",
  "end_element_id": "4:98765432-dcba-10fe-5432-0123456789ab:1",
  "start_node_content_hash": "a7b9c2d4e6f8901234567890abcdef1234567890abcdef1234567890abcdef12",
  "end_node_content_hash": "c9d8e5f7a9012345678901bcdef2345678901bcdef2345678901bcdef234567",
  "properties": {
    "since": "2020-01-01",
    "role": "Senior Developer"
  }
}
```

## Understanding ElementIds

Starting with Neo4j 5.0, ElementIds are the primary way to identify nodes and relationships. This export tool exclusively uses ElementIds for future compatibility:

- **element_id**: The unique identifier for a node or relationship
- **start_element_id**: For relationships, the ElementId of the start node
- **end_element_id**: For relationships, the ElementId of the end node

### ElementId Format

ElementIds typically follow the pattern: `{database-id}:{uuid}:{local-id}`

For example: `4:68c06cde-1611-4b8e-a003-616fb97012a3:0`

Note: In Neo4j 4.4, ElementIds may appear as simple numeric strings (e.g., "0", "1", "4642") that correspond to the internal numeric IDs. These will transition to the full UUID format in Neo4j 5.x and later.

### Compatibility Notes

- **Neo4j 4.4+**: The export tool works with Neo4j 4.4 and later versions
- **Neo4j 5.x**: Full ElementId support
- **Neo4j 6.0**: Ready for when numeric IDs are completely removed
- **No numeric IDs**: This tool does not export legacy numeric IDs

## Content-Based Identifiers

Starting with version 0.14.0, the export tool generates stable, content-based identifiers:

### For Nodes
- **NET_node_content_hash**: SHA-256 hash based on labels and properties
- Changes when node content changes, stays same when content is identical
- Consistent across Neo4j versions and exports

### For Relationships
- **NET_rel_identity_hash**: SHA-256 hash based on type, element IDs, and properties
- **start_node_content_hash**: Content hash of the start node
- **end_node_content_hash**: Content hash of the end node

These identifiers enable:
- Change detection between exports
- Data snapshot comparisons
- Version-independent content tracking

For full specification and examples, see [Neo4JExportToolID.md](./Neo4JExportToolID.md).

## Error and Warning Records

When errors or warnings occur during export, they are written as special record types:

### Error Record
```json
{
  "type": "error",
  "timestamp": "2025-07-08T03:08:02.123Z",
  "message": "Failed to serialize property 'data' for node",
  "element_id": "4:68c06cde-1611-4b8e-a003-616fb97012a3:0",
  "details": "Property contains unsupported type"
}
```

### Warning Record
```json
{
  "type": "warning",
  "timestamp": "2025-07-08T03:08:02.456Z",
  "message": "Skipped temporal property with nanosecond precision",
  "element_id": "4:68c06cde-1611-4b8e-a003-616fb97012a3:0"
}
```

## Important: Record Type Field Differences

Different record types have different required and optional fields. When processing JSONL exports, always check the `type` field first before accessing type-specific fields:

### Node Records
- **Always have**: `type`, `element_id`, `export_id`, `labels`, `properties`
- **May have**: `NET_node_content_hash` (if content hashing is enabled)
- **Note**: The `labels` field is an array of strings

### Relationship Records
- **Always have**: `type`, `element_id`, `export_id`, `label`, `start_element_id`, `end_element_id`, `properties`
- **May have**: `NET_rel_identity_hash`, `start_node_content_hash`, `end_node_content_hash` (if content hashing is enabled)
- **Note**: The `label` field is a single string (the relationship type)

### Error and Warning Records
- **Always have**: `type`, `timestamp`, `message`
- **May have**: `line`, `element_id`, `details`
- **Never have**: `labels`, `properties`, `start_element_id`, `end_element_id`

### Processing Example

```javascript
// Correct: Check type before accessing type-specific fields
records.forEach(record => {
  const recordType = record.type;
  
  if (recordType === "node") {
    // Safe to access node-specific fields
    const labels = record.labels;
    const properties = record.properties;
  } else if (recordType === "relationship") {
    // Safe to access relationship-specific fields
    const relType = record.label;
    const startId = record.start_element_id;
    const endId = record.end_element_id;
  } else if (recordType === "error" || recordType === "warning") {
    // Only access error/warning fields
    const message = record.message;
    const timestamp = record.timestamp;
    // DO NOT access labels or properties - they don't exist!
  }
});
```

## Performance Tracking

The export tool tracks performance metrics for each pagination strategy:

### Export Manifest
Detailed per-label statistics are included in the metadata:

```json
"export_manifest": {
  "total_export_duration_seconds": 1.367092,
  "file_statistics": [
    {
      "label": "Person",
      "record_count": 7,
      "bytes_written": 1736,
      "export_duration_ms": 99
    }
  ]
}
```

## Reserved Fields

The metadata will include a `_reserved` field and padding. These ensure future compatibility and can be safely ignored by readers.