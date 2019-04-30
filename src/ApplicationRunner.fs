namespace KafkaApplication

module ApplicationRunner =
    open Kafka
    open ServiceIdentification
    open OptionOperators

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

    let runApplication = KafkaApplicationRunner.run
