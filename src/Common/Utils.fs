namespace Lmc.KafkaApplication

type Serialize = Serialize of (obj -> string)

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
module AppRootStatus =
    open System
    open Suave
    open Suave.Filters
    open Suave.Operators
    open Suave.Successful
    open Lmc.ApplicationStatus
    open Lmc.ErrorHandling
    open Lmc.Environment

    let status instance environment dockerImageVersion =
        let valueOrNA = Option.defaultValue "N/A"

        let dockerImageVersion = dockerImageVersion |> valueOrNA |> DockerImageVersion
        let nomadJobName = "NOMAD_JOB_NAME" |> Envs.tryResolve |> valueOrNA |> NomadJobName
        let nomadAllocId = "NOMAD_ALLOC_ID" |> Envs.tryResolve |> valueOrNA |> NomadAllocationId

        ApplicationStatus.create {
            new ApplicationStatusFeature.ICurrentApplication with
                member __.Instance = instance
                member __.Environment = environment

            interface ApplicationStatusFeature.IAssemblyInformation with
                member __.GitBranch = GitBranch AssemblyVersionInformation.AssemblyMetadata_gitbranch
                member __.GitCommit = GitCommit AssemblyVersionInformation.AssemblyMetadata_gitcommit
                member __.GitRepository = GitRepository.empty

            interface ApplicationStatusFeature.IDockerApplication with
                member __.DockerImageVersion = dockerImageVersion

            interface ApplicationStatusFeature.INomadApplication with
                member __.NomadJobName = nomadJobName
                member __.NomadAllocationId = nomadAllocId
        }

    // todo - use giraffe nad Lmc.WebApplication
    let route status: Http.HttpContext -> Async<Http.HttpContext option> =
        GET >=> choose [
            path "/appRoot/status"
                >=> request ((fun _ -> status) >> OK)
        ]
