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

/// Entry point for the Neo4j Export test suite
module Neo4jExport.Tests.Program

open Expecto

/// Main entry point - runs all tests in the assembly
[<EntryPoint>]
let main argv =
    // Register custom FsCheck generators
    Helpers.Generators.registerGenerators ()

    // Configure Expecto for better output based on log level
    let cliArgs =
        let logLevel =
            System.Environment.GetEnvironmentVariable(Helpers.TestConstants.Env.TestLogLevel)

        match logLevel with
        | "Debug"
        | "debug"
        | "DEBUG" -> [| "--debug" |]
        | _ -> [||]

    // Combine with user-provided arguments
    let allArgs = Array.append cliArgs argv

    // Run all tests found in the assembly
    Tests.runTestsInAssemblyWithCLIArgs [] allArgs
