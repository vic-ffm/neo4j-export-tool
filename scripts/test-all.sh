#!/bin/bash
# Comprehensive test run - includes all tests including container tests

echo "Running ALL tests (including container tests)..."
echo "=============================================="
echo "This will take approximately 8-10 minutes due to container startup times."
echo ""

# Set test log level if not already set
export N4JET_TEST_LOG_LEVEL="${N4JET_TEST_LOG_LEVEL:-Info}"

# Navigate to project root if not already there
PROJECT_ROOT="$(dirname "$0")/.."
cd "$PROJECT_ROOT"

# Run all tests
dotnet run --project Neo4jExport.Tests/Neo4jExport.Tests.fsproj -- --summary

echo ""
echo "Comprehensive test run complete!"
echo "For faster feedback during development, use: ./scripts/test-quick.sh"