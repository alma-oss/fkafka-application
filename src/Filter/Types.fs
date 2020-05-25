namespace Lmc.KafkaApplication.Filter

open ServiceIdentification
open Lmc.KafkaApplication
open ContractAggregate.Intent
open Events

// Errors

type FilterConfigurationError =
    | NotFound of string
    | NotSet
    | MissingOutputStream
    | MissingFilterContent
    | MissingGetCommonEvent

type FilterApplicationError =
    | ApplicationConfigurationError of ApplicationConfigurationError
    | FilterConfigurationError of FilterConfigurationError

// Filter configuration

type FilterConfiguration = {
    Spots: Spot list
    Intents: Intent list
}

type FilterContent<'InputEvent, 'OutputEvent> = ProcessedBy -> 'InputEvent -> 'OutputEvent list

// Filter Application Configuration

type internal FilterParts<'InputEvent, 'OutputEvent> = {
    Configuration: Configuration<'InputEvent, 'OutputEvent> option
    FilterConfiguration: FilterConfiguration option
    FilterTo: ConnectionName option
    FilterContent: FilterContent<'InputEvent, 'OutputEvent> option
    CreateCustomValues: CreateCustomValues<'InputEvent, 'OutputEvent> option
    GetCommonEvent: GetCommonEvent<'InputEvent, 'OutputEvent> option
    GetIntent: GetIntent<'InputEvent> option
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
        GetIntent = None
    }

type FilterApplicationConfiguration<'InputEvent, 'OutputEvent> = private FilterApplicationConfiguration of Result<FilterParts<'InputEvent, 'OutputEvent>, FilterApplicationError>

type internal FilterApplicationParts<'InputEvent, 'OutputEvent> = {
    Application: KafkaApplication<'InputEvent, 'OutputEvent>
    FilterConfiguration: FilterConfiguration
}

type FilterApplication<'InputEvent, 'OutputEvent> = internal FilterApplication of Result<FilterApplicationParts<'InputEvent, 'OutputEvent>, FilterApplicationError>

[<RequireQualifiedAccess>]
module internal FilterApplication =
    let application { Application = application } = application
