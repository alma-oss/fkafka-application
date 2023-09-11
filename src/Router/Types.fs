namespace Alma.KafkaApplication.Router

open Alma.Kafka
open Alma.Kafka.MetaData
open Alma.KafkaApplication

// Errors

type RouterError =
    | StreamNameIsNotInstance of Alma.ServiceIdentification.InstanceError

type RouterConfigurationError =
    | InvalidConfiguration of file: string * exn
    | NotFound of string
    | NotSet
    | MissingRouteEvent
    | MissingFromDomain
    | MissingGetCommonEvent
    | OutputBrokerListNotSet
    | RouterErrors of RouterError list

type ContentBasedRouterApplicationError =
    | ApplicationConfigurationError of ApplicationConfigurationError
    | RouterConfigurationError of RouterConfigurationError
    | ConnectionConfigurationError of ConnectionConfigurationError

// Content-Based Router Application Configuration

type RouterConfiguration = private RouterConfiguration of Map<EventName, StreamName>

type RouteEvent<'InputEvent, 'OutputEvent> = ProcessedBy -> TracedEvent<'InputEvent> -> TracedEvent<'OutputEvent> option

type internal RouteEventHandler<'InputEvent, 'OutputEvent, 'Dependencies> =
    | Simple of RouteEvent<'InputEvent, 'OutputEvent>
    | WithApplication of (PatternRuntimeParts<'Dependencies> -> RouteEvent<'InputEvent, 'OutputEvent>)

type internal RouterParts<'InputEvent, 'OutputEvent, 'Dependencies> = {
    Configuration: Configuration<'InputEvent, 'OutputEvent, 'Dependencies> option
    RouterConfiguration: RouterConfiguration option
    RouteToBrokerList: BrokerList option
    RouteEvent: RouteEventHandler<'InputEvent, 'OutputEvent, 'Dependencies> option
    FromDomain: OutputFromDomain<'OutputEvent> option
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

type ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> = private ContentBasedRouterApplicationConfiguration of Result<RouterParts<'InputEvent, 'OutputEvent, 'Dependencies>, ContentBasedRouterApplicationError>

type internal ContentBasedRouterApplicationParts<'InputEvent, 'OutputEvent, 'Dependencies> = {
    Application: KafkaApplication<'InputEvent, 'OutputEvent, 'Dependencies>
    RouterConfiguration: RouterConfiguration
}

type ContentBasedRouterApplication<'InputEvent, 'OutputEvent, 'Dependencies> = 
    internal ContentBasedRouterApplication of Result<ContentBasedRouterApplicationParts<'InputEvent, 'OutputEvent, 'Dependencies>, ContentBasedRouterApplicationError>

[<RequireQualifiedAccess>]
module internal ContentBasedRouterApplication =
    let application { Application = application } = application
