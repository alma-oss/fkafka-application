namespace Lmc.KafkaApplication

open System
open Microsoft.Extensions.Logging
open Giraffe

open Lmc.Kafka
open Lmc.Kafka.MetaData
open Lmc.Metrics
open Lmc.ServiceIdentification
open Lmc.Logging
open Lmc.Tracing
open Lmc.ErrorHandling

[<Measure>] type Second
[<Measure>] type Attempt

//
// Metrics
//

type CustomMetric = {
    Name: MetricName
    Type: MetricType
    Description: string
}

[<RequireQualifiedAccess>]
module CustomMetric =
    let fromMetric metric metricType description =
        {
            Name = metric
            Type = metricType
            Description = description
        }

type ResourceMetricInInterval = {
    Resource: ResourceAvailability
    Interval: int<Second>
    Checker: unit -> ResourceStatus
}

type MetricsRoute = private MetricsRoute of string

type InvalidMetricsRouteError = InvalidMetricsRouteError of string

[<RequireQualifiedAccess>]
module internal MetricsRoute =
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

[<RequireQualifiedAccess>]
module internal ConnectionName =
    let value (ConnectionName name) = name
    let runtimeName (ConnectionName name): RuntimeConnectionName = name

type ConnectionConfiguration = {
    BrokerList: BrokerList
    Topic: Instance
}

[<RequireQualifiedAccess>]
module internal ConnectionConfiguration =
    let toKafkaConnectionConfiguration (connection: ConnectionConfiguration): Lmc.Kafka.ConnectionConfiguration =
        {
            BrokerList = connection.BrokerList
            Topic = connection.Topic |> StreamName.Instance
        }

type Connections = Map<ConnectionName, ConnectionConfiguration>

[<RequireQualifiedAccess>]
module Connections =
    let Default = ConnectionName "__default"
    let Supervision = ConnectionName "__supervision"

    let internal empty: Connections =
        Map.empty

//
// Errors
//

// Handlers and policies

type ErrorMessage = string

type ProducerErrorPolicy =
    | Shutdown
    | ShutdownIn of int<Second>
    | Retry
    | RetryIn of int<Second>

type ProducerErrorHandler = ILogger -> ErrorMessage -> ProducerErrorPolicy

type ConsumeErrorPolicy =
    | Shutdown
    | ShutdownIn of int<Second>
    | Retry
    | RetryIn of int<Second>
    | Continue

type ConsumeErrorHandler = ILogger -> ErrorMessage -> ConsumeErrorPolicy

// Error types

[<RequireQualifiedAccess>]
type EnvironmentError =
    | VariableNotFoundError of string
    | InvalidFormatError of string
    | NoVariablesFoundError
    | LoadError of string

[<RequireQualifiedAccess>]
type InstanceError =
    | VariableNotFoundError of string
    | InvalidFormatError of Lmc.ServiceIdentification.InstanceError

[<RequireQualifiedAccess>]
type CurrentEnvironmentError =
    | VariableNotFoundError of string
    | InvalidFormatError of Lmc.EnvironmentModel.EnvironmentError

[<RequireQualifiedAccess>]
type SpotError =
    | VariableNotFoundError of string
    | InvalidFormatError of Lmc.ServiceIdentification.SpotError

[<RequireQualifiedAccess>]
type GroupIdError =
    | VariableNotFoundError of string

[<RequireQualifiedAccess>]
type ConnectionConfigurationError =
    | VariableNotFoundError of string
    | TopicIsNotInstanceError of Lmc.ServiceIdentification.InstanceError list

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
    | InvalidPort of string
    | VariableNotFoundError of string

type KafkaApplicationError =
    | KafkaApplicationError of ErrorMessage
    | InstanceError of InstanceError
    | CurrentEnvironmentError of CurrentEnvironmentError
    | SpotError of SpotError
    | GroupIdError of GroupIdError
    | ConnectionConfigurationError of ConnectionConfigurationError
    | RequiredEnvironmentVariablesErrors of EnvironmentError list
    | EnvironmentError of EnvironmentError
    | ConsumeHandlerError of ConsumeHandlerError list
    | ProduceError of ProduceError list
    | MetricsError of MetricsError
    | LoggingError of LoggingError

//
// Events
//

type TracedEvent<'Event> =
    {
        Event: 'Event
        Trace: Trace
    }

    member this.Finish() =
        this.Trace |> Trace.finish

    interface IDisposable with
        member this.Dispose() =
            this.Finish()

