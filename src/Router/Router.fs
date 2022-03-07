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

        let parse path = result {
            let! rawRouting =
                try
                    path
                    |> File.ReadAllText
                    |> RoutingSchema.Parse
                    |> Ok
                with e ->
                    Error (InvalidConfiguration (path, e))

            let! routing =
                rawRouting.Route
                |> Seq.map (fun route ->
                    result {
                        let! topicInstance =
                            Create.Instance(route.TargetStream)
                            |> Result.mapError StreamNameIsNotInstance

                        return (EventName route.Event, StreamName.Instance topicInstance)
                    }
                )
                |> Seq.toList
                |> Validation.ofResults
                |> Result.mapError RouterErrors

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
        open Microsoft.Extensions.Logging
        open Lmc.KafkaApplication

        let routeEvent (logger: ILogger) getEventType (produce: StreamName -> TracedEvent<'OutputEvent> -> IO<unit>) router (eventToRoute: TracedEvent<'OutputEvent>): IO<unit> = asyncResult {
            let eventType = eventToRoute |> getEventType

            match router |> Configuration.getStreamFor eventType with
            | Some stream ->
                logger.LogTrace("Route event {event} to stream {stream}", (eventType |> EventName.value), (stream |> StreamName.value))
                do! eventToRoute |> produce stream

            | _ ->
                logger.LogTrace("Route event {event} -> ignore, there is no defined stream for this event type.", eventType)
        }
