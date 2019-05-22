namespace KafkaApplication.Deriver

module internal Metrics =
    open Kafka
    open KafkaApplication

    let createKeysForInputEvent<'InputEvent, 'OutputEvent> (getCommonEventData: GetCommonEventData<'InputEvent, 'OutputEvent>) (InputStreamName (StreamName inputStream)) event =
        let commonData = event |> Input |> getCommonEventData

        SimpleDataSetKeys [
            ("event", commonData.Event |> EventName.value)
            ("input_stream", inputStream)
        ]

    let createKeysForOutputEvent<'InputEvent, 'OutputEvent> (getCommonEventData: GetCommonEventData<'InputEvent, 'OutputEvent>) (OutputStreamName (StreamName outputStream)) event =
        let commonData = event |> Output |> getCommonEventData

        SimpleDataSetKeys [
            ("event", commonData.Event |> EventName.value)
            ("output_stream", outputStream)
        ]
