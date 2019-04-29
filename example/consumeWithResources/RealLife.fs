namespace RealLifeExample
open Confluent.Kafka
open KafkaApplication

module Program =
    open Kafka
    open Metrics
    open KafkaApplication
    open ServiceIdentification
    open ApplicationMetrics

    let tee f a =
        f a
        a

    let createInputKeys (InputStreamName (StreamName inputStream)) (event: RawEvent) =
        SimpleDataSetKeys [
            ("event", event.Event |> EventName.value)
            ("input_stream", inputStream)
        ]

    let createOutputKeys (OutputStreamName (StreamName outputStream)) (event: RawEvent) =
        SimpleDataSetKeys [
            ("event", event.Event |> EventName.value)
            ("output_stream", outputStream)
        ]

    let run () =

        let instance = {
            Domain = Domain "arch"
            Context = Context "example"
            Purpose = Purpose "reallife"
            Version = Version "dev"
        }

        let inputStream = StreamName "consents-consentorStream-development-v1"
        let brokerList = BrokerList "kfall-1.dev1.services.lmc:9092"

        let outputStream = "example-outputStream-common-all"

        //
        // pattern
        //

        let route event =
            event

        //
        // run simple app
        //

        // metrics
        let incrementInputCount = incrementTotalInputEventCount (CreateInputEventKeys createInputKeys) instance (InputStreamName inputStream)
        let incrementOutputCount = incrementTotalOutputEventCount (CreateOutputEventKeys createOutputKeys) instance (OutputStreamName (Kafka.StreamName outputStream))

        // service status
        let markAsEnabled () =
            ServiceStatus.enable instance Audience.Sys |> ignore

        let markAsDisabled () =
            ServiceStatus.disable instance Audience.Sys |> ignore

        let kafkaConfiguration = {
            Connection = {
                BrokerList = brokerList
                Topic = inputStream
            }
            GroupId = GroupId.Random
            Logger = Some {
                Log = printfn "%s"
            }
            Checker = Some (Kafka.Checker.defaultChecker |> ResourceChecker.updateResourceStatusOnCheck instance brokerList)
            ServiceStatus = Some {
                MarkAsEnabled = markAsEnabled
                MarkAsDisabled = markAsDisabled
            }
        }

        // app
        let produce event =
            event
            |> tee (incrementOutputCount)
            |> (fun e -> printfn " -> response: %A" e.Event)

        showStateOnWebServerAsync instance (MetricsRoute.createOrFail "/metrics")
        |> Async.Start

        enableInstance instance

        let mutable runConsuming = true
        while runConsuming do
            try
                runConsuming <- false

                Kafka.Consumer.consume kafkaConfiguration RawEvent.parse
                |> Seq.map (tee incrementInputCount)
                |> Seq.iter (route >> produce)
            with
            | :? KafkaException as e ->
                printfn "Error: %A ..." e
                markAsDisabled()
                printfn "Waiting for reboot ..."
                System.Threading.Thread.Sleep(10 * 1000)
                runConsuming <- true

            if runConsuming then
                printfn "Reboot ..."

        //Kafka.Consumer.consumeResult kafkaConfiguration RawEvent.parse (fun e ->
        //    printfn "Error: %A ..." e
        //    markAsDisabled()
        //    printfn "Waiting for reboot ..."
        //    System.Threading.Thread.Sleep(10 * 1000)
        //    printfn "Reboot ..."
        //)
        //|> Seq.take 20
        //|> Seq.map (tee incrementInputCount)
        //|> Seq.iter (route >> produce)
