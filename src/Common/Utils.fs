namespace Alma.KafkaApplication

open Alma.ApplicationStatus
open Microsoft.Extensions.Logging

type Serialize = Serialize of (obj -> string)

type Git = {
    Branch: GitBranch option
    Commit: GitCommit option
    Repository: GitRepository option
}

[<AutoOpen>]
module internal Utils =
    let tee f a =
        f a
        a

    [<RequireQualifiedAccess>]
    module Map =
        /// Merge new values with the current values (replacing already defined values).
        let merge currentValues newValues =
            currentValues
            |> Map.fold (fun merged name connection ->
                if merged |> Map.containsKey name then merged
                else merged.Add(name, connection)
            ) newValues

    module FileParser =
        open System.IO
        open Feather.ErrorHandling

        type FilePath = string

        let parseFromPath parse notFound (path: FilePath): Result<'Parsed, 'Error> =
            result {
                if not (File.Exists(path)) then
                    return! Error (notFound path)

                return! parse path
            }

    module Serializer =
        open Alma.Serializer

        let toJson = Serialize Serialize.toJson

    type ApplicationState =
        | Off
        | Building
        | Starting
        | Running
        | Retrying
        | Stopping
        | ShuttingDown

    type ChangeApplicationState = ILogger -> unit
    type IsApplicationState = ILogger -> unit -> bool
    type IsOneOfApplicationStates = ILogger -> ApplicationState list -> bool

    [<RequireQualifiedAccess>]
    module ApplicationState =
        let mutable private applicationState = Off

        let private changeTo (state: ApplicationState) (logger: ILogger) =
            match applicationState, state with
            | oldState, newState when oldState = newState ->
                logger.LogDebug("Application state is not changed, it is already {state}", applicationState)

            | Off, Building
            | Building, Starting
            | Starting, Running
            | Retrying, Running
            | Running, Retrying
            | _, Stopping
            | _, ShuttingDown
            | ShuttingDown, Off ->
                applicationState <- state
                logger.LogDebug("Application state changed to {state}", applicationState)

            | _ ->
                logger.LogWarning("Application state is {state} and cannot be changed to {newState}", applicationState, state)

        let build: ChangeApplicationState = changeTo Building
        let start: ChangeApplicationState = changeTo Starting
        let run: ChangeApplicationState = changeTo Running
        let retry: ChangeApplicationState = changeTo Retrying
        let stop: ChangeApplicationState = changeTo Stopping
        let shutDown: ChangeApplicationState = changeTo ShuttingDown
        let finish: ChangeApplicationState = changeTo Off

        let private is state (logger: ILogger) () =
            let result = applicationState = state

            logger.LogDebug("Application state is {applicationState} and asking for {state} -> {result}", applicationState, state, result)
            result

        let isBuilding: IsApplicationState = is Building
        let isStarting: IsApplicationState = is Starting
        let isRunning: IsApplicationState = is Running
        let isRetrying: IsApplicationState = is Retrying
        let isStopping: IsApplicationState = is Stopping
        let isShuttingDown: IsApplicationState = is ShuttingDown

        let isOneOf: IsOneOfApplicationStates = fun logger states ->
            let result = states |> List.contains applicationState

            logger.LogDebug("Application state is {applicationState} and asking for {states} -> {result}", applicationState, (states |> List.map string |> String.concat ", "), result)
            result

    [<AutoOpen>]
    module GracefulShutdownHandlers =
        open System
        open System.Threading
        open System.Runtime.InteropServices

        type Cancellations =
            {
                Main: CancellationTokenSource
                Children: CancellationTokenSource
            }

            interface IDisposable with
                member this.Dispose() =
                    this.Children.Dispose()
                    this.Main.Dispose()

        /// GracefulShutdown handler, it will unregister all handlers upon dispose
        type GracefulShutdown =
            private {
                Logger: ILogger
                SignalHandler: PosixSignalRegistration
                ConsoleCancelEventHandler: ConsoleCancelEventHandler
            }

            interface IDisposable with
                member this.Dispose () =
                    this.Logger.LogDebug("Disposing graceful shutdown handlers ...")
                    this.SignalHandler.Dispose()
                    Console.CancelKeyPress.RemoveHandler(this.ConsoleCancelEventHandler)

        [<RequireQualifiedAccess>]
        module GracefulShutdown =
            type private Stop = {
                Gracefully: unit -> unit
                Immediately: unit -> unit
            }

            let private handler (logger: ILogger) cancellations stop =
                if ApplicationState.isOneOf logger [ Retrying; Stopping; ShuttingDown ]
                then
                    logger.LogInformation("Immediately stopping the application ...")
                    ApplicationState.shutDown logger

                    cancellations.Children.Cancel()
                    cancellations.Main.Cancel()

                    stop.Immediately()
                else
                    logger.LogInformation("Gracefully stopping the application ...")
                    ApplicationState.stop logger

                    cancellations.Children.Cancel()
                    stop.Gracefully()

            let private enableSignalHandler (logger: ILogger) cancellations =
                PosixSignalRegistration.Create(PosixSignal.SIGTERM, fun arg ->
                    logger.LogDebug("PosixSignal {signal}", arg.Signal)

                    handler logger cancellations {
                        Gracefully = fun () -> arg.Cancel <- true
                        Immediately = fun () -> arg.Cancel <- false
                    }
                )

            let private enableConsoleKeyPressHandler (logger: ILogger) cancellations =
                ConsoleCancelEventHandler (fun _ arg ->
                    logger.LogDebug("CancelKeyPress {key}", arg.SpecialKey)

                    handler logger cancellations {
                        Gracefully = fun () -> arg.Cancel <- true
                        Immediately = fun () -> arg.Cancel <- false
                    }
                )
                |> tee Console.CancelKeyPress.AddHandler

            let enable logger cancellations =
                {
                    Logger = logger
                    SignalHandler = enableSignalHandler logger cancellations
                    ConsoleCancelEventHandler = enableConsoleKeyPressHandler logger cancellations
                }

[<RequireQualifiedAccess>]
module LoggerFactory =
    open System.IO
    open Alma.Environment
    open Alma.ServiceIdentification
    open Alma.Logging
    open Alma.Tracing
    open Feather.ErrorHandling

    type LoggerEnvVar = {
        Instance: string
        LogTo: string
        Verbosity: string
        LoggerTags: string
        EnableTraceProvider: bool
    }

    let common loggerEnvVars envFiles = result {
        do!
            envFiles
            |> List.tryFind File.Exists
            |> Option.map Envs.loadResolvedFromFile
            |> Option.defaultValue (Ok ())

        let! instanceValue = loggerEnvVars.Instance |> Envs.tryResolve |> Result.ofOption "Missing instance"
        let! instance = Create.Instance(instanceValue) |> Result.mapError (sprintf "%A")

        return LoggerFactory.create [
            LogToFromEnvironment loggerEnvVars.LogTo

            if loggerEnvVars.EnableTraceProvider then
                UseLevel LogLevel.Trace
                UseProvider (LoggerProvider.TracingProvider.create())

            LogToSerilog (SerilogOptions.ofInstance instance @ [
                SerilogOption.UseLevelFromEnvironment loggerEnvVars.Verbosity
                AddMetaFromEnvironment loggerEnvVars.LoggerTags

                IgnorePathHealthCheck
                IgnorePathMetrics
            ])
        ]
    }

    let internal createLogger (loggerFactory: ILoggerFactory) name =
        loggerFactory.CreateLogger(name)

[<RequireQualifiedAccess>]
module internal AppRootStatus =
    open Feather.ErrorHandling
    open Alma.Environment

    let private mapDockerImageVersion onEmpty (Alma.Kafka.MetaData.DockerImageVersion version) =
        match version with
        | null | "" -> onEmpty
        | version -> version
        |> DockerImageVersion

    let status instance currentEnvironment (git: Git) dockerImageVersion =
        let dockerImageVersion =
            match dockerImageVersion with
            | Some version -> version |> mapDockerImageVersion "N/A"
            | _ -> DockerImageVersion "N/A"

        ApplicationStatus.create {
            new ApplicationStatusFeature.ICurrentApplication with
                member __.Instance = instance
                member __.Environment = currentEnvironment

            interface ApplicationStatusFeature.IAssemblyInformation with
                member __.GitBranch = git.Branch |> Option.defaultValue GitBranch.empty
                member __.GitCommit = git.Commit |> Option.defaultValue GitCommit.empty
                member __.GitRepository = git.Repository |> Option.defaultValue GitRepository.empty

            interface ApplicationStatusFeature.IDockerApplication with
                member __.DockerImageVersion = dockerImageVersion
        }

[<RequireQualifiedAccess>]
module internal WebServer =
    open Microsoft.Extensions.DependencyInjection

    open Giraffe
    open Saturn

    open Alma.WebApplication

    type Show<'Data> = (unit -> 'Data) option
    type Port = Port of int

    let defaultPort = Port 8080

    let web (loggerFactory: ILoggerFactory) (Port port) (showMetrics: Show<string>) (showStatus: Show<ApplicationStatus>) (showInternalState: HttpHandler option) (httpHandlers: HttpHandler list) =
        application {
            url $"http://0.0.0.0:{port}/"
            use_router (choose [
                Handler.Public.healthCheck

                match showMetrics with
                | Some metrics -> Handler.Public.metrics (fun _ -> metrics())
                | _ -> ()

                match showStatus with
                | Some status -> Handler.Public.appRootStatus (fun _ -> status())
                | _ -> ()

                match showInternalState with
                | Some handler -> handler
                | _ -> ()

                yield! httpHandlers

                Handler.Public.notFoundJson
            ])
            memory_cache
            use_gzip

            service_config (fun services ->
                services
                    .AddSingleton(loggerFactory)
                    .AddLogging()
                    .AddGiraffe()
            )
        }

[<RequireQualifiedAccess>]
module internal Async =
    open System.Threading

    let startAndAllowCancellation (logger: ILogger) (name: string) (cancellation: CancellationTokenSource) xA =
        logger.LogDebug("Start {name} in the background.", name)
        Async.Start(xA, cancellationToken = cancellation.Token)

    let runSynchronouslyAndAllowCancellation (logger: ILogger) (name: string) (cancellation: CancellationTokenSource) xA =
        logger.LogDebug("Run {name} synchronously.", name)
        Async.RunSynchronously(xA, cancellationToken = cancellation.Token)

    let runSynchronouslyAndFinishTheTask (logger: ILogger) (name: string) xA =
        logger.LogDebug("Run {name} synchronously.", name)
        Async.RunSynchronously(xA)
