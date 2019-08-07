namespace KafkaApplication.Router

open Kafka
open KafkaApplication

//
// Event data
//

type SerializedEvent = SerializedEvent of string

type EventToRoute = {
    Serialized: SerializedEvent
    Raw: RawEvent
}

[<RequireQualifiedAccess>]
module internal EventToRoute =
    let raw { Raw = raw } = raw
    let serialized { Serialized = (SerializedEvent serialized) } = serialized

    let parse message =
        {
            Serialized = SerializedEvent message
            Raw = message |> RawEvent.parse
        }

//
// Router
//

type RouterError =
    | StreamNameIsNotInstance of string

type Router = private Router of Map<EventName, StreamName>

module internal Router =
    open FSharp.Data
    open System.IO
    open ServiceIdentification

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
                |> Router
        }

    let getStreamFor event (Router router) =
        router
        |> Map.tryFind event

    let getOutputStreams (Router router) =
        router
        |> Map.toList
        |> List.map snd

// Errors

type RouterConfigurationError =
    | NotFound of string
    | NotSet
    | OutputBrokerListNotSet
    | RouterError of RouterError

type ContentBasedRouterApplicationError =
    | ApplicationConfigurationError of ApplicationConfigurationError
    | RouterConfigurationError of RouterConfigurationError
    | ConnectionConfigurationError of ConnectionConfigurationError

// Content-Based Router Application Configuration

type internal RouterParts<'InputEvent, 'OutputEvent> = {
    Configuration: Configuration<'InputEvent, 'OutputEvent> option
    RouterConfiguration: Router option
    RouteToBrokerList: BrokerList option
}

[<RequireQualifiedAccess>]
module internal RouterParts =
    let defaultRouter = {
        Configuration = None
        RouterConfiguration = None
        RouteToBrokerList = None
    }

type ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent> = private ContentBasedRouterApplicationConfiguration of Result<RouterParts<'InputEvent, 'OutputEvent>, ContentBasedRouterApplicationError>

type internal ContentBasedRouterApplicationParts<'InputEvent, 'OutputEvent> = {
    Application: KafkaApplication<'InputEvent, 'OutputEvent>
    RouterConfiguration: Router
}

type ContentBasedRouterApplication<'InputEvent, 'OutputEvent> = internal ContentBasedRouterApplication of Result<ContentBasedRouterApplicationParts<'InputEvent, 'OutputEvent>, ContentBasedRouterApplicationError>

[<RequireQualifiedAccess>]
module internal ContentBasedRouterApplication =
    let application { Application = application } = application
