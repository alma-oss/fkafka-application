namespace RealLifeExample

module Program =
    open Kafka
    open KafkaApplication
    open Logging

    open Suave
    open Suave.Filters
    open Suave.Operators
    open Suave.Successful

    let createInputKeys (InputStreamName inputStream) (event: RawEvent) =
        SimpleDataSetKeys [
            ("event", event.Event |> EventName.value)
            ("input_stream", inputStream |> StreamName.value)
        ]

    let createOutputKeys (OutputStreamName outputStream) (event: RawEvent) =
        SimpleDataSetKeys [
            ("event", event.Event |> EventName.value)
            ("output_stream", outputStream |> StreamName.value)
        ]

    let run () =
        //
        // pattern
        //

        let isActivatedContracts (event: RawEvent) =
            event.Event = EventName "contract_activated"

        let filterDomainData (event: RawEvent) =
            { event with DomainData = None }

        let fromDomain: FromDomain<RawEvent> =
            fun (Serialize serialize) event ->
                serialize event

        //
        // run simple app
        //

        kafkaApplication {
            merge (environment {
                file [".env"]

                instance "INSTANCE"
                ifSetDo "VERBOSITY" Log.setVerbosityLevel
                logToGraylog "GRAYLOG" "GRAYLOG_SERVICE"

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

                supervision {
                    BrokerList = "BROKER_LIST"
                    Topic = "SUPERVISION_STREAM"
                }
            })

            parseEventWith RawEvent.parse

            merge (partialKafkaApplication {
                produceTo "outputStream" fromDomain

                consume (fun app events ->
                    let produce = app.ProduceTo.["outputStream"]

                    events
                    |> Seq.take 1000
                    |> Seq.iter (filterDomainData >> produce)
                )
            })

            consumeFrom "contracts" (fun app contractEvents ->
                let produce = app.ProduceTo.["outputStream"]

                contractEvents
                |> Seq.filter isActivatedContracts
                //|> Seq.take 1
                |> Seq.iter (filterDomainData >> produce)
            )

            showMetricsOn "/metrics"

            addRoute (
                GET >=> choose [
                    path "/my-new-route"
                        >=> request ((fun _ -> "Hello world!") >> OK)
                ]
            )

            showInputEventsWith createInputKeys
            showOutputEventsWith createOutputKeys
        }
        |> run
