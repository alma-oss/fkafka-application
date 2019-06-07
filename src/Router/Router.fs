namespace KafkaApplication.Router

module internal ContentBasedRouter =
    open Kafka
    open KafkaApplication
    open OptionOperators

    let private formatLogMessage (EventName eventName) (streamName: StreamName) =
        (sprintf "Route event %s to %A ..." eventName streamName)

    let private sendToStream log produceTo eventToRoute (stream: StreamName) =
        log <| sprintf "-> Sending to stream %A ..." stream
        eventToRoute |> produceTo stream

    let routeEvent log produce router eventToRoute =
        let eventType = eventToRoute.Raw.Event

        router
        |> Router.getStreamFor eventType
        |>! (
            tee (formatLogMessage eventType >> log)
            >> tee (sendToStream log produce eventToRoute)
            >> ignore
        )
