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
echo ""

# Find all F# source files and process them
while IFS= read -r -d '' file; do
    process_file "$file"
done < <(find ../Neo4jExport/src -name "*.fs" -type f -print0 | sort -z)

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