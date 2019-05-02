namespace KafkaApplication

module ApplicationRunner =
    open Kafka
    open ServiceIdentification
    open OptionOperators

    module private KafkaApplicationRunner =
        let private produceInstanceStarted produceSingleMessage logger box (supervisionProducer: ConnectedProducer) =
            box
            |> ApplicationEvents.createInstanceStarted
            |> ApplicationEvents.serialize
            |> produceSingleMessage supervisionProducer

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

        let private consumeWithErrorHandling (logger: KafkaApplication.Logger) runtimeParts flushProducers consumeEvents consumeLastEvent (consumeHandler: RuntimeConsumeHandlerForConnection<_>) =
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

        let run
            (consumeEvents: ConsumerConfiguration -> 'Event seq)
            (consumeLastEvent: ConsumerConfiguration -> 'Event option)
            connectProducer
            produceSingleMessage
            flushProducer
            closeProducer
            (application: KafkaApplicationParts<'Event>) =
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

    let runApplication = KafkaApplicationRunner.run
