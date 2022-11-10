module Lmc.KafkaApplication.Test.CommitMessage

open Expecto
open Lmc.Kafka
open Lmc.ServiceIdentification
open Microsoft.Extensions.Logging
open Lmc.Kafka.MetaData
open Lmc.Logging
open Lmc.KafkaApplication

let okOrFail = function
    | Ok ok -> ok
    | Error error -> failtestf "Fail on %A" error

let instance (value: string) = Create.Instance(value) |> okOrFail

type InputEvent = string
type OutputEvent = string

type TestableConfigurationParts = {
    LoggerFactory: ILoggerFactory
    Environment: Map<string, string>
    Instance: Instance option
    CurrentEnvironment: Lmc.EnvironmentModel.Environment option
    Git: Git
    DockerImageVersion: DockerImageVersion option
    Spot: Spot option
    GroupId: GroupId option
    GroupIds: Map<ConnectionName, GroupId>
    CommitMessage: CommitMessage option
    CommitMessages: Map<ConnectionName, CommitMessage>
    Connections: Connections
    ProduceTo: ConnectionName list
    ShowMetrics: bool
    ShowAppRootStatus: bool
    CustomMetrics: CustomMetric list
}

module internal TestableConfigurationParts =
    let ofConfigurationParts (parts: ConfigurationParts<_, _, _>) =
        {
            LoggerFactory = parts.LoggerFactory
            Environment = parts.Environment
            Instance = parts.Instance
            CurrentEnvironment = parts.CurrentEnvironment
            Git = parts.Git
            DockerImageVersion = parts.DockerImageVersion
            Spot = parts.Spot
            GroupId = parts.GroupId
            GroupIds = parts.GroupIds
            CommitMessage = parts.CommitMessage
            CommitMessages = parts.CommitMessages
            Connections = parts.Connections
            ProduceTo = parts.ProduceTo
            ShowMetrics = parts.ShowMetrics
            ShowAppRootStatus = parts.ShowAppRootStatus
            CustomMetrics = parts.CustomMetrics
        }

    let ofValidConfiguration: Configuration<_, _, _> -> TestableConfigurationParts option = function
        | Configuration (Ok parts) -> Some (parts |> ofConfigurationParts)
        | _ -> None

[<Tests>]
let commitMessageTest =
    testList "KafkaApplication - commit message" [
        testCase "should be set correctly" <| fun _ ->
            let instance = instance "development-kafkaApplication-commitMessage-test"

            let app =
                partialKafkaApplication {
                    useInstance instance
                }
                |> TestableConfigurationParts.ofValidConfiguration

            let expectedConfiguration =
                TestableConfigurationParts.ofConfigurationParts {
                    defaultParts with
                        Instance = Some instance
                        CommitMessage = None
                }

            match app with
            | Some parts -> Expect.equal parts expectedConfiguration "description"
            | _ -> failwithf "Invalid"

        testCase "should be set correctly with manual commit" <| fun _ ->
            let instance = instance "development-kafkaApplication-commitMessage-test"
            let manualCommit = CommitMessage.Manually FailOnNotCommittedMessage.WithException

            (* use loggerFactory = LoggerFactory.create [
                LoggerOption.UseLevel LogLevel.Trace

                LoggerOption.LogToSerilog [
                    SerilogOption.LogToConsole
                    SerilogOption.AddMeta ("facility", "kafka-app-test")
                ]
            ] *)

            let app =
                partialKafkaApplication {
                    // useLoggerFactory loggerFactory
                    useCommitMessage manualCommit
                    useInstance instance
                }
                |> TestableConfigurationParts.ofValidConfiguration

            let expectedConfiguration =
                TestableConfigurationParts.ofConfigurationParts {
                    defaultParts with
                        // LoggerFactory = loggerFactory
                        Instance = Some instance
                        CommitMessage = Some manualCommit
                }

            match app with
            | Some parts -> Expect.equal parts expectedConfiguration "description"
            | _ -> failtest "Invalid"

        testCase "should be set correctly with manual commit by merged partial app" <| fun _ ->
            let instance = instance "development-kafkaApplication-commitMessage-test"
            let manualCommit = CommitMessage.Manually FailOnNotCommittedMessage.WithException

            (* use loggerFactory = LoggerFactory.create [
                LoggerOption.UseLevel LogLevel.Trace

                LoggerOption.LogToSerilog [
                    SerilogOption.LogToConsole
                    SerilogOption.AddMeta ("facility", "kafka-app-test")
                ]
            ] *)

            let app =
                partialKafkaApplication {
                    // useLoggerFactory loggerFactory
                    useCommitMessage manualCommit

                    merge (partialKafkaApplication {
                        useInstance instance
                    })
                }
                |> TestableConfigurationParts.ofValidConfiguration

            let expectedConfiguration =
                TestableConfigurationParts.ofConfigurationParts {
                    defaultParts with
                        // LoggerFactory = loggerFactory
                        Instance = Some instance
                        CommitMessage = Some manualCommit
                }

            match app with
            | Some parts -> Expect.equal parts expectedConfiguration "description"
            | _ -> failtest "Invalid"
    ]
