namespace KafkaApplication

open Kafka
open ServiceIdentification

//
// Logging
//

type Log = string -> string -> unit

type Logger = {
    Debug: Log
    Log: Log
    Verbose: Log
    Warning: Log
    Error: Log
}

module Logger =
    open Logging

    let quietLogger =
        let ignore _context _message = ()
        {
            Debug = ignore
            Log = ignore
            Verbose = ignore
            Warning = ignore
            Error = ignore
        }

    let defaultLogger =
        {
            Debug = Log.debug
            Log = Log.normal
            Verbose = Log.verbose
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

module ConnectionName =
    let value (ConnectionName name) = name

type Connections = Map<ConnectionName, ConnectionConfiguration>

module Connections =
    let Default = ConnectionName "__default"

    let empty: Connections =
        Map.empty

//
// Errors
//

type OnErrorPolicy =
    | Reboot
    | Continue
    | Shutdown

type ErrorHandler = Logger -> string -> OnErrorPolicy

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

type ConsumeHandlerError =
    | MissingConfiguration of ConnectionName

type MetricsError =
    | InvalidRoute of InvalidMetricsRouteError

type KafkaApplicationError =
    | KafkaApplicationError of string
    | InstanceError of InstanceError
    | SpotError of SpotError
    | GroupIdError of GroupIdError
    | ConnectionConfigurationError of ConnectionConfigurationError
    | EnvironmentError of EnvironmentError
    | ConsumeHandlerError of ConsumeHandlerError
    | MetricsError of MetricsError

//
// Consume handlers
//

type ConsumeRuntimeParts = {
    Logger: Logger
    Environment: Map<string, string>
    Connections: Connections
    ConsumerConfigurations: Map<ConnectionName, ConsumerConfiguration>
}

type ConsumeHandler<'Event> =
    | Events of (ConsumeRuntimeParts -> 'Event seq -> unit)
    | LastEvent of (ConsumeRuntimeParts -> 'Event -> unit)

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
    Connection: ConnectionName
    Configuration: ConsumerConfiguration
    OnError: ErrorHandler
    Handler: RuntimeConsumeHandler<'Event>
}

//
// Configuration / Application
//

type ConfigurationParts<'Event> = {
    Logger: Logger
    Environment: Map<string, string>
    Instance: Instance option
    Spot: Spot option
    GroupId: GroupId option
    GroupIds: Map<ConnectionName, GroupId>
    Connections: Connections
    ConsumeHandlers: ConsumeHandlerForConnection<'Event> list
    OnConsumeErrorHandlers: Map<ConnectionName, ErrorHandler>
    MetricsRoute: MetricsRoute option
}

[<AutoOpen>]
module internal ConfigurationParts =
    let defaultParts =
        {
            Logger = Logger.defaultLogger
            Environment = Map.empty
            Instance = None
            Spot = None
            GroupId = None
            GroupIds = Map.empty
            Connections = Connections.empty
            ConsumeHandlers = []
            OnConsumeErrorHandlers = Map.empty
            MetricsRoute = None
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
    Logger: Logger
    Environment: Map<string, string>
    Box: Box
    ConsumerConfigurations: Map<ConnectionName, ConsumerConfiguration>
    ConsumeHandlers: RuntimeConsumeHandlerForConnection<'Event> list
    MetricsRoute: MetricsRoute option
}

type KafkaApplication<'Event> = private KafkaApplication of Result<KafkaApplicationParts<'Event>, KafkaApplicationError>

//
// Common helpers
//

module internal OptionOperators =
    /// Default value - if value is None, default value will be used
    let (<?=>) defaultValue opt = Option.defaultValue opt defaultValue

    /// Or else - if value is None, other option will be used
    let (<??>) other opt = Option.orElse opt other

    /// Mandatory - if value is None, error will be returned
    let (<?!>) (opt: 'a option) (errorMessage: string): Result<'a, KafkaApplicationError> =
        match opt with
        | Some value -> Ok value
        | None -> sprintf "[KafkaApplicationBuilder] %s" errorMessage |> KafkaApplicationError |> Result.Error

    /// Map action with side-effect and ignore the unit option result
    let (|>!) (opt: 'a option) (action: 'a -> unit) =
        opt
        |> Option.map action
        |> ignore

module internal Map =
    /// Merge new values with the current values (replacing already defined values).
    let merge currentValues newValues =
        currentValues
        |> Map.fold (fun merged name connection ->
            if merged |> Map.containsKey name then merged
            else merged.Add(name, connection)
        ) newValues

module internal List =
    /// Merge new values with the current values (ignoring already defined values) if there is any new values.
    let merge currentValues newValues =
        if newValues |> List.isEmpty then currentValues
        else newValues
