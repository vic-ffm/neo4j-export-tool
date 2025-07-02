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
    let isCancellationRequested (context: ApplicationContext) =
        context.CancellationTokenSource.Token.IsCancellationRequested

    /// Requests cancellation of all operations
    let cancel (context: ApplicationContext) =
        context.CancellationTokenSource.Cancel()

    /// Registers a temporary file for cleanup
    let addTempFile (context: ApplicationContext) (path: string) = context.TempFiles.Add(path)
