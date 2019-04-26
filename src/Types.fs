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
type GroupIdError =
    | VariableNotFoundError of string

[<RequireQualifiedAccess>]
type ConnectionConfigurationError =
    | VariableNotFoundError of string

type ConsumeHandlerError =
    | MissingConfiguration of ConnectionName

type KafkaApplicationError =
    | KafkaApplicationError of string
    | InstanceError of InstanceError
    | GroupIdError of GroupIdError
    | ConnectionConfigurationError of ConnectionConfigurationError
    | EnvironmentError of EnvironmentError
    | ConsumeHandlerError of ConsumeHandlerError

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
    GroupId: GroupId option
    GroupIds: Map<ConnectionName, GroupId>
    Connections: Connections
    ConsumeHandlers: ConsumeHandlerForConnection<'Event> list
    OnConsumeErrorHandlers: Map<ConnectionName, ErrorHandler>
}

[<AutoOpen>]
module internal ConfigurationParts =
    let defaultParts =
        {
            Logger = Logger.defaultLogger
            Environment = Map.empty
            Instance = None
            GroupId = None
            GroupIds = Map.empty
            Connections = Connections.empty
            ConsumeHandlers = []
            OnConsumeErrorHandlers = Map.empty
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
