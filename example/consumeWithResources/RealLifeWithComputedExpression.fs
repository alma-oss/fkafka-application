namespace RealLifeWithComputedExpressionExample
open Kafka

module Program =
    open KafkaApplication

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

        //
        // pattern
        //

        let isActivatedContracts (event: RawEvent) =
            event.Event = EventName "contract_activated"

        let filterDomainData (event: RawEvent) =
            { event with DomainData = None }

        let toDto (event: RawEvent) =
            // todo
            event

        //
        // run simple app
        //

        kafkaApplication {
            merge (environment {
                file [".reallife.env"]

                instance "INSTANCE"
                ifSetDo "VERBOSITY" Logging.Log.setVerbosityLevel

                connect {
                    BrokerList = "BROKER_LIST"
                    Topic = "INPUT_STREAM"
                }

                connectTo "contracts" {
                    BrokerList = "BROKER_LIST"
                    Topic = "CONTRACTS_STREAM"
                }

                connectTo "outputStream" {
                    BrokerList = "BROKER_LIST"
                    Topic = "OUTPUT_STREAM"
                }
            })

            produceTo "outputStream" // toDto

            consume (fun _ events ->
                events
                |> Seq.take 1000
                |> Seq.iter ignore
            )

            consumeFrom "contracts" (fun app contractEvents ->
                use producer = app.Producers.["outputStream"]
                let produce = app.Produces.["outputStream"] producer

                try
                    contractEvents
                    |> Seq.filter isActivatedContracts
                    |> Seq.take 1
                    |> Seq.iter (filterDomainData >> toDto >> produce)
                finally
                    producer.Flush()
            )

            showMetricsOn "/metrics"
            showInputEventsWith createInputKeys
            showOutputEventsWith createOutputKeys
        }
        |> run
