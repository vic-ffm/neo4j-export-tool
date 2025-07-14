#!/bin/bash

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

# Counter variables
PROCESSED=0
SKIPPED=0
ADDED=0

# Get the directory where this script is located
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
# Get the parent directory (project root)
PROJECT_ROOT="$( cd "$SCRIPT_DIR/.." && pwd )"
SOLUTION_FILE="$PROJECT_ROOT/Neo4jExport.sln"

# Create the F# comment header from LICENSE file
create_header() {
    cat << 'EOF'
// MIT License
// 
// Copyright (c) 2025-present State Government of Victoria
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

EOF
}

# Check if file already has the license header
has_license_header() {
    local file="$1"
    # Check for both "MIT License" and "Copyright (c)" in the first 30 lines
    if head -n 30 "$file" | grep -q "MIT License" && head -n 30 "$file" | grep -q "Copyright (c).*State Government of Victoria"; then
        return 0
    else
        return 1
    fi
}

# Add license header to a file
add_license_to_file() {
    local file="$1"
    local temp_file="${file}.tmp"
    
    # Create temporary file with header + original content
    create_header > "$temp_file"
    cat "$file" >> "$temp_file"
    
    # Replace original file
    mv "$temp_file" "$file"
}

# Process a single F# file
process_file() {
    local file="$1"
    local relative_path="${file#$PWD/}"
    
    ((PROCESSED++))
    
    if has_license_header "$file"; then
        echo -e "${YELLOW}[SKIP]${NC} $relative_path - already has license header"
        ((SKIPPED++))
    else
        add_license_to_file "$file"
        echo -e "${GREEN}[ADD]${NC} $relative_path - license header added"
        ((ADDED++))
    fi
}

echo "Adding MIT license headers to F# source files..."
echo "============================================="
echo "Solution: $SOLUTION_FILE"

# Use process substitution to avoid subshell issues with counters
while IFS= read -r project_relative_path; do
    # Convert Windows paths to Unix paths
    project_relative_path=$(echo "$project_relative_path" | tr '\\' '/')
    
    # Convert to full path
    project_full_path="$PROJECT_ROOT/$project_relative_path"
    
    if [ -f "$project_full_path" ]; then
        project_name=$(basename "$project_full_path" .fsproj)
        echo ""
        echo "Processing project: $project_name"
        echo "----------------------------------------"
        
        # Extract all <Compile Include="..."> entries from the project file
        while IFS= read -r relative_path; do
            # Convert relative path to full path
            project_dir=$(dirname "$project_full_path")
            full_path="$project_dir/$relative_path"
            
            if [ -f "$full_path" ]; then
                process_file "$full_path"
            else
                echo -e "${RED}[ERROR]${NC} File not found: $full_path"
            fi
        done < <(grep '<Compile Include="' "$project_full_path" | sed 's/.*Include="\([^"]*\)".*/\1/')
    else
        echo -e "${RED}[ERROR]${NC} Project file not found: $project_full_path"
    fi
done < <(grep -E 'Project.*\.fsproj"' "$SOLUTION_FILE" | sed -E 's/.*"([^"]+\.fsproj)".*/\1/')

# Summary
echo ""
echo "============================================="
echo "Summary:"
echo "  Total files processed: $PROCESSED"
echo -e "  ${GREEN}Headers added: $ADDED${NC}"
echo -e "  ${YELLOW}Already had headers: $SKIPPED${NC}"
echo ""

if [ $ADDED -gt 0 ]; then
    echo -e "${GREEN}✓ Successfully added license headers to $ADDED files.${NC}"
else
    echo -e "${YELLOW}✓ All files already have license headers. No changes made.${NC}"
fi