namespace Lmc.KafkaApplication

open Lmc.ApplicationStatus

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
        open Lmc.ErrorHandling

        type FilePath = string

        let parseFromPath parse notFound (path: FilePath): Result<'Parsed, 'Error> =
            result {
                if not (File.Exists(path)) then
                    return! Error (notFound path)

                return! parse path
            }

    module Serializer =
        open Lmc.Serializer

        let toJson = Serialize Serialize.toJson

[<RequireQualifiedAccess>]
module LoggerFactory =
    open System.IO
    open Microsoft.Extensions.Logging
    open Lmc.Environment
    open Lmc.ServiceIdentification
    open Lmc.Logging
    open Lmc.Tracing
    open Lmc.ErrorHandling

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
            ])
        ]
    }

[<RequireQualifiedAccess>]
module internal AppRootStatus =
    open Lmc.ErrorHandling
    open Lmc.Environment

    let private mapDockerImageVersion onEmpty (Lmc.Kafka.MetaData.DockerImageVersion version) =
        match version with
        | null | "" -> onEmpty
        | version -> version
        |> DockerImageVersion

    let status instance currentEnvironment (git: Git) dockerImageVersion =
        let dockerImageVersion =
            match dockerImageVersion with
            | Some version -> version |> mapDockerImageVersion "N/A"
            | _ -> DockerImageVersion "N/A"

        let nomad = maybe {
            let! nomadJobName = "NOMAD_JOB_NAME" |> Envs.tryResolve
            let! nomadAllocId = "NOMAD_ALLOC_ID" |> Envs.tryResolve

            return NomadJobName nomadJobName, NomadAllocationId nomadAllocId
        }

        match nomad with
        | Some (nomadJobName, nomadAllocId) ->
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

                interface ApplicationStatusFeature.INomadApplication with
                    member __.NomadJobName = nomadJobName
                    member __.NomadAllocationId = nomadAllocId
            }
        | _ ->
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
    open Microsoft.Extensions.Logging

    open Giraffe
    open Saturn

    open Lmc.WebApplication

    type Show<'Data> = (unit -> 'Data) option

    let web (loggerFactory: ILoggerFactory) (showMetrics: Show<string>) (showStatus: Show<ApplicationStatus>) (httpHandlers: HttpHandler list) =
        application {
            url "http://0.0.0.0:8080/"
            use_router (choose [
                Handler.healthCheck Handler.accessDeniedJson

                match showMetrics with
                | Some metrics -> Handler.metrics (fun _ -> metrics())
                | _ -> ()

                match showStatus with
                | Some status -> Handler.appRootStatus (fun _ -> status())
                | _ -> ()

                yield! httpHandlers

                Handler.resourceNotFound
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
