namespace RealLifeExample

[<RequireQualifiedAccess>]
module Dice =
    let roll () = System.Random().Next(0, 6)
    let isRollOver i = roll() >= i

module Program =
    open Microsoft.Extensions.Logging
    open Lmc.ServiceIdentification
    open Lmc.Tracing
    open Lmc.Kafka
    open Lmc.KafkaApplication

    type InputEvent = string
    type OutputEvent = string

    (* let createInputKeys (InputStreamName inputStream) (event: InputEvent) =
        SimpleDataSetKeys [
            ("event", event.Event |> EventName.value)
            ("input_stream", inputStream |> StreamName.value)
        ]

    let createOutputKeys (OutputStreamName outputStream) (event: OutputEvent) =
        SimpleDataSetKeys [
            ("event", event.Event |> EventName.value)
            ("output_stream", outputStream |> StreamName.value)
        ] *)

    let run envFiles loggerFactory =
        //
        // pattern
        //

        let parseEvent: ParseEvent<InputEvent> =
            id

        (* let fromDomain: FromDomain<OutputEvent> =
            fun (Serialize serialize) event ->
                let common = event |> RawEvent.toCommon
                MessageToProduce.create (
                    MessageKey.Delimited [ common.Zone |> Zone.value; common.Bucket |> Bucket.value ],
                    serialize event
                ) *)

        //
        // run simple app
        //

        run<InputEvent, OutputEvent, _> <|
        kafkaApplication {
            useLoggerFactory loggerFactory
            useCommitMessage (CommitMessage.Manually FailOnNotCommittedMessage.WithException)

            merge (environment {
                file envFiles

                instance "INSTANCE"
                currentEnvironment "ENVIRONMENT"

                connect {
                    BrokerList = "KAFKA_BROKER"
                    Topic = "INPUT_STREAM"
                }

                groupId "GROUP_ID"
            })

            parseEventWith parseEvent

            showMetrics
            showAppRootStatus

            consume (fun app { Event = event; Trace = trace } ->
                printfn "------------------------------------------------------"
                let logger = app.LoggerFactory.CreateLogger("Consume")
                logger |> Consumer.logLastMessageManuallyCommittedState

                app.Connections[Connections.Default]
                |> printfn "Connection:\n%A\n"
                |> ignore

                logger.LogInformation("Start consuming event: {event} ({trace})", event, (trace |> Trace.id))

                if Dice.isRollOver 2 then
                    // simulation of error, which leads to skip the commit
                    logger.LogInformation("SKIP event: {event}", event)
                    failwithf "SKIP event"

                logger.LogInformation("Consumed and processed event: {event}", event)

                printfn "------------------------------------------------------"
                ()
            )
        }
