namespace KafkaApplication

open Kafka
open ServiceIdentification

//
// Logging
//

type Log = string -> string -> unit

type ApplicationLogger = {
    Debug: Log
    Log: Log
    Verbose: Log
    VeryVerbose: Log
    Warning: Log
    Error: Log
}

module ApplicationLogger =
    open Logging

    let quietLogger =
        let ignore _context _message = ()
        {
            Debug = ignore
            Log = ignore
            Verbose = ignore
            VeryVerbose = ignore
            Warning = ignore
            Error = ignore
        }

    let defaultLogger =
        {
            Debug = Log.debug
            Log = Log.normal
            Verbose = Log.verbose
            VeryVerbose = Log.veryVerbose
            Warning = Log.warning
            Error = Log.error
        }

//
// Metrics
//

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

//
// Kafka connections
//

type ConnectionName = ConnectionName of string
type RuntimeConnectionName = string

module ConnectionName =
    let value (ConnectionName name) = name
    let runtimeName (ConnectionName name): RuntimeConnectionName = name

type Connections = Map<ConnectionName, ConnectionConfiguration>

module Connections =
    let Default = ConnectionName "__default"
    let Supervision = ConnectionName "__supervision"

    let empty: Connections =
        Map.empty

//
// Errors
//

type OnErrorPolicy =
    | Reboot
    | Continue
    | Shutdown

type ErrorHandler = ApplicationLogger -> string -> OnErrorPolicy

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

[<RequireQualifiedAccess>]
type ConsumeHandlerError =
    | MissingConfiguration of ConnectionName

[<RequireQualifiedAccess>]
type ProduceError =
    | MissingConfiguration of ConnectionName

type MetricsError =
    | InvalidRoute of InvalidMetricsRouteError
    | MetricError of Metrics.MetricError

type KafkaApplicationError =
    | KafkaApplicationError of string
    | InstanceError of InstanceError
    | SpotError of SpotError
    | GroupIdError of GroupIdError
    | ConnectionConfigurationError of ConnectionConfigurationError
    | EnvironmentError of EnvironmentError
    | ConsumeHandlerError of ConsumeHandlerError
    | ProduceError of ProduceError
    | MetricsError of MetricsError

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
type Serialize = Serialize of (obj -> string)
type FromDomain<'OutputEvent> = Serialize -> 'OutputEvent -> string

//
// Consume handlers
//

type PreparedConsumeRuntimeParts<'OutputEvent> = {
    Logger: ApplicationLogger
    Environment: Map<string, string>
    Connections: Connections
    ConsumerConfigurations: Map<RuntimeConnectionName, ConsumerConfiguration>
    IncrementOutputEventCount: (OutputStreamName -> 'OutputEvent -> unit)
    ProduceTo: Map<RuntimeConnectionName, PreparedProduceEvent<'OutputEvent>>
}

type ConsumeRuntimeParts<'OutputEvent> = {
    Logger: ApplicationLogger
    Environment: Map<string, string>
    Connections: Connections
    ConsumerConfigurations: Map<RuntimeConnectionName, ConsumerConfiguration>
    IncrementOutputEventCount: (OutputStreamName -> 'OutputEvent -> unit)
    ProduceTo: Map<RuntimeConnectionName, ProduceEvent<'OutputEvent>>
}

module internal PreparedConsumeRuntimeParts =
    let toRuntimeParts (producers: Map<RuntimeConnectionName, ConnectedProducer>) (preparedRuntimeParts: PreparedConsumeRuntimeParts<'OutputEvent>): ConsumeRuntimeParts<'OutputEvent> =
        {
            Logger = preparedRuntimeParts.Logger
            Environment = preparedRuntimeParts.Environment
            Connections = preparedRuntimeParts.Connections
            ConsumerConfigurations = preparedRuntimeParts.ConsumerConfigurations
            IncrementOutputEventCount = preparedRuntimeParts.IncrementOutputEventCount
            ProduceTo =
                preparedRuntimeParts.ProduceTo
                |> Map.map (fun connection produce -> produce producers.[connection])
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
    OnError: ErrorHandler
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
    Connections: Connections
    ConsumeHandlers: ConsumeHandlerForConnection<'InputEvent, 'OutputEvent> list
    OnConsumeErrorHandlers: Map<ConnectionName, ErrorHandler>
    ProduceTo: ConnectionName list
    FromDomain: Map<ConnectionName, FromDomain<'OutputEvent>>
    MetricsRoute: MetricsRoute option
    CreateInputEventKeys: CreateInputEventKeys<'InputEvent> option
    CreateOutputEventKeys: CreateOutputEventKeys<'OutputEvent> option
    KafkaChecker: Checker option
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
            Connections = Connections.empty
            ConsumeHandlers = []
            OnConsumeErrorHandlers = Map.empty
            ProduceTo = []
            FromDomain = Map.empty
            MetricsRoute = None
            CreateInputEventKeys = None
            CreateOutputEventKeys = None
            KafkaChecker = None
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
    ConsumerConfigurations: Map<RuntimeConnectionName, ConsumerConfiguration>
    ConsumeHandlers: RuntimeConsumeHandlerForConnection<'InputEvent, 'OutputEvent> list
    Producers: Map<RuntimeConnectionName, NotConnectedProducer>
    MetricsRoute: MetricsRoute option
    PreparedRuntimeParts: PreparedConsumeRuntimeParts<'OutputEvent>
}

type KafkaApplication<'InputEvent, 'OutputEvent> = private KafkaApplication of Result<KafkaApplicationParts<'InputEvent, 'OutputEvent>, KafkaApplicationError>
