namespace KafkaApplication

[<AutoOpen>]
module EnvironmentBuilder =
    open System.IO
    open OptionOperators
    open Environment
    open ServiceIdentification
    open Kafka

    let private tee f a =
        f a
        a

    type EnvironmentBuilder internal (logger) =
        let debugConfiguration (parts: ConfigurationParts<_>) =
            parts
            |> sprintf "%A"
            |> parts.Logger.Debug "Environment"

        let (>>=) (Configuration configuration) f =
            configuration
            |> Result.bind ((tee debugConfiguration) >> f)
            |> Configuration

        let (<!>) state f =
            state >>= (f >> Ok)

        member __.Yield (_): Configuration<'Event> =
            { defaultParts with Logger = logger }
            |> Ok
            |> Configuration

        member __.Run(state): Configuration<'Event> =
            state

        [<CustomOperation("file")>]
        member __.File(state, envFileLocations): Configuration<'Event> =
            state <!> fun parts ->
                { parts with
                    Environment =
                        envFileLocations
                        |> List.tryFind File.Exists
                        |> Option.map (getEnvs (parts.Logger.Warning "Dotenv") >> Environment.merge parts.Environment)
                        <?=> parts.Environment
                }

        [<CustomOperation("check")>]
        member __.Check(state, name, checker): Configuration<'Event> =
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
        member __.Require(state, names): Configuration<'Event> =
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
        member __.Instance(state, instanceVariableName): Configuration<'Event> =
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

        [<CustomOperation("groupId")>]
        member __.GroupId(state, groupIdVariableName): Configuration<'Event> =
            state >>= fun parts ->
                result {
                    let! groupId =
                        groupIdVariableName
                        |> getEnvironmentValue parts GroupId.Id GroupIdError.VariableNotFoundError

                    return { parts with GroupId = Some groupId }
                }
                |> Result.mapError GroupIdError

        [<CustomOperation("connect")>]
        member __.Connect(state, connectionConfiguration: EnvironmentConnectionConfiguration): Configuration<'Event> =
            state >>= fun parts ->
                result {
                    let! brokerList =
                        connectionConfiguration.BrokerList
                        |> getEnvironmentValue parts BrokerList ConnectionConfigurationError.VariableNotFoundError

                    let! topic =
                        connectionConfiguration.BrokerList
                        |> getEnvironmentValue parts StreamName ConnectionConfigurationError.VariableNotFoundError

                    let connectionConfiguration: ConnectionConfiguration = {
                        BrokerList = brokerList
                        Topic = topic
                    }

                    return { parts with Connections = Some connectionConfiguration}
                }
                |> Result.mapError ConnectionConfigurationError

    let environmentWithLogger logger = EnvironmentBuilder(logger)
    let environment = EnvironmentBuilder(defaultParts.Logger)
