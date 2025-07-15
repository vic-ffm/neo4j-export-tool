module Neo4jExport.Tests.EndToEnd.Infrastructure.ContainerLifecycle

open System
open System.Threading
open System.Collections.Concurrent
open Neo4j.Driver
open Testcontainers.Neo4j
open DotNet.Testcontainers.Builders
open DotNet.Testcontainers.Configurations
open Expecto
open Neo4jExport.Tests.Helpers.TestLog

// Container state using a singleton pattern for reuse across tests
type ContainerState =
    | NotStarted
    | Starting of Async<Neo4jContainer>
    | Started of Neo4jContainer
    | Failed of exn
    | Disposed

// Thread-safe container registry for version-specific containers
type private ContainerRegistry() =
    static let containers =
        ConcurrentDictionary<string, ContainerState ref>()

    static member GetOrCreate(version: string) : ContainerState ref =
        containers.GetOrAdd(version, fun _ -> ref NotStarted)

// Configuration for Neo4j containers
type Neo4jContainerConfig =
    { Version: string
      StartupTimeout: TimeSpan
      EnableDebugLogging: bool }

// Default configurations
module ContainerConfig =
    let defaultConfig =
        { Version = "5.26.9"
          StartupTimeout = TimeSpan.FromSeconds(30.0)
          EnableDebugLogging = false }

    let neo4j4Config =
        { defaultConfig with
            Version = "4.4.44" }

    let neo4j5Config = defaultConfig

// Container lifecycle management
module ContainerLifecycle =

    // Create a new Neo4j container with the specified configuration
    let private createContainer (config: Neo4jContainerConfig) =
        let builder = Neo4jBuilder()
        let password = "testpassword123" // Neo4j 5+ requires 8+ character passwords

        let guidPrefix =
            Guid.NewGuid().ToString("N").Substring(0, 8)

        builder
            .WithImage($"neo4j:{config.Version}")
            .WithName($"neo4j-test-{config.Version}-{guidPrefix}")
            // Note: WithAdminPassword method name may vary by Testcontainers version
            // Update this based on actual API available in 4.6.0
            .WithEnvironment("NEO4J_AUTH", $"neo4j/{password}")
            .WithWaitStrategy(
                Wait.ForUnixContainer().UntilPortIsAvailable(7687).UntilCommandIsCompleted("cypher-shell", "--help")
            )
        |> ignore

        if config.EnableDebugLogging then
            builder.WithOutputConsumer(Consume.RedirectStdoutAndStderrToConsole())
            |> ignore

        builder.Build()

    // Start or get an existing container for the specified version
    let getOrStartContainer (config: Neo4jContainerConfig) : Async<Result<Neo4jContainer, exn>> =
        async {
            let containerRef =
                ContainerRegistry.GetOrCreate(config.Version)

            containerOperation "Requesting container" $"neo4j:{config.Version}"

            // Thread-safe container startup
            let rec ensureStarted () =
                async {
                    match !containerRef with
                    | Started container ->
                        containerOperation "Container already running" $"neo4j:{config.Version}"
                        return Ok container
                    | Failed ex ->
                        containerOperation "Container previously failed" $"neo4j:{config.Version}"
                        return Error ex
                    | Disposed ->
                        containerOperation "Container was disposed" $"neo4j:{config.Version}"
                        return Error(ObjectDisposedException("Container has been disposed"))
                    | NotStarted ->
                        // Try to transition to Starting state
                        let startAsync =
                            async {
                                try
                                    containerOperation "Creating container" $"neo4j:{config.Version}"
                                    let container = createContainer config
                                    containerOperation "Starting container" $"neo4j:{config.Version}"
                                    do! container.StartAsync() |> Async.AwaitTask
                                    containerOperation "Container started successfully" $"neo4j:{config.Version}"
                                    containerRef := Started container
                                    return container
                                with ex ->
                                    containerOperation
                                        $"Container startup failed: {ex.Message}"
                                        $"neo4j:{config.Version}"

                                    containerRef := Failed ex
                                    return raise ex
                            }

                        // Use lock for thread-safe state transition
                        let shouldStart =
                            lock containerRef (fun () ->
                                match !containerRef with
                                | NotStarted ->
                                    containerRef := Starting startAsync
                                    true
                                | _ -> false)

                        if shouldStart then
                            // We won the race, start the container
                            try
                                let! container = startAsync
                                return Ok container
                            with ex ->
                                return Error ex
                        else
                            // Someone else is starting it, wait and retry
                            do! Async.Sleep 100
                            return! ensureStarted ()

                    | Starting startAsync ->
                        // Container is being started by another thread, wait for completion
                        try
                            let! container = startAsync
                            return Ok container
                        with ex ->
                            return Error ex
                }

            return! ensureStarted ()
        }

    // Get the connection string for a running container
    let getConnectionString (container: Neo4jContainer) = container.GetConnectionString()

    // Dispose a specific container
    let disposeContainer (version: string) : Async<unit> =
        async {
            let containerRef =
                ContainerRegistry.GetOrCreate(version)

            match !containerRef with
            | Started container ->
                containerRef := Disposed

                do!
                    container.DisposeAsync().AsTask()
                    |> Async.AwaitTask
            | _ -> ()
        }

    // Dispose all containers (for cleanup in test teardown)
    let disposeAllContainers () : Async<unit> =
        async {
            // Note: This requires making containers field accessible or adding a GetAllVersions method to ContainerRegistry
            // For now, track versions externally or modify ContainerRegistry to expose registered versions
            let versions = [ "4.4.44"; "5.26.9" ] // Known versions we use

            let! _ =
                versions
                |> Seq.map disposeContainer
                |> Async.Parallel

            return ()
        }

