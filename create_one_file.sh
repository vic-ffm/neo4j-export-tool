#!/bin/bash

# Create one_file.fs by concatenating all F# source files
# with clear file name markers

OUTPUT_FILE="one_file.fs"
SOURCE_DIR="Neo4jExport/src"

# Remove existing output file if it exists
rm -f "$OUTPUT_FILE"

# Header for the concatenated file
cat >> "$OUTPUT_FILE" << 'EOF'
// ============================================================================
// CONCATENATED F# SOURCE FILES FROM Neo4jExport/src/
// Generated on: $(date)
// ============================================================================
//
// This file contains all F# source files from the Neo4jExport project
// concatenated together with clear file boundaries.
//
// IMPORTANT: This is for reference only. Do not compile this file directly.
// ============================================================================

EOF

# Replace $(date) with actual date
sed -i '' "s/\$(date)/$(date)/" "$OUTPUT_FILE" 2>/dev/null || sed -i "s/\$(date)/$(date)/" "$OUTPUT_FILE"

# Function to add a file with clear boundaries
add_file() {
    local file="$1"
    local filename=$(basename "$file")
    
    echo "" >> "$OUTPUT_FILE"
    echo "// ============================================================================" >> "$OUTPUT_FILE"
    echo "// FILE: $filename" >> "$OUTPUT_FILE"
    echo "// PATH: $file" >> "$OUTPUT_FILE"
    echo "// ============================================================================" >> "$OUTPUT_FILE"
    echo "" >> "$OUTPUT_FILE"
    
    cat "$file" >> "$OUTPUT_FILE"
    
    echo "" >> "$OUTPUT_FILE"
    echo "// ============================================================================" >> "$OUTPUT_FILE"
    echo "// END OF FILE: $filename" >> "$OUTPUT_FILE"
    echo "// ============================================================================" >> "$OUTPUT_FILE"
    echo "" >> "$OUTPUT_FILE"
}

# Process files in the order they appear in the .fsproj file
# This ensures proper F# compilation order is documented
if [ -f "Neo4jExport/Neo4jExport.fsproj" ]; then
    echo "Processing files in project order..." >&2
    
    # Extract file paths from .fsproj
    grep '<Compile Include="src/' Neo4jExport/Neo4jExport.fsproj | \
        sed 's/.*Include="src\/\([^"]*\)".*/\1/' | \
        while read -r file; do
            if [ -f "$SOURCE_DIR/$file" ]; then
                echo "Adding $file..." >&2
                add_file "$SOURCE_DIR/$file"
            fi
        done
else
    echo "Warning: .fsproj file not found. Processing files alphabetically..." >&2
    
    # Fallback: process all .fs files alphabetically
    for file in "$SOURCE_DIR"/*.fs; do
        if [ -f "$file" ]; then
            echo "Adding $(basename "$file")..." >&2
            add_file "$file"
        fi
    done
fi

# Add footer
cat >> "$OUTPUT_FILE" << 'EOF'

// ============================================================================
// END OF CONCATENATED FILES
// ============================================================================
// 
// Total files concatenated: $(count)
// ============================================================================
EOF

# Count files and update footer
FILE_COUNT=$(grep -c "^// FILE: " "$OUTPUT_FILE")
sed -i '' "s/\$(count)/$FILE_COUNT/" "$OUTPUT_FILE" 2>/dev/null || sed -i "s/\$(count)/$FILE_COUNT/" "$OUTPUT_FILE"

echo "Successfully created $OUTPUT_FILE with $FILE_COUNT F# source files."
echo "File size: $(ls -lh "$OUTPUT_FILE" | awk '{print $5}')"