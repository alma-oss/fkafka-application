namespace KafkaApplication

module ApplicationRunner =
    open Kafka
    open ServiceIdentification
    open OptionOperators

    module private KafkaApplicationRunner =
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
            |> ApplicationEvents.fromDomain Serializer.serialize
            |> produceSingleMessage supervisionProducer

            logger.Verbose "Supervision" "Instance started produced."

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

            application.MetricsRoute
            |> Option.map (ApplicationMetrics.showStateOnWebServerAsync instance)
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

    let internal runKafkaApplication: RunKafkaApplication<'InputEvent, 'OutputEvent> =
        fun beforeRun (KafkaApplication application) ->
            try
                match application with
                | Ok app ->
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
                | Error error ->
                    error
                    |> logApplicationError "Application"
                    |> WithError
            with
            | error ->
                error
                |> logApplicationError "Application"
                |> WithRuntimeError
