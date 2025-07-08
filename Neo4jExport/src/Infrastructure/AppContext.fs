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

namespace Neo4jExport

open System.Threading
open System.Diagnostics
open System.Collections.Concurrent

/// Application context management for lifecycle and resource tracking
module AppContext =
    /// Creates a new application context with initialized resources
    let create () =
        { CancellationTokenSource = new CancellationTokenSource()
          TempFiles = new ConcurrentBag<string>()
          ActiveProcesses = new ConcurrentBag<Process>() }

    /// Gets the cancellation token from the context
    let getCancellationToken (context: ApplicationContext) = context.CancellationTokenSource.Token

    /// Checks if cancellation has been requested
    let isCancellationRequested (context: ApplicationContext) = CancellationOperations.check context

    /// Requests cancellation of all operations
    let cancel (context: ApplicationContext) =
        context.CancellationTokenSource.Cancel()

    /// Registers a temporary file for cleanup
    let addTempFile (context: ApplicationContext) (path: string) = context.TempFiles.Add(path)
