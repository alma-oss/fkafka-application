namespace KafkaApplication.Deriver

open KafkaApplication
open KafkaApplication.Pattern

// Errors

type DeriverConfigurationError =
    | NotFound of string
    | NotSet
    | MissingOutputStream
    | MissingDeriveEvent
    | MissingGetCommonEvent

type DeriverApplicationError =
    | ApplicationConfigurationError of ApplicationConfigurationError
    | DeriverConfigurationError of DeriverConfigurationError

// Deriver configuration

type DeriveEvent<'InputEvent, 'OutputEvent> = 'InputEvent -> 'OutputEvent list

// Deriver Application Configuration

type DeriverParts<'InputEvent, 'OutputEvent> = {
    Configuration: Configuration<'InputEvent, 'OutputEvent> option
    DeriveTo: ConnectionName option
    DeriveEvent: DeriveEvent<'InputEvent, 'OutputEvent> option
    GetCommonEvent: GetCommonEvent<'InputEvent, 'OutputEvent> option
}

module DeriverParts =
    let defaultDeriver = {
        Configuration = None
        DeriveTo = None
        DeriveEvent = None
        GetCommonEvent = None
    }

type DeriverApplicationConfiguration<'InputEvent, 'OutputEvent> = private DeriverApplicationConfiguration of Result<DeriverParts<'InputEvent, 'OutputEvent>, DeriverApplicationError>

type DeriverApplicationParts<'InputEvent, 'OutputEvent> = {
    Application: KafkaApplication<'InputEvent, 'OutputEvent>
}

type DeriverApplication<'InputEvent, 'OutputEvent> = internal DeriverApplication of Result<DeriverApplicationParts<'InputEvent, 'OutputEvent>, DeriverApplicationError>

module DeriverApplication =
    let application { Application = application } = application
