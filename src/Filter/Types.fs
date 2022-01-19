namespace Lmc.KafkaApplication.Filter

open Lmc.ServiceIdentification
open Lmc.Kafka.MetaData
open Lmc.KafkaApplication

// Errors
type FilterConfigurationError =
    | NotFound of string
    | NotSet
    | InvalidSpot of Lmc.ServiceIdentification.SpotError list
    | MissingOutputStream
    | MissingFilterContent
    | MissingGetCommonEvent

type FilterApplicationError =
    | ApplicationConfigurationError of ApplicationConfigurationError
    | FilterConfigurationError of FilterConfigurationError

// Filter configuration

type FilterConfiguration<'FilterValue> = {
    Spots: Spot list
    FilterValues: 'FilterValue list
}

type FilterContent<'InputEvent, 'OutputEvent> = ProcessedBy -> TracedEvent<'InputEvent> -> TracedEvent<'OutputEvent> option

// Filter Application Configuration

type internal FilterParts<'InputEvent, 'OutputEvent, 'FilterValue> = {
    Configuration: Configuration<'InputEvent, 'OutputEvent> option
    FilterConfiguration: FilterConfiguration<'FilterValue> option
    FilterTo: ConnectionName option
    FilterContent: FilterContent<'InputEvent, 'OutputEvent> option
    CreateCustomValues: CreateCustomValues<'InputEvent, 'OutputEvent> option
    GetCommonEvent: GetCommonEvent<'InputEvent, 'OutputEvent> option
    GetFilterValue: GetFilterValue<'InputEvent, 'FilterValue> option
}

[<RequireQualifiedAccess>]
module internal FilterParts =
    let defaultFilter = {
        Configuration = None
        FilterConfiguration = None
        FilterTo = None
        FilterContent = None
        CreateCustomValues = None
        GetCommonEvent = None
        GetFilterValue = None
    }

type FilterApplicationConfiguration<'InputEvent, 'OutputEvent, 'FilterValue> = private FilterApplicationConfiguration of Result<FilterParts<'InputEvent, 'OutputEvent, 'FilterValue>, FilterApplicationError>

type internal FilterApplicationParts<'InputEvent, 'OutputEvent, 'FilterValue> = {
    Application: KafkaApplication<'InputEvent, 'OutputEvent>
    FilterConfiguration: FilterConfiguration<'FilterValue>
}

type FilterApplication<'InputEvent, 'OutputEvent, 'FilterValue> = internal FilterApplication of Result<FilterApplicationParts<'InputEvent, 'OutputEvent, 'FilterValue>, FilterApplicationError>

[<RequireQualifiedAccess>]
module internal FilterApplication =
    let application { Application = application } = application
