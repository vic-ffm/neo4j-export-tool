#!/bin/bash
# Quick feedback script - provides guidance for fast test runs

echo "==============================================================="
echo "                    QUICK TEST GUIDANCE"
echo "==============================================================="
echo ""
echo "For fast feedback during development, you have several options:"
echo ""
echo "1. Run dotnet build to check compilation:"
echo "   $ dotnet build"
echo ""
echo "2. Run specific test files during active development:"
echo "   $ dotnet run --project Neo4jExport.Tests/Neo4jExport.Tests.fsproj -- --filter \"Serialization\""
echo "   $ dotnet run --project Neo4jExport.Tests/Neo4jExport.Tests.fsproj -- --filter \"Workflow\""
echo ""
echo "3. Run all non-container tests (manual list):"
echo "   Unfortunately, Expecto doesn't support including/excluding test patterns well."
echo "   Container tests take 8-10 minutes and cannot be easily excluded."
echo ""
echo "4. For comprehensive testing before commits:"
echo "   $ ./scripts/test-all.sh"
echo ""
echo "==============================================================="
echo ""

# Set test log level if not already set
export N4JET_TEST_LOG_LEVEL="${N4JET_TEST_LOG_LEVEL:-Info}"

# Navigate to project root if not already there
PROJECT_ROOT="$(dirname "$0")/.."
cd "$PROJECT_ROOT"

# Just build for now to ensure compilation
echo "Running build to ensure compilation..."
dotnet build --nologo

if [ $? -eq 0 ]; then
    echo ""
    echo "✅ Build successful!"
    echo ""
    echo "To run specific tests, use the examples above."
    echo "To run ALL tests (including slow container tests), use: ./scripts/test-all.sh"
else
    echo ""
    echo "❌ Build failed - fix compilation errors before running tests."
    exit 1
fi