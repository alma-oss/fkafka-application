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

type ProducerSerializer<'Event> = ProducerSerializer of ('Event -> string)

type NotConnectedProducer = Kafka.Producer.NotConnectedProducer
type ConnectedProducer = Kafka.Producer.TopicProducer

type PreparedProduceEvent<'Event> = ConnectedProducer -> 'Event -> unit
type ProduceEvent<'Event> = 'Event -> unit

type private PreparedProducer<'Event> = {
    Connection: ConnectionName
    Producer: NotConnectedProducer
    Produce: PreparedProduceEvent<'Event>
}

//
// Consume handlers
//

type PreparedConsumeRuntimeParts<'Event> = {
    Logger: ApplicationLogger
    Environment: Map<string, string>
    Connections: Connections
    ConsumerConfigurations: Map<RuntimeConnectionName, ConsumerConfiguration>
    IncrementOutputEventCount: (OutputStreamName -> 'Event -> unit)
    ProduceTo: Map<RuntimeConnectionName, PreparedProduceEvent<'Event>>
}

type ConsumeRuntimeParts<'Event> = {
    Logger: ApplicationLogger
    Environment: Map<string, string>
    Connections: Connections
    ConsumerConfigurations: Map<RuntimeConnectionName, ConsumerConfiguration>
    IncrementOutputEventCount: (OutputStreamName -> 'Event -> unit)
    ProduceTo: Map<RuntimeConnectionName, ProduceEvent<'Event>>
}

module internal PreparedConsumeRuntimeParts =
    let toRuntimeParts (producers: Map<RuntimeConnectionName, ConnectedProducer>) (preparedRuntimeParts: PreparedConsumeRuntimeParts<'Event>): ConsumeRuntimeParts<'Event> =
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

type ConsumeHandler<'Event> =
    | Events of (ConsumeRuntimeParts<'Event> -> 'Event seq -> unit)
    | LastEvent of (ConsumeRuntimeParts<'Event> -> 'Event -> unit)

type RuntimeConsumeHandler<'Event> =
    | Events of ('Event seq -> unit)
    | LastEvent of ('Event -> unit)

module ConsumeHandler =
    let toRuntime runtimeParts = function
        | ConsumeHandler.Events eventsHandler -> eventsHandler runtimeParts |> RuntimeConsumeHandler.Events
        | ConsumeHandler.LastEvent lastEventHandler -> lastEventHandler runtimeParts |> RuntimeConsumeHandler.LastEvent

type ConsumeHandlerForConnection<'Event> = {
    Connection: ConnectionName
    Handler: ConsumeHandler<'Event>
}

type RuntimeConsumeHandlerForConnection<'Event> = {
    Connection: RuntimeConnectionName
    Configuration: ConsumerConfiguration
    OnError: ErrorHandler
    Handler: ConsumeHandler<'Event>
    IncrementInputCount: 'Event -> unit
}

//
// Configuration / Application
//

type ConfigurationParts<'Event> = {
    Logger: ApplicationLogger
    Environment: Map<string, string>
    Instance: Instance option
    Spot: Spot option
    GroupId: GroupId option
    GroupIds: Map<ConnectionName, GroupId>
    Connections: Connections
    ConsumeHandlers: ConsumeHandlerForConnection<'Event> list
    OnConsumeErrorHandlers: Map<ConnectionName, ErrorHandler>
    ProduceTo: ConnectionName list
    MetricsRoute: MetricsRoute option
    CreateInputEventKeys: CreateInputEventKeys<'Event> option
    CreateOutputEventKeys: CreateOutputEventKeys<'Event> option
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
            MetricsRoute = None
            CreateInputEventKeys = None
            CreateOutputEventKeys = None
            KafkaChecker = None
        }

    let getEnvironmentValue (parts: ConfigurationParts<_>) success error name =
        name
        |> Environment.tryGetEnv parts.Environment
        |> Option.map success
        |> Result.ofOption (sprintf "Environment variable for \"%s\" is not set." name)
        |> Result.mapError error

type Configuration<'Event> = private Configuration of Result<ConfigurationParts<'Event>, KafkaApplicationError>

module private Configuration =
    let result (Configuration result) = result

type KafkaApplicationParts<'Event> = {
    Logger: ApplicationLogger
    Environment: Map<string, string>
    Box: Box
    ConsumerConfigurations: Map<RuntimeConnectionName, ConsumerConfiguration>
    ConsumeHandlers: RuntimeConsumeHandlerForConnection<'Event> list
    Producers: Map<RuntimeConnectionName, NotConnectedProducer>
    MetricsRoute: MetricsRoute option
    PreparedRuntimeParts: PreparedConsumeRuntimeParts<'Event>
}

type KafkaApplication<'Event> = private KafkaApplication of Result<KafkaApplicationParts<'Event>, KafkaApplicationError>
