namespace KafkaApplication.Router

module internal Metrics =
    open Kafka
    open KafkaApplication

    let createKeysForInputEvent (InputStreamName (StreamName inputStream)) { Raw = event } =
        SimpleDataSetKeys [
            ("event", event.Event |> EventName.value)
            ("input_stream", inputStream)
        ]

    let createKeysForOutputEvent (OutputStreamName (StreamName outputStream)) { Raw = event } =
        SimpleDataSetKeys [
            ("event", event.Event |> EventName.value)
            ("output_stream", outputStream)
        ]
