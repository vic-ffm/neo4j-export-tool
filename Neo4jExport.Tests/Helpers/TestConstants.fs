module Neo4jExport.Tests.Helpers.TestConstants

/// Test-specific configuration constants following the N4JET pattern
module Env =
    /// Controls the minimum log level for test output
    /// Values: Debug, Info, Warn, Error, Fatal (case-insensitive)
    /// Default: Info
    let TestLogLevel = "N4JET_TEST_LOG_LEVEL"

/// Default values for test configuration
module Defaults =
    /// Default log level for tests
    let TestLogLevel = "Info"
