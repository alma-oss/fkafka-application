namespace Lmc.KafkaApplication

open Microsoft.Extensions.Logging
open Lmc.Kafka
open Lmc.KafkaApplication
open Lmc.ServiceIdentification
open Lmc.Metrics

// Errors

type ApplicationConfigurationError =
    | ConfigurationNotSet
    | AlreadySetConfiguration
    | InvalidConfiguration of KafkaApplicationError

// Common

type internal PatternName = PatternName of string

// Run Patterns

type internal RunPattern<'Pattern, 'InputEvent, 'OutputEvent, 'Dependencies> = RunKafkaApplication<'InputEvent, 'OutputEvent, 'Dependencies> -> 'Pattern -> ApplicationShutdown

/// Application pattern parts exposed in handlers
type PatternRuntimeParts = {
    LoggerFactory: ILoggerFactory
    Box: Box
    Environment: Map<string, string>
    IncrementMetric: MetricName -> SimpleDataSetKeys -> unit
    SetMetric: MetricName -> SimpleDataSetKeys -> MetricValue -> unit
    EnableResource: ResourceAvailability -> unit
    DisableResource: ResourceAvailability -> unit
}

[<AutoOpen>]
module internal PatternUtils =
    let patternLogger (PatternName name) (loggerFactory: ILoggerFactory) =
        LoggerFactory.createLogger loggerFactory ($"KafkaApplication.Pattern<{name}>")

[<RequireQualifiedAccess>]
module internal PatternRuntimeParts =
    let fromConsumeParts<'OutputEvent, 'Dependencies> patternName (consumeRuntimeParts: ConsumeRuntimeParts<'OutputEvent, 'Dependencies>) =
        {
            LoggerFactory = consumeRuntimeParts.LoggerFactory
            Box = consumeRuntimeParts.Box
            Environment = consumeRuntimeParts.Environment
            IncrementMetric = consumeRuntimeParts.IncrementMetric
            SetMetric = consumeRuntimeParts.SetMetric
            EnableResource = consumeRuntimeParts.EnableResource
            DisableResource = consumeRuntimeParts.DisableResource
        }

[<RequireQualifiedAccess>]
module internal PatternRunner =
    let runPattern<'PatternParts, 'PatternError, 'InputEvent, 'OutputEvent, 'Dependencies>
        (PatternName pattern)
        (getKafkaApplication: 'PatternParts -> KafkaApplication<'InputEvent,'OutputEvent, 'Dependencies>)
        (beforeRun: 'PatternParts -> BeforeRun<'InputEvent, 'OutputEvent, 'Dependencies>)
        (run: RunKafkaApplication<'InputEvent, 'OutputEvent, 'Dependencies>)
        (patternApplication: Result<'PatternParts, 'PatternError>) =

        match patternApplication with
        | Ok patternParts ->
            patternParts
            |> getKafkaApplication
            |> run (beforeRun patternParts)
        | Error error ->
            error
            |> sprintf "[Critical Error] %s Application cannot start because of %A" pattern
            |> ErrorMessage
            |> WithCriticalError

// Build Patterns

type internal GetConfiguration<'PatternParts, 'InputEvent, 'OutputEvent, 'Dependencies> = 'PatternParts -> Configuration<'InputEvent, 'OutputEvent, 'Dependencies> option
type internal DebugConfiguration<'PatternParts, 'InputEvent, 'OutputEvent, 'Dependencies> = PatternName -> GetConfiguration<'PatternParts, 'InputEvent, 'OutputEvent, 'Dependencies> -> 'PatternParts -> unit

module internal PatternBuilder =
    open Lmc.ErrorHandling.Option.Operators
    open ApplicationBuilder

    let debugPatternConfiguration: DebugConfiguration<'PatternParts, 'InputEvent, 'OutputEvent, 'Dependencies> =
        fun pattern getConfiguration patternParts ->
            patternParts
            |> getConfiguration
            |>! fun configuration ->
                configuration <!> tee (fun parts ->
                    (patternLogger pattern parts.LoggerFactory)
                        .LogTrace("Configuration: {configuration}", patternParts)
                )
                |> ignore

// Pattern Metrics

type InputOrOutputEvent<'InputEvent, 'OutputEvent> =
    | Input of 'InputEvent
    | Output of 'OutputEvent

type CreateCustomValues<'InputEvent, 'OutputEvent> = InputOrOutputEvent<'InputEvent, 'OutputEvent> -> (string * string) list
type GetCommonEvent<'InputEvent, 'OutputEvent> = InputOrOutputEvent<'InputEvent, 'OutputEvent> -> CommonEvent
type GetFilterValue<'InputEvent, 'FilterValue> = 'InputEvent -> 'FilterValue option

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
