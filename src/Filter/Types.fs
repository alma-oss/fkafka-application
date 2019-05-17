namespace KafkaApplication.Filter

open KafkaApplication
open ServiceIdentification
open Kafka

// Errors

type ApplicationConfigurationError =
    | ConfigurationNotSet
    | AlreadySetConfiguration
    | InvalidConfiguration of KafkaApplicationError

type FilterConfigurationError =
    | NotFound of string
    | NotSet
    | MissingOutputStream
    | MissingFilterContent
    | MissingGetCommonEventData

type FilterApplicationError =
    | ApplicationConfigurationError of ApplicationConfigurationError
    | FilterConfigurationError of FilterConfigurationError

// Filter configuration

type FilterConfiguration = {
    Spots: Spot list
}

type InputOrOutputEvent<'InputEvent, 'OutputEvent> =
    | Input of 'InputEvent
    | Output of 'OutputEvent

type CommonEventData = {
    Event: EventName
    Spot: Spot
}

type FilterContent<'InputEvent, 'OutputEvent> = 'InputEvent -> 'OutputEvent list

type GetCommonEventData<'InputEvent, 'OutputEvent> = InputOrOutputEvent<'InputEvent, 'OutputEvent> -> CommonEventData

// Filter Application Configuration

type FilterParts<'InputEvent, 'OutputEvent> = {
    Configuration: Configuration<'InputEvent, 'OutputEvent> option
    FilterConfiguration: FilterConfiguration option
    FilterTo: ConnectionName option
    FilterContent: FilterContent<'InputEvent, 'OutputEvent> option  // todo - maybe it should be just `input to output` or something more generic, because filtering a content is just an option
    GetCommonEventData: GetCommonEventData<'InputEvent, 'OutputEvent> option
}

module FilterParts =
    let defaultFilter = {
        Configuration = None
        FilterConfiguration = None
        FilterTo = None
        FilterContent = None
        GetCommonEventData = None
    }

type FilterApplicationConfiguration<'InputEvent, 'OutputEvent> = private FilterApplicationConfiguration of Result<FilterParts<'InputEvent, 'OutputEvent>, FilterApplicationError>

type FilterApplicationParts<'InputEvent, 'OutputEvent> = {
    Application: KafkaApplication<'InputEvent, 'OutputEvent>
    FilterConfiguration: FilterConfiguration
}

type FilterApplication<'InputEvent, 'OutputEvent> = internal FilterApplication of Result<FilterApplicationParts<'InputEvent, 'OutputEvent>, FilterApplicationError>