//
// Produce
//

type ProducerSerializer<'OutputEvent> = ProducerSerializer of ('OutputEvent -> string)

type NotConnectedProducer = Producer.NotConnected
type ConnectedProducer = Producer

type ProduceEvent<'OutputEvent> = TracedEvent<'OutputEvent> -> unit
type PreparedProduceEvent<'OutputEvent> = ConnectedProducer -> ProduceEvent<'OutputEvent>

type private PreparedProducer<'OutputEvent> = {
    Connection: ConnectionName
    Producer: NotConnectedProducer
    Produce: PreparedProduceEvent<'OutputEvent>
}

// Output events
type FromDomain<'OutputEvent> = Serialize -> 'OutputEvent -> MessageToProduce

//
// Consume handlers
//

type ParseEvent<'InputEvent> = string -> 'InputEvent

type ParsedEvent<'InputEvent> = {
    Commit: ManualCommit
    Event: 'InputEvent
    ConsumeTrace: Trace
}

type ParsedEventResult<'InputEvent> = Consumer.ConsumedResult<ParsedEvent<'InputEvent>>

[<RequireQualifiedAccess>]
module Event =
    let parse (parseEvent: ParseEvent<'Event>): Consumer.ParseEvent<ParsedEvent<'Event>> =
        fun tracedMessage ->
            {
                Event = tracedMessage.Message |> parseEvent
                ConsumeTrace = tracedMessage.Trace
                Commit = tracedMessage.Commit
            }

    let event ({ Event = event }: ParsedEvent<'Event>) = event

type internal PreparedConsumeRuntimeParts<'OutputEvent> = {
    LoggerFactory: ILoggerFactory
    Box: Box
    GitCommit: MetaData.GitCommit
    DockerImageVersion: MetaData.DockerImageVersion
    Environment: Map<string, string>
    Connections: Connections
    ConsumerConfigurations: Map<RuntimeConnectionName, ConsumerConfiguration>
    ProduceTo: Map<RuntimeConnectionName, PreparedProduceEvent<'OutputEvent>>
    IncrementMetric: MetricName -> SimpleDataSetKeys -> unit
    SetMetric: MetricName -> SimpleDataSetKeys -> MetricValue -> unit
    EnableResource: ResourceAvailability -> unit
    DisableResource: ResourceAvailability -> unit
}

/// Application parts exposed in consume handlers
type ConsumeRuntimeParts<'OutputEvent> = {
    LoggerFactory: ILoggerFactory
    Box: Box
    ProcessedBy: ProcessedBy
    Environment: Map<string, string>
    Connections: Connections
    ConsumerConfigurations: Map<RuntimeConnectionName, ConsumerConfiguration>
    ProduceTo: Map<RuntimeConnectionName, ProduceEvent<'OutputEvent>>
    IncrementMetric: MetricName -> SimpleDataSetKeys -> unit
    SetMetric: MetricName -> SimpleDataSetKeys -> MetricValue -> unit
    EnableResource: ResourceAvailability -> unit
    DisableResource: ResourceAvailability -> unit
}

[<RequireQualifiedAccess>]
module internal PreparedConsumeRuntimeParts =
    let toRuntimeParts (producers: Map<RuntimeConnectionName, ConnectedProducer>) (preparedRuntimeParts: PreparedConsumeRuntimeParts<'OutputEvent>): ConsumeRuntimeParts<'OutputEvent> =
        {
            LoggerFactory = preparedRuntimeParts.LoggerFactory
            Box = preparedRuntimeParts.Box
            ProcessedBy = {
                Instance = preparedRuntimeParts.Box |> Box.instance
                Commit = preparedRuntimeParts.GitCommit
                ImageVersion = preparedRuntimeParts.DockerImageVersion
            }
            Environment = preparedRuntimeParts.Environment
            Connections = preparedRuntimeParts.Connections
            ConsumerConfigurations = preparedRuntimeParts.ConsumerConfigurations
            ProduceTo =
                preparedRuntimeParts.ProduceTo
                |> Map.map (fun connection produce -> produce producers.[connection])
            IncrementMetric = preparedRuntimeParts.IncrementMetric
            SetMetric = preparedRuntimeParts.SetMetric
            EnableResource = preparedRuntimeParts.EnableResource
            DisableResource = preparedRuntimeParts.DisableResource
        }

[<RequireQualifiedAccess>]
module TracedEvent =
    let event ({ Event = event }: TracedEvent<'InputEvent>) = event
    let trace ({ Trace = trace }: TracedEvent<'InputEvent>) = trace

    let startHandle ({ Event = event; ConsumeTrace = trace }: ParsedEvent<'InputEvent>): TracedEvent<'InputEvent> =
        {
            Event = event
            Trace =
                "Handle event"
                |> Trace.ChildOf.startActive trace
                |> Trace.addTags [
                    "peer.service", "kafka"
                    "component", (sprintf "fkafka-application (%s)" AssemblyVersionInformation.AssemblyVersion)
                ]
        }

    let continueAs pattern name (event: TracedEvent<'Event>) =
        { event with
            Trace =
                name
                |> Trace.ChildOf.startActive event.Trace
                |> Trace.addTags [
                    "peer.service", "kafka"
                    "component", (sprintf "fkafka-application (%s)" AssemblyVersionInformation.AssemblyVersion)
                    "pattern", pattern
                ]
        }

    let finish (event: TracedEvent<'Event>) =
        event.Finish()

type ConsumeHandler<'InputEvent, 'OutputEvent> =
    | Events of (ConsumeRuntimeParts<'OutputEvent> -> TracedEvent<'InputEvent> -> unit)

type RuntimeConsumeHandler<'InputEvent> =
    | Events of (TracedEvent<'InputEvent> -> unit)

[<RequireQualifiedAccess>]
module internal ConsumeHandler =
    let toRuntime runtimeParts = function
        | ConsumeHandler.Events eventsHandler -> eventsHandler runtimeParts |> RuntimeConsumeHandler.Events

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
// Custom tasks
//

[<RequireQualifiedAccess>]
type TaskErrorPolicy =
    | Restart
    | Ignore

/// Application parts exposed in custom task handlers
type CustomTaskRuntimeParts = {
    LoggerFactory: ILoggerFactory
    Box: Box
    Environment: Map<string, string>
    IncrementMetric: MetricName -> SimpleDataSetKeys -> unit
    SetMetric: MetricName -> SimpleDataSetKeys -> MetricValue -> unit
    EnableResource: ResourceAvailability -> unit
    DisableResource: ResourceAvailability -> unit
}

type internal PreparedCustomTask = PreparedCustomTask of TaskErrorPolicy * (CustomTaskRuntimeParts -> Async<unit>)
type internal CustomTask = private CustomTask of TaskErrorPolicy * Async<unit>

[<RequireQualifiedAccess>]
module internal CustomTask =
    let prepare runtimeParts (PreparedCustomTask (policy, prepareTask)) =
        CustomTask (policy, prepareTask runtimeParts)

[<RequireQualifiedAccess>]
module internal CustomTasks =
    let prepare runtimeParts preparedTasks =
        preparedTasks
        |> List.map (CustomTask.prepare runtimeParts)

//
// Configuration / Application
//

type internal ConfigurationParts<'InputEvent, 'OutputEvent> = {
    LoggerFactory: ILoggerFactory
    Environment: Map<string, string>
    Instance: Instance option
    CurrentEnvironment: Lmc.EnvironmentModel.Environment option
    Git: Git
    DockerImageVersion: DockerImageVersion option
    Spot: Spot option
    GroupId: GroupId option
    GroupIds: Map<ConnectionName, GroupId>
    CommitMessage: CommitMessage option
    CommitMessages: Map<ConnectionName, CommitMessage>
    ParseEvent: (ConsumeRuntimeParts<'OutputEvent> -> ParseEvent<'InputEvent>) option
    Connections: Connections
    ConsumeHandlers: ConsumeHandlerForConnection<'InputEvent, 'OutputEvent> list
    OnConsumeErrorHandlers: Map<ConnectionName, ConsumeErrorHandler>
    ProduceTo: ConnectionName list
    ProducerErrorHandler: ProducerErrorHandler option
    FromDomain: Map<ConnectionName, FromDomain<'OutputEvent>>
    ShowMetrics: bool
    ShowAppRootStatus: bool
    CustomMetrics: CustomMetric list
    IntervalResourceCheckers: ResourceMetricInInterval list
    CreateInputEventKeys: CreateInputEventKeys<'InputEvent> option
    CreateOutputEventKeys: CreateOutputEventKeys<'OutputEvent> option
    KafkaChecker: Checker option
    CustomTasks: PreparedCustomTask list
    HttpHandlers: HttpHandler list
}

[<AutoOpen>]
module internal ConfigurationParts =
    let defaultLoggerFactory =
        LoggerFactory.create [
            LogToSimpleConsole
            UseLevel LogLevel.Warning
        ]

    let defaultParts: ConfigurationParts<'InputEvent, 'OutputEvent> =
        {
            LoggerFactory = defaultLoggerFactory
            Environment = Map.empty
            Instance = None
            CurrentEnvironment = None
            Git = {
                Branch = None
                Commit = None
                Repository = None
            }
            DockerImageVersion = None
            Spot = None
            GroupId = None
            GroupIds = Map.empty
            CommitMessage = None
            CommitMessages = Map.empty
            ParseEvent = None
            Connections = Connections.empty
            ConsumeHandlers = []
            OnConsumeErrorHandlers = Map.empty
            ProduceTo = []
            ProducerErrorHandler = None
            FromDomain = Map.empty
            ShowMetrics = false
            ShowAppRootStatus = false
            CustomMetrics = []
            IntervalResourceCheckers = []
            CreateInputEventKeys = None
            CreateOutputEventKeys = None
            KafkaChecker = None
            CustomTasks = []
            HttpHandlers = []
        }

    let getEnvironmentValue (parts: ConfigurationParts<_, _>) success error name =
        parts.Environment
        |> Map.tryFind name
        |> Option.map success
        |> Result.ofOption (sprintf "Environment variable for \"%s\" is not set." name)
        |> Result.mapError error

type Configuration<'InputEvent, 'OutputEvent> = internal Configuration of Result<ConfigurationParts<'InputEvent, 'OutputEvent>, KafkaApplicationError>

[<RequireQualifiedAccess>]
module private Configuration =
    let result (Configuration result) = result

type internal KafkaApplicationParts<'InputEvent, 'OutputEvent> = {
    LoggerFactory: ILoggerFactory
    Environment: Map<string, string>
    Box: Box
    Git: Git
    DockerImageVersion: DockerImageVersion option
    CurrentEnvironment: Lmc.EnvironmentModel.Environment
    ParseEvent: ConsumeRuntimeParts<'OutputEvent> -> ParseEvent<'InputEvent>
    ConsumerConfigurations: Map<RuntimeConnectionName, ConsumerConfiguration>
    ConsumeHandlers: RuntimeConsumeHandlerForConnection<'InputEvent, 'OutputEvent> list
    Producers: Map<RuntimeConnectionName, NotConnectedProducer>
    ProducerErrorHandler: ProducerErrorHandler
    ServiceStatus: ServiceStatus.ServiceStatus
    ShowMetrics: bool
    ShowAppRootStatus: bool
    CustomMetrics: CustomMetric list
    IntervalResourceCheckers: ResourceMetricInInterval list
    PreparedRuntimeParts: PreparedConsumeRuntimeParts<'OutputEvent>
    CustomTasks: CustomTask list
    HttpHandlers: HttpHandler list
}

type KafkaApplication<'InputEvent, 'OutputEvent> = internal KafkaApplication of Result<KafkaApplicationParts<'InputEvent, 'OutputEvent>, KafkaApplicationError>

//
// Application types
//

type ApplicationShutdown =
    | Successfully
    | WithCriticalError of ErrorMessage
    | WithRuntimeError of ErrorMessage

[<RequireQualifiedAccess>]
module ApplicationShutdown =
    let withStatusCode = function
        | Successfully -> 0
        | _ -> 1

    let withStatusCodeAndLogResult (loggerFactory: ILoggerFactory) = function
        | Successfully -> 0
        | WithCriticalError error ->
            loggerFactory
                .CreateLogger("KafkaApplication")
                .LogCritical("Application shutdown with critical error: {error}", error)
            1
        | WithRuntimeError error ->
            loggerFactory
                .CreateLogger("KafkaApplication")
                .LogCritical("Application shutdown with runtime error: {error}", error)
            1

type internal BeforeRun<'InputEvent, 'OutputEvent> = KafkaApplicationParts<'InputEvent, 'OutputEvent> -> unit
type internal Run<'InputEvent, 'OutputEvent> = KafkaApplication<'InputEvent, 'OutputEvent> -> ApplicationShutdown

type internal RunKafkaApplication<'InputEvent, 'OutputEvent> = BeforeRun<'InputEvent, 'OutputEvent> -> Run<'InputEvent, 'OutputEvent>
