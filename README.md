# Neo4j Export Tool

<p align="center">
  <strong>Export ANY Neo4j database to JSONL format</strong>
</p>

<p align="center">
  <a href="#features">Features</a> •
  <a href="#quick-start">Quick Start</a> •
  <a href="#installation">Installation</a> •
  <a href="#usage">Usage</a> •
  <a href="#configuration">Configuration</a> •
  <a href="#output-format">Output Format</a> •
  <a href="#performance">Performance</a>
</p>

---

## Overview

Neo4j Export Tool is a memory efficient utility for exporting Neo4j databases (versions 3.5+ through 5.x) to JSONL format. Built with F# for type safety and functional reliability, it handles multi terabyte datasets while maintaining constant memory usage of ~150MB.

### Key Capabilities

- 🚀 **50K-60K records/second** throughput (hardware dependent)
- 💾 **Constant memory usage** regardless of database size
- 🔄 **Universal compatibility** with Neo4j 3.5+ (no APOC required)
- 🛡️ **Enterprise resilience** with circuit breakers and retry logic
- 📊 **Comprehensive statistics** and progress monitoring
- 🔒 **Security design** for handling untrusted data

## Features

- **Type-safe**: Leverages F#'s type system for compile-time guarantees
- **Streaming architecture**: Single-pass export with pagination
- **Fault tolerant**: Circuit breakers, exponential backoff, and graceful degradation
- **Observable**: Structured logging with real-time progress updates
- **Resource aware**: Automatic disk space and memory monitoring
- **Cross-platform**: Runs on Linux, macOS, and Windows

## Quick Start

### Using Docker (Recommended)

```bash
# 1. Clone the repository
git clone https://github.com/yourusername/neo4j-export-tool.git
cd neo4j-export-tool

# 2. Configure your Neo4j connection
cp .env.example .env
# Edit .env with your Neo4j credentials

# 3. Run the export
docker compose -f neo4j-export-runner.compose.yaml up --build

# Your export will appear in ./exports/
```

### Using Pre-built Binaries

Download the appropriate binary for your platform

## Running

### Prerequisites

- **For Docker**: Docker Desktop 20.10+ with Docker Compose v2
- **For binaries**: No prerequisites (self-contained)
- **For development**: .NET SDK 9.0+

### Using Docker

```bash
docker compose -f neo4j-export-runner.compose.yaml build
```

### Building from Source

```bash
# Clone the repository
git clone https://github.com/yourusername/neo4j-export-tool.git
cd neo4j-export-tool

# Restore dependencies
dotnet restore

# Build the project
dotnet build

# Run tests
dotnet test

# Create a release build
dotnet publish -c Release -o ./publish
```

### Building Platform Binaries

#### Using Docker (Cross-platform builds)

Use the Makefile to build self-contained binaries for any platform:

```bash
# Build all platforms at once
make binaries

# Or build specific platforms
make macos-apple-silicon    # macOS Apple Silicon
make macos-intel           # macOS Intel
make linux-amd64           # Linux x64
make linux-arm64           # Linux ARM64
make windows-x64           # Windows x64
make windows-arm64         # Windows ARM64
```

#### Native builds (On your current machine)

For faster builds on your current platform:

```bash
# Build for your current OS/architecture
./build-binaries-on-host.sh
```

All binaries will be created in the `dist/` directory with consistent naming:
- `neo4j-export-darwin-arm64` (macOS Apple Silicon)
- `neo4j-export-darwin-amd64` (macOS Intel)
- `neo4j-export-linux-amd64` (Linux x64)
- `neo4j-export-linux-arm64` (Linux ARM64)
- `neo4j-export-windows-amd64.exe` (Windows x64)
- `neo4j-export-windows-arm64.exe` (Windows ARM64)

## Usage

### Basic Usage

```bash
# Using Docker
docker compose -f neo4j-export-runner.compose.yaml run --rm neo4j-export

# Using binary (with environment variables)
export NEO4J_URI=bolt://localhost:7687
export NEO4J_USER=neo4j
export NEO4J_PASSWORD=your-password
export OUTPUT_DIRECTORY=./exports
./neo4j-export

# Using dotnet run (development)
dotnet run --project Neo4jExport/Neo4jExport.fsproj
```

### Running with Configuration File

```bash
# Load environment from .env file
dotenv -e .env -- ./neo4j-export

# Or source the file (bash/zsh)
set -a; source .env; set +a
./neo4j-export
```

### Docker Compose Usage

