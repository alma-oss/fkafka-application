namespace KafkaApplication.Filter

open ServiceIdentification
open KafkaApplication

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
}

type FilterContent<'InputEvent, 'OutputEvent> = 'InputEvent -> 'OutputEvent list

// Filter Application Configuration

type FilterParts<'InputEvent, 'OutputEvent> = {
    Configuration: Configuration<'InputEvent, 'OutputEvent> option
    FilterConfiguration: FilterConfiguration option
    FilterTo: ConnectionName option
    FilterContent: FilterContent<'InputEvent, 'OutputEvent> option
    CreateCustomValues: CreateCustomValues<'InputEvent, 'OutputEvent> option
    GetCommonEvent: GetCommonEvent<'InputEvent, 'OutputEvent> option
}

module FilterParts =
    let defaultFilter = {
        Configuration = None
        FilterConfiguration = None
        FilterTo = None
        FilterContent = None
        CreateCustomValues = None
        GetCommonEvent = None
    }

type FilterApplicationConfiguration<'InputEvent, 'OutputEvent> = private FilterApplicationConfiguration of Result<FilterParts<'InputEvent, 'OutputEvent>, FilterApplicationError>

type FilterApplicationParts<'InputEvent, 'OutputEvent> = {
    Application: KafkaApplication<'InputEvent, 'OutputEvent>
    FilterConfiguration: FilterConfiguration
}

type FilterApplication<'InputEvent, 'OutputEvent> = internal FilterApplication of Result<FilterApplicationParts<'InputEvent, 'OutputEvent>, FilterApplicationError>

module FilterApplication =
    let application { Application = application } = application
