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

type internal DeriveEventHandler<'InputEvent, 'OutputEvent, 'Dependencies> =
    | Simple of DeriveInputToOutputEvent<'InputEvent, 'OutputEvent>
    | WithApplication of (PatternRuntimeParts<'Dependencies> -> DeriveInputToOutputEvent<'InputEvent, 'OutputEvent>)

// Deriver Application Configuration

type internal DeriverParts<'InputEvent, 'OutputEvent, 'Dependencies> = {
    Configuration: Configuration<'InputEvent, 'OutputEvent, 'Dependencies> option
    DeriveTo: ConnectionName option
    DeriveEvent: DeriveEventHandler<'InputEvent, 'OutputEvent, 'Dependencies> option
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

type DeriverApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
    private DeriverApplicationConfiguration of Result<DeriverParts<'InputEvent, 'OutputEvent, 'Dependencies>, DeriverApplicationError>

type DeriverApplicationParts<'InputEvent, 'OutputEvent, 'Dependencies> = {
    Application: KafkaApplication<'InputEvent, 'OutputEvent, 'Dependencies>
}

type DeriverApplication<'InputEvent, 'OutputEvent, 'Dependencies> =
    internal DeriverApplication of Result<DeriverApplicationParts<'InputEvent, 'OutputEvent, 'Dependencies>, DeriverApplicationError>

[<RequireQualifiedAccess>]
module internal DeriverApplication =
    let application { Application = application } = application
