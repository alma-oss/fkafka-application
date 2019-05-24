namespace DummyExample

module Dice =
    let private random = System.Random()
    let roll () =
        random.Next(1, 7)

module DummyKafka =
    open Confluent.Kafka
    open Kafka
    open Metrics.ServiceStatus

    let consume (configuration: ConsumerConfiguration) =
        let (markAsEnabled, markAsDisabled) =
            match configuration.ServiceStatus with
            | Some { MarkAsEnabled = (MarkAsEnabled enable); MarkAsDisabled = (MarkAsDisabled disable) } -> ( enable, disable)
            | _ -> (ignore, ignore)

        let (checkKafka, checkTopic) =
            match configuration.Checker with
            | Some checker ->
                let handle = Handle()

                (fun () -> checker.CheckCluster handle),
                (fun () -> checker.CheckTopic configuration.Connection.Topic handle)
            | _ -> failwithf "No checker is set"

        seq {
            printfn "Start consuming ..."
            let mutable i = 0
            let mutable waitTimeModifier = 1

            while true do
                match checkKafka(), checkTopic() with
                | true, true ->
                    markAsEnabled()

                    if waitTimeModifier <> 1 then
                        waitTimeModifier <- 1

                    printfn "\n - reading from %A<%A> ..." configuration.Connection.Topic configuration.Connection.BrokerList
                    System.Threading.Thread.Sleep(1000)

                    if Dice.roll() = 1 && Dice.roll() = 1 && Dice.roll() = 1 then
                        raise (KafkaException(ErrorCode.IllegalGeneration))

                    yield i
                    i <- i + 1
                | _ ->
                    markAsDisabled()

                    if waitTimeModifier = 1 then printfn "\n - reading is not available - resource is down"
                    printfn " -> waiting %s" (String.replicate waitTimeModifier "...")
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
    open Confluent.Kafka
    open Kafka
    open Metrics
    open Metrics.ServiceStatus
    open KafkaApplication
    open ServiceIdentification
    open ApplicationMetrics

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

        let instance = {
            Domain = Domain "arch"
            Context = Context "example"
            Purpose = Purpose "dummy"
            Version = Version "dev"
        }

        let inputStream = StreamName "example-stream-common-all"
        let brokerList = BrokerList "kfall-1"

        let outputStream = "example-outputStream-common-all"

        //
        // pattern
        //

        let double event =
            event * 2

        //
        // run simple app
        //

        // metrics
        let incrementInputCount = incrementTotalInputEventCount (CreateInputEventKeys createInputKeys) instance (InputStreamName inputStream)
        let incrementOutputCount = incrementTotalOutputEventCount (CreateOutputEventKeys createOutputKeys) instance (OutputStreamName (Kafka.StreamName outputStream))

        // service status
        let markAsEnabled () =
            ServiceStatus.markAsEnabled instance Audience.Sys
            |> function
                | Ok enable -> enable |> MarkAsEnabled.execute
                | Error e -> failwithf "Error: %A" e

        let markAsDisabled () =
            ServiceStatus.markAsDisabled instance Audience.Sys
            |> function
                | Ok disable -> disable |> MarkAsDisabled.execute
                | Error e -> failwithf "Error: %A" e

        let kafkaConfiguration = {
            Connection = {
                BrokerList = brokerList
                Topic = inputStream
            }
            GroupId = GroupId.Random
            Logger = Some {
                Log = printfn "%s"
            }
            Checker = Some (DummyCheck.checker |> ResourceChecker.updateResourceStatusOnCheck instance brokerList)
            ServiceStatus = Some {
                MarkAsEnabled = MarkAsEnabled markAsEnabled
                MarkAsDisabled = MarkAsDisabled markAsDisabled
            }
        }

        // app
        let produce event =
            event
            |> tee (incrementOutputCount)
            |> printfn " -> response: %A"

        showStateOnWebServerAsync instance (MetricsRoute.createOrFail "/metrics")
        |> Async.Start

        enableContext instance

        let mutable runConsuming = true
        while runConsuming do
            try
                runConsuming <- false

                DummyKafka.consume kafkaConfiguration
                |> Seq.map (tee incrementInputCount)
                |> Seq.iter (double >> produce)
            with
            | :? KafkaException as e ->
                printfn "Error: %A ..." e
                markAsDisabled()
                System.Threading.Thread.Sleep(10 * 1000)
                runConsuming <- true

            if runConsuming then
                printfn "Reboot ..."

        Successfully
