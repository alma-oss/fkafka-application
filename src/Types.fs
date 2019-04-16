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
// Configuration / Application
//

type ConsumeRuntimeParts = {
    Logger: Logger
    Environment: Map<string, string>
    ConsumerConfiguration: ConsumerConfiguration
}

type ConfigurationParts<'Event> = {
    Logger: Logger
    Environment: Map<string, string>
    Instance: Instance option
    GroupId: GroupId
    ConnectionConfiguration: ConnectionConfiguration option
    Consume: (ConsumeRuntimeParts -> 'Event -> unit) option
    OnError: (Logger -> string -> OnErrorPolicy) option
}

type Configuration<'Event> = private Configuration of Result<ConfigurationParts<'Event>, KafkaApplicationError>

type KafkaApplicationParts<'Event> = {
    Logger: Logger
    Environment: Map<string, string>
    Instance: Instance
    ConsumerConfiguration: ConsumerConfiguration
    Consume: 'Event -> unit
    OnError: Logger -> string -> OnErrorPolicy
}

type KafkaApplication<'Event> = private KafkaApplication of Result<KafkaApplicationParts<'Event>, KafkaApplicationError>
