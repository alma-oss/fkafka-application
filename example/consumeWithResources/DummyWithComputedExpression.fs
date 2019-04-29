namespace DummyWithComputedExpressionExample

module Dice =
    let private random = System.Random()
    let roll () =
        random.Next(1, 7)

module DummyKafka =
    open Confluent.Kafka
    open Kafka

    let consume (configuration: ConsumerConfiguration) =
        let (markAsEnabled, markAsDisabled) =
            match configuration.ServiceStatus with
            | Some { MarkAsEnabled = enable; MarkAsDisabled = disable } -> (enable, disable)
            | _ -> (ignore, ignore)

        let log msg =
            match configuration.Logger with
            | Some { Log = log } -> log msg
            | _ -> ()

        let (checkKafka, checkTopic) =
            match configuration.Checker with
            | Some checker ->
                let handle = Handle()

                (fun () -> checker.CheckCluster handle),
                (fun () -> checker.CheckTopic configuration.Connection.Topic handle)
            | _ ->
                log "Without checkers ..."
                (fun _ -> true), (fun _ -> true)

        seq {
            log "Start consuming ..."
            let mutable i = 0
            let mutable waitTimeModifier = 1

            while true do
                match checkKafka(), checkTopic() with
                | true, true ->
                    markAsEnabled()

                    if waitTimeModifier <> 1 then
                        waitTimeModifier <- 1

                    log <| sprintf "\n - reading from %A<%A> ..." configuration.Connection.Topic configuration.Connection.BrokerList
                    System.Threading.Thread.Sleep(1000)

                    if Dice.roll() = 1 && Dice.roll() = 1 && Dice.roll() = 1 then
                        raise (KafkaException(ErrorCode.IllegalGeneration))

                    yield i
                    i <- i + 1
                | _ ->
                    markAsDisabled()

                    if waitTimeModifier = 1 then log <| sprintf "\n - reading is not available - resource is down"
                    log <| sprintf " -> waiting %s" (String.replicate waitTimeModifier "...")
                    System.Threading.Thread.Sleep(3000 * waitTimeModifier)
                    waitTimeModifier <- waitTimeModifier * 2
        }

module DummyCheck =
    let checker: Kafka.Checker =
        {
            Kafka.Checker.defaultChecker with
                CheckCluster = fun _ -> Dice.roll() <> 1
                CheckTopic = fun _ _ -> Dice.roll() <> 1
        }

module Program =
    open Logging
    open Kafka
    open KafkaApplication

    let tee f a =
        f a
        a

    let createInputKeys (InputStreamName (StreamName inputStream)) event =
        let eventType =
            if event % 2 = 0 then "even" else "odd"
            |> sprintf "event_%s"

        SimpleDataSetKeys [
            ("event", eventType)
            ("input_stream", inputStream)
        ]

    let createOutputKeys (OutputStreamName (StreamName outputStream)) _ =
        SimpleDataSetKeys [
            ("event", "doubled")
            ("output_stream", outputStream)
        ]

    let run () =
        //
        // pattern
        //

        let double event =
            event * 2

        //
        // run simple app
        //

        let produce incrementOutputCount outputStream event =
            event
            |> tee (incrementOutputCount outputStream)
            |> printfn " -> response<%A>: %A" outputStream

        Log.setVerbosityLevel "q"

        let logger = Logger.defaultLogger

        let environment = environmentWithLogger logger

        kafkaApplication {
            useLogger logger

            merge (environment {
                file ["./.env"; "./.dist.env"]

                ifSetDo "VERBOSITY" Log.setVerbosityLevel

                instance "INSTANCE"
                //groupId "GROUP_ID"

                require [ "OUTPUT_STREAM" ]

                connect {
                    BrokerList = "BROKER_LIST"
                    Topic = "INPUT_STREAM"
                }

                connectTo "application" {
                    BrokerList = "BROKER_LIST"
                    Topic = "INPUT_STREAM"
                }
            })

            checkKafkaWith DummyCheck.checker

            consumeLastFrom "application" (fun parts lastMessage ->
                //let outputStream =
                //    parts.Environment.["OUTPUT_STREAM"]
                //    |> StreamName
                //    |> OutputStreamName

                //let interactionConfiguration = parts.Connections.["interaction"]    // todo add some method for this

                //Kafka.Consumer.consume interactionConfiguration id
                //|> Seq.map (
                //    tee incrementConsumedEvents
                //    >> tee fillStatePerEvent
                //)
                //|> Seq.takeWhile (hasLastAggregatedCorrelationId >> not)
                //|> Seq.iter ignore

                parts.Logger.Log "Application Last Message" <| sprintf "%i" lastMessage
            )

            onConsumeErrorFor "application" (fun _ _ -> Continue)

            consumeFrom "application" (fun parts events ->
                parts.Logger.Log "PreConsume" "First two events: "
                events
                |> Seq.take 2
                |> Seq.iter ((sprintf "- %A") >> parts.Logger.Log "PreConsume")
            )

            showInputEventsWith createInputKeys
            showOutputEventsWith createOutputKeys

            consume (fun parts events ->
                let outputStream =
                    parts.Environment.["OUTPUT_STREAM"]
                    |> StreamName
                    |> OutputStreamName

                events
                //|> Seq.map (tee incrementInputCount)
                //|> Seq.take 20
                |> Seq.iter (double >> produce parts.IncrementOutputEventCount outputStream)
            )

            onConsumeError (fun logger _ ->
                logger.Log "Application" "Waiting for reboot ..."
                System.Threading.Thread.Sleep(10 * 1000)

                Reboot
            )

            showMetricsOn "/metrics"
        }
        |> run DummyKafka.consume
        ()
