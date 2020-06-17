namespace Lmc.KafkaApplication.Deriver

open Lmc.KafkaApplication
open Lmc.Consents.Events.Events

// Errors

type DeriverConfigurationError =
    | NotFound of string
    | NotSet
    | MissingOutputStream
    | MissingDeriveEvent
    | MissingGetCommonEvent

type DeriverApplicationError =
    | ApplicationConfigurationError of ApplicationConfigurationError
    | KafkaApplicationError of KafkaApplicationError
    | DeriverConfigurationError of DeriverConfigurationError

// Deriver configuration

type DeriveEvent<'InputEvent, 'OutputEvent> = ProcessedBy -> 'InputEvent -> 'OutputEvent list

type internal DeriveEventHandler<'InputEvent, 'OutputEvent> =
    | Simple of DeriveEvent<'InputEvent, 'OutputEvent>
    | WithApplication of (PatternRuntimeParts -> DeriveEvent<'InputEvent, 'OutputEvent>)

// Deriver Application Configuration

type internal DeriverParts<'InputEvent, 'OutputEvent> = {
    Configuration: Configuration<'InputEvent, 'OutputEvent> option
    DeriveTo: ConnectionName option
    DeriveEvent: DeriveEventHandler<'InputEvent, 'OutputEvent> option
    CreateCustomValues: CreateCustomValues<'InputEvent, 'OutputEvent> option
    GetCommonEvent: GetCommonEvent<'InputEvent, 'OutputEvent> option
}

module internal DeriverParts =
    let defaultDeriver = {
        Configuration = None
        DeriveTo = None
        DeriveEvent = None
        CreateCustomValues = None
        GetCommonEvent = None
    }

type DeriverApplicationConfiguration<'InputEvent, 'OutputEvent> = private DeriverApplicationConfiguration of Result<DeriverParts<'InputEvent, 'OutputEvent>, DeriverApplicationError>

type DeriverApplicationParts<'InputEvent, 'OutputEvent> = {
    Application: KafkaApplication<'InputEvent, 'OutputEvent>
}

type DeriverApplication<'InputEvent, 'OutputEvent> = internal DeriverApplication of Result<DeriverApplicationParts<'InputEvent, 'OutputEvent>, DeriverApplicationError>

[<RequireQualifiedAccess>]
module internal DeriverApplication =
    let application { Application = application } = application
