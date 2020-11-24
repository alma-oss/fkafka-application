namespace Lmc.KafkaApplication.Router

open Lmc.Kafka
open Lmc.KafkaApplication
open Lmc.Consents.Events.Events

// Errors

type RouterError =
    | StreamNameIsNotInstance of string

type RouterConfigurationError =
    | NotFound of string
    | NotSet
    | MissingRouteEvent
    | MissingFromDomain
    | MissingGetCommonEvent
    | OutputBrokerListNotSet
    | RouterError of RouterError

type ContentBasedRouterApplicationError =
    | ApplicationConfigurationError of ApplicationConfigurationError
    | RouterConfigurationError of RouterConfigurationError
    | ConnectionConfigurationError of ConnectionConfigurationError

// Content-Based Router Application Configuration

type RouterConfiguration = private RouterConfiguration of Map<EventName, StreamName>

type RouteEvent<'InputEvent, 'OutputEvent> = ProcessedBy -> 'InputEvent -> 'OutputEvent option

type internal RouteEventHandler<'InputEvent, 'OutputEvent> =
    | Simple of RouteEvent<'InputEvent, 'OutputEvent>
    | WithApplication of (PatternRuntimeParts -> RouteEvent<'InputEvent, 'OutputEvent>)

type internal RouterParts<'InputEvent, 'OutputEvent> = {
    Configuration: Configuration<'InputEvent, 'OutputEvent> option
    RouterConfiguration: RouterConfiguration option
    RouteToBrokerList: BrokerList option
    RouteEvent: RouteEventHandler<'InputEvent, 'OutputEvent> option
    FromDomain: FromDomain<'OutputEvent> option
    CreateCustomValues: CreateCustomValues<'InputEvent, 'OutputEvent> option
    GetCommonEvent: GetCommonEvent<'InputEvent, 'OutputEvent> option
}

[<RequireQualifiedAccess>]
module internal RouterParts =
    let defaultRouter = {
        Configuration = None
        RouterConfiguration = None
        RouteToBrokerList = None
        RouteEvent = None
        FromDomain = None
        CreateCustomValues = None
        GetCommonEvent = None
    }

type ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent> = private ContentBasedRouterApplicationConfiguration of Result<RouterParts<'InputEvent, 'OutputEvent>, ContentBasedRouterApplicationError>

type internal ContentBasedRouterApplicationParts<'InputEvent, 'OutputEvent> = {
    Application: KafkaApplication<'InputEvent, 'OutputEvent>
    RouterConfiguration: RouterConfiguration
}

type ContentBasedRouterApplication<'InputEvent, 'OutputEvent> = internal ContentBasedRouterApplication of Result<ContentBasedRouterApplicationParts<'InputEvent, 'OutputEvent>, ContentBasedRouterApplicationError>

[<RequireQualifiedAccess>]
module internal ContentBasedRouterApplication =
    let application { Application = application } = application
