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
        let private prepareProducer (connections: Connections) incrementOutputCount =
            fun name (ProducerSerializer serialize) ->
                result {
                    let! connection =
                        connections.TryFind name
                        |> Result.ofOption "No connection was found for producer."
                        |> Result.mapError KafkaApplicationError    // todo - use more specific error

                    let producer = Producer.createProducer connection.BrokerList
                    let produce producer = Producer.produceMessage producer connection.Topic
                    let incrementOutputCount = incrementOutputCount (OutputStreamName connection.Topic)

                    let produceEvent producer event =
                        event
                        |> tee (serialize >> (produce producer))
                        |> incrementOutputCount

                    return {
                        Connection = name
                        Producer = producer
                        Produce = produceEvent
                    }
                }

        let private composeRuntimeConsumeHandlersForConnections
            consumerConfigurations
            runtimeParts
            (getErrorHandler: ConnectionName -> ErrorHandler)
            incrementInputCount
            ({ Connection = connection; Handler = handler }: ConsumeHandlerForConnection<'Event>) =

            match consumerConfigurations |> Map.tryFind connection with
            | Some configuration ->
                Ok {
                    Connection = connection
                    Configuration = configuration
                    Handler = handler |> ConsumeHandler.toRuntime runtimeParts
                    OnError = connection |> getErrorHandler
                    IncrementInputCount =
                        match incrementInputCount with
                        | Some incrementInputCount -> incrementInputCount (InputStreamName configuration.Connection.Topic)
                        | None -> ignore
                }
            | _ ->
                MissingConfiguration connection
                |> ConsumeHandlerError
                |> Error

        let buildApplication (Configuration configuration): KafkaApplication<'Event> =
            result {
                let! configurationParts = configuration

                //
                // required parts
                //
                let! instance = configurationParts.Instance <?!> "Instance is required."
                let! connections = configurationParts.Connections |> assertNotEmpty "At least one connection configuration is required."
                let! consumeHandlers = configurationParts.ConsumeHandlers |> assertNotEmpty "At least one consume handler is required."

                //
                // optional parts
                //
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
                let kafkaChecker = configurationParts.KafkaChecker <?=> Kafka.Checker.defaultChecker

                //
                // composed parts
                //

                // consumers
                let! markAsEnabled =
                    Metrics.ServiceStatus.markAsEnabled instance Metrics.Audience.Sys
                    |> Result.mapError (MetricError >> MetricsError)
                let! markAsDisabled =
                    Metrics.ServiceStatus.markAsDisabled instance Metrics.Audience.Sys
                    |> Result.mapError (MetricError >> MetricsError)

                let consumerConfigurations =
                    connections
                    |> Map.map (fun name connection -> {
                        Connection = connection
                        GroupId = groupIds.TryFind name <?=> defaultGroupId
                        Logger = { Log = logger.Verbose "Kafka" } |> Some
                        Checker = kafkaChecker |> ResourceChecker.updateResourceStatusOnCheck instance connection.BrokerList |> Some
                        ServiceStatus = { MarkAsEnabled = markAsEnabled; MarkAsDisabled = markAsDisabled } |> Some
                    })

                // input/output metrics
                let incrementInputCount =
                    configurationParts.CreateInputEventKeys
                    |> Option.map (fun createInputKeys ->
                        ApplicationMetrics.incrementTotalInputEventCount createInputKeys instance
                    )
                let incrementOutputCount =
                    match configurationParts.CreateOutputEventKeys with
                    | Some createOutputKeys -> ApplicationMetrics.incrementTotalOutputEventCount createOutputKeys instance
                    | _ -> fun _ -> ignore

                // producers
                let! preparedProducers =
                    configurationParts.ProducerSerializers
                    |> Map.map (prepareProducer connections incrementOutputCount)
                    |> Map.toList
                    |> List.map snd
                    |> Result.sequence

                let (producers, produces) =
                    preparedProducers
                    |> List.fold (fun (producers: Map<ConnectionName, KafkaProducer>, produces: Map<ConnectionName, ProduceEvent<'Event>>) preparedProducer ->
                        (
                            producers.Add(preparedProducer.Connection, preparedProducer.Producer),
                            produces.Add(preparedProducer.Connection, preparedProducer.Produce)
                        )
                    ) (Map.empty, Map.empty)

                //
                // runtime parts
                //
                let runtimeParts: ConsumeRuntimeParts<'Event> = {
                    Logger = logger
                    Environment = environment
                    Connections = connections
                    ConsumerConfigurations = consumerConfigurations
                    IncrementOutputEventCount = incrementOutputCount
                    Producers = producers
                    Produces = produces
                }

                let composeRuntimeHandler =
                    composeRuntimeConsumeHandlersForConnections
                        consumerConfigurations
                        runtimeParts
                        getErrorHandler
                        incrementInputCount

                let! runtimeConsumeHandlers =
                    consumeHandlers
                    |> List.map composeRuntimeHandler
                    |> Result.sequence

                return {
                    Logger = logger
                    Environment = environment
                    Box = box
                    ConsumerConfigurations = consumerConfigurations
                    ConsumeHandlers = runtimeConsumeHandlers
                    MetricsRoute = configurationParts.MetricsRoute
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

        [<CustomOperation("checkKafkaWith")>]
        member __.CheckKafkaWith(state, checker): Configuration<'Event> =
            state <!> fun parts -> { parts with KafkaChecker = Some checker }

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

        [<CustomOperation("produceTo")>]
        member __.ProduceTo(state, name, serialize): Configuration<'Event> =
            state <!> fun parts -> { parts with ProducerSerializers = parts.ProducerSerializers.Add(ConnectionName name, ProducerSerializer serialize) }

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
                        ProducerSerializers = newParts.ProducerSerializers |> Map.merge currentParts.ProducerSerializers
                        MetricsRoute = newParts.MetricsRoute <??> currentParts.MetricsRoute
                        CreateInputEventKeys = newParts.CreateInputEventKeys <??> currentParts.CreateInputEventKeys
                        CreateOutputEventKeys = newParts.CreateOutputEventKeys <??> currentParts.CreateOutputEventKeys
                        KafkaChecker = newParts.KafkaChecker <??> currentParts.KafkaChecker
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

        [<CustomOperation("showInputEventsWith")>]
        member __.ShowInputEventsWith(state, createInputEventKeys): Configuration<'Event> =
            state <!> fun parts -> { parts with CreateInputEventKeys = Some (CreateInputEventKeys createInputEventKeys) }

        [<CustomOperation("showOutputEventsWith")>]
        member __.ShowOutputEventsWith(state, createOutputEventKeys): Configuration<'Event> =
            state <!> fun parts -> { parts with CreateOutputEventKeys = Some (CreateOutputEventKeys createOutputEventKeys) }

    let kafkaApplication = KafkaApplicationBuilder()

    module private KafkaApplicationRunner =

        let private consume
            (consumeEvents: ConsumerConfiguration -> 'Event seq)
            (consumeLastEvent: ConsumerConfiguration -> 'Event option)
            configuration
            incrementInputEventCount = function
            | Events eventsHandler ->
                configuration
                |> consumeEvents
                |> Seq.map (tee incrementInputEventCount)
                |> eventsHandler
            | LastEvent lastEventHandler ->
                configuration
                |> consumeLastEvent
                |>! lastEventHandler

        let private consumeWithErrorHandling (logger: KafkaApplication.Logger) consumeEvents consumeLastEvent (consumeHandler: RuntimeConsumeHandlerForConnection<_>) =
            let context =
                consumeHandler.Connection
                |> ConnectionName.value
                |> sprintf "Kafka<%s>"

            let mutable runConsuming = true
            while runConsuming do
                try
                    runConsuming <- false

                    consumeHandler.Handler
                    |> consume consumeEvents consumeLastEvent consumeHandler.Configuration consumeHandler.IncrementInputCount
                with
                | :? Confluent.Kafka.KafkaException as e ->
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
                // todo - produce `instance_started` event, id application_connection is available

            application.MetricsRoute
            |>! (fun route ->
                ApplicationMetrics.showStateOnWebServerAsync instance route
                |> Async.Start
            )

            application.ConsumeHandlers
            |> List.rev
            |> List.iter (consumeWithErrorHandling application.Logger consumeEvents consumeLastEvent)

    let run (kafka_consume: ConsumerConfiguration -> 'Event seq) (KafkaApplication application) =
        let kafka_consumeLast = (fun _ -> None) // never return any last message - todo - remove and use directly from kafka

        match application with
        | Ok app -> KafkaApplicationRunner.run kafka_consume kafka_consumeLast app
        | Error error -> failwithf "[Application] Error:\n%A" error
