namespace KafkaApplication

open Kafka
open KafkaApplication
open ServiceIdentification
open Metrics
open ContractAggregate.Intent

// Errors

type ApplicationConfigurationError =
    | ConfigurationNotSet
    | AlreadySetConfiguration
    | InvalidConfiguration of KafkaApplicationError

// Common

type internal PatternName = PatternName of string

// Run Patterns

type internal RunPattern<'Pattern, 'InputEvent, 'OutputEvent> = RunKafkaApplication<'InputEvent, 'OutputEvent> -> 'Pattern -> ApplicationShutdown

type PatternRuntimeParts = {
    Logger: ApplicationLogger
    Box: Box
    Environment: Map<string, string>
    IncrementMetric: MetricName -> SimpleDataSetKeys -> unit
    EnableResource: ResourceAvailability -> unit
    DisableResource: ResourceAvailability -> unit
}

[<RequireQualifiedAccess>]
module internal PatternRuntimeParts =
    let fromConsumeParts<'OutputEvent> (consumeRuntimeParts: ConsumeRuntimeParts<'OutputEvent>) =
        {
            Logger = consumeRuntimeParts.Logger
            Box = consumeRuntimeParts.Box
            Environment = consumeRuntimeParts.Environment
            IncrementMetric = consumeRuntimeParts.IncrementMetric
            EnableResource = consumeRuntimeParts.EnableResource
            DisableResource = consumeRuntimeParts.DisableResource
        }

[<RequireQualifiedAccess>]
module internal PatternRunner =
    let runPattern<'PatternParts, 'PatternError, 'InputEvent, 'OutputEvent>
        (PatternName pattern)
        (getKafkaApplication: 'PatternParts -> KafkaApplication<'InputEvent,'OutputEvent>)
        (beforeRun: 'PatternParts -> BeforeRun<'InputEvent, 'OutputEvent>)
        (run: RunKafkaApplication<'InputEvent, 'OutputEvent>)
        (patternApplication: Result<'PatternParts, 'PatternError>) =

        match patternApplication with
        | Ok patternParts ->
            patternParts
            |> getKafkaApplication
            |> run (beforeRun patternParts)
        | Error error ->
            error
            |> logApplicationError (sprintf "%s Application" pattern)
            |> WithError

// Build Patterns

type internal GetConfiguration<'PatternParts, 'InputEvent, 'OutputEvent> = 'PatternParts -> Configuration<'InputEvent, 'OutputEvent> option
type internal DebugConfiguration<'PatternParts, 'InputEvent, 'OutputEvent> = PatternName -> GetConfiguration<'PatternParts, 'InputEvent, 'OutputEvent> -> 'PatternParts -> unit

module internal PatternBuilder =
    open ApplicationBuilder
    open OptionOperators

    let debugPatternConfiguration: DebugConfiguration<'PatternParts, 'InputEvent, 'OutputEvent> =
        fun (PatternName pattern) getConfiguration patternParts ->
            patternParts
            |> getConfiguration
            |>! fun configuration ->
                configuration <!> tee (fun parts ->
                    patternParts
                    |> sprintf "%A"
                    |> parts.Logger.Debug pattern
                )
                |> ignore

// Pattern Metrics

type InputOrOutputEvent<'InputEvent, 'OutputEvent> =
    | Input of 'InputEvent
    | Output of 'OutputEvent

type CreateCustomValues<'InputEvent, 'OutputEvent> = InputOrOutputEvent<'InputEvent, 'OutputEvent> -> (string * string) list
type GetCommonEvent<'InputEvent, 'OutputEvent> = InputOrOutputEvent<'InputEvent, 'OutputEvent> -> CommonEvent
type GetIntent<'InputEvent> = 'InputEvent -> Intent option

module internal PatternMetrics =
    let createKeysForInputEvent<'InputEvent, 'OutputEvent>
        (createCustomValues: CreateCustomValues<'InputEvent, 'OutputEvent>)
        (getCommonEvent: GetCommonEvent<'InputEvent, 'OutputEvent>)
        (InputStreamName inputStream)
        event =

        let event = Input event
        let commonEvent = event |> getCommonEvent

        [
            ("event", commonEvent.Event |> EventName.value)
            ("input_stream", inputStream |> StreamName.value)
        ]
        @ createCustomValues event
        |> List.distinctBy fst
        |> SimpleDataSetKeys

    let createKeysForOutputEvent<'InputEvent, 'OutputEvent>
        (createCustomValues: CreateCustomValues<'InputEvent, 'OutputEvent>)
        (getCommonEvent: GetCommonEvent<'InputEvent, 'OutputEvent>)
        (OutputStreamName outputStream)
        event =

        let event = Output event
        let commonEvent = event |> getCommonEvent

        [
            ("event", commonEvent.Event |> EventName.value)
            ("output_stream", outputStream |> StreamName.value)
        ]
        @ createCustomValues event
        |> List.distinctBy fst
        |> SimpleDataSetKeys
