namespace Lmc.KafkaApplication.Deriver

open Lmc.KafkaApplication
open Lmc.Kafka.MetaData
open Lmc.ErrorHandling

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

type DeriveEvent<'InputEvent, 'OutputEvent> = ProcessedBy -> TracedEvent<'InputEvent> -> TracedEvent<'OutputEvent> list
type DeriveEventResult<'InputEvent, 'OutputEvent> = ProcessedBy -> TracedEvent<'InputEvent> -> Result<TracedEvent<'OutputEvent> list, ErrorMessage>
type DeriveEventAsyncResult<'InputEvent, 'OutputEvent> = ProcessedBy -> TracedEvent<'InputEvent> -> AsyncResult<TracedEvent<'OutputEvent> list, ErrorMessage>

type internal DeriveInputToOutputEvent<'InputEvent, 'OutputEvent> =
    | DeriveEvent of DeriveEvent<'InputEvent, 'OutputEvent>
    | DeriveEventResult of DeriveEventResult<'InputEvent, 'OutputEvent>
    | DeriveEventAsyncResult of DeriveEventAsyncResult<'InputEvent, 'OutputEvent>

type internal DeriveEventHandler<'InputEvent, 'OutputEvent> =
    | Simple of DeriveInputToOutputEvent<'InputEvent, 'OutputEvent>
    | WithApplication of (PatternRuntimeParts -> DeriveInputToOutputEvent<'InputEvent, 'OutputEvent>)

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
