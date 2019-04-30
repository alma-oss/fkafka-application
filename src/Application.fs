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
        let private prepareProducer (connections: Connections) incrementOutputCount name =
            result {
                let! connection =
                    connections
                    |> Map.tryFind name
                    |> Result.ofOption (ProduceError.MissingConfiguration name)

                let producer = Producer.createProducer connection.BrokerList
                let produce producer = Producer.produceMessage producer connection.Topic
                let incrementOutputCount = incrementOutputCount (OutputStreamName connection.Topic)

                let produceEvent producer event =
                    event
                    |> tee (Serializer.serialize >> (produce producer))
                    |> incrementOutputCount

                return {
                    Connection = name
                    Producer = producer
                    Produce = produceEvent
                }
            }

        let private composeRuntimeConsumeHandlersForConnections
            runtimeConsumerConfigurations
            runtimeParts
            (getErrorHandler: ConnectionName -> ErrorHandler)
            incrementInputCount
            ({ Connection = connection; Handler = handler }: ConsumeHandlerForConnection<'Event>) =
            result {
                let runtimeConnectionName = connection |> ConnectionName.runtimeName

                let! configuration =
                    runtimeConsumerConfigurations
                    |> Map.tryFind runtimeConnectionName
                    |> Result.ofOption (ConsumeHandlerError.MissingConfiguration connection)

                return {
                    Connection = runtimeConnectionName
                    Configuration = configuration
                    Handler = handler |> ConsumeHandler.toRuntime runtimeParts
                    OnError = connection |> getErrorHandler
                    IncrementInputCount =
                        match incrementInputCount with
                        | Some incrementInputCount -> incrementInputCount (InputStreamName configuration.Connection.Topic)
                        | None -> ignore
                }
            }

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
                let supervisionConnection = connections |> Map.tryFind Connections.Supervision

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

                let runtimeConsumerConfigurations =
                    connections
                    |> Map.fold (fun (runtimeConsumerConfigurations: Map<RuntimeConnectionName, ConsumerConfiguration>) name connection ->
                        let runtimeConnection = name |> ConnectionName.runtimeName

                        runtimeConsumerConfigurations.Add(
                            runtimeConnection,
                            {
                                Connection = connection
                                GroupId = groupIds.TryFind name <?=> defaultGroupId
                                Logger = { Log = logger.Verbose (sprintf "Kafka<%s>" runtimeConnection ) } |> Some
                                Checker = kafkaChecker |> ResourceChecker.updateResourceStatusOnCheck instance connection.BrokerList |> Some
                                ServiceStatus = { MarkAsEnabled = markAsEnabled; MarkAsDisabled = markAsDisabled } |> Some
                            }
                        )
                    ) Map.empty

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
                    configurationParts.ProduceTo
                    |> List.map (prepareProducer connections incrementOutputCount)
                    |> Result.sequence
                    |> Result.mapError ProduceError

                let (producers, produces) =
                    preparedProducers
                    |> List.fold (fun (producers: Map<RuntimeConnectionName, KafkaProducer>, produces: Map<RuntimeConnectionName, ProduceEvent<'Event>>) preparedProducer ->
                        let { Connection = (ConnectionName connection); Producer = producer; Produce = produce} = preparedProducer

                        ( producers.Add(connection, producer), produces.Add(connection, produce) )
                    ) (Map.empty, Map.empty)

                //
                // runtime parts
                //
                let runtimeParts: ConsumeRuntimeParts<'Event> = {
                    Logger = logger
                    Environment = environment
                    Connections = connections
                    ConsumerConfigurations = runtimeConsumerConfigurations
                    IncrementOutputEventCount = incrementOutputCount
                    Producers = producers
                    Produces = produces
                }

                let composeRuntimeHandler =
                    composeRuntimeConsumeHandlersForConnections
                        runtimeConsumerConfigurations
                        runtimeParts
                        getErrorHandler
                        incrementInputCount

                let! runtimeConsumeHandlers =
                    consumeHandlers
                    |> List.map composeRuntimeHandler
                    |> Result.sequence
                    |> Result.mapError ConsumeHandlerError

                return {
                    Logger = logger
                    Environment = environment
                    Box = box
                    ConsumerConfigurations = runtimeConsumerConfigurations
                    ConsumeHandlers = runtimeConsumeHandlers
                    MetricsRoute = configurationParts.MetricsRoute
                    SupervisionConnection = supervisionConnection
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

        [<CustomOperation("useSupervision")>]
        member __.Supervision(state, connectionConfiguration): Configuration<'Event> =
            state <!> fun parts -> { parts with Connections = parts.Connections.Add(Connections.Supervision, connectionConfiguration) }

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
        member __.ProduceTo(state, name): Configuration<'Event> =
            state <!> fun parts -> { parts with ProduceTo = ConnectionName name :: parts.ProduceTo }

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
                        ProduceTo = newParts.ProduceTo |> List.merge currentParts.ProduceTo
                        MetricsRoute = newParts.MetricsRoute <??> currentParts.MetricsRoute
                        CreateInputEventKeys = newParts.CreateInputEventKeys <??> currentParts.CreateInputEventKeys
                        CreateOutputEventKeys = newParts.CreateOutputEventKeys <??> currentParts.CreateOutputEventKeys
                        KafkaChecker = newParts.KafkaChecker <??> currentParts.KafkaChecker
                    }
                |> Configuration.result

        /// Start an asynchronous web server on http://127.0.0.1:8080 and shows metrics for prometheus.
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

        let private produceInstanceStarted logger box supervisionConnection =
            use producer = Producer.createProducer supervisionConnection.BrokerList

            box
            |> ApplicationEvents.createInstanceStarted
            |> ApplicationEvents.serialize
            |> Producer.produceSingleMessage producer supervisionConnection.Topic

            logger.Verbose "Supervision" "Instance started produced."

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
            application.Logger.Debug "Application" <| sprintf "Configuration:\n%A" application
            application.Logger.Log "Application" "Starts ..."

            let instance =
                application.Box
                |> Box.instance
                |> tee (ApplicationMetrics.enableContext)

            application.SupervisionConnection
            |>! produceInstanceStarted application.Logger application.Box

            application.MetricsRoute
            |>! (fun route ->
                ApplicationMetrics.showStateOnWebServerAsync instance route
                |> Async.Start
            )

            application.ConsumeHandlers
            |> List.rev
            |> List.iter (consumeWithErrorHandling application.Logger consumeEvents consumeLastEvent)

    let run (KafkaApplication application) =
        let consume configuration =
            Consumer.consume configuration RawEvent.parse       // todo - what with parse?

        let consumeLast configuration =
            Consumer.consumeLast configuration RawEvent.parse   // todo - what with parse?

        match application with
        | Ok app -> KafkaApplicationRunner.run consume consumeLast app
        | Error error -> failwithf "[Application] Error:\n%A" error

    // todo - remove
    let _runDummy (kafka_consume: ConsumerConfiguration -> 'Event seq) (kafka_consumeLast: ConsumerConfiguration -> 'Event option) (KafkaApplication application) =
        match application with
        | Ok app -> KafkaApplicationRunner.run kafka_consume kafka_consumeLast app
        | Error error -> failwithf "[Application] Error:\n%A" error
