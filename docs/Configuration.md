# Neo4j Export Tool - Configuration Guide

This guide documents all configuration options available for the Neo4j Export Tool. All configuration is done through environment variables.

## Table of Contents
- [Configuration Overview](#configuration-overview)
- [Essential Settings](#essential-settings)
- [Performance Tuning](#performance-tuning)
- [Resource Management](#resource-management)
- [Export Behavior](#export-behavior)
- [Error Handling & Resilience](#error-handling--resilience)
- [Debugging & Validation](#debugging--validation)
- [Security Settings](#security-settings)
- [Advanced Settings](#advanced-settings)
  - [Memory Estimation](#memory-estimation)
  - [JSON Serialization](#json-serialization)
  - [Path Serialization](#path-serialization)
  - [Nested Elements](#nested-elements)
  - [Label Management](#label-management)
  - [Collection Limits](#collection-limits)
- [Common Configurations](#common-configurations)
- [Environment Variable Reference](#environment-variable-reference)

## Configuration Overview

The Neo4j Export Tool uses environment variables for all configuration. You can set these in several ways:

### Method 1: Using a .env file (Recommended)
```bash
# Copy the example configuration
cp .env.example .env

# Edit with your settings
nano .env

# Run with dotenv
dotenv -e .env -- ./neo4j-export
```

### Method 2: Inline environment variables
```bash
NEO4J_URI=bolt://localhost:7687 \
NEO4J_USER=neo4j \
NEO4J_PASSWORD=password \
OUTPUT_DIRECTORY=./exports \
./neo4j-export
```

### Method 3: Export to shell environment
```bash
export NEO4J_URI=bolt://localhost:7687
export NEO4J_USER=neo4j
export NEO4J_PASSWORD=password
export OUTPUT_DIRECTORY=./exports
./neo4j-export
```

## Essential Settings

These are the core settings required for basic operation:

### `NEO4J_URI`
- **Description**: Neo4j connection URI
- **Default**: `bolt://localhost:7687`
- **Required**: Yes
- **Supported schemes**: `bolt://`, `neo4j://`, `bolt+s://`, `neo4j+s://`
- **Examples**:
  ```bash
  # Local Neo4j
  NEO4J_URI=bolt://localhost:7687

  # Remote server
  NEO4J_URI=bolt://neo4j.example.com:7687

  # Neo4j Aura (secure)
  NEO4J_URI=neo4j+s://xxxxxxxx.databases.neo4j.io

  # Docker host access
  NEO4J_URI=bolt://host.docker.internal:7687
  ```

### `NEO4J_USER`
- **Description**: Neo4j username
- **Default**: `neo4j`
- **Required**: Yes (unless authentication is disabled)

### `NEO4J_PASSWORD`
- **Description**: Neo4j password
- **Default**: (empty string)
- **Required**: Yes (unless authentication is disabled)
- **Security Note**: Use secure methods to provide passwords in production

### `OUTPUT_DIRECTORY`
- **Description**: Directory where export files will be saved
- **Default**: `.` (current directory)
- **Required**: No
- **Notes**:
  - Directory will be created if it doesn't exist
  - Ensure sufficient disk space (typically 2-3x database size)
  - For Docker, this should be the container path (e.g., `/data/export`)

## Performance Tuning

Optimise export performance based on your hardware and database size:

### `BATCH_SIZE`
- **Description**: Number of records to process in each batch
- **Default**: `10000`
- **Range**: 1000-100000
- **Impact**:
  - Higher values = better throughput but more memory usage
  - Lower values = less memory but slower export
- **Recommendations**:
  ```bash
  # Small databases or limited memory
  BATCH_SIZE=5000

  # Large databases with ample memory
  BATCH_SIZE=50000

  # Multi-TB databases
  BATCH_SIZE=100000
  ```

### `JSON_BUFFER_SIZE_KB`
- **Description**: Initial buffer size for JSON serialisation (in KB)
- **Default**: `16`
- **Range**: 4-256
- **Impact**: Larger buffers reduce memory allocations for large properties
- **Recommendations**:
  ```bash
  # Databases with large text properties
  JSON_BUFFER_SIZE_KB=64

  # Mostly numeric/small properties
  JSON_BUFFER_SIZE_KB=8
  ```

### `SKIP_SCHEMA_COLLECTION`
- **Description**: Skip the initial schema metadata collection phase
- **Default**: `false`
- **Values**: `true`, `false`
- **Impact**:
  - `true` = Faster start, no schema in metadata
  - `false` = Slower start, complete schema information
- **Use cases**:
  ```bash
  # Speed up export when schema isn't needed
  SKIP_SCHEMA_COLLECTION=true
  ```

## Resource Management

Control resource usage to prevent system overload:

### `MAX_MEMORY_MB`
- **Description**: Maximum memory usage before triggering garbage collection
- **Default**: `1024` (1GB)
- **Range**: 512-8192
- **Notes**:
  - Tool uses ~150MB baseline + batch processing overhead
  - Set based on available system memory
- **Examples**:
  ```bash
  # Limited memory system
  MAX_MEMORY_MB=512

  # High-memory server
  MAX_MEMORY_MB=4096
  ```

### `MIN_DISK_GB`
- **Description**: Minimum free disk space required (in GB)
- **Default**: `10`
- **Range**: 1-1000
- **Behaviour**: Export stops if free space falls below threshold
- **Recommendations**:
  ```bash
  # Small exports
  MIN_DISK_GB=5

  # Large production databases
  MIN_DISK_GB=100
  ```

## Export Behaviour

Control how the export process behaves:

### `QUERY_TIMEOUT_SECONDS`
- **Description**: Timeout for individual Cypher queries
- **Default**: `300` (5 minutes)
- **Range**: 60-3600
- **Use cases**:
  ```bash
  # Fast queries expected
  QUERY_TIMEOUT_SECONDS=60

  # Large, complex queries
  QUERY_TIMEOUT_SECONDS=600
  ```

## Error Handling & Resilience

Configure retry behaviour and error handling:

### `MAX_RETRIES`
- **Description**: Maximum retry attempts for transient failures
- **Default**: `5`
- **Range**: 0-20
- **Notes**: Uses exponential backoff between retries

### `RETRY_DELAY_MS`
- **Description**: Initial delay between retry attempts (milliseconds)
- **Default**: `1000` (1 second)
- **Range**: 100-10000
- **Behaviour**: Doubles after each retry up to MAX_RETRY_DELAY_MS

### `MAX_RETRY_DELAY_MS`
- **Description**: Maximum delay between retries (milliseconds)
- **Default**: `30000` (30 seconds)
- **Range**: 1000-300000

Example configuration for unstable networks:
```bash
MAX_RETRIES=10
RETRY_DELAY_MS=2000
MAX_RETRY_DELAY_MS=60000
```

## Debugging & Validation

Options for troubleshooting and ensuring data integrity:

### `DEBUG`
- **Description**: Enable verbose debug logging
- **Default**: `false`
- **Values**: `true`, `false`
- **Output includes**:
  - Query details
  - Memory usage statistics
  - Retry attempts
  - Performance metrics

### `VALIDATE_JSON`
- **Description**: Validate JSON output during export
- **Default**: `true`
- **Values**: `true`, `false`
- **Trade-off**:
  - `true` = Ensures valid JSON but slightly slower
  - `false` = Faster but no validation

## Security Settings

### `ALLOW_INSECURE`
- **Description**: Allow insecure TLS connections
- **Default**: `false`
- **Values**: `true`, `false`
- **Warning**: Only use in development/test environments
- **Use case**:
  ```bash
  # Development with self-signed certificates
  ALLOW_INSECURE=true
  ```

## Advanced Settings

### Memory Estimation

Fine-tune memory estimation for optimal performance:

### `NEO4J_EXPORT_AVG_RECORD_SIZE`
- **Description**: Average record size in bytes for memory estimation
- **Default**: `1024` (1KB)
- **Range**: 100-10000
- **Guidance**:
  ```bash
  # Small nodes (few properties)
  NEO4J_EXPORT_AVG_RECORD_SIZE=500

  # Large nodes (many/large properties)
  NEO4J_EXPORT_AVG_RECORD_SIZE=5000
  ```

### `NEO4J_EXPORT_OVERHEAD_MULTIPLIER`
- **Description**: Processing overhead multiplier
- **Default**: `2.0`
- **Range**: 1.5-5.0
- **Purpose**: Reserves memory for serialisation buffers

### `NEO4J_EXPORT_MIN_MEMORY_RESERVATION`
- **Description**: Minimum memory reservation in bytes
- **Default**: `104857600` (100MB)
- **Range**: 52428800-1073741824 (50MB-1GB)

### Path Serialisation

Control how Neo4j paths are serialised:

### `MAX_PATH_LENGTH`
- **Description**: Maximum path length before truncation
- **Default**: `100000`
- **Behaviour**: Paths longer than this are truncated with warning

### `PATH_FULL_MODE_LIMIT`
- **Description**: Node count threshold for full path serialisation
- **Default**: `1000`
- **Behaviour**: Paths with more nodes switch to compact mode (no properties)

### `PATH_COMPACT_MODE_LIMIT`
- **Description**: Node count threshold for compact path serialisation
- **Default**: `10000`
- **Behaviour**: Paths with more nodes switch to IDs-only mode

### `PATH_PROPERTY_DEPTH`
- **Description**: Maximum property nesting depth in paths
- **Default**: `5`
- **Range**: 1-10

### Nested Elements

Control serialisation of nested graph elements:

### `MAX_NESTED_DEPTH`
- **Description**: Maximum nesting depth for graph elements
- **Default**: `10`
- **Purpose**: Prevents infinite recursion in circular references

### `NESTED_SHALLOW_MODE_DEPTH`
- **Description**: Depth to switch to shallow serialisation
- **Default**: `5`
- **Behaviour**: Omits properties but includes labels/types

### `NESTED_REFERENCE_MODE_DEPTH`
- **Description**: Depth to switch to reference-only mode
- **Default**: `8`
- **Behaviour**: Only includes IDs and type information

### Label Management

Control label serialisation to manage JSON size:

### `MAX_LABELS_PER_NODE`
- **Description**: Maximum labels per node in full mode
- **Default**: `100`
- **Notes**: Additional labels are truncated with warning

### `MAX_LABELS_IN_REFERENCE_MODE`
- **Description**: Maximum labels in reference mode
- **Default**: `10`
- **Context**: Used for nodes in paths or nested structures

### `MAX_LABELS_IN_PATH_COMPACT`
- **Description**: Maximum labels in path compact mode
- **Default**: `5`
- **Purpose**: Keeps path representations concise

### Collection Limits

### `MAX_COLLECTION_ITEMS`
- **Description**: Maximum items in lists/maps
- **Default**: `10000`
- **Behaviour**: Larger collections are truncated with warning

## Common Configurations

### Small Database / Development
```bash
NEO4J_URI=bolt://localhost:7687
NEO4J_USER=neo4j
NEO4J_PASSWORD=password
OUTPUT_DIRECTORY=./exports
BATCH_SIZE=5000
MAX_MEMORY_MB=512
DEBUG=true
```

### Large Production Database
```bash
NEO4J_URI=bolt+s://prod-server:7687
NEO4J_USER=export_user
NEO4J_PASSWORD=${SECURE_PASSWORD}
OUTPUT_DIRECTORY=/data/exports
BATCH_SIZE=50000
MAX_MEMORY_MB=4096
MIN_DISK_GB=100
SKIP_SCHEMA_COLLECTION=true
MAX_RETRIES=10
```

### Multi-TB Database Optimisation
```bash
BATCH_SIZE=100000
MAX_MEMORY_MB=8192
MIN_DISK_GB=500
JSON_BUFFER_SIZE_KB=64
SKIP_SCHEMA_COLLECTION=true
NEO4J_EXPORT_AVG_RECORD_SIZE=2048
PATH_FULL_MODE_LIMIT=500
MAX_COLLECTION_ITEMS=5000
```

### Docker Configuration
```bash
NEO4J_URI=bolt://host.docker.internal:7687
NEO4J_USER=neo4j
NEO4J_PASSWORD=password
OUTPUT_DIRECTORY=/data/export
```

## Environment Variable Reference

For a complete list of all environment variables with their defaults, see the [.env.example](../.env.example) file in the repository root.

### Quick Reference Table

| Category | Variable | Default | Type |
|----------|----------|---------|------|
| **Connection** | NEO4J_URI | bolt://localhost:7687 | String |
| | NEO4J_USER | neo4j | String |
| | NEO4J_PASSWORD | (empty) | String |
| **Output** | OUTPUT_DIRECTORY | . | String |
| **Performance** | BATCH_SIZE | 10000 | Integer |
| | JSON_BUFFER_SIZE_KB | 16 | Integer |
| | SKIP_SCHEMA_COLLECTION | false | Boolean |
| **Resources** | MAX_MEMORY_MB | 1024 | Integer |
| | MIN_DISK_GB | 10 | Integer |
| **Resilience** | MAX_RETRIES | 5 | Integer |
| | RETRY_DELAY_MS | 1000 | Integer |
| | QUERY_TIMEOUT_SECONDS | 300 | Integer |
| **Debug** | DEBUG | false | Boolean |
| | VALIDATE_JSON | true | Boolean |
| **Security** | ALLOW_INSECURE | false | Boolean |

For advanced settings, refer to the sections above or the .env.example file.
