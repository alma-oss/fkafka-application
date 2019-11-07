namespace KafkaApplication.Router

module internal Metrics =
    open Kafka
    open KafkaApplication

    let createKeysForInputEvent (InputStreamName inputStream) { Raw = event } =
        SimpleDataSetKeys [
            ("event", event.Event |> EventName.value)
            ("input_stream", inputStream |> StreamName.value)
        ]

    let createKeysForOutputEvent (OutputStreamName outputStream) (ProcessedEventToRoute ({ Raw = event }, _)) =
        SimpleDataSetKeys [
            ("event", event.Event |> EventName.value)
            ("output_stream", outputStream |> StreamName.value)
        ]
