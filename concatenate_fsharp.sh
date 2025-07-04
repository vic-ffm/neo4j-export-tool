#!/bin/bash

# Script to concatenate all F# files in Neo4jExport directory
# Output file will be created in the current directory

OUTPUT_FILE="concatenated_fsharp_files.txt"

# Clear the output file if it exists
> "$OUTPUT_FILE"

echo "Concatenating F# files from Neo4jExport directory..."
echo "================================================" >> "$OUTPUT_FILE"
echo "F# Files Concatenation" >> "$OUTPUT_FILE"
echo "Generated on: $(date)" >> "$OUTPUT_FILE"
echo "================================================" >> "$OUTPUT_FILE"
echo "" >> "$OUTPUT_FILE"

# Function to add a file with header, removing copyright notice
add_file_with_header() {
    local file="$1"
    if [ -f "$file" ]; then
        echo "" >> "$OUTPUT_FILE"
        echo "=================================================================================" >> "$OUTPUT_FILE"
        echo "FILE: $file" >> "$OUTPUT_FILE"
        echo "=================================================================================" >> "$OUTPUT_FILE"
        echo "" >> "$OUTPUT_FILE"
        
        # Remove copyright notice (MIT License block) from F# files
        if [[ "$file" == *.fs ]]; then
            # Use awk to skip the copyright notice at the beginning of the file
            # The pattern matches from "// MIT License" to "// SOFTWARE." (inclusive)
            awk '
                BEGIN { skip = 0; found_end = 0 }
                /^\/\/ MIT License/ { skip = 1 }
                /^\/\/ SOFTWARE\.$/ { if (skip) { found_end = 1; next } }
                { 
                    if (skip && found_end) { skip = 0; found_end = 0 }
                    if (!skip) print 
                }
            ' "$file" | sed '/^$/N;/^\n$/d' >> "$OUTPUT_FILE"
        else
            # For non-.fs files (like .fsproj), just cat normally
            cat "$file" >> "$OUTPUT_FILE"
        fi
        
        echo "" >> "$OUTPUT_FILE"
        echo "Successfully added: $file"
    else
        echo "Warning: File not found - $file"
    fi
}

# Add the project file first
add_file_with_header "Neo4jExport/Neo4jExport.fsproj"

# Add all F# source files in src/ directory (in order they appear in the project)
# Based on the typical F# compilation order, we'll add them systematically

# Core type definitions and utilities first
add_file_with_header "Neo4jExport/src/Constants.fs"
add_file_with_header "Neo4jExport/src/Types.fs"
add_file_with_header "Neo4jExport/src/Log.fs"
add_file_with_header "Neo4jExport/src/Utils.fs"
add_file_with_header "Neo4jExport/src/JsonConfig.fs"
add_file_with_header "Neo4jExport/src/JsonHelpers.fs"
add_file_with_header "Neo4jExport/src/RecordTypes.fs"
add_file_with_header "Neo4jExport/src/Security.fs"
add_file_with_header "Neo4jExport/src/ErrorTracking.fs"
add_file_with_header "Neo4jExport/src/LabelStatsTracker.fs"

# Export serialization modules
add_file_with_header "Neo4jExport/src/Export/Serialization/Context.fs"
add_file_with_header "Neo4jExport/src/Export/Serialization/Primitives.fs"
add_file_with_header "Neo4jExport/src/Export/Serialization/Collections.fs"
add_file_with_header "Neo4jExport/src/Export/Serialization/Temporal.fs"
add_file_with_header "Neo4jExport/src/Export/Serialization/Spatial.fs"
add_file_with_header "Neo4jExport/src/Export/Serialization/GraphElements.fs"
add_file_with_header "Neo4jExport/src/Export/Serialization/Path.fs"
add_file_with_header "Neo4jExport/src/Export/Serialization/Engine.fs"

# Export core modules
add_file_with_header "Neo4jExport/src/Export/Types.fs"
add_file_with_header "Neo4jExport/src/Export/Utils.fs"
add_file_with_header "Neo4jExport/src/Export/Core.fs"
add_file_with_header "Neo4jExport/src/Export/BatchProcessing.fs"

# Application modules
add_file_with_header "Neo4jExport/src/Configuration.fs"
add_file_with_header "Neo4jExport/src/AppContext.fs"
add_file_with_header "Neo4jExport/src/Neo4j.fs"
add_file_with_header "Neo4jExport/src/Metadata.fs"
add_file_with_header "Neo4jExport/src/MetadataWriter.fs"
add_file_with_header "Neo4jExport/src/Export.fs"
add_file_with_header "Neo4jExport/src/Monitoring.fs"
add_file_with_header "Neo4jExport/src/SignalHandling.fs"
add_file_with_header "Neo4jExport/src/Cleanup.fs"
add_file_with_header "Neo4jExport/src/Preflight.fs"
add_file_with_header "Neo4jExport/src/Workflow.fs"
add_file_with_header "Neo4jExport/src/Program.fs"

echo ""
echo "================================================"
echo "Concatenation complete!"
echo "Output file: $OUTPUT_FILE"
echo "Total size: $(du -h "$OUTPUT_FILE" | cut -f1)"
echo "Total lines: $(wc -l < "$OUTPUT_FILE")"
echo "================================================"