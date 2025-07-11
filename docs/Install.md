# Neo4j Export Tool - Installation Guide

This guide provides instructions for installing and running the Neo4j Export Tool on different platforms.

## Table of Contents
- [Windows](#windows)
- [macOS](#macos)
- [Linux](#linux)
- [Docker](#docker)

## Download Binaries

Download the appropriate binary for your platform from the [latest release](https://github.com/vic-ffm/neo4j-export-tool/releases/latest):

| Platform | Architecture | Download |
|----------|--------------|----------|
| Windows | x64 (Intel/AMD) | `neo4j-export-windows-x64.zip` (includes exe, batch script, and instructions) |
| Windows | ARM64 | `neo4j-export-windows-arm64.zip` (includes exe, batch script, and instructions) |
| macOS | Apple Silicon (M1/M2/M3) | `neo4j-export-darwin-arm64` |
| macOS | Intel | `neo4j-export-darwin-amd64` |
| Linux | x64 (Intel/AMD) | `neo4j-export-linux-amd64` |
| Linux | ARM64 | `neo4j-export-linux-arm64` |

## Windows

### Quick Start with Windows Bundle

1. **Download and extract**
   - Download `neo4j-export-windows-x64.zip` (or `arm64` version for ARM devices)
   - Extract the zip file to a folder (e.g., `C:\neo4j-export\`)
   - The folder will contain:
     - `neo4j-export-windows-amd64.exe` (the export tool)
     - `run-neo4j-export-windows.bat` (batch script for easy setup)
     - `Install.txt` (quick start instructions)

2. **Configure the batch script**
   - Right-click `run-neo4j-export-windows.bat` and select "Edit" (or open in Notepad)
   - Modify the environment variables:
   ```batch
   set N4JET_NEO4J_URI=bolt://localhost:7687
   set N4JET_NEO4J_USER=neo4j
   set N4JET_NEO4J_PASSWORD=your-password-here
   set N4JET_OUTPUT_DIRECTORY=C:\neo4j-exports
   ```

3. **Run the export**
   - Double-click `run-neo4j-export-windows.bat`
   - The script will create the output directory if needed and run the export
   - Press any key to close the window when complete

### Manual Command Line Usage

```powershell
# Set environment variables
$env:N4JET_NEO4J_URI = "bolt://localhost:7687"
$env:N4JET_NEO4J_USER = "neo4j"
$env:N4JET_NEO4J_PASSWORD = "your-password"
$env:N4JET_OUTPUT_DIRECTORY = "C:\neo4j-exports"

# Run the export
.\neo4j-export-windows-amd64.exe
```

## macOS

### Quick Start

1. **Download and prepare the binary**
   ```bash
   # Download the appropriate binary (example for Apple Silicon)
   curl -L -o neo4j-export https://github.com/vic-ffm/neo4j-export-tool/releases/latest/download/neo4j-export-darwin-arm64

   # Make it executable
   chmod +x neo4j-export

   # Remove quarantine attribute (required on macOS)
   xattr -d com.apple.quarantine neo4j-export

   # Optionally move to PATH
   sudo mv neo4j-export /usr/local/bin/
   ```

2. **Run the export**
   ```bash
   # Set environment variables and run
   N4JET_NEO4J_URI="bolt://localhost:7687" \
   N4JET_NEO4J_USER="neo4j" \
   N4JET_NEO4J_PASSWORD="your-password" \
   N4JET_OUTPUT_DIRECTORY="./exports" \
   neo4j-export
   ```

### Setting Environment Variables

#### Bash (~/.bashrc or ~/.bash_profile)
```bash
# Add to your ~/.bashrc or ~/.bash_profile
export N4JET_NEO4J_URI="bolt://localhost:7687"
export N4JET_NEO4J_USER="neo4j"
export N4JET_NEO4J_PASSWORD="your-password"
export N4JET_OUTPUT_DIRECTORY="$HOME/neo4j-exports"

# Reload configuration
source ~/.bashrc
```

#### Zsh (~/.zshrc)
```zsh
# Add to your ~/.zshrc
export N4JET_NEO4J_URI="bolt://localhost:7687"
export N4JET_NEO4J_USER="neo4j"
export N4JET_NEO4J_PASSWORD="your-password"
export N4JET_OUTPUT_DIRECTORY="$HOME/neo4j-exports"

# Reload configuration
source ~/.zshrc
```

#### Fish (~/.config/fish/config.fish)
```fish
# Add to your ~/.config/fish/config.fish
set -x N4JET_NEO4J_URI "bolt://localhost:7687"
set -x N4JET_NEO4J_USER "neo4j"
set -x N4JET_NEO4J_PASSWORD "your-password"
set -x N4JET_OUTPUT_DIRECTORY "$HOME/neo4j-exports"

# Reload configuration
source ~/.config/fish/config.fish
```

## Linux

### Quick Start

1. **Download and prepare the binary**
   ```bash
   # Download the appropriate binary (example for x64)
   wget https://github.com/vic-ffm/neo4j-export-tool/releases/latest/download/neo4j-export-linux-amd64

   # Make it executable
   chmod +x neo4j-export-linux-amd64

   # Optionally move to PATH with renamed binary
   sudo mv neo4j-export-linux-amd64 /usr/local/bin/neo4j-export
   ```

2. **Run the export**
   ```bash
   # Set environment variables and run
   N4JET_NEO4J_URI="bolt://localhost:7687" \
   N4JET_NEO4J_USER="neo4j" \
   N4JET_NEO4J_PASSWORD="your-password" \
   N4JET_OUTPUT_DIRECTORY="./exports" \
   ./neo4j-export-linux-amd64
   ```

### Setting Environment Variables

#### Bash (~/.bashrc)
```bash
# Add to your ~/.bashrc
export N4JET_NEO4J_URI="bolt://localhost:7687"
export N4JET_NEO4J_USER="neo4j"
export N4JET_NEO4J_PASSWORD="your-password"
export N4JET_OUTPUT_DIRECTORY="$HOME/neo4j-exports"

# Optional: Add alias for convenience
alias neo4j-export='/usr/local/bin/neo4j-export'

# Reload configuration
source ~/.bashrc
```

#### Zsh (~/.zshrc)
```zsh
# Add to your ~/.zshrc
export N4JET_NEO4J_URI="bolt://localhost:7687"
export N4JET_NEO4J_USER="neo4j"
export N4JET_NEO4J_PASSWORD="your-password"
export N4JET_OUTPUT_DIRECTORY="$HOME/neo4j-exports"

# Optional: Add alias for convenience
alias neo4j-export='/usr/local/bin/neo4j-export'

# Reload configuration
source ~/.zshrc
```

#### Fish (~/.config/fish/config.fish)
```fish
# Add to your ~/.config/fish/config.fish
set -x N4JET_NEO4J_URI "bolt://localhost:7687"
set -x N4JET_NEO4J_USER "neo4j"
set -x N4JET_NEO4J_PASSWORD "your-password"
set -x N4JET_OUTPUT_DIRECTORY "$HOME/neo4j-exports"

# Optional: Add alias for convenience
alias neo4j-export '/usr/local/bin/neo4j-export'

# Reload configuration
source ~/.config/fish/config.fish
```

### Systemd Service (Optional)

Create a systemd service for scheduled exports:

```ini
# /etc/systemd/system/neo4j-export.service
[Unit]
Description=Neo4j Export Service
After=network.target

[Service]
Type=oneshot
User=your-username
Environment="N4JET_NEO4J_URI=bolt://localhost:7687"
Environment="N4JET_NEO4J_USER=neo4j"
Environment="N4JET_NEO4J_PASSWORD=your-password"
Environment="N4JET_OUTPUT_DIRECTORY=/var/neo4j-exports"
ExecStart=/usr/local/bin/neo4j-export

[Install]
WantedBy=multi-user.target
```

## Docker

### Using Docker Compose

1. **Download the required files**
   ```bash
   # Create a project directory
   mkdir neo4j-export && cd neo4j-export

   # Download the example environment file
   curl -L -o .env.example https://github.com/vic-ffm/neo4j-export-tool/raw/main/.env.example

   # Download the Docker Compose file
   curl -L -o neo4j-export-runner.compose.yaml https://github.com/vic-ffm/neo4j-export-tool/raw/main/neo4j-export-runner.compose.yaml

   # Download the Dockerfile
   curl -L -o Dockerfile https://github.com/vic-ffm/neo4j-export-tool/raw/main/Dockerfile
   ```

2. **Configure your environment**
   ```bash
   # Copy the example environment file
   cp .env.example .env

   # Edit .env with your Neo4j credentials and settings
   # Key settings to update:
   # - N4JET_NEO4J_URI (use bolt://host.docker.internal:7687 for local Neo4j)
   # - N4JET_NEO4J_USER
   # - N4JET_NEO4J_PASSWORD
   # - N4JET_OUTPUT_DIRECTORY (typically /data/export for Docker)
   ```

3. **Run the export**
   ```bash
   # Build and run
   docker compose -f neo4j-export-runner.compose.yaml up --build

   # Or run without building (if image exists)
   docker compose -f neo4j-export-runner.compose.yaml run --rm neo4j-export
   ```

### Using Docker CLI

```bash
# Run with environment file
docker run --rm \
  --env-file .env \
  -v $(pwd)/exports:/data/export \
  --network host \
  neo4j-export-fsharp:latest

# Run with inline environment variables
docker run --rm \
  -e N4JET_NEO4J_URI=bolt://localhost:7687 \
  -e N4JET_NEO4J_USER=neo4j \
  -e N4JET_NEO4J_PASSWORD=your-password \
  -e N4JET_OUTPUT_DIRECTORY=/data/export \
  -v $(pwd)/exports:/data/export \
  --network host \
  neo4j-export-fsharp:latest
```

### Building the Docker Image

```bash
# Clone the repository
git clone https://github.com/vic-ffm/neo4j-export-tool.git
cd neo4j-export-tool

# Build the image
docker build -t neo4j-export-fsharp:latest .
```

## Verifying Your Installation

After installation, verify the tool works correctly:

```bash
# Check version (sets minimal environment to avoid connection)
N4JET_NEO4J_URI=bolt://localhost:7687 N4JET_NEO4J_USER=test N4JET_NEO4J_PASSWORD=test ./neo4j-export --version

# Run with debug mode to test connection
N4JET_DEBUG=true ./neo4j-export
```

## Troubleshooting

### Common Issues

1. **Permission Denied (macOS/Linux)**
   ```bash
   chmod +x neo4j-export-*
   ```

2. **"Cannot be opened because it is from an unidentified developer" (macOS)**
   ```bash
   xattr -d com.apple.quarantine neo4j-export-*
   ```

3. **Connection Refused**
   - Verify Neo4j is running: `neo4j status`
   - Check the URI and port are correct
   - For Docker, use `host.docker.internal` instead of `localhost`

4. **Authentication Failed**
   - Verify username and password
   - Check if Neo4j requires authentication

### Getting Help

1. Enable debug mode: `N4JET_DEBUG=true`
2. Check the logs for detailed error messages
3. Refer to the [main documentation](./README.md)
4. Report issues on GitHub

## Next Steps

- See [Configuration Guide](./Configuration.md) for all available options
- Read [Metadata.md](./Metadata.md) to understand the export format
- Check [Neo4JExportToolID.md](./Neo4JExportToolID.md) for content-based ID specification
