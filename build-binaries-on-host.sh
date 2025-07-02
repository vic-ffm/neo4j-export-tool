#!/bin/bash
# Build script for native compilation on the host machine
# This ensures binaries are placed in the /dist directory consistently

set -e

# Detect the current platform
OS=$(uname -s | tr '[:upper:]' '[:lower:]')
ARCH=$(uname -m)

# Map architecture names
case "$ARCH" in
    x86_64)
        ARCH="amd64"
        RUNTIME_ARCH="x64"
        ;;
    aarch64|arm64)
        ARCH="arm64"
        RUNTIME_ARCH="arm64"
        ;;
    *)
        echo "Unsupported architecture: $ARCH"
        exit 1
        ;;
esac

# Map OS names for runtime identifier
case "$OS" in
    darwin)
        OS_NAME="darwin"
        RUNTIME_OS="osx"
        BINARY_NAME="neo4j-export"
        ;;
    linux)
        OS_NAME="linux"
        RUNTIME_OS="linux"
        BINARY_NAME="neo4j-export"
        ;;
    mingw*|cygwin*|msys*)
        OS_NAME="windows"
        RUNTIME_OS="win"
        BINARY_NAME="neo4j-export.exe"
        ;;
    *)
        echo "Unsupported OS: $OS"
        exit 1
        ;;
esac

# Construct runtime identifier
RID="${RUNTIME_OS}-${RUNTIME_ARCH}"
OUTPUT_NAME="neo4j-export-${OS_NAME}-${ARCH}"
if [ "$OS_NAME" = "windows" ]; then
    OUTPUT_NAME="${OUTPUT_NAME}.exe"
fi

echo "Building for platform: $RID"
echo "Output binary: dist/$OUTPUT_NAME"

# Create dist directory if it doesn't exist
mkdir -p dist

# Build the project
dotnet publish Neo4jExport/Neo4jExport.fsproj \
    -c Release \
    -r "$RID" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -p:DebugType=embedded \
    -o ./publish-temp

# Move the binary to dist with the correct name
mv "./publish-temp/$BINARY_NAME" "./dist/$OUTPUT_NAME"

# Clean up temporary directory
rm -rf ./publish-temp

# Make executable (not needed on Windows)
if [ "$OS_NAME" != "windows" ]; then
    chmod +x "./dist/$OUTPUT_NAME"
fi

echo "✓ Build complete: dist/$OUTPUT_NAME"
ls -lh "dist/$OUTPUT_NAME"