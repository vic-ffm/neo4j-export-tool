@echo off
REM ===============================================
REM Neo4j Export Tool - Windows Batch Script
REM ===============================================
REM This script sets up the environment and runs the Neo4j export tool
REM Modify the values below according to your environment

REM ===============================================
REM Neo4j Connection Settings
REM ===============================================

REM Neo4j connection URI (common Windows patterns)
REM For local Neo4j: bolt://localhost:7687
REM For remote Neo4j: bolt://your-server.domain.com:7687
REM For Neo4j Aura: neo4j+s://xxxxxxxx.databases.neo4j.io
set NEO4J_URI=bolt://localhost:7687

REM Neo4j credentials
set NEO4J_USER=neo4j
set NEO4J_PASSWORD=your-password-here

REM ===============================================
REM Output Configuration
REM ===============================================

REM Output directory - common Windows paths
REM Examples:
REM   C:\neo4j-exports
REM   C:\Users\%USERNAME%\Documents\neo4j-exports
REM   D:\data\exports
REM   \\network-share\exports
set OUTPUT_DIRECTORY=C:\neo4j-exports

REM Create output directory if it doesn't exist
if not exist "%OUTPUT_DIRECTORY%" (
    echo Creating output directory: %OUTPUT_DIRECTORY%
    mkdir "%OUTPUT_DIRECTORY%"
)

REM ===============================================
REM Resource Management (Optional)
REM ===============================================

REM Minimum free disk space in GB (default: 10)
set MIN_DISK_GB=10

REM Maximum memory usage in MB (default: 1024)
set MAX_MEMORY_MB=1024

REM ===============================================
REM Export Behavior (Optional)
REM ===============================================

REM Skip schema collection for faster exports (true/false)
set SKIP_SCHEMA_COLLECTION=false

REM Batch size for processing (default: 10000)
set BATCH_SIZE=10000

REM ===============================================
REM Error Handling and Resilience (Optional)
REM ===============================================

REM Maximum retry attempts (default: 5)
set MAX_RETRIES=5

REM Initial retry delay in milliseconds (default: 1000)
set RETRY_DELAY_MS=1000

REM Query timeout in seconds (default: 300)
set QUERY_TIMEOUT_SECONDS=300

REM ===============================================
REM Debugging (Optional)
REM ===============================================

REM Enable debug logging (true/false)
set DEBUG=false

REM Validate JSON output (true/false)
set VALIDATE_JSON=true

REM ===============================================
REM Security Settings (Optional)
REM ===============================================

REM Allow insecure TLS connections (true/false)
REM WARNING: Only set to true for self-signed certificates in dev/test
set ALLOW_INSECURE=false

REM ===============================================
REM Display Configuration
REM ===============================================

echo.
echo Neo4j Export Tool Configuration:
echo ================================
echo Neo4j URI: %NEO4J_URI%
echo Neo4j User: %NEO4J_USER%
echo Output Directory: %OUTPUT_DIRECTORY%
echo Debug Mode: %DEBUG%
echo.

REM ===============================================
REM Run the Export Tool
REM ===============================================

echo Starting Neo4j export...
echo.

REM For Windows x64 (AMD64) - Most common
neo4j-export-windows-amd64.exe

REM For Windows ARM64 (uncomment if using ARM64 Windows)
REM neo4j-export-windows-arm64.exe

REM ===============================================
REM Check Exit Code
REM ===============================================

if %ERRORLEVEL% EQU 0 (
    echo.
    echo Export completed successfully!
    echo Check the output directory: %OUTPUT_DIRECTORY%
) else (
    echo.
    echo Export failed with error code: %ERRORLEVEL%
    echo.
    echo Common error codes:
    echo   1 = Configuration error
    echo   2 = Connection error
    echo   3 = Export error
    echo   4 = File system error
    echo   5 = Cancellation requested
    echo   99 = Unknown error
)

REM ===============================================
REM Optional: Open output directory in Explorer
REM ===============================================

REM Uncomment the line below to automatically open the export directory
REM explorer "%OUTPUT_DIRECTORY%"

REM ===============================================
REM Keep window open to see results
REM ===============================================

echo.
pause