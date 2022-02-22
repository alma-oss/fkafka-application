namespace RealLifeExample

open Microsoft.Extensions.Logging
open Lmc.ServiceIdentification
open Lmc.Kafka
open Lmc.KafkaApplication
open Lmc.ErrorHandling
open Lmc.Tracing

type InputEvent = string
type OutputEvent = string

[<RequireQualifiedAccess>]
module Dice =
    let roll () = System.Random().Next(0, 6)
    let isRollOver i =
        use rollTrace = "Dice rool" |> Trace.ChildOf.startFromActive
        printfn "[Dice] roll with trace: %A" (rollTrace |> Trace.id)

        let roll = roll()

        rollTrace
        |> Trace.addTags [ "rool", string roll ]
        |> ignore

        roll >= i

[<RequireQualifiedAccess>]
module App =
    let parseEvent: ParseEvent<InputEvent> =
        id

    (* let fromDomain: FromDomain<OutputEvent> =
        fun (Serialize serialize) event ->
            let common = event |> RawEvent.toCommon
            MessageToProduce.create (
                MessageKey.Delimited [ common.Zone |> Zone.value; common.Bucket |> Bucket.value ],
                serialize event
            ) *)

    let fromDomain: FromDomain<OutputEvent> =
        fun (Serialize serialize) event ->
            MessageToProduce.create (
                MessageKey.Simple "key",
                event
            )

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

    let kafkaApplicationExample envFiles loggerFactory: Application<InputEvent, OutputEvent, _> =
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

            consume (fun app { Event = event; Trace = trace } -> asyncResult {
                printfn "------------------------------------------------------"
                let consumeTrace =
                    "consume event in example"
                    |> Trace.ChildOf.start trace

                let logger = app.LoggerFactory.CreateLogger("Consume")
                logger |> Consumer.logLastMessageManuallyCommittedState

                app.Connections[Connections.Default]
                |> printfn "Connection:\n%A\n"
                |> ignore

                logger.LogInformation("Start consuming event: {event} ({trace})", event, (consumeTrace |> Trace.id))

                if Dice.isRollOver 5 then
                    // simulation of error, which leads to skip the commit
                    logger.LogInformation("SKIP event: {event}", event)
                    consumeTrace |> Trace.addTags [ "skip", "true" ] |> ignore
                    failwithf "SKIP event"

                logger.LogInformation("Consumed and processing event: {event} ...", event)
                do! AsyncResult.sleep 2000

                logger.LogInformation("Consumed and processed event: {event}", event)
                consumeTrace |> Trace.addTags [ "skip", "false" ] |> ignore

                printfn "------------------------------------------------------"
                return ()
            })
        }

    open Lmc.KafkaApplication.Deriver

    let deriverExample envFiles loggerFactory: Application<InputEvent, OutputEvent, _> =
        let deriveEvent (app: PatternRuntimeParts): DeriveEventAsyncResult<InputEvent, OutputEvent> = fun processedBy { Event = event; Trace = trace } -> asyncResult {
            printfn "------------------------------------------------------"
            let finish = ignore

            use deriveTrace =
                "derive event in example"
                |> Trace.ChildOf.startActive trace

            let logger = app.LoggerFactory.CreateLogger("Derive")
            logger |> Consumer.logLastMessageManuallyCommittedState

            logger.LogInformation("Start deriving event: {event} ({trace})", event, (deriveTrace |> Trace.id))

            if Dice.isRollOver 6 then
                // simulation of error, which leads to skip the commit
                logger.LogInformation("SKIP event: {event}", event)
                deriveTrace |> Trace.addTags [ "skip", "true" ] |> finish
                failwithf "SKIP event"

            logger.LogInformation("Consumed and deriving event: {event} ...", event)
            do! AsyncResult.sleep 2000

            logger.LogInformation("Consumed and derived event: {event}", event)
            deriveTrace |> Trace.addTags [ "skip", "false" ] |> finish

            printfn "------------------------------------------------------"

            return []
        }

        //let deriveEvent (app: PatternRuntimeParts): DeriveEvent<InputEvent, OutputEvent> = fun processedBy { Event = event; Trace = trace } -> []

        deriver {
            from (partialKafkaApplication {
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

                    connectTo "outputStream" {
                        BrokerList = "KAFKA_BROKER"
                        Topic = "OUTPUT_STREAM"
                    }

                    groupId "GROUP_ID"
                })

                parseEventWith parseEvent

                showMetrics
                showAppRootStatus
            })

            deriveToWithApplication "outputStream" deriveEvent fromDomain
        }

module Program =
    let run envFiles loggerFactory =
        // App.kafkaApplicationExample
        App.deriverExample
            envFiles
            loggerFactory
        |> run
