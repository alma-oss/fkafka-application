namespace KafkaApplication

open Kafka
open KafkaApplication

// Errors

type ApplicationConfigurationError =
    | ConfigurationNotSet
    | AlreadySetConfiguration
    | InvalidConfiguration of KafkaApplicationError

// Common

type PatternName = PatternName of string

// Run Patterns

type RunPatternApplication<'InputEvent, 'OutputEvent> = BeforeRun<'InputEvent, 'OutputEvent> -> Run<'InputEvent, 'OutputEvent>
type RunPattern<'Pattern, 'InputEvent, 'OutputEvent> = RunPatternApplication<'InputEvent, 'OutputEvent> -> 'Pattern -> unit

module internal PatternRunner =
    let runPattern<'PatternParts, 'PatternError, 'InputEvent, 'OutputEvent>
        (PatternName pattern)
        (getKafkaApplication: 'PatternParts -> KafkaApplication<'InputEvent,'OutputEvent>)
        (beforeRun: 'PatternParts -> BeforeRun<'InputEvent, 'OutputEvent>)
        (run: RunPatternApplication<'InputEvent, 'OutputEvent>)
        (patternApplication: Result<'PatternParts, 'PatternError>) =

        match patternApplication with
        | Ok patternParts ->
            patternParts
            |> getKafkaApplication
            |> run (beforeRun patternParts)
        | Error error -> failwithf "[%s Application] Error:\n%A" pattern error

// Build Patterns

type GetConfiguration<'PatternParts, 'InputEvent, 'OutputEvent> = 'PatternParts -> Configuration<'InputEvent, 'OutputEvent> option
type DebugConfiguration<'PatternParts, 'InputEvent, 'OutputEvent> = PatternName -> GetConfiguration<'PatternParts, 'InputEvent, 'OutputEvent> -> 'PatternParts -> unit

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

type GetCommonEvent<'InputEvent, 'OutputEvent> = InputOrOutputEvent<'InputEvent, 'OutputEvent> -> CommonEvent

module internal PatternMetrics =
    let createKeysForInputEvent<'InputEvent, 'OutputEvent> (getCommonEvent: GetCommonEvent<'InputEvent, 'OutputEvent>) (InputStreamName (StreamName inputStream)) event =
        let commonEvent = event |> Input |> getCommonEvent

        SimpleDataSetKeys [
            ("event", commonEvent.Event |> EventName.value)
            ("input_stream", inputStream)
        ]

    let createKeysForOutputEvent<'InputEvent, 'OutputEvent> (getCommonEvent: GetCommonEvent<'InputEvent, 'OutputEvent>) (OutputStreamName (StreamName outputStream)) event =
        let commonData = event |> Output |> getCommonEvent

        SimpleDataSetKeys [
            ("event", commonData.Event |> EventName.value)
            ("output_stream", outputStream)
        ]
