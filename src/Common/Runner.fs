namespace KafkaApplication

module internal ApplicationRunner =
    open Kafka
    open ServiceIdentification
    open OptionOperators

    module private KafkaApplicationRunner =
        open System
        open Metrics
        open Metrics.ServiceStatus

        let private checkResources (application: KafkaApplicationParts<_, _>) =
            let instance = application.Box |> Box.instance
            let enable = ResourceAvailability.enable instance >> ignore
            let disable = ResourceAvailability.disable instance >> ignore

            let debugResource = application.Logger.Debug "Resource"
            let debugResourceResult resource = sprintf "Checked %A with %A" resource >> debugResource

            application.IntervalResourceCheckers
            |> List.rev
            |> List.map (fun { Resource = resource; Interval = interval; Checker = checker } ->
                application.Logger.Verbose "Resource" <| sprintf "Start checking for %A" resource

                let intervalInMilliseconds = (int interval) * 1000

                let checkResource () =
                    debugResource <| sprintf "Check %A" resource

                    checker()
                    |> tee (debugResourceResult resource)
                    |> function
                        | Up -> enable resource
                        | Down -> disable resource

                async {
                    checkResource()

                    while true do
                        do! Async.Sleep intervalInMilliseconds
                        checkResource()
                }
            )
            |> List.iter Async.Start

        let private wait (seconds: int<KafkaApplication.Second>) =
            Threading.Thread.Sleep(TimeSpan.FromSeconds (float seconds))

        let rec private connectProducersWithErrorHandling connectProducer application =
            try
                application.Producers |> Map.map (fun _ -> connectProducer)
            with
            | e ->
                application.Logger.Error "Application" <| sprintf "%A" e
                let log = application.Logger.Log "Application"

                match application.ProducerErrorHandler application.Logger e.Message with
                | ProducerErrorPolicy.Shutdown ->
                    log "Producer could not connect, application will shutdown."
                    failwithf "Producer could not connect due to:\n\t%A" e.Message
                | ProducerErrorPolicy.ShutdownIn seconds ->
                    log <| sprintf "Producer could not connect, application will shutdown in %i seconds." seconds
                    wait seconds
                    failwithf "Producer could not connect due to:\n\t%A" e.Message
                | ProducerErrorPolicy.Retry ->
                    log "Producer could not connect, try again."
                    connectProducersWithErrorHandling connectProducer application
                | ProducerErrorPolicy.RetryIn seconds ->
                    log <| sprintf "Producer could not connect, try again in %i seconds." seconds
                    wait seconds
                    connectProducersWithErrorHandling connectProducer application

        let private produceInstanceStarted produceSingleMessage logger box (supervisionProducer: ConnectedProducer) =
            box
            |> ApplicationEvents.createInstanceStarted
            |> ApplicationEvents.fromDomain Serializer.toJson
            |> produceSingleMessage supervisionProducer

            logger.Verbose "Application<Supervision>" "Instance started produced."

        let private consume<'InputEvent>
            (consumeEvents: ConsumerConfiguration -> 'InputEvent seq)
            (consumeLastEvent: ConsumerConfiguration -> 'InputEvent option)
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

        let private consumeWithErrorHandling<'InputEvent, 'OutputEvent>
            (logger: ApplicationLogger)
            (runtimeParts: ConsumeRuntimeParts<'OutputEvent>)
            flushProducers
            consumeEvents
            consumeLastEvent
            (consumeHandler: RuntimeConsumeHandlerForConnection<'InputEvent, 'OutputEvent>) =
            let context = sprintf "Kafka<%s>" consumeHandler.Connection

            let mutable runConsuming = true
            while runConsuming do
                try
                    try
                        runConsuming <- false

                        consumeHandler.Handler
                        |> ConsumeHandler.toRuntime runtimeParts
                        |> consume consumeEvents consumeLastEvent consumeHandler.Configuration consumeHandler.IncrementInputCount
                    with
                    | :? Confluent.Kafka.KafkaException as e ->
                        logger.Error context <| sprintf "%A" e
                        let log = logger.Log context

                        match consumeHandler.OnError logger e.Message with
                        | Retry ->
                            runConsuming <- true
                            log "Retry current consume ..."
                        | RetryIn seconds ->
                            runConsuming <- true
                            log <| sprintf "Retry current consume in %i seconds ..." seconds
                            wait seconds
                        | Continue ->
                            log "Continue to next consume ..."
                            runConsuming <- false
                        | Shutdown ->
                            log "Shutting down the application ..."
                            raise e
                        | ShutdownIn seconds ->
                            log <| sprintf "Shutting down the application in %i seconds ..." seconds
                            wait seconds
                            raise e
                finally
                    logger.Verbose context "Flush all producers ..."
                    flushProducers()

        let private doWithAllProducers producers action =
            producers
            |> Map.toList
            |> List.map snd
            |> List.iter action

        let run<'InputEvent, 'OutputEvent>
            (consumeEvents: ConsumerConfiguration -> 'InputEvent seq)
            (consumeLastEvent: ConsumerConfiguration -> 'InputEvent option)
            connectProducer
            produceSingleMessage
            flushProducer
            closeProducer
            (application: KafkaApplicationParts<'InputEvent, 'OutputEvent>) =
            application.Logger.Debug "Application" <| sprintf "Configuration:\n%A" application
            application.Logger.Log "Application" "Starts ..."

            let instance =
                application.Box
                |> Box.instance
                |> tee (ApplicationMetrics.enableContext)

            application.ServiceStatus.MarkAsDisabled
            |> MarkAsDisabled.execute

            application |> checkResources

            application.MetricsRoute
            |> Option.map (ApplicationMetrics.showStateOnWebServerAsync instance application.CustomMetrics)
            |>! Async.Start

            application.Logger.Verbose "Application" "Connect producers ..."
            let connectedProducers =
                application
                |> connectProducersWithErrorHandling connectProducer
            application.Logger.Verbose "Application" "All producers are connected."

            let doWithAllProducers = doWithAllProducers connectedProducers

            let runtimeParts =
                application.PreparedRuntimeParts
                |> PreparedConsumeRuntimeParts.toRuntimeParts connectedProducers

            connectedProducers
            |> Map.tryFind (Connections.Supervision |> ConnectionName.runtimeName)
            |>! produceInstanceStarted produceSingleMessage application.Logger application.Box

            let flushAllProducers () = flushProducer |> doWithAllProducers

            try
                application.ConsumeHandlers
                |> List.rev
                |> List.iter (consumeWithErrorHandling application.Logger runtimeParts flushAllProducers consumeEvents consumeLastEvent)
            finally
                application.Logger.Verbose "Application" "Close producers ..."
                closeProducer |> doWithAllProducers

    let runKafkaApplication: RunKafkaApplication<'InputEvent, 'OutputEvent> =
        fun beforeRun (KafkaApplication application) ->
            match application with
            | Ok app ->
                try
                    let consume configuration =
                        Consumer.consume configuration app.ParseEvent

                    let consumeLast configuration =
                        Consumer.consumeLast configuration app.ParseEvent

                    app
                    |> (tee beforeRun)
                    |> KafkaApplicationRunner.run
                        consume
                        consumeLast
                        Producer.connect
                        Producer.produceSingle
                        Producer.TopicProducer.flush
                        Producer.TopicProducer.close
                    Successfully
                with
                | error ->
                    error
                    |> sprintf "Exception:\n%A"
                    |> tee (app.Logger.Error "RuntimeError" >> fun _ ->
                        app.Logger.Log "Application" "Shutting down with runtime error ..."
                        System.Threading.Thread.Sleep(System.TimeSpan.FromSeconds(2.0)) // Main thread waits till logger logs error message
                    )
                    |> WithRuntimeError
            | Error error ->
                error
                |> logApplicationError "Error"
                |> WithError
