#!/bin/bash

# Get the directory where this script is located
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
PROJECT_ROOT="$( cd "$SCRIPT_DIR/.." && pwd )"

if [ $# -eq 0 ]; then
    echo "Usage: $0 <new-version>"
    echo "Example: $0 0.11.0"
    exit 1
fi

NEW_VERSION=$1

if ! [[ $NEW_VERSION =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "Error: Version must be in format X.Y.Z (e.g., 0.11.0)"
    exit 1
fi

echo "Updating version to $NEW_VERSION..."

echo "$NEW_VERSION" > "$PROJECT_ROOT/.version"

sed -i.bak "s|<Version>.*</Version>|<Version>$NEW_VERSION</Version>|g" "$PROJECT_ROOT/Directory.Build.props"
sed -i.bak "s|<AssemblyVersion>.*</AssemblyVersion>|<AssemblyVersion>$NEW_VERSION.0</AssemblyVersion>|g" "$PROJECT_ROOT/Directory.Build.props"
sed -i.bak "s|<FileVersion>.*</FileVersion>|<FileVersion>$NEW_VERSION.0</FileVersion>|g" "$PROJECT_ROOT/Directory.Build.props"
rm "$PROJECT_ROOT/Directory.Build.props.bak"

sed -i.bak "s|Version [0-9]\+\.[0-9]\+\.[0-9]\+|Version $NEW_VERSION|g" "$PROJECT_ROOT/README.md"
rm "$PROJECT_ROOT/README.md.bak"

echo "✓ Version updated to $NEW_VERSION"
echo ""
echo "Files updated:"
echo "  - .version"
echo "  - Directory.Build.props" 
echo "  - README.md"
echo ""
echo "The version will be automatically used in:"
echo "  - F# code (via assembly version)"
echo "  - Makefile (via .version file)"
echo "  - Docker compose (via VERSION env var)"