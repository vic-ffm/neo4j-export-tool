#!/bin/bash

set -e

OS=$(uname -s | tr '[:upper:]' '[:lower:]')
ARCH=$(uname -m)

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

RID="${RUNTIME_OS}-${RUNTIME_ARCH}"
OUTPUT_NAME="neo4j-export-${OS_NAME}-${ARCH}"
if [ "$OS_NAME" = "windows" ]; then
    OUTPUT_NAME="${OUTPUT_NAME}.exe"
fi

echo "Building for platform: $RID"
echo "Output binary: dist/$OUTPUT_NAME"

mkdir -p dist

dotnet publish Neo4jExport/Neo4jExport.fsproj \
    -c Release \
    -r "$RID" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -p:DebugType=embedded \
    -o ./publish-temp

mv "./publish-temp/$BINARY_NAME" "./dist/$OUTPUT_NAME"

rm -rf ./publish-temp

if [ "$OS_NAME" != "windows" ]; then
    chmod +x "./dist/$OUTPUT_NAME"
fi

echo "✓ Build complete: dist/$OUTPUT_NAME"
ls -lh "dist/$OUTPUT_NAME"