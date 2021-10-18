namespace RealLifeExample

module Program =
    open Lmc.Kafka
    open Lmc.KafkaApplication

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

    let run loggerFactory =
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
            useLoggerFactory loggerFactory

            merge (environment {
                file [".env"]

                instance "INSTANCE"

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

                consume (fun app ->
                    let produce = app.ProduceTo.["outputStream"]

                    fun { Event = event } ->
                        event
                        |> filterDomainData
                        |> produce
                )
            })

            consumeFrom "contracts" (fun app ->
                let produce = app.ProduceTo.["outputStream"]

                fun { Event = contractEvent } ->
                    if contractEvent |> isActivatedContracts then
                        contractEvent
                        |> filterDomainData
                        |> produce
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
