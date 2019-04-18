namespace KafkaApplication

open Kafka

[<AutoOpen>]
module KafkaApplication =
    open OptionOperators

    let private tee f a =
        f a
        a

    [<AutoOpen>]
    module private KafkaApplicationBuilder =

        let buildApplication (Configuration configuration): KafkaApplication<'Event> =
            result {
                let! configurationParts = configuration

                let logger = configurationParts.Logger
                let environment = configurationParts.Environment
                let groupId = configurationParts.GroupId <?=> GroupId.Random

                let! instance = configurationParts.Instance <?!> "Instance is required."
                //let! connections = Connections <?!> "At least one connection configuration is required."
                let! connections =
                    if configurationParts.Connections |> Map.isEmpty then Error (KafkaApplicationError "At least one connection configuration is required.")
                    else Ok configurationParts.Connections
                let! consume = configurationParts.Consume <?!> "Consume function is required."

                let onError = configurationParts.OnConsumeError <?=> (fun _ _ -> Shutdown)

                let consumerConfiguration = Kafka.ConsumerConfiguration.createWithConnection connectionConfiguration groupId

                let publicParts: ConsumeRuntimeParts = {
                    Logger = logger
                    Environment = environment
                    Connections = connections
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

        member __.Yield (_): Configuration<'Event> =
            defaultParts
            |> Ok
            |> Configuration

        member __.Bind(state, f): Configuration<'Event> =
            state >>= f

        member __.Run(state): KafkaApplication<'Evnet> =
            buildApplication state

        [<CustomOperation("useLogger")>]
        member __.Logger(state, logger: KafkaApplication.Logger): Configuration<'Event> =
            state <!> fun parts -> { parts with Logger = logger }

        [<CustomOperation("useInstance")>]
        member __.Instance(state, instance): Configuration<'Event> =
            state <!> fun parts -> { parts with Instance = Some instance }

        [<CustomOperation("useGroupId")>]
        member __.GroupId(state, groupId): Configuration<'Event> =
            state <!> fun parts -> { parts with GroupId = Some groupId }

        [<CustomOperation("connect")>]
        member __.Connect(state, connectionConfiguration): Configuration<'Event> =
            state <!> fun parts -> { parts with Connections = Some connectionConfiguration }

        [<CustomOperation("consume")>]
        member __.Consume(state, consume): Configuration<'Event> =
            state <!> fun parts -> { parts with Consume = Some consume }

        [<CustomOperation("onConsumeError")>]
        member __.OnConsumeError(state, onConsumeError): Configuration<'Event> =
            state <!> fun parts -> { parts with OnConsumeError = Some onConsumeError }

        /// Add other configuration and merge it with current.
        /// New configuration values have higher priority. New values (only those with Some value) will replace already set configuration values.
        /// (Except of logger)
        [<CustomOperation("merge")>]
        member __.Merge(state, configuration): Configuration<'Event> =
            state >>= fun currentParts ->
                configuration <!> fun newParts ->
                    {
                        Logger = currentParts.Logger
                        Environment = Environment.update currentParts.Environment newParts.Environment
                        Instance = newParts.Instance <??> currentParts.Instance
                        GroupId = newParts.GroupId <??> currentParts.GroupId
                        Connections = Connections <??> Connections
                        Consume = newParts.Consume <??> currentParts.Consume
                        OnConsumeError = newParts.OnConsumeError <??> currentParts.OnConsumeError
                    }
                |> Configuration.result

    let kafkaApplication = KafkaApplicationBuilder()

    module private KafkaApplicationRunner =
        // todo - remove the `consume` parameter and use Kafka.consume directly
        let run (kafka_consume: ConsumerConfiguration -> 'Event seq) (application: KafkaApplicationParts<'Event>) =
            let log = application.Logger.Log "Application"
            let logVerbose = application.Logger.Verbose "Application"
            log "Starts ..."

            logVerbose <| sprintf "Instance:\n%A" application.Instance
            logVerbose <| sprintf "Kafka:\n%A" application.ConsumerConfiguration

            // todo - produce `instance_started` event, id application_connection is available

            let markAsEnabled() =
                log "... Mark as enabled ..."
            let markAsDisabled() =
                log "... Mark as disabled ..."

            let mutable runConsuming = true
            while runConsuming do
                try
                    markAsEnabled()
                    runConsuming <- false

                    application.ConsumerConfiguration
                    |> kafka_consume  // todo replace with Kafka.consume
                    |> application.Consume
                with
                | :? Confluent.Kafka.KafkaException as e ->
                    markAsDisabled()

                    e
                    |> sprintf "%A"
                    |> application.Logger.Error "Kafka"

                    match application.OnError application.Logger e.Message with
                    | Reboot -> runConsuming <- true
                    | Shutdown -> runConsuming <- false

                if runConsuming then
                    log "Reboot ..."

    let run (kafka_consume: ConsumerConfiguration -> 'Event seq) (KafkaApplication application) =
        match application with
        | Ok app -> KafkaApplicationRunner.run kafka_consume app
        | Error error -> failwithf "[Application] Error:\n%A" error
