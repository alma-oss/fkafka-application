namespace Alma.KafkaApplication

open System
open System.Threading
open Microsoft.Extensions.Logging
open Giraffe

open Alma.Kafka
open Alma.Kafka.MetaData
open Alma.Metrics
open Alma.ServiceIdentification
open Alma.Logging
open Alma.Tracing
open Feather.ErrorHandling

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
module ConnectionName =
    let internal value (ConnectionName name) = name
    let runtimeName (ConnectionName name): RuntimeConnectionName = name

type ConnectionConfiguration = {
    BrokerList: BrokerList
    Topic: Instance
}

[<RequireQualifiedAccess>]
module internal ConnectionConfiguration =
    let toKafkaConnectionConfiguration (connection: ConnectionConfiguration): Alma.Kafka.ConnectionConfiguration =
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

type ErrorMessage =
    | ErrorMessage of string
    | RuntimeError of exn
    | Errors of ErrorMessage list

[<RequireQualifiedAccess>]
module ErrorMessage =
    let rec value = function
        | ErrorMessage message -> [ message ]
        | RuntimeError error -> [ sprintf "%A" error ]
        | Errors [] -> []
        | Errors errors -> errors |> List.collect value

    let format e =
        match value e with
        | [] -> "No error message."
        | [ message ] -> message
        | messages -> messages |> String.concat "\n"

type internal IO<'Data> = AsyncResult<'Data, ErrorMessage>

[<RequireQualifiedAccess>]
module internal IO =
    let runList (io: IO<unit> list): IO<unit> =
        io
        |> AsyncResult.ofSequentialAsyncResults RuntimeError
        |> AsyncResult.mapError Errors
        |> AsyncResult.ignore

type ProducerErrorPolicy =
    | Shutdown
    | ShutdownIn of int<Second>
    | Retry
    | RetryIn of int<Second>

type ProducerErrorHandler = ILogger -> exn -> ProducerErrorPolicy

type ConsumeErrorPolicy =
    | Shutdown
    | ShutdownIn of int<Second>
    | Retry
    | RetryIn of int<Second>
    | Continue

type ConsumeErrorHandler = ILogger -> exn -> ConsumeErrorPolicy

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
    | InvalidFormatError of Alma.ServiceIdentification.InstanceError

[<RequireQualifiedAccess>]
type CurrentEnvironmentError =
    | VariableNotFoundError of string
    | InvalidFormatError of Alma.EnvironmentModel.EnvironmentError

[<RequireQualifiedAccess>]
type SpotError =
    | VariableNotFoundError of string
    | InvalidFormatError of Alma.ServiceIdentification.SpotError

[<RequireQualifiedAccess>]
type GroupIdError =
    | VariableNotFoundError of string

[<RequireQualifiedAccess>]
type ConnectionConfigurationError =
    | VariableNotFoundError of string
    | TopicIsNotInstanceError of Alma.ServiceIdentification.InstanceError list

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

type ProduceEvent<'OutputEvent> = TracedEvent<'OutputEvent> -> IO<unit>
type PreparedProduceEvent<'OutputEvent> = ConnectedProducer -> ProduceEvent<'OutputEvent>

type private PreparedProducer<'OutputEvent> = {
    Connection: ConnectionName
    Producer: NotConnectedProducer
    Produce: PreparedProduceEvent<'OutputEvent>
}

// Output events
type FromDomain<'OutputEvent> = Serialize -> 'OutputEvent -> MessageToProduce
type FromDomainResult<'OutputEvent> = Serialize -> 'OutputEvent -> Result<MessageToProduce, ErrorMessage>
type FromDomainAsyncResult<'OutputEvent> = Serialize -> 'OutputEvent -> AsyncResult<MessageToProduce, ErrorMessage>

type internal OutputFromDomain<'OutputEvent> =
    | FromDomain of FromDomain<'OutputEvent>
    | FromDomainResult of FromDomainResult<'OutputEvent>
    | FromDomainAsyncResult of FromDomainAsyncResult<'OutputEvent>

//
// Consume handlers
//

type ParseEvent<'InputEvent> = string -> 'InputEvent
type ParseEventResult<'InputEvent> = string -> Result<'InputEvent, ErrorMessage>
type ParseEventAsyncResult<'InputEvent> = string -> AsyncResult<'InputEvent, ErrorMessage>

type internal ParseInputEvent<'InputEvent> =
    | ParseEvent of ParseEvent<'InputEvent>
    | ParseEventResult of ParseEventResult<'InputEvent>
    | ParseEventAsyncResult of ParseEventAsyncResult<'InputEvent>

type ParsedEvent<'InputEvent> = {
    Commit: ManualCommit
    Event: 'InputEvent
    ConsumeTrace: Trace
}

type ParsedEventAsyncResult<'InputEvent> = Consumer.ConsumedAsyncResult<ParsedEvent<'InputEvent>>

type internal ConsumeEventsWithConfiguration<'InputEvent> = ConsumerConfiguration -> ParsedEventAsyncResult<'InputEvent> seq

[<RequireQualifiedAccess>]
module Event =
    open Feather.ErrorHandling.AsyncResult.Operators

    let private errorToConsumeError = function
        | RuntimeError error -> ConsumeError.RuntimeException error
        | error ->
            error
            |> ErrorMessage.value
            |> String.concat "\n"
            |> ConsumeError.RuntimeError

    let internal parse<'Event> (parseEvent: ParseInputEvent<'Event>): Consumer.ParseEventAsyncResult<ParsedEvent<'Event>> =
        fun tracedMessage -> asyncResult {
            let parseEvent =
                match parseEvent with
                | ParseEvent parseEvent -> parseEvent >> AsyncResult.ofSuccess
                | ParseEventResult parseEvent -> parseEvent >> AsyncResult.ofResult
                | ParseEventAsyncResult parseEvent -> parseEvent

            let! parsedEvent =
                tracedMessage.Message
                |> parseEvent <@> errorToConsumeError

            return {
                Event = parsedEvent
                ConsumeTrace = tracedMessage.Trace
                Commit = tracedMessage.Commit
            }
        }

    let event ({ Event = event }: ParsedEvent<'Event>) = event

type internal PreparedConsumeRuntimeParts<'OutputEvent> = {
    LoggerFactory: ILoggerFactory
    Box: Box
    CurrentEnvironment: Alma.EnvironmentModel.Environment
    GitCommit: MetaData.GitCommit
    DockerImageVersion: MetaData.DockerImageVersion
    Environment: Map<string, string>
    Connections: Connections
    ConsumerConfigurations: Map<RuntimeConnectionName, ConsumerConfiguration>
    ProduceTo: Map<RuntimeConnectionName, PreparedProduceEvent<'OutputEvent>>
    IncrementMetric: MetricName -> SimpleDataSetKeys -> unit
    IncrementMetricBy: MetricName -> SimpleDataSetKeys -> MetricValue -> unit
    SetMetric: MetricName -> SimpleDataSetKeys -> MetricValue -> unit
    EnableResource: ResourceAvailability -> unit
    DisableResource: ResourceAvailability -> unit
}

/// Application parts exposed in consume handlers
type ConsumeRuntimeParts<'OutputEvent, 'Dependencies> = {
    LoggerFactory: ILoggerFactory
    Box: Box
    CurrentEnvironment: Alma.EnvironmentModel.Environment
    ProcessedBy: ProcessedBy
    Environment: Map<string, string>
    Connections: Connections
    ConsumerConfigurations: Map<RuntimeConnectionName, ConsumerConfiguration>
    ProduceTo: Map<RuntimeConnectionName, ProduceEvent<'OutputEvent>>
    IncrementMetric: MetricName -> SimpleDataSetKeys -> unit
    IncrementMetricBy: MetricName -> SimpleDataSetKeys -> MetricValue -> unit
    SetMetric: MetricName -> SimpleDataSetKeys -> MetricValue -> unit
    EnableResource: ResourceAvailability -> unit
    DisableResource: ResourceAvailability -> unit
    Dependencies: 'Dependencies option
    Cancellation: CancellationTokenSource
    StoreCurrentOffsetInternally: bool
}

type Initialization<'OutputEvent, 'Dependencies> = ConsumeRuntimeParts<'OutputEvent, 'Dependencies> -> ConsumeRuntimeParts<'OutputEvent, 'Dependencies>
type InitializationResult<'OutputEvent, 'Dependencies> = ConsumeRuntimeParts<'OutputEvent, 'Dependencies> -> Result<ConsumeRuntimeParts<'OutputEvent, 'Dependencies>, ErrorMessage>
type InitializationAsyncResult<'OutputEvent, 'Dependencies> = ConsumeRuntimeParts<'OutputEvent, 'Dependencies> -> AsyncResult<ConsumeRuntimeParts<'OutputEvent, 'Dependencies>, ErrorMessage>

/// Application initialization - it allows to set up dependencies before any consuming starts
type ApplicationInitialization<'OutputEvent, 'Dependencies> =
    | Initialization of Initialization<'OutputEvent, 'Dependencies>
    | InitializationResult of InitializationResult<'OutputEvent, 'Dependencies>
    | InitializationAsyncResult of InitializationAsyncResult<'OutputEvent, 'Dependencies>

[<RequireQualifiedAccess>]
module internal ApplicationInitialization =
    let initialize = function
        | Initialization initialize -> initialize >> AsyncResult.ofSuccess
        | InitializationResult initialize -> initialize >> AsyncResult.ofResult
        | InitializationAsyncResult initialize -> initialize

type internal RuntimeConsumeEvents<'InputEvent, 'OutputEvent, 'Dependencies> =
    ConsumeRuntimeParts<'OutputEvent, 'Dependencies>
        -> ConsumerConfiguration
        -> ParsedEventAsyncResult<'InputEvent> seq

[<RequireQualifiedAccess>]
module internal PreparedConsumeRuntimeParts =
    let toRuntimeParts cancellation (producers: Map<RuntimeConnectionName, ConnectedProducer>) (preparedRuntimeParts: PreparedConsumeRuntimeParts<'OutputEvent>): ConsumeRuntimeParts<'OutputEvent, 'Dependencies> =
        {
            LoggerFactory = preparedRuntimeParts.LoggerFactory
            Box = preparedRuntimeParts.Box
            CurrentEnvironment = preparedRuntimeParts.CurrentEnvironment
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
            IncrementMetricBy = preparedRuntimeParts.IncrementMetricBy
            SetMetric = preparedRuntimeParts.SetMetric
            EnableResource = preparedRuntimeParts.EnableResource
            DisableResource = preparedRuntimeParts.DisableResource
            Dependencies = None
            Cancellation = cancellation
            StoreCurrentOffsetInternally = false
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

type ConsumeEvents<'InputEvent, 'OutputEvent, 'Dependencies> = ConsumeRuntimeParts<'OutputEvent, 'Dependencies> -> TracedEvent<'InputEvent> -> unit
type ConsumeEventsResult<'InputEvent, 'OutputEvent, 'Dependencies> = ConsumeRuntimeParts<'OutputEvent, 'Dependencies> -> TracedEvent<'InputEvent> -> Result<unit, ErrorMessage>
type ConsumeEventsAsyncResult<'InputEvent, 'OutputEvent, 'Dependencies> = ConsumeRuntimeParts<'OutputEvent, 'Dependencies> -> TracedEvent<'InputEvent> -> AsyncResult<unit, ErrorMessage>

type internal ConsumeHandler<'InputEvent, 'OutputEvent, 'Dependencies> =
    | ConsumeEvents of ConsumeEvents<'InputEvent, 'OutputEvent, 'Dependencies>
    | ConsumeEventsResult of ConsumeEventsResult<'InputEvent, 'OutputEvent, 'Dependencies>
    | ConsumeEventsAsyncResult of ConsumeEventsAsyncResult<'InputEvent, 'OutputEvent, 'Dependencies>

type RuntimeConsumeHandler<'InputEvent> =
    | Events of (TracedEvent<'InputEvent> -> IO<unit>)

[<RequireQualifiedAccess>]
module internal ConsumeHandler =
    let toRuntime runtimeParts: ConsumeHandler<'InputEvent, 'OutputEvent, 'Dependencies> -> RuntimeConsumeHandler<'InputEvent> = function
        | ConsumeEvents eventsHandler -> eventsHandler runtimeParts >> AsyncResult.ofSuccess |> Events
        | ConsumeEventsResult eventsHandler -> eventsHandler runtimeParts >> AsyncResult.ofResult |> Events
        | ConsumeEventsAsyncResult eventsHandler -> eventsHandler runtimeParts |> Events

type internal ConsumeHandlerForConnection<'InputEvent, 'OutputEvent, 'Dependencies> = {
    Connection: ConnectionName
    Handler: ConsumeHandler<'InputEvent, 'OutputEvent, 'Dependencies>
}

type internal RuntimeConsumeHandlerForConnection<'InputEvent, 'OutputEvent, 'Dependencies> = {
    Connection: RuntimeConnectionName
    Configuration: ConsumerConfiguration
    OnError: ConsumeErrorHandler
    Handler: ConsumeHandler<'InputEvent, 'OutputEvent, 'Dependencies>
    IncrementInputCount: 'InputEvent -> unit
}

[<RequireQualifiedAccess>]
module internal ConsumerConfiguration =
    /// This function allows to modify only the specific parts of Configuration of the handler
    /// Some parts are already used elsewhere and should not be changed, it will fail if you try to change them
    let updateByRuntimeConfiguration (mapped: ConsumerConfiguration) (original: ConsumerConfiguration) =
        if
            mapped.Connection <> original.Connection
            || mapped.GroupId <> original.GroupId
            || mapped.CommitMessage <> original.CommitMessage
            || mapped.CountLag <> original.CountLag
            // other fields contains functions or complex types, we do not want to compare them
        then
            failwith "You cannot change Consumer configuration this way."

        { original with GetCheckpoint = mapped.GetCheckpoint }

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
    IncrementMetricBy: MetricName -> SimpleDataSetKeys -> MetricValue -> unit
    SetMetric: MetricName -> SimpleDataSetKeys -> MetricValue -> unit
    EnableResource: ResourceAvailability -> unit
    DisableResource: ResourceAvailability -> unit
    ConsumerConfigurations: Map<RuntimeConnectionName, ConsumerConfiguration>
}

type CustomTaskName = CustomTaskName of string
type internal PreparedCustomTask = PreparedCustomTask of CustomTaskName * TaskErrorPolicy * (CustomTaskRuntimeParts -> Async<unit>)
type internal CustomTask = private CustomTask of CustomTaskName * TaskErrorPolicy * Async<unit>

[<RequireQualifiedAccess>]
module internal CustomTask =
    let prepare runtimeParts (PreparedCustomTask (name, policy, prepareTask)) =
        CustomTask (name, policy, prepareTask runtimeParts)

[<RequireQualifiedAccess>]
module internal CustomTasks =
    let prepare runtimeParts preparedTasks =
        preparedTasks
        |> List.map (CustomTask.prepare runtimeParts)

//
// Configuration / Application
//

type internal ConfigurationParts<'InputEvent, 'OutputEvent, 'Dependencies> = {
    LoggerFactory: ILoggerFactory
    Environment: Map<string, string>
    Instance: Instance option
    CurrentEnvironment: Alma.EnvironmentModel.Environment option
    Initialize: ApplicationInitialization<'OutputEvent, 'Dependencies> option
    Git: Git
    DockerImageVersion: DockerImageVersion option
    Spot: Spot option
    GroupId: GroupId option
    GroupIds: Map<ConnectionName, GroupId>
    CommitMessage: CommitMessage option
    CommitMessages: Map<ConnectionName, CommitMessage>
    ParseEvent: (ConsumeRuntimeParts<'OutputEvent, 'Dependencies> -> ParseInputEvent<'InputEvent>) option
    Connections: Connections
    ConsumeHandlers: ConsumeHandlerForConnection<'InputEvent, 'OutputEvent, 'Dependencies> list
    OnConsumeErrorHandlers: Map<ConnectionName, ConsumeErrorHandler>
    ProduceTo: ConnectionName list
    ProducerErrorHandler: ProducerErrorHandler option
    FromDomain: Map<ConnectionName, OutputFromDomain<'OutputEvent>>
    ShowMetrics: bool
    ShowAppRootStatus: bool
    ShowInternalState: string option
    CustomMetrics: CustomMetric list
    IntervalResourceCheckers: ResourceMetricInInterval list
    CreateInputEventKeys: CreateInputEventKeys<'InputEvent> option
    CreateOutputEventKeys: CreateOutputEventKeys<'OutputEvent> option
    KafkaChecker: Checker option
    CustomTasks: PreparedCustomTask list
    HttpHandlers: HttpHandler list
    WebServerPort: WebServer.Port
}

[<AutoOpen>]
module internal ConfigurationParts =
    let defaultLoggerFactory =
        LoggerFactory.create [
            LogToSimpleConsole
            UseLevel LogLevel.Warning
        ]

    let defaultParts: ConfigurationParts<'InputEvent, 'OutputEvent, 'Dependencies> =
        {
            LoggerFactory = defaultLoggerFactory
            Environment = Map.empty
            Instance = None
            CurrentEnvironment = None
            Initialize = None
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
            ShowInternalState = None
            CustomMetrics = []
            IntervalResourceCheckers = []
            CreateInputEventKeys = None
            CreateOutputEventKeys = None
            KafkaChecker = None
            CustomTasks = []
            HttpHandlers = []
            WebServerPort = WebServer.defaultPort
        }

    let getEnvironmentValue (parts: ConfigurationParts<_, _, _>) success error name =
        parts.Environment
        |> Map.tryFind name
        |> Option.map success
        |> Result.ofOption (sprintf "Environment variable for \"%s\" is not set." name)
        |> Result.mapError error

type Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
    internal Configuration of Result<ConfigurationParts<'InputEvent, 'OutputEvent, 'Dependencies>, KafkaApplicationError>

[<RequireQualifiedAccess>]
module private Configuration =
    let result (Configuration result) = result

type internal KafkaApplicationParts<'InputEvent, 'OutputEvent, 'Dependencies> = {
    LoggerFactory: ILoggerFactory
    Initialize: ApplicationInitialization<'OutputEvent, 'Dependencies>
    Cancellation: Cancellations
    Environment: Map<string, string>
    Box: Box
    Git: Git
    DockerImageVersion: DockerImageVersion option
    CurrentEnvironment: Alma.EnvironmentModel.Environment
    ConsumerConfigurations: Map<RuntimeConnectionName, ConsumerConfiguration>
    ConsumeHandlers: RuntimeConsumeHandlerForConnection<'InputEvent, 'OutputEvent, 'Dependencies> list
    ConsumeEvents: RuntimeConsumeEvents<'InputEvent, 'OutputEvent, 'Dependencies>
    Producers: Map<RuntimeConnectionName, NotConnectedProducer>
    ProducerErrorHandler: ProducerErrorHandler
    ServiceStatus: ServiceStatus.ServiceStatus
    ShowMetrics: bool
    ShowAppRootStatus: bool
    ShowInternalState: string option
    CustomMetrics: CustomMetric list
    IntervalResourceCheckers: ResourceMetricInInterval list
    PreparedRuntimeParts: PreparedConsumeRuntimeParts<'OutputEvent>
    CustomTasks: CustomTask list
    HttpHandlers: HttpHandler list
    WebServerPort: WebServer.Port
}

type KafkaApplication<'InputEvent, 'OutputEvent, 'Dependencies> =
    internal KafkaApplication of Result<KafkaApplicationParts<'InputEvent, 'OutputEvent, 'Dependencies>, KafkaApplicationError>

    with
        member this.LoggerFactory
            with get () =
                match this with
                | KafkaApplication (Ok app) -> app.LoggerFactory
                | _ -> defaultLoggerFactory

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
            (LoggerFactory.createLogger loggerFactory "KafkaApplication")
                .LogCritical("Application shutdown with critical error: {error}", error)
            1
        | WithRuntimeError error ->
            (LoggerFactory.createLogger loggerFactory "KafkaApplication")
                .LogCritical("Application shutdown with runtime error: {error}", error)
            1

type internal BeforeStart<'InputEvent, 'OutputEvent, 'Dependencies> = BeforeStart of (KafkaApplicationParts<'InputEvent, 'OutputEvent, 'Dependencies> -> unit)
type internal BeforeRun<'OutputEvent, 'Dependencies> = BeforeRun of (ConsumeRuntimeParts<'OutputEvent,'Dependencies> -> ConsumeRuntimeParts<'OutputEvent,'Dependencies>) option
type internal Run<'InputEvent, 'OutputEvent, 'Dependencies> = KafkaApplication<'InputEvent, 'OutputEvent, 'Dependencies> -> ApplicationShutdown

type internal RunKafkaApplication<'InputEvent, 'OutputEvent, 'Dependencies> =
    BeforeStart<'InputEvent, 'OutputEvent, 'Dependencies>
        -> BeforeRun<'OutputEvent, 'Dependencies>
        -> Run<'InputEvent, 'OutputEvent, 'Dependencies>

[<RequireQualifiedAccess>]
module internal BeforeStart =
    let empty = BeforeStart ignore

[<RequireQualifiedAccess>]
module internal BeforeRun =
    let empty = BeforeRun None
