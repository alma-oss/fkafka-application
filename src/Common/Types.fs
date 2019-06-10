namespace KafkaApplication

open Kafka
open Metrics
open ServiceIdentification

[<Measure>] type second
[<Measure>] type attempt

//
// Metrics
//

type CustomMetric = {
    Name: MetricName
    Type: MetricType
    Description: string
}

type ResourceMetricInInterval = {
    Resource: ResourceAvailability
    Interval: int<second>
    Checker: unit -> ResourceStatus
}

type MetricsRoute = private MetricsRoute of string

type InvalidMetricsRouteError = InvalidMetricsRouteError of string

module MetricsRoute =
    let create (route: string) =
        if route.StartsWith "/" then MetricsRoute route |> Ok
        else route |> InvalidMetricsRouteError |> Error

    let createOrFail route =
        route
        |> create
        |> function
            | Ok route -> route
            | Error e -> failwithf "%A" e

    let value (MetricsRoute route) = route

type InputStreamName = InputStreamName of StreamName
type OutputStreamName = OutputStreamName of StreamName

type SimpleDataSetKeys = SimpleDataSetKeys of (string * string) list

type CreateInputEventKeys<'InputEvent> = CreateInputEventKeys of (InputStreamName -> 'InputEvent -> SimpleDataSetKeys)
type CreateOutputEventKeys<'OutputEvent> = CreateOutputEventKeys of (OutputStreamName -> 'OutputEvent -> SimpleDataSetKeys)

//
// Environment
//

type EnvironmentConnectionConfiguration = {
    BrokerList: string
    Topic: string
}

type EnvironmentManyTopicsConnectionConfiguration = {
    BrokerList: string
    Topics: string list
}

//
// Kafka connections
//

type ManyTopicsConnectionConfiguration = {
    BrokerList: BrokerList
    Topics: Instance list
}

type ConnectionName = ConnectionName of string
type RuntimeConnectionName = string

module ConnectionName =
    let value (ConnectionName name) = name
    let runtimeName (ConnectionName name): RuntimeConnectionName = name

type ConnectionConfiguration = {
    BrokerList: BrokerList
    Topic: Instance
}

module internal ConnectionConfiguration =
    let toKafkaConnectionConfiguration (connection: ConnectionConfiguration): Kafka.ConnectionConfiguration =
        {
            BrokerList = connection.BrokerList
            Topic = connection.Topic |> StreamName.Instance
        }

type Connections = Map<ConnectionName, ConnectionConfiguration>

module Connections =
    let Default = ConnectionName "__default"
    let Supervision = ConnectionName "__supervision"

    let empty: Connections =
        Map.empty

//
// Errors
//

// Handlers and policies

type ErrorMessage = string

type ProducerErrorPolicy =
    | Shutdown
    | ShutdownIn of int<second>
    | Retry
    | RetryIn of int<second>

type ProducerErrorHandler = ApplicationLogger -> ErrorMessage -> ProducerErrorPolicy

type ConsumeErrorPolicy =
    | Shutdown
    | ShutdownIn of int<second>
    | Retry
    | RetryIn of int<second>
    | Continue

type ConsumeErrorHandler = ApplicationLogger -> ErrorMessage -> ConsumeErrorPolicy

// Error types

[<RequireQualifiedAccess>]
type EnvironmentError =
    | VariableNotFoundError of string
    | InvalidFormatError of string

[<RequireQualifiedAccess>]
type InstanceError =
    | VariableNotFoundError of string
    | InvalidFormatError of string

[<RequireQualifiedAccess>]
type SpotError =
    | VariableNotFoundError of string
    | InvalidFormatError of string

[<RequireQualifiedAccess>]
type GroupIdError =
    | VariableNotFoundError of string

[<RequireQualifiedAccess>]
type ConnectionConfigurationError =
    | VariableNotFoundError of string
    | TopicIsNotInstanceError of string

[<RequireQualifiedAccess>]
type ConsumeHandlerError =
    | MissingConfiguration of ConnectionName

[<RequireQualifiedAccess>]
type ProduceError =
    | MissingConnectionConfiguration of ConnectionName
    | MissingFromDomainConfiguration of ConnectionName

type MetricsError =
    | InvalidRoute of InvalidMetricsRouteError
    | MetricError of MetricError
    | InvalidMetricName of MetricNameError

[<RequireQualifiedAccess>]
type LoggingError =
    | InvalidGraylogHost of Logging.Graylog.HostError
    | VariableNotFoundError of string

type KafkaApplicationError =
    | KafkaApplicationError of ErrorMessage
    | InstanceError of InstanceError
    | SpotError of SpotError
    | GroupIdError of GroupIdError
    | ConnectionConfigurationError of ConnectionConfigurationError
    | EnvironmentError of EnvironmentError
    | ConsumeHandlerError of ConsumeHandlerError
    | ProduceError of ProduceError
    | MetricsError of MetricsError
    | LoggingError of LoggingError

//
// Produce
//

type ProducerSerializer<'OutputEvent> = ProducerSerializer of ('OutputEvent -> string)

type NotConnectedProducer = Kafka.Producer.NotConnectedProducer
type ConnectedProducer = Kafka.Producer.TopicProducer

type PreparedProduceEvent<'OutputEvent> = ConnectedProducer -> 'OutputEvent -> unit
type ProduceEvent<'OutputEvent> = 'OutputEvent -> unit

type private PreparedProducer<'OutputEvent> = {
    Connection: ConnectionName
    Producer: NotConnectedProducer
    Produce: PreparedProduceEvent<'OutputEvent>
}

// Output events
type SerializedEvent = string

type Serialize = Serialize of (obj -> SerializedEvent)
type FromDomain<'OutputEvent> = Serialize -> 'OutputEvent -> SerializedEvent

//
// Consume handlers
//

type PreparedConsumeRuntimeParts<'OutputEvent> = {
    Logger: ApplicationLogger
    Box: Box
    Environment: Map<string, string>
    Connections: Connections
    ConsumerConfigurations: Map<RuntimeConnectionName, ConsumerConfiguration>
    ProduceTo: Map<RuntimeConnectionName, PreparedProduceEvent<'OutputEvent>>
    IncrementMetric: MetricName -> SimpleDataSetKeys -> unit
}

type ConsumeRuntimeParts<'OutputEvent> = {
    Logger: ApplicationLogger
    Box: Box
    Environment: Map<string, string>
    Connections: Connections
    ConsumerConfigurations: Map<RuntimeConnectionName, ConsumerConfiguration>
    ProduceTo: Map<RuntimeConnectionName, ProduceEvent<'OutputEvent>>
    IncrementMetric: MetricName -> SimpleDataSetKeys -> unit
    EnableResource: ResourceAvailability -> unit
    DisableResource: ResourceAvailability -> unit
}

module internal PreparedConsumeRuntimeParts =
    let toRuntimeParts (producers: Map<RuntimeConnectionName, ConnectedProducer>) (preparedRuntimeParts: PreparedConsumeRuntimeParts<'OutputEvent>): ConsumeRuntimeParts<'OutputEvent> =
        let instance = preparedRuntimeParts.Box |> Box.instance

        {
            Logger = preparedRuntimeParts.Logger
            Box = preparedRuntimeParts.Box
            Environment = preparedRuntimeParts.Environment
            Connections = preparedRuntimeParts.Connections
            ConsumerConfigurations = preparedRuntimeParts.ConsumerConfigurations
            ProduceTo =
                preparedRuntimeParts.ProduceTo
                |> Map.map (fun connection produce -> produce producers.[connection])
            IncrementMetric = preparedRuntimeParts.IncrementMetric
            EnableResource = ResourceAvailability.enable instance >> ignore
            DisableResource = ResourceAvailability.disable instance >> ignore
        }

type ConsumeHandler<'InputEvent, 'OutputEvent> =
    | Events of (ConsumeRuntimeParts<'OutputEvent> -> 'InputEvent seq -> unit)
    | LastEvent of (ConsumeRuntimeParts<'OutputEvent> -> 'InputEvent -> unit)

type RuntimeConsumeHandler<'InputEvent> =
    | Events of ('InputEvent seq -> unit)
    | LastEvent of ('InputEvent -> unit)

module ConsumeHandler =
    let toRuntime runtimeParts = function
        | ConsumeHandler.Events eventsHandler -> eventsHandler runtimeParts |> RuntimeConsumeHandler.Events
        | ConsumeHandler.LastEvent lastEventHandler -> lastEventHandler runtimeParts |> RuntimeConsumeHandler.LastEvent

type ConsumeHandlerForConnection<'InputEvent, 'OutputEvent> = {
    Connection: ConnectionName
    Handler: ConsumeHandler<'InputEvent, 'OutputEvent>
}

type RuntimeConsumeHandlerForConnection<'InputEvent, 'OutputEvent> = {
    Connection: RuntimeConnectionName
    Configuration: ConsumerConfiguration
    OnError: ConsumeErrorHandler
    Handler: ConsumeHandler<'InputEvent, 'OutputEvent>
    IncrementInputCount: 'InputEvent -> unit
}

//
// Configuration / Application
//

type ConfigurationParts<'InputEvent, 'OutputEvent> = {
    Logger: ApplicationLogger
    Environment: Map<string, string>
    Instance: Instance option
    Spot: Spot option
    GroupId: GroupId option
    GroupIds: Map<ConnectionName, GroupId>
    ParseEvent: ParseEvent<'InputEvent> option
    Connections: Connections
    ConsumeHandlers: ConsumeHandlerForConnection<'InputEvent, 'OutputEvent> list
    OnConsumeErrorHandlers: Map<ConnectionName, ConsumeErrorHandler>
    ProduceTo: ConnectionName list
    ProducerErrorHandler: ProducerErrorHandler option
    FromDomain: Map<ConnectionName, FromDomain<'OutputEvent>>
    MetricsRoute: MetricsRoute option
    CustomMetrics: CustomMetric list
    IntervalResourceCheckers: ResourceMetricInInterval list
    CreateInputEventKeys: CreateInputEventKeys<'InputEvent> option
    CreateOutputEventKeys: CreateOutputEventKeys<'OutputEvent> option
    KafkaChecker: Checker option
    GraylogHost: Logging.Graylog.Host option
}

[<AutoOpen>]
module internal ConfigurationParts =
    let defaultParts =
        {
            Logger = ApplicationLogger.defaultLogger
            Environment = Map.empty
            Instance = None
            Spot = None
            GroupId = None
            GroupIds = Map.empty
            ParseEvent = None
            Connections = Connections.empty
            ConsumeHandlers = []
            OnConsumeErrorHandlers = Map.empty
            ProduceTo = []
            ProducerErrorHandler = None
            FromDomain = Map.empty
            MetricsRoute = None
            CustomMetrics = []
            IntervalResourceCheckers = []
            CreateInputEventKeys = None
            CreateOutputEventKeys = None
            KafkaChecker = None
            GraylogHost = None
        }

    let getEnvironmentValue (parts: ConfigurationParts<_, _>) success error name =
        name
        |> Environment.tryGetEnv parts.Environment
        |> Option.map success
        |> Result.ofOption (sprintf "Environment variable for \"%s\" is not set." name)
        |> Result.mapError error

type Configuration<'InputEvent, 'OutputEvent> = private Configuration of Result<ConfigurationParts<'InputEvent, 'OutputEvent>, KafkaApplicationError>

module private Configuration =
    let result (Configuration result) = result

type KafkaApplicationParts<'InputEvent, 'OutputEvent> = {
    Logger: ApplicationLogger
    Environment: Map<string, string>
    Box: Box
    ParseEvent: ParseEvent<'InputEvent>
    ConsumerConfigurations: Map<RuntimeConnectionName, ConsumerConfiguration>
    ConsumeHandlers: RuntimeConsumeHandlerForConnection<'InputEvent, 'OutputEvent> list
    Producers: Map<RuntimeConnectionName, NotConnectedProducer>
    ProducerErrorHandler: ProducerErrorHandler
    ServiceStatus: ServiceStatus.ServiceStatus
    MetricsRoute: MetricsRoute option
    CustomMetrics: CustomMetric list
    IntervalResourceCheckers: ResourceMetricInInterval list
    PreparedRuntimeParts: PreparedConsumeRuntimeParts<'OutputEvent>
}

type KafkaApplication<'InputEvent, 'OutputEvent> = internal KafkaApplication of Result<KafkaApplicationParts<'InputEvent, 'OutputEvent>, KafkaApplicationError>

//
// Application types
//

type ApplicationShutdown =
    | Successfully
    | WithError of ErrorMessage
    | WithRuntimeError of ErrorMessage

module ApplicationShutdown =
    let withStatusCode = function
        | Successfully -> 0
        | _ -> 1

type BeforeRun<'InputEvent, 'OutputEvent> = KafkaApplicationParts<'InputEvent, 'OutputEvent> -> unit
type Run<'InputEvent, 'OutputEvent> = KafkaApplication<'InputEvent, 'OutputEvent> -> ApplicationShutdown

type RunKafkaApplication<'InputEvent, 'OutputEvent> = BeforeRun<'InputEvent, 'OutputEvent> -> Run<'InputEvent, 'OutputEvent>
