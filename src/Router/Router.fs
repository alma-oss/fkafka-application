namespace Lmc.KafkaApplication.Router

[<RequireQualifiedAccess>]
module internal Router =
    open Lmc.Kafka
    open Lmc.ServiceIdentification
    open Lmc.ErrorHandling

    [<RequireQualifiedAccess>]
    module Configuration =
        open FSharp.Data
        open System.IO

        type private RoutingSchema = JsonProvider<"src/Router/schema/routingSchema.json">

        let parse path =
            result {
                let rawRouting =
                    path
                    |> File.ReadAllText
                    |> RoutingSchema.Parse

                let! routing =
                    rawRouting.Route
                    |> Seq.map (fun route ->
                        result {
                            let! topicInstance =
                                route.TargetStream
                                |> Instance.parse "-"
                                |> Result.ofOption (StreamNameIsNotInstance route.TargetStream)

                            return (EventName route.Event, StreamName.Instance topicInstance)
                        }
                    )
                    |> Seq.toList
                    |> Result.sequence

                return
                    routing
                    |> Map.ofList
                    |> RouterConfiguration
            }

        let getStreamFor event (RouterConfiguration router) =
            router
            |> Map.tryFind event

        let getOutputStreams (RouterConfiguration router) =
            router
            |> Map.toList
            |> List.map snd

    [<RequireQualifiedAccess>]
    module Routing =
        open Lmc.KafkaApplication
        open Lmc.ErrorHandling.Option.Operators

        let private formatLogMessage (EventName eventName) (streamName: StreamName) =
            sprintf "Route event %s to %A ..." eventName streamName

        let private sendToStream log produceTo eventToRoute (stream: StreamName) =
            log <| sprintf "-> Sending to stream %A ..." stream

            eventToRoute
            |> produceTo stream

        let routeEvent log getEventType produce router eventToRoute =
            let eventType = eventToRoute |> getEventType

            router
            |> Configuration.getStreamFor eventType
            |>! (
                tee (formatLogMessage eventType >> log)
                >> sendToStream log produce eventToRoute
            )
