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

module EventToRoute =
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

type Router = private Router of Map<EventName, StreamName>

module Router =
    open FSharp.Data
    open System.IO

    type private RoutingSchema = JsonProvider<"src/Router/schema/routingSchema.json">

    let private empty =
        Router Map.empty<EventName, StreamName>

    let parse path =
        let rawRouting =
            path
            |> File.ReadAllText
            |> RoutingSchema.Parse

        rawRouting.Route
        |> Seq.fold (fun (Router router) route ->
            (EventName route.Event, StreamName route.TargetStream)
            |> router.Add
            |> Router
        ) empty

    let getStreamFor event (Router router) =
        router
        |> Map.tryFind event

    let getOutputStreams (Router router) =
        router
        |> Map.toList
        |> List.map snd

// Errors

type ApplicationConfigurationError =    // todo - common
    | ConfigurationNotSet
    | AlreadySetConfiguration
    | InvalidConfiguration of KafkaApplicationError

type RouterConfigurationError =
    | NotFound of string
    | NotSet
    | OutputBrokerListNotSet

type ContentBasedRouterApplicationError =
    | ApplicationConfigurationError of ApplicationConfigurationError
    | RouterConfigurationError of RouterConfigurationError
    | ConnectionConfigurationError of ConnectionConfigurationError

// Content-Based Router Application Configuration

type RouterParts<'InputEvent, 'OutputEvent> = {
    Configuration: Configuration<'InputEvent, 'OutputEvent> option
    RouterConfiguration: Router option
    RouteToBrokerList: BrokerList option
}

module RouterParts =
    let defaultRouter = {
        Configuration = None
        RouterConfiguration = None
        RouteToBrokerList = None
    }

type ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent> = private ContentBasedRouterApplicationConfiguration of Result<RouterParts<'InputEvent, 'OutputEvent>, ContentBasedRouterApplicationError>

type ContentBasedRouterApplicationParts<'InputEvent, 'OutputEvent> = {
    Application: KafkaApplication<'InputEvent, 'OutputEvent>
    RouterConfiguration: Router
}

type ContentBasedRouterApplication<'InputEvent, 'OutputEvent> = internal ContentBasedRouterApplication of Result<ContentBasedRouterApplicationParts<'InputEvent, 'OutputEvent>, ContentBasedRouterApplicationError>