```yaml
# docker-compose.yml example
services:
  neo4j-export:
    image: neo4j-export-fsharp:latest
    env_file:
      - .env
    volumes:
      - ./exports:/data/export
    network_mode: host  # For local Neo4j access
```

## Configuration

All configuration is done through environment variables. Copy `.env.example` to `.env` and customise:

### Essential Settings

| Variable | Description | Default |
|----------|-------------|---------|
| `NEO4J_URI` | Neo4j connection URI (bolt://, neo4j://, bolt+s://, neo4j+s://) | `bolt://localhost:7687` |
| `NEO4J_USER` | Neo4j username | `neo4j` |
| `NEO4J_PASSWORD` | Neo4j password | _(empty)_ |
| `OUTPUT_DIRECTORY` | Directory for export files | `.` (current directory) |

### Performance Tuning

| Variable | Description | Default |
|----------|-------------|---------|
| `BATCH_SIZE` | Records per batch | `10000` |
| `MAX_MEMORY_MB` | Memory limit before GC | `1024` |
| `MIN_DISK_GB` | Minimum free disk space | `10` |
| `JSON_BUFFER_SIZE_KB` | Initial JSON buffer size | `16` |

### Advanced Options

<details>
<summary>Click to expand advanced configuration options</summary>

| Variable | Description | Default |
|----------|-------------|---------|
| `SKIP_SCHEMA_COLLECTION` | Skip schema metadata collection | `false` |
| `MAX_RETRIES` | Max retry attempts for failures | `5` |
| `RETRY_DELAY_MS` | Initial retry delay (uses exponential backoff) | `1000` |
| `QUERY_TIMEOUT_SECONDS` | Timeout for individual queries | `300` |
| `DEBUG` | Enable debug logging | `false` |
| `VALIDATE_JSON` | Validate JSON during export | `true` |
| `ALLOW_INSECURE` | Allow insecure TLS connections | `false` |

</details>

### Examples

<details>
<summary>Docker Host Access</summary>

```bash
# From Docker container to host Neo4j
NEO4J_URI=bolt://host.docker.internal:7687
NEO4J_USER=neo4j
NEO4J_PASSWORD=your-password
OUTPUT_DIRECTORY=/data/export
```

</details>

<details>
<summary>Large Database Optimization</summary>

```bash
# For multi-TB databases
BATCH_SIZE=50000
MAX_MEMORY_MB=2048
MIN_DISK_GB=100
JSON_BUFFER_SIZE_KB=64
SKIP_SCHEMA_COLLECTION=true
```

</details>

## Output Format

The tool exports to JSON Lines (JSONL) format, where each line is a valid JSON object:

### File Naming Convention

```
[database]_[timestamp]_[nodes]n_[rels]r_[exportId].jsonl
```

Example: `movies_20241230T143022Z_3610n_4643r_a1b2c3d4.jsonl`

### File Structure

**Line 1: Metadata**
```json
{
  "export_metadata": {
    "export_id": "a1b2c3d4-b5c6-d7e8-f9g0-h1i2j3k4l5m6",
    "export_timestamp_utc": "2024-12-30T14:30:22.123Z",
    "tool_version": "0.10.0",
    "duration_seconds": 45.678
  },
  "source_system": {
    "type": "neo4j",
    "version": "5.15.0",
    "edition": "enterprise",
    "database": {
      "name": "movies",
      "created_at": "2024-01-15T10:00:00Z"
    }
  },
  "database_statistics": {
    "nodeCount": 3610,
    "relCount": 4643,
    "labelCount": 5,
    "relTypeCount": 8
  },
  "export_manifest": {
    "labels": {
      "Person": { "count": 2000, "bytes": 524288 },
      "Movie": { "count": 1610, "bytes": 419430 }
    }
  }
}
```

**Lines 2+: Data Records**
```json
{"type":"node","id":123,"labels":["Person"],"properties":{"name":"Tom Hanks","born":1956}}
{"type":"node","id":456,"labels":["Movie"],"properties":{"title":"Forrest Gump","released":1994}}
{"type":"relationship","id":789,"label":"ACTED_IN","start":123,"end":456,"properties":{"role":"Forrest"}}
```

### Data Type Support

The exporter handles all Neo4j data types:
- **Primitives**: String, Integer, Float, Boolean, null
- **Temporal**: Date, Time, DateTime, LocalDateTime, Duration
- **Spatial**: Point (2D/3D, Cartographic/Geographic)
- **Collections**: Lists and nested structures
- **Special**: Preserves original encoding for data fidelity

## Performance

### Resource Usage

- **Memory**: Constant ~150MB regardless of database size
- **Disk I/O**: Sequential writes, benefits from fast storage
- **Network**: Streaming with configurable batch size

### Optimisation Tips

1. **Increase batch size** for better throughput (if memory allows):
   ```bash
   BATCH_SIZE=50000
   ```

2. **Skip schema collection** for faster exports:
   ```bash
   SKIP_SCHEMA_COLLECTION=true
   ```

3. **Use SSD storage** for output directory

4. **Ensure sufficient disk space** (typically 2-3x uncompressed database size)

## Architecture

```
┌─────────────────┐     ┌──────────────────┐     ┌─────────────────┐
│                 │     │                  │     │                 │
│  Configuration  │────▶│  Neo4j Client    │────▶│  Export Engine  │
│   (.env file)   │     │ (with retries)   │     │   (streaming)   │
│                 │     │                  │     │                 │
└─────────────────┘     └──────────────────┘     └─────────────────┘
                                │                          │
                                ▼                          ▼
                        ┌──────────────────┐     ┌─────────────────┐
                        │                  │     │                 │
                        │ Circuit Breaker  │     │   JSONL Writer  │
                        │   (resilience)   │     │ (Utf8JsonWriter)│
                        │                  │     │                 │
                        └──────────────────┘     └─────────────────┘
                                                          │
                                                          ▼
                                                 ┌─────────────────┐
                                                 │                 │
                                                 │  Output File    │
                                                 │   (.jsonl)      │
                                                 │                 │
                                                 └─────────────────┘
```

### Key Components

- **Types.fs**: Core domain types and error definitions
- **Configuration.fs**: Environment-based configuration with validation
- **Neo4j.fs**: Database client with circuit breaker pattern
- **Export.fs**: Streaming export engine with pagination
- **Metadata.fs**: Database introspection and statistics
- **Monitoring.fs**: Background resource monitoring
- **SignalHandling.fs**: Graceful shutdown (SIGINT/SIGTERM)

## Troubleshooting

### Common Issues

<details>
<summary>Connection Refused</summary>

**Problem**: Cannot connect to Neo4j

**Solution**:
- Verify Neo4j is running: `neo4j status`
- Check URI format: `bolt://` not `http://`
- For Docker: use `host.docker.internal` instead of `localhost`
- Verify firewall allows port 7687

</details>

<details>
<summary>Authentication Failed</summary>

**Problem**: Invalid username/password

**Solution**:
- Default credentials are `neo4j/neo4j` (requires password change)
- Check for special characters in password (may need escaping)
- Verify credentials with: `cypher-shell -u neo4j -p yourpassword`

</details>

<details>
<summary>Insufficient Disk Space</summary>

**Problem**: Export stops with disk space error

**Solution**:
- Check available space: `df -h`
- Adjust `MIN_DISK_GB` if needed
- Export file is typically 2-3x the size of your database
- Consider exporting to a different volume

</details>

<details>
<summary>Memory Issues</summary>

**Problem**: High memory usage or OOM errors

**Solution**:
- Reduce `BATCH_SIZE` (e.g., to 5000)
- Ensure `MAX_MEMORY_MB` is appropriate for your system
- The tool uses ~150MB baseline + batch processing overhead

</details>

### Debug Mode

Enable debug logging for troubleshooting:

```bash
DEBUG=true ./neo4j-export
```

This provides:
- Detailed query information
- Memory usage statistics
- Retry attempt logs
- Performance metrics

## Error Codes

The tool uses specific exit codes for different error types:

| Exit Code | Error Type | Description |
|-----------|------------|-------------|
| 0 | Success | Export completed successfully |
| 1 | Unknown Error | Unexpected error occurred |
| 2 | Connection Error | Cannot connect to Neo4j |
| 3 | Resource Error | Insufficient disk/memory |
| 5 | Export Error | Error during export process |
| 6 | Configuration Error | Invalid configuration |
| 7 | Query Error | Cypher query failed |
| 130 | User Cancelled | Export cancelled by user (Ctrl+C) |


### Development Setup

```bash
# Install .NET SDK 9.0
# https://dotnet.microsoft.com/download

# Install Fantomas for code formatting
dotnet tool install -g fantomas

# Run format check
dotnet fantomas src/ --check

# Apply formatting
dotnet fantomas src/
```

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- Built with [F#](https://fsharp.org/) for functional reliability
- Uses [Neo4j .NET Driver](https://github.com/neo4j/neo4j-dotnet-driver) for database connectivity

---