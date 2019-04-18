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
// Errors
//

type OnErrorPolicy =
    | Reboot
    | Shutdown

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

type KafkaApplicationError =
    | KafkaApplicationError of string
    | InstanceError of InstanceError
    | GroupIdError of GroupIdError
    | ConnectionConfigurationError of ConnectionConfigurationError
    | EnvironmentError of EnvironmentError

//
// Kafka connections
//

type Connections = Map<string, ConnectionConfiguration>

module Connections =
    let empty: Connections =
        Map.empty

//
// Configuration / Application
//

type ConsumeRuntimeParts = {
    Logger: Logger
    Environment: Map<string, string>
    Connections: Connections
    ConsumerConfiguration: ConsumerConfiguration
}

type ConfigurationParts<'Event> = {
    Logger: Logger
    Environment: Map<string, string>
    Instance: Instance option
    GroupId: GroupId option
    Connections: Connections
    Consume: (ConsumeRuntimeParts -> 'Event seq -> unit) option
    OnConsumeError: (Logger -> string -> OnErrorPolicy) option
}

[<AutoOpen>]
module internal ConfigurationParts =
    let defaultParts =
        {
            Logger = Logger.defaultLogger
            Environment = Map.empty
            Instance = None
            GroupId = None
            Connections = Connections.empty
            Consume = None
            OnConsumeError = None
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
    Instance: Instance
    ConsumerConfiguration: ConsumerConfiguration
    Consume: 'Event seq -> unit
    OnError: Logger -> string -> OnErrorPolicy
}

type KafkaApplication<'Event> = private KafkaApplication of Result<KafkaApplicationParts<'Event>, KafkaApplicationError>

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
