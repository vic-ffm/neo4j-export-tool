# Neo4j Export Tool

<p align="center">
  <strong>Export ANY Neo4j database to JSONL format</strong>
</p>

<p align="center">
  <a href="#overview">Overview</a> •
  <a href="#quick-start">Quick Start</a> •
  <a href="#documentation">Documentation</a> •
  <a href="#support">Support</a>
</p>

---

## Overview

Neo4j Export Tool is a memory efficient utility for exporting Neo4j databases (versions 4.4+ through 5.x) to JSONL format. Built with F# for type safety and functional reliability, it handles multi terabyte datasets while maintaining constant memory usage of ~150MB.


## Why Use This Tool?

- ✅ **Works with ANY Neo4j**: No APOC or plugins required, just standard Cypher
- ✅ **Handles huge databases**: Export terabytes of data with constant memory usage
- ✅ **Production-ready**: Battle-tested with circuit breakers and comprehensive error handling
- ✅ **Cross-platform**: Native binaries for Windows, macOS, and Linux
- ✅ **Simple to use**: Just set 4 environment variables and run
- ✅ **Preserves all data types**: Full support for temporal, spatial, and nested structures

## Quick Start

### Option 1: Download Binary

Download the latest release for your platform from the [Releases](https://github.com/vic-ffm/neo4j-export-tool/releases) page.

**Windows users**: Download the `.zip` bundle which includes the executable, batch script, and quick start guide.

**macOS/Linux users**: Download the appropriate binary and make it executable with `chmod +x neo4j-export-*`

### Option 2: Using Docker

```bash
# 1. Clone the repository
git clone https://github.com/vic-ffm/neo4j-export-tool.git
cd neo4j-export-tool

# 2. Configure your Neo4j connection
cp .env.example .env
# Edit .env with your Neo4j credentials

# 3. Run the export
docker compose -f neo4j-export-runner.compose.yaml up --build

# Your export will appear in ./exports/
```

### Basic Usage

```bash
# Set your Neo4j connection details
export N4JET_NEO4J_URI=bolt://localhost:7687
export N4JET_NEO4J_USER=neo4j
export N4JET_NEO4J_PASSWORD=your-password
export N4JET_OUTPUT_DIRECTORY=./exports

# Run the export
./neo4j-export
```

For platform-specific installation instructions, see [docs/Install.md](docs/Install.md).

## Documentation

### 📚 User Guides
- [**Installation Guide**](docs/Install.md) - Platform-specific installation instructions
- [**Configuration Reference**](docs/Configuration.md) - All environment variables and options
- [**Output Format**](docs/Metadata.md) - JSONL format specification and metadata structure
- [**Data Types**](docs/Types.md) - Supported Neo4j data types and their JSON representation
- [**Export IDs**](docs/Neo4JExportToolID.md) - Content-based ID specification

### 🔧 Development
- [**Building from Source**](#building-from-source) - Build instructions for developers
- [**Versioning**](docs/VERSIONING.md) - Version strategy and release process
- [**Improvements**](docs/Improvements.md) - Future enhancements and roadmap

## Key Configuration

The tool uses environment variables for configuration:

```bash
N4JET_NEO4J_URI=bolt://localhost:7687     # Neo4j connection URI
N4JET_NEO4J_USER=neo4j                    # Username
N4JET_NEO4J_PASSWORD=your-password        # Password
N4JET_OUTPUT_DIRECTORY=./exports          # Where to save exports
```

See [docs/Configuration.md](docs/Configuration.md) for all available options.


## Output Format

The tool exports to JSON Lines (JSONL) format where:
- **Line 1**: Comprehensive metadata about the export
- **Lines 2+**: Individual node and relationship records

Example filename: `movies_20241230T143022Z_3610n_4643r_a1b2c3d4.jsonl`

For detailed format specification, see [docs/Metadata.md](docs/Metadata.md).

## Performance

- 🚀 **50K-60K records/second** throughput (hardware dependent)
- 💾 **Constant ~150MB memory** usage regardless of database size
- 📊 **Handles multi-TB databases** with streaming architecture

For performance tuning tips, see [docs/Configuration.md](docs/Configuration.md).

## Building from Source

### Prerequisites
- .NET SDK 9.0+
- Neo4j 4.4+ or 5.x

### Build Commands

```bash
# Clone and build
git clone https://github.com/vic-ffm/neo4j-export-tool
cd neo4j-export-tool
dotnet build

# Run tests
dotnet test

# Create platform-specific binaries
./build-binaries-on-host.sh
```

### Docker Build

```bash
# Build all platform binaries
make binaries

# Build specific platforms
make windows-x64
make macos-apple-silicon
make linux-amd64
```


## Support

### 🐛 Found a Bug?
Report issues on our [GitHub Issues](https://github.com/vic-ffm/neo4j-export-tool/issues) page.

### 💡 Feature Requests
See [docs/Improvements.md](docs/Improvements.md) for planned features or suggest new ones via GitHub Issues.

### 🔍 Troubleshooting
- Enable debug mode: `N4JET_DEBUG=true ./neo4j-export`
- Check [docs/Install.md](docs/Install.md#troubleshooting) for common issues
- Review exit codes in error messages

### 📖 Additional Resources
- [Neo4j Documentation](https://neo4j.com/docs/)
- [JSONL Format Specification](https://jsonlines.org/)

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- Built with [F#](https://fsharp.org/) for functional reliability
- Uses [Neo4j .NET Driver](https://github.com/neo4j/neo4j-dotnet-driver) for database connectivity
