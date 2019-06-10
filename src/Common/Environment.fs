namespace KafkaApplication
open ApplicationBuilder

[<AutoOpen>]
module EnvironmentBuilder =
    open System.IO
    open OptionOperators
    open Environment
    open ServiceIdentification
    open Kafka
    open KafkaApplication
    open KafkaApplicationBuilder

    type EnvironmentBuilder internal (logger) =
        let debugConfiguration (parts: ConfigurationParts<_, _>) =
            parts
            |> sprintf "%A"
            |> parts.Logger.Debug "Environment"

        let (>>=) (Configuration configuration) f =
            configuration
            |> Result.bind ((tee debugConfiguration) >> f)
            |> Configuration

        let (<!>) state f =
            state >>= (f >> Ok)

        let connectTo state connectionName (connectionConfiguration: EnvironmentConnectionConfiguration): Configuration<'InputEvent, 'OutputEvent> =
            state >>= fun parts ->
                result {
                    let! brokerList =
                        connectionConfiguration.BrokerList
                        |> getEnvironmentValue parts BrokerList ConnectionConfigurationError.VariableNotFoundError

                    let! topic =
                        connectionConfiguration.Topic
                        |> getEnvironmentValue parts id ConnectionConfigurationError.VariableNotFoundError

                    let! topicInstance =
                        topic
                        |> Instance.parse "-"
                        |> Result.ofOption (ConnectionConfigurationError.TopicIsNotInstanceError topic)

                    let connectionConfiguration: ConnectionConfiguration = {
                        BrokerList = brokerList
                        Topic = topicInstance
                    }

                    return { parts with Connections = parts.Connections.Add(connectionName, connectionConfiguration) }
                }
                |> Result.mapError ConnectionConfigurationError

        let connectManyTo state (connectionConfiguration: EnvironmentManyTopicsConnectionConfiguration): Configuration<'InputEvent, 'OutputEvent> =
            state >>= fun parts ->
                result {
                    let! brokerList =
                        connectionConfiguration.BrokerList
                        |> getEnvironmentValue parts BrokerList ConnectionConfigurationError.VariableNotFoundError

                    let! configurations =
                        connectionConfiguration.Topics
                        |> List.map (fun topic ->
                            result {
                                let! topicInstance =
                                    topic
                                    |> Instance.parse "-"
                                    |> Result.ofOption (ConnectionConfigurationError.TopicIsNotInstanceError topic)

                                return (
                                    topic |> ConnectionName,
                                    { BrokerList = brokerList; Topic = topicInstance }
                                )
                            }
                        )
                        |> Result.sequence

                    let connectionConfigurations: Connections =
                        configurations
                        |> Map.ofList

                    return { parts with Connections = parts.Connections |> Map.merge connectionConfigurations }
                }
                |> Result.mapError ConnectionConfigurationError

        member __.Yield (_): Configuration<'InputEvent, 'OutputEvent> =
            { defaultParts with Logger = logger }
            |> Ok
            |> Configuration

        member __.Run(state): Configuration<'InputEvent, 'OutputEvent> =
            state

        [<CustomOperation("file")>]
        member __.File(state, envFileLocations): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts ->
                let environment =
                    envFileLocations
                    |> List.tryFind File.Exists
                    |> Option.map (getEnvs (parts.Logger.Warning "Dotenv") >> Environment.merge parts.Environment)
                    <?=> parts.Environment

                environment
                |> Map.toList
                |> sprintf "%A"
                |> parts.Logger.VeryVerbose "Dotenv"

                { parts with Environment = environment }

        [<CustomOperation("check")>]
        member __.Check(state, name, checker): Configuration<'InputEvent, 'OutputEvent> =
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
        member __.Require(state, names): Configuration<'InputEvent, 'OutputEvent> =
            state >>= fun parts ->
                result {
                    let! _ =
                        names
                        |> List.map (getEnvironmentValue parts id EnvironmentError.VariableNotFoundError)
                        |> Result.sequence

                    return parts
                }
                |> Result.mapError EnvironmentError

        [<CustomOperation("instance")>]
        member __.Instance(state, instanceVariableName): Configuration<'InputEvent, 'OutputEvent> =
            state >>= fun parts ->
                result {
                    let! instanceString =
                        instanceVariableName
                        |> getEnvironmentValue parts id InstanceError.VariableNotFoundError

                    let! instance =
                        instanceString
                        |> Instance.parse "-"
                        |> Result.ofOption (sprintf "Value \"%s\" for Instance is not in correct format (expecting values separated by \"-\")." instanceString)
                        |> Result.mapError InstanceError.InvalidFormatError

                    return { parts with Instance = Some instance }
                }
                |> Result.mapError InstanceError

        [<CustomOperation("spot")>]
        member __.Spot(state, spotVariableName): Configuration<'InputEvent, 'OutputEvent> =
            state >>= fun parts ->
                result {
                    let! spotString =
                        spotVariableName
                        |> getEnvironmentValue parts id SpotError.VariableNotFoundError

                    let! spot =
                        spotString
                        |> Spot.parse "-"
                        |> Result.ofOption (sprintf "Value \"%s\" for Spot is not in correct format (expecting values separated by \"-\")." spotString)
                        |> Result.mapError SpotError.InvalidFormatError

                    return { parts with Spot = Some spot }
                }
                |> Result.mapError SpotError

        [<CustomOperation("groupId")>]
        member __.GroupId(state, groupIdVariableName): Configuration<'InputEvent, 'OutputEvent> =
            state >>= fun parts ->
                result {
                    let! groupId =
                        groupIdVariableName
                        |> getEnvironmentValue parts GroupId.Id GroupIdError.VariableNotFoundError

                    return { parts with GroupId = Some groupId }
                }
                |> Result.mapError GroupIdError

        [<CustomOperation("supervision")>]
        member __.Supervision(state, connectionConfiguration: EnvironmentConnectionConfiguration): Configuration<'InputEvent, 'OutputEvent> =
            connectTo state Connections.Supervision connectionConfiguration

        [<CustomOperation("connect")>]
        member __.Connect(state, connectionConfiguration: EnvironmentConnectionConfiguration): Configuration<'InputEvent, 'OutputEvent> =
            connectTo state Connections.Default connectionConfiguration

        [<CustomOperation("connectTo")>]
        member __.ConnectTo(state, name, connectionConfiguration: EnvironmentConnectionConfiguration): Configuration<'InputEvent, 'OutputEvent> =
            connectTo state (ConnectionName name) connectionConfiguration

        [<CustomOperation("connectManyToBroker")>]
        member __.ConnectManyToBroker(state, connectionConfiguration: EnvironmentManyTopicsConnectionConfiguration): Configuration<'InputEvent, 'OutputEvent> =
            connectManyTo state connectionConfiguration

        [<CustomOperation("ifSetDo")>]
        member __.IfSetDo(state, name, action): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts ->
                name
                |> parts.Environment.TryFind
                |>! action

                parts

        [<CustomOperation("logToGraylog")>]
        member __.LogToGraylog(state, graylogHostVariableName): Configuration<'InputEvent, 'OutputEvent> =
            state >>= fun parts ->
                result {
                    let! graylogHostValue =
                        graylogHostVariableName
                        |> getEnvironmentValue parts id LoggingError.VariableNotFoundError

                    return!
                        graylogHostValue
                        |> addGraylogHostToParts parts
                }
                |> Result.mapError LoggingError

    let environmentWithLogger logger = EnvironmentBuilder(logger)
    let environment = EnvironmentBuilder(defaultParts.Logger)
