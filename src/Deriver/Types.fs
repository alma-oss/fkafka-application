namespace KafkaApplication.Deriver

open KafkaApplication
open Kafka

// Errors

type ApplicationConfigurationError =
    | ConfigurationNotSet
    | AlreadySetConfiguration
    | InvalidConfiguration of KafkaApplicationError

type DeriverConfigurationError =
    | NotFound of string
    | NotSet
    | MissingOutputStream
    | MissingDeriveEvent
    | MissingGetCommonEventData

type DeriverApplicationError =
    | ApplicationConfigurationError of ApplicationConfigurationError
    | DeriverConfigurationError of DeriverConfigurationError

// Deriver configuration

type InputOrOutputEvent<'InputEvent, 'OutputEvent> =
    | Input of 'InputEvent
    | Output of 'OutputEvent

type CommonEventData = {
    Event: EventName
}

type DeriveEvent<'InputEvent, 'OutputEvent> = 'InputEvent -> 'OutputEvent list

type GetCommonEventData<'InputEvent, 'OutputEvent> = InputOrOutputEvent<'InputEvent, 'OutputEvent> -> CommonEventData

// Deriver Application Configuration

type DeriverParts<'InputEvent, 'OutputEvent> = {
    Configuration: Configuration<'InputEvent, 'OutputEvent> option
    DeriveTo: ConnectionName option
    DeriveEvent: DeriveEvent<'InputEvent, 'OutputEvent> option
    GetCommonEventData: GetCommonEventData<'InputEvent, 'OutputEvent> option
}

module DeriverParts =
    let defaultDeriver = {
        Configuration = None
        DeriveTo = None
        DeriveEvent = None
        GetCommonEventData = None
    }

type DeriverApplicationConfiguration<'InputEvent, 'OutputEvent> = private DeriverApplicationConfiguration of Result<DeriverParts<'InputEvent, 'OutputEvent>, DeriverApplicationError>

type DeriverApplicationParts<'InputEvent, 'OutputEvent> = {
    Application: KafkaApplication<'InputEvent, 'OutputEvent>
}

type DeriverApplication<'InputEvent, 'OutputEvent> = internal DeriverApplication of Result<DeriverApplicationParts<'InputEvent, 'OutputEvent>, DeriverApplicationError>
