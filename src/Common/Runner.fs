namespace KafkaApplication

module ApplicationRunner =
    open Kafka
    open ServiceIdentification
    open OptionOperators

    module private KafkaApplicationRunner =
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

            let connectedProducers = application.Producers |> Map.map (fun _ -> connectProducer)
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
        fun beforeRun parseEvent (KafkaApplication application) ->
            let consume configuration =
                Consumer.consume configuration parseEvent

            let consumeLast configuration =
                Consumer.consumeLast configuration parseEvent

            match application with
            | Ok app ->
                app
                |> (tee beforeRun)
                |> KafkaApplicationRunner.run
                    consume
                    consumeLast
                    Producer.connect
                    Producer.produceSingle
                    Producer.TopicProducer.flush
                    Producer.TopicProducer.close
            | Error error -> failwithf "[Application] Error:\n%A" error

    // todo remove
    let _runDummy = KafkaApplicationRunner.run
