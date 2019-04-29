namespace KafkaApplication

open Kafka
open ServiceIdentification

[<AutoOpen>]
module KafkaApplication =
    open OptionOperators

    let private tee f a =
        f a
        a

    let private assertNotEmpty error collection =
        if collection |> Seq.isEmpty then Error (KafkaApplicationError error)
        else Ok collection

    [<AutoOpen>]
    module private KafkaApplicationBuilder =

        let private composeRuntimeConsumeHandlersForConnections
            consumerConfigurations
            runtimeParts
            (getErrorHandler: ConnectionName -> ErrorHandler)
            ({ Connection = connection; Handler = handler }: ConsumeHandlerForConnection<'Event>) =

            match consumerConfigurations |> Map.tryFind connection with
            | Some configuration ->
                Ok {
                    Connection = connection
                    Configuration = configuration
                    Handler = handler |> ConsumeHandler.toRuntime runtimeParts
                    OnError = connection |> getErrorHandler
                }
            | _ ->
                MissingConfiguration connection
                |> ConsumeHandlerError
                |> Error

        let buildApplication (Configuration configuration): KafkaApplication<'Event> =
            result {
                let! configurationParts = configuration

                // required parts
                let! instance = configurationParts.Instance <?!> "Instance is required."
                let! connections = configurationParts.Connections |> assertNotEmpty "At least one connection configuration is required."
                let! consumeHandlers = configurationParts.ConsumeHandlers |> assertNotEmpty "At least one consume handler is required."

                // optional parts
                let defaultErrorHandler = (fun _ _ -> Shutdown)
                let getErrorHandler connection =
                    match configurationParts.OnConsumeErrorHandlers |> Map.tryFind connection with
                    | Some errorHandler -> errorHandler
                    | _ -> (fun _ _ -> Shutdown)

                let spot = configurationParts.Spot <?=> { Zone = Zone "common"; Bucket = Bucket "all" }
                let box = Box.createFromValues instance.Domain instance.Context instance.Purpose instance.Version spot.Zone spot.Bucket

                let logger = configurationParts.Logger
                let environment = configurationParts.Environment
                let defaultGroupId = configurationParts.GroupId <?=> GroupId.Random
                let groupIds = configurationParts.GroupIds
                let metricsRoute = configurationParts.MetricsRoute

                // composed parts
                let consumerConfigurations =
                    connections
                    |> Map.map (fun name connection ->
                        groupIds.TryFind name <?=> defaultGroupId
                        |> Kafka.ConsumerConfiguration.createWithConnection connection
                    )

                let runtimeParts: ConsumeRuntimeParts = {
                    Logger = logger
                    Environment = environment
                    Connections = connections
                    ConsumerConfigurations = consumerConfigurations
                }

                let! runtimeConsumeHandlers =
                    consumeHandlers
                    |> List.map (composeRuntimeConsumeHandlersForConnections consumerConfigurations runtimeParts getErrorHandler)
                    |> Result.sequence

                return {
                    Logger = configurationParts.Logger
                    Environment = configurationParts.Environment
                    Box = box
                    ConsumerConfigurations = consumerConfigurations
                    ConsumeHandlers = runtimeConsumeHandlers
                    MetricsRoute = metricsRoute
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

        [<CustomOperation("useSpot")>]
        member __.Spot(state, spot): Configuration<'Event> =
            state <!> fun parts -> { parts with Spot = Some spot }

        [<CustomOperation("useGroupId")>]
        member __.GroupId(state, groupId): Configuration<'Event> =
            state <!> fun parts -> { parts with GroupId = Some groupId }

        [<CustomOperation("connect")>]
        member __.Connect(state, connectionConfiguration): Configuration<'Event> =
            state <!> fun parts -> { parts with Connections = parts.Connections.Add(Connections.Default, connectionConfiguration) }

        [<CustomOperation("connectTo")>]
        member __.ConnectTo(state, name, connectionConfiguration): Configuration<'Event> =
            state <!> fun parts -> { parts with Connections = parts.Connections.Add(ConnectionName name, connectionConfiguration) }

        [<CustomOperation("consume")>]
        member __.Consume(state, consumeHandler): Configuration<'Event> =
            state <!> fun parts -> { parts with ConsumeHandlers = { Connection = Connections.Default; Handler = ConsumeHandler.Events consumeHandler} :: parts.ConsumeHandlers }

        [<CustomOperation("consumeFrom")>]
        member __.ConsumeFrom(state, name, consumeHandler): Configuration<'Event> =
            state <!> fun parts -> { parts with ConsumeHandlers = { Connection = ConnectionName name; Handler = ConsumeHandler.Events consumeHandler} :: parts.ConsumeHandlers }

        [<CustomOperation("consumeLast")>]
        member __.ConsumeLast(state, consumeHandler): Configuration<'Event> =
            state <!> fun parts -> { parts with ConsumeHandlers = { Connection = Connections.Default; Handler = ConsumeHandler.LastEvent consumeHandler} :: parts.ConsumeHandlers }

        [<CustomOperation("consumeLastFrom")>]
        member __.ConsumeLastFrom(state, name, consumeHandler): Configuration<'Event> =
            state <!> fun parts -> { parts with ConsumeHandlers = { Connection = ConnectionName name; Handler = ConsumeHandler.LastEvent consumeHandler} :: parts.ConsumeHandlers }

        [<CustomOperation("onConsumeError")>]
        member __.OnConsumeError(state, onConsumeError): Configuration<'Event> =
            state <!> fun parts -> { parts with OnConsumeErrorHandlers = parts.OnConsumeErrorHandlers.Add(Connections.Default, onConsumeError) }

        [<CustomOperation("onConsumeErrorFor")>]
        member __.OnConsumeErrorFor(state, name, onConsumeError): Configuration<'Event> =
            state <!> fun parts -> { parts with OnConsumeErrorHandlers = parts.OnConsumeErrorHandlers.Add(ConnectionName name, onConsumeError) }

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
                        Spot = newParts.Spot <??> currentParts.Spot
                        GroupId = newParts.GroupId <??> currentParts.GroupId
                        GroupIds = newParts.GroupIds |> Map.merge currentParts.GroupIds
                        Connections = newParts.Connections |> Map.merge currentParts.Connections
                        ConsumeHandlers = newParts.ConsumeHandlers |> List.merge currentParts.ConsumeHandlers
                        OnConsumeErrorHandlers = newParts.OnConsumeErrorHandlers |> Map.merge currentParts.OnConsumeErrorHandlers
                        MetricsRoute = newParts.MetricsRoute <??> currentParts.MetricsRoute
                    }
                |> Configuration.result

        /// It will start an asynchronous web server on http://127.0.0.1:8080 and shows metrics for prometheus.
        [<CustomOperation("showMetricsOn")>]
        member __.ShowMetricsOn(state, route): Configuration<'Event> =
            state >>= fun parts ->
                result {
                    let! route =
                        route
                        |> MetricsRoute.create
                        |> Result.mapError InvalidRoute

                    return { parts with MetricsRoute = Some route }
                }
                |> Result.mapError MetricsError

    let kafkaApplication = KafkaApplicationBuilder()

    module private KafkaApplicationRunner =

        let private consume consumeEvents consumeLastEvent configuration = function
            | Events eventsHandler ->
                configuration
                |> consumeEvents
                |> eventsHandler
            | LastEvent lastEventHandler ->
                configuration
                |> consumeLastEvent
                |>! lastEventHandler

        let private consumeWithErrorHandling (logger: KafkaApplication.Logger) markAsEnabled markAsDisabled consumeEvents consumeLastEvent (consumeHandler: RuntimeConsumeHandlerForConnection<_>) =
            let context =
                consumeHandler.Connection
                |> ConnectionName.value
                |> sprintf "Kafka<%s>"

            let mutable runConsuming = true
            while runConsuming do
                try
                    markAsEnabled()
                    runConsuming <- false

                    consumeHandler.Handler
                    |> consume consumeEvents consumeLastEvent consumeHandler.Configuration
                with
                | :? Confluent.Kafka.KafkaException as e ->
                    markAsDisabled()
                    logger.Error context <| sprintf "%A" e

                    match consumeHandler.OnError logger e.Message with
                    | Reboot ->
                        runConsuming <- true
                        logger.Log context "Reboot current consume ..."
                    | Continue ->
                        logger.Log context "Continue to next consume ..."
                        runConsuming <- false
                    | Shutdown ->
                        logger.Log context "Shuting down the application ..."
                        raise e

        let run (consumeEvents: ConsumerConfiguration -> 'Event seq) (consumeLastEvent: ConsumerConfiguration -> 'Event option) (application: KafkaApplicationParts<'Event>) =
            let log = application.Logger.Log "Application"
            let logVerbose = application.Logger.Verbose "Application"
            log "Starts ..."

            logVerbose <| sprintf "Box:\n%A" application.Box
            logVerbose <| sprintf "Kafka:\n%A" application.ConsumerConfigurations

            let instance =
                application.Box
                |> Box.instance
                |> tee (ApplicationMetrics.enableInstance)

            application.MetricsRoute
            |>! (fun route ->
                ApplicationMetrics.showStateOnWebServerAsync instance route
                |> Async.Start
            )

            let markAsEnabled() =
                // todo - produce `instance_started` event, id application_connection is available
                log "... Mark as enabled | instance_started ..."
            let markAsDisabled() =
                log "... Mark as disabled ..."

            application.ConsumeHandlers
            |> List.rev
            |> List.iter (consumeWithErrorHandling application.Logger markAsEnabled markAsDisabled consumeEvents consumeLastEvent)

    let run (kafka_consume: ConsumerConfiguration -> 'Event seq) (KafkaApplication application) =
        let kafka_consumeLast = (fun _ -> None) // never return any last message - todo - remove and use directly from kafka

        match application with
        | Ok app -> KafkaApplicationRunner.run kafka_consume kafka_consumeLast app
        | Error error -> failwithf "[Application] Error:\n%A" error