// Test fixture helpers for Expecto integration
module ContainerFixture =

    // Container info for tests that need connection details
    type ContainerInfo =
        { Driver: IDriver
          ConnectionString: string
          Password: string }

    // Execute a test with a Neo4j container
    let withContainer (config: Neo4jContainerConfig) (testFunc: IDriver -> Async<unit>) : Async<unit> =
        async {
            match! ContainerLifecycle.getOrStartContainer config with
            | Ok container ->
                let connectionString =
                    ContainerLifecycle.getConnectionString container
                // Using the same password as configured in createContainer
                let password = "testpassword123"

                use driver =
                    GraphDatabase.Driver(connectionString, AuthTokens.Basic("neo4j", password))

                // Verify connection
                use session = driver.AsyncSession()
                let! cursor = session.RunAsync("RETURN 1") |> Async.AwaitTask
                let! _ = cursor.SingleAsync() |> Async.AwaitTask

                // Run the test
                do! testFunc driver

            | Error ex -> failtest $"Failed to start Neo4j container: {ex.Message}"
        }

    // Execute a test with container info
    let withContainerInfo (config: Neo4jContainerConfig) (testFunc: ContainerInfo -> Async<unit>) : Async<unit> =
        async {
            match! ContainerLifecycle.getOrStartContainer config with
            | Ok container ->
                let connectionString =
                    ContainerLifecycle.getConnectionString container

                let password = "testpassword123"

                use driver =
                    GraphDatabase.Driver(connectionString, AuthTokens.Basic("neo4j", password))

                // Verify connection
                use session = driver.AsyncSession()
                let! cursor = session.RunAsync("RETURN 1") |> Async.AwaitTask
                let! _ = cursor.SingleAsync() |> Async.AwaitTask

                // Run the test with full info
                let info =
                    { Driver = driver
                      ConnectionString = connectionString
                      Password = password }

                do! testFunc info

            | Error ex -> failtest $"Failed to start Neo4j container: {ex.Message}"
        }

    // Execute a test with the default container
    let withDefaultContainer (testFunc: IDriver -> Async<unit>) : Async<unit> =
        withContainer ContainerConfig.defaultConfig testFunc

    // Helper to create testAsync with container
    let testWithContainer name config testFunc =
        testAsync name { do! withContainer config testFunc }

    // Helper for default container tests
    let testWithDefaultContainer name testFunc =
        testWithContainer name ContainerConfig.defaultConfig testFunc
