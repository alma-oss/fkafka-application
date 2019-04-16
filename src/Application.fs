namespace KafkaApplication

open System.IO
open Environment
open Kafka
open ServiceIdentification

[<AutoOpen>]
module KafkaApplication =
    let private tee f a =
        f a
        a

    [<AutoOpen>]
    module private KafkaApplicationBuilder =
        let (<??>) defaultValue opt = Option.defaultValue opt defaultValue
        let (<?!>) (opt: 'a option) (errorMessage: string): Result<'a, KafkaApplicationError> =
            match opt with
            | Some value -> Ok value
            | None -> sprintf "[KafkaApplicationBuilder] %s" errorMessage |> KafkaApplicationError |> Result.Error

        let defaultParts =
            {
                Logger = Logger.defaultLogger
                Environment = Map.empty
                Instance = None
                GroupId = GroupId.Random
                ConnectionConfiguration = None
                Consume = None
                OnError = None
            }

        let buildApplication (Configuration configuration): KafkaApplication<'Event> =
            result {
                let! configurationParts = configuration

                let logger = configurationParts.Logger
                let environment = configurationParts.Environment

                let! instance = configurationParts.Instance <?!> "Instance is required."
                let! connectionConfiguration = configurationParts.ConnectionConfiguration <?!> "Connection configuration is required."
                let! consume = configurationParts.Consume <?!> "Consume function is required."

                let onError = configurationParts.OnError <??> (fun _ _ -> Shutdown)

                let consumerConfiguration = Kafka.ConsumerConfiguration.createWithConnection connectionConfiguration GroupId.Random

                let publicParts: ConsumeRuntimeParts = {
                    Logger = logger
                    Environment = environment
                    ConsumerConfiguration = consumerConfiguration
                }

                return {
                    Logger = configurationParts.Logger
                    Environment = configurationParts.Environment
                    Instance = instance
                    ConsumerConfiguration = consumerConfiguration
                    Consume = consume publicParts
                    OnError = onError
                }
            }
            |> KafkaApplication

    type KafkaApplicationBuilder internal () =
        let debugConfiguration (parts: ConfigurationParts<_>) =
            parts
            |> sprintf "%A"
            |> parts.Logger.Debug "Configuration"

        let (>>=) (Configuration configuration) f =
            configuration
            |> Result.bind ((tee debugConfiguration) >> f)
            |> Configuration

        let (<!>) state f =
            state >>= (f >> Ok)

        let getEnv (parts: ConfigurationParts<_>) success error name =
            name
            |> Environment.tryGetEnv parts.Environment
            |> Option.map success
            |> Result.ofOption (sprintf "Environment variable for \"%s\" is not set." name)
            |> Result.mapError error

        member __.Yield (_) =
            defaultParts
            |> Ok
            |> Configuration

        member __.Bind(state, f) =
            state >>= f

        //member __.Combine(state: KafkaApplicationParts, x) =
        //    state

        //member __.Delay(f) =
        //    // f is whole computed expression as a funcion
        //    f()

        member __.Run(state) =
            buildApplication state

        [<CustomOperation("logger")>]
        member __.Logger(state, logger: KafkaApplication.Logger): Configuration<'Event> =
            state <!> fun parts -> { parts with Logger = logger }

        [<CustomOperation("envFile")>]
        member __.EnvFile(state, envFileLocations): Configuration<'Event> =
            state <!> fun parts ->
                { parts with
                    Environment =
                        envFileLocations
                        |> List.tryFind File.Exists
                        |> Option.map (getEnvs (parts.Logger.Warning "Dotenv") >> Environment.merge parts.Environment)
                        <??> parts.Environment
                }

        [<CustomOperation("checkEnv")>]
        member __.CheckEnv(state, name, checker): Configuration<'Event> =
            state >>= fun parts ->
                result {
                    let! value =
                        name
                        |> getEnv parts id EnvironmentError.InvalidFormatError

                    let! _ =
                        value
                        |> checker
                        |> Result.ofOption (sprintf "Value \"%s\" for %s is not in correct format." value name)
                        |> Result.mapError EnvironmentError.InvalidFormatError

                    return parts
                }
                |> Result.mapError EnvironmentError

        [<CustomOperation("instanceFromEnv")>]
        member __.InstanceFromEnv(state, instanceVariableName): Configuration<'Event> =
            state >>= fun parts ->
                result {
                    let! instanceString =
                        instanceVariableName
                        |> getEnv parts id InstanceError.VariableNotFoundError

                    let! instance =
                        instanceString
                        |> Instance.parse "-"
                        |> Result.ofOption (sprintf "Value \"%s\" for Instance is not in correct format (expecting values separated by \"-\")." instanceString)
                        |> Result.mapError InstanceError.InvalidFormatError

                    return { parts with Instance = Some instance }
                }
                |> Result.mapError InstanceError

        [<CustomOperation("groupIdFromEnv")>]
        member __.GroupIdFromEnv(state, groupIdVariableName): Configuration<'Event> =
            state >>= fun parts ->
                result {
                    let! groupId =
                        groupIdVariableName
                        |> getEnv parts GroupId.Id GroupIdError.VariableNotFoundError

                    return { parts with GroupId = groupId }
                }
                |> Result.mapError GroupIdError

        [<CustomOperation("connect")>]
        member __.Connect(state, connectionConfiguration): Configuration<'Event> =
            state <!> fun parts -> { parts with ConnectionConfiguration = Some connectionConfiguration }

        [<CustomOperation("connectFromEnv")>]
        member __.ConnectFromEnv(state, connectionConfiguration: EnvironmentConnectionConfiguration): Configuration<'Event> =
            state >>= fun parts ->
                result {
                    let! brokerList =
                        connectionConfiguration.BrokerList
                        |> getEnv parts BrokerList ConnectionConfigurationError.VariableNotFoundError

                    let! topic =
                        connectionConfiguration.BrokerList
                        |> getEnv parts StreamName ConnectionConfigurationError.VariableNotFoundError

                    let connectionConfiguration: ConnectionConfiguration = {
                        BrokerList = brokerList
                        Topic = topic
                    }

                    return { parts with ConnectionConfiguration = Some connectionConfiguration}
                }
                |> Result.mapError ConnectionConfigurationError

        [<CustomOperation("consume")>]
        member __.Consume(state, consume): Configuration<'Event> =
            state <!> fun parts -> { parts with Consume = Some consume }

        [<CustomOperation("onError")>]
        member __.OnError(state, onError): Configuration<'Event> =
            state <!> fun parts -> { parts with OnError = Some onError }

        /// Define required environment variables, all values must be presented in currently loaded Environment
        [<CustomOperation("envRequire")>]
        member __.EnvRequire(state, names): Configuration<'Event> =
            state >>= fun parts ->
                result {
                    let! _ =
                        names
                        |> List.map (getEnv parts id EnvironmentError.VariableNotFoundError)
                        |> Result.sequence

                    return parts
                }
                |> Result.mapError EnvironmentError

    let kafkaApplication = KafkaApplicationBuilder()

    // todo - remove the `consume` parameter and use Kafka.consume directly
    let run (kafka_consume: ConsumerConfiguration -> seq<'Event>) (KafkaApplication application) =
        match application with
        | Ok app ->
            let log = app.Logger.Log "Application"
            let logVerbose = app.Logger.Verbose "Application"
            log "Starts ..."

            logVerbose <| sprintf "Instance:\n%A" app.Instance
            logVerbose <| sprintf "Kafka:\n%A" app.ConsumerConfiguration

            let markAsEnabled() =
                log "... Mark as enabled ..."
            let markAsDisabled() =
                log "... Mark as disabled ..."

            let mutable runConsuming = true
            while runConsuming do
                try
                    markAsEnabled()
                    runConsuming <- false

                    app.ConsumerConfiguration
                    |> kafka_consume  // todo replace with Kafka.consume
                    |> app.Consume
                with
                | :? Confluent.Kafka.KafkaException as e ->
                    markAsDisabled()

                    e
                    |> sprintf "%A"
                    |> app.Logger.Error "Kafka"

                    match app.OnError app.Logger e.Message with
                    | Reboot -> runConsuming <- true
                    | Shutdown -> runConsuming <- false

                if runConsuming then
                    log "Reboot ..."
        | Error error ->
            failwithf "[Application] Error:\n%A" error
