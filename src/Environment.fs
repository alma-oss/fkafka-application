namespace KafkaApplication

[<AutoOpen>]
module EnvironmentBuilder =
    open System.IO
    open OptionOperators
    open Environment
    open ServiceIdentification
    open Kafka

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
                        |> getEnvironmentValue parts StreamName ConnectionConfigurationError.VariableNotFoundError

                    let connectionConfiguration: ConnectionConfiguration = {
                        BrokerList = brokerList
                        Topic = topic
                    }

                    return { parts with Connections = parts.Connections.Add(connectionName, connectionConfiguration) }
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
                { parts with
                    Environment =
                        envFileLocations
                        |> List.tryFind File.Exists
                        |> Option.map (getEnvs (parts.Logger.Warning "Dotenv") >> Environment.merge parts.Environment)
                        <?=> parts.Environment
                }

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

                    return! Error (SpotError.InvalidFormatError (sprintf "Parsing for Spot \"%s\" is not implemented yet." spotString))

                    //let! spot =
                    //    spotString
                    //    |> Spot.parse "-"
                    //    |> Result.ofOption (sprintf "Value \"%s\" for Spot is not in correct format (expecting values separated by \"-\")." spotString)
                    //    |> Result.mapError SpotError.InvalidFormatError

                    //return { parts with Spot = Some spot }
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

        [<CustomOperation("ifSetDo")>]
        member __.IfSetDo(state, name, action): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts ->
                name
                |> parts.Environment.TryFind
                |>! action

                parts

    let environmentWithLogger logger = EnvironmentBuilder(logger)
    let environment = EnvironmentBuilder(defaultParts.Logger)
