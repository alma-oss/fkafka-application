namespace Alma.KafkaApplication

[<AutoOpen>]
module EnvironmentBuilder =
    open System.IO
    open Microsoft.Extensions.Logging
    open Alma.ServiceIdentification
    open Alma.Kafka
    open Alma.Kafka.MetaData
    open Alma.KafkaApplication
    open Alma.Environment
    open Alma.EnvironmentModel
    open Feather.ErrorHandling
    open Feather.ErrorHandling.Option.Operators

    type EnvironmentBuilder internal (loggerFactory: ILoggerFactory) =
        let logger = LoggerFactory.createLogger loggerFactory "KafkaApplication.Environment"

        let debugConfiguration (parts: ConfigurationParts<_, _, _>) =
            logger.LogTrace("Configuration: {configuration}", parts)

        let (>>=) (Configuration configuration) f =
            configuration
            |> Result.bind ((tee debugConfiguration) >> f)
            |> Configuration

        let (<!>) state f =
            state >>= (f >> Ok)

        let connectTo state connectionName (connectionConfiguration: EnvironmentConnectionConfiguration): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state >>= fun parts ->
                result {
                    let! brokerList =
                        connectionConfiguration.BrokerList
                        |> getEnvironmentValue parts BrokerList ConnectionConfigurationError.VariableNotFoundError

                    let! topic =
                        connectionConfiguration.Topic
                        |> getEnvironmentValue parts id ConnectionConfigurationError.VariableNotFoundError

                    let! topicInstance =
                        Create.Instance(topic)
                        |> Result.mapError (List.singleton >> ConnectionConfigurationError.TopicIsNotInstanceError)

                    let connectionConfiguration: ConnectionConfiguration = {
                        BrokerList = brokerList
                        Topic = topicInstance
                    }

                    return { parts with Connections = parts.Connections.Add(connectionName, connectionConfiguration) }
                }
                |> Result.mapError ConnectionConfigurationError

        let connectManyTo state (connectionConfiguration: EnvironmentManyTopicsConnectionConfiguration): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state >>= fun parts ->
                result {
                    let! brokerList =
                        connectionConfiguration.BrokerList
                        |> getEnvironmentValue parts BrokerList ConnectionConfigurationError.VariableNotFoundError

                    let! configurations =
                        connectionConfiguration.Topics
                        |> List.map (fun topic ->
                            result {
                                let! topicInstance = Create.Instance(topic)

                                return (
                                    topic |> ConnectionName,
                                    { BrokerList = brokerList; Topic = topicInstance }
                                )
                            }
                        )
                        |> Validation.ofResults
                        |> Result.mapError ConnectionConfigurationError.TopicIsNotInstanceError

                    let connectionConfigurations: Connections =
                        configurations
                        |> Map.ofList

                    return { parts with Connections = parts.Connections |> Map.merge connectionConfigurations }
                }
                |> Result.mapError ConnectionConfigurationError

        member __.Yield (_): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            { defaultParts
                with
                    LoggerFactory = loggerFactory
                    Environment = Envs.getAll()
            }
            |> Ok
            |> Configuration

        member __.Run(state): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state

        [<CustomOperation("file")>]
        member __.File(state, envFileLocations): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state >>= fun parts ->
                result {
                    do!
                        envFileLocations
                        |> List.tryFind File.Exists
                        |> function
                            | Some file -> file |> Envs.loadResolvedFromFile
                            | _ -> Ok ()
                        |> Result.mapError EnvironmentError.LoadError

                    let environment = Envs.getAll()

                    let variables =
                        environment
                        |> Map.toList
                        |> List.map (fun (k, v) -> $"{k}:{v}")
                        |> String.concat "; "
                    logger.LogDebug("Environment variables: {variables}", variables)

                    return { parts with Environment = environment }
                }
                |> Result.mapError EnvironmentError

        [<CustomOperation("check")>]
        member __.Check(state, name, checker): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state >>= fun parts ->
                result {
                    let! value =
                        name
                        |> getEnvironmentValue parts id EnvironmentError.InvalidFormatError

                    let! _ =
                        value
                        |> checker
                        |> Result.ofOption (sprintf "Value \"%s\" for %s is not correct." value name)
                        |> Result.mapError EnvironmentError.InvalidFormatError

                    return parts
                }
                |> Result.mapError EnvironmentError

        /// Define required environment variables, all values must be presented in currently loaded Environment
        [<CustomOperation("require")>]
        member __.Require(state, names): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state >>= fun parts ->
                result {
                    let! _ =
                        names
                        |> List.map (getEnvironmentValue parts id EnvironmentError.VariableNotFoundError)
                        |> Validation.ofResults

                    return parts
                }
                |> Result.mapError RequiredEnvironmentVariablesErrors

        [<CustomOperation("instance")>]
        member __.Instance(state, instanceVariableName): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state >>= fun parts ->
                result {
                    let! instanceString =
                        instanceVariableName
                        |> getEnvironmentValue parts id InstanceError.VariableNotFoundError

                    let! instance =
                        Create.Instance(instanceString)
                        |> Result.mapError InstanceError.InvalidFormatError

                    return { parts with Instance = Some instance }
                }
                |> Result.mapError InstanceError

        [<CustomOperation("currentEnvironment")>]
        member __.CurrentEnvironment(state, currentEnvironmentVariableName): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state >>= fun parts ->
                result {
                    let! currentEnvironmentString =
                        currentEnvironmentVariableName
                        |> getEnvironmentValue parts id CurrentEnvironmentError.VariableNotFoundError

                    let! currentEnvironment =
                        currentEnvironmentString
                        |> Environment.parse
                        |> Result.mapError CurrentEnvironmentError.InvalidFormatError

                    return { parts with CurrentEnvironment = Some currentEnvironment }
                }
                |> Result.mapError CurrentEnvironmentError

        [<CustomOperation("dockerImageVersion")>]
        member __.DockerImageVersion(state, dockerImageVersion): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state >>= fun parts ->
                result {
                    let! dockerImageVersionString =
                        dockerImageVersion
                        |> getEnvironmentValue parts id EnvironmentError.VariableNotFoundError

                    return { parts with DockerImageVersion = Some (DockerImageVersion dockerImageVersionString) }
                }
                |> Result.mapError EnvironmentError

        [<CustomOperation("spot")>]
        member __.Spot(state, spotVariableName): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state >>= fun parts ->
                result {
                    let! spotString =
                        spotVariableName
                        |> getEnvironmentValue parts id SpotError.VariableNotFoundError

                    let! spot =
                        Create.Spot(spotString)
                        |> Result.mapError SpotError.InvalidFormatError

                    return { parts with Spot = Some spot }
                }
                |> Result.mapError SpotError

        [<CustomOperation("groupId")>]
        member __.GroupId(state, groupIdVariableName): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state >>= fun parts ->
                result {
                    let! groupId =
                        groupIdVariableName
                        |> getEnvironmentValue parts GroupId.Id GroupIdError.VariableNotFoundError

                    return { parts with GroupId = Some groupId }
                }
                |> Result.mapError GroupIdError

        [<CustomOperation("supervision")>]
        member __.Supervision(state, connectionConfiguration: EnvironmentConnectionConfiguration): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            connectTo state Connections.Supervision connectionConfiguration

        [<CustomOperation("connect")>]
        member __.Connect(state, connectionConfiguration: EnvironmentConnectionConfiguration): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            connectTo state Connections.Default connectionConfiguration

        [<CustomOperation("connectTo")>]
        member __.ConnectTo(state, name, connectionConfiguration: EnvironmentConnectionConfiguration): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            connectTo state (ConnectionName name) connectionConfiguration

        [<CustomOperation("connectManyToBroker")>]
        member __.ConnectManyToBroker(state, connectionConfiguration: EnvironmentManyTopicsConnectionConfiguration): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            connectManyTo state connectionConfiguration

        [<CustomOperation("ifSetDo")>]
        member __.IfSetDo(state, name, action): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun parts ->
                name
                |> parts.Environment.TryFind
                |>! action

                parts

    let environmentWithLogger logger = EnvironmentBuilder(logger)
    let environment = EnvironmentBuilder(defaultParts.LoggerFactory)
