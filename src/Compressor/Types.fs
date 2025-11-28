namespace Alma.KafkaApplication.Compressor

open Alma.KafkaApplication
open Alma.Kafka
open Alma.Kafka.MetaData
open Feather.ErrorHandling

// Errors

type CompressorConfigurationError =
    | NotFound of string
    | NotSet
    | MissingBatchThreshold
    | MissingPickEvent
    | MissingSendBatch
    | IncompleteOffsetHandlers

type CompressorApplicationError =
    | ApplicationConfigurationError of ApplicationConfigurationError
    | KafkaApplicationError of KafkaApplicationError
    | CompressorConfigurationError of CompressorConfigurationError
    | BatchThresholdVariableNotSet of string
    | InvalidBatchThresholdVariable of string
    | InvalidBatchThresholdValue of int

// Compressor configuration

type PickEvent<'InputEvent, 'OutputEvent> = ProcessedBy -> TracedEvent<'InputEvent> -> TracedEvent<'OutputEvent> option
type PickEventResult<'InputEvent, 'OutputEvent> = ProcessedBy -> TracedEvent<'InputEvent> -> Result<TracedEvent<'OutputEvent> option, ErrorMessage>
type PickEventAsyncResult<'InputEvent, 'OutputEvent> = ProcessedBy -> TracedEvent<'InputEvent> -> AsyncResult<TracedEvent<'OutputEvent> option, ErrorMessage>

type internal PickInputAsOutputEvent<'InputEvent, 'OutputEvent> =
    | PickEvent of PickEvent<'InputEvent, 'OutputEvent>
    | PickEventResult of PickEventResult<'InputEvent, 'OutputEvent>
    | PickEventAsyncResult of PickEventAsyncResult<'InputEvent, 'OutputEvent>

type internal PickEventHandler<'InputEvent, 'OutputEvent, 'Dependencies> =
    | Simple of PickInputAsOutputEvent<'InputEvent, 'OutputEvent>
    | WithApplication of (PatternRuntimeParts<'Dependencies> -> PickInputAsOutputEvent<'InputEvent, 'OutputEvent>)

// Offset Access Configuration

type SetOffset = GroupId -> TopicPartitionOffset -> AsyncResult<unit, ErrorMessage>
type GetOffset = Alma.Kafka.GetCheckpoint

[<RequireQualifiedAccess>]
module internal SetOffset =
    let ignore: SetOffset = fun _ _ -> AsyncResult.ofSuccess ()

[<RequireQualifiedAccess>]
type internal SetOffsetHandler<'Dependencies> =
    | Simple of SetOffset
    | WithApplication of (PatternRuntimeParts<'Dependencies> -> SetOffset)

[<RequireQualifiedAccess>]
type internal GetOffsetHandler<'Dependencies> =
    | Simple of GetOffset
    | WithApplication of (PatternRuntimeParts<'Dependencies> -> GetOffset)

// Send Batch Configuration

type SendBatch<'OutputEvent> = CompressedBatch<TracedEvent<'OutputEvent>> -> AsyncResult<unit, ErrorMessage>

[<RequireQualifiedAccess>]
type internal SendBatchHandler<'OutputEvent, 'Dependencies> =
    | Simple of SendBatch<'OutputEvent>
    | WithApplication of (PatternRuntimeParts<'Dependencies> -> SendBatch<'OutputEvent>)

// Compressor Application Configuration

type internal CompressorParts<'InputEvent, 'OutputEvent, 'Dependencies> = {
    Configuration: Configuration<'InputEvent, 'OutputEvent, 'Dependencies> option
    BatchSize: BatchThreshold option
    SetOffset: SetOffsetHandler<'Dependencies> option
    GetOffset: GetOffsetHandler<'Dependencies> option
    PickEvent: PickEventHandler<'InputEvent, 'OutputEvent, 'Dependencies> option
    SendBatch: SendBatchHandler<'OutputEvent, 'Dependencies> option
    CreateCustomValues: CreateCustomValues<'InputEvent, 'OutputEvent> option
    GetCommonEvent: GetCommonEvent<'InputEvent, 'OutputEvent> option
}

module internal CompressorParts =
    let defaultCompressor = {
        Configuration = None
        BatchSize = None
        SetOffset = None
        GetOffset = None
        PickEvent = None
        SendBatch = None
        CreateCustomValues = None
        GetCommonEvent = None
    }

type CompressorApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
    private CompressorApplicationConfiguration of Result<CompressorParts<'InputEvent, 'OutputEvent, 'Dependencies>, CompressorApplicationError>

type CompressorApplicationParts<'InputEvent, 'OutputEvent, 'Dependencies> = {
    Application: KafkaApplication<'InputEvent, 'OutputEvent, 'Dependencies>
    GetOffset: ConsumeRuntimeParts<'OutputEvent, 'Dependencies> -> GetOffset option
}

type CompressorApplication<'InputEvent, 'OutputEvent, 'Dependencies> =
    internal CompressorApplication of Result<CompressorApplicationParts<'InputEvent, 'OutputEvent, 'Dependencies>, CompressorApplicationError>

[<RequireQualifiedAccess>]
module internal CompressorApplication =
    let application { Application = application } = application
