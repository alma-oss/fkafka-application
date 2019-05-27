namespace KafkaApplication

open Kafka
open KafkaApplication
open ConsentEvents.Intent

// Errors

type ApplicationConfigurationError =
    | ConfigurationNotSet
    | AlreadySetConfiguration
    | InvalidConfiguration of KafkaApplicationError

// Common

type PatternName = PatternName of string

// Run Patterns

type RunPattern<'Pattern, 'InputEvent, 'OutputEvent> = RunKafkaApplication<'InputEvent, 'OutputEvent> -> 'Pattern -> ApplicationShutdown

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

type CreateCustomValues<'InputEvent, 'OutputEvent> = InputOrOutputEvent<'InputEvent, 'OutputEvent> -> (string * string) list
type GetCommonEvent<'InputEvent, 'OutputEvent> = InputOrOutputEvent<'InputEvent, 'OutputEvent> -> CommonEvent
type GetIntent<'InputEvent> = 'InputEvent -> Intent option

module internal PatternMetrics =
    let createKeysForInputEvent<'InputEvent, 'OutputEvent>
        (createCustomValues: CreateCustomValues<'InputEvent, 'OutputEvent>)
        (getCommonEvent: GetCommonEvent<'InputEvent, 'OutputEvent>)
        (InputStreamName (StreamName inputStream))
        event =
        let event = Input event
        let commonEvent = event |> getCommonEvent

        [
            ("event", commonEvent.Event |> EventName.value)
            ("input_stream", inputStream)
        ]
        @ createCustomValues event
        |> List.distinctBy fst
        |> SimpleDataSetKeys

    let createKeysForOutputEvent<'InputEvent, 'OutputEvent>
        (createCustomValues: CreateCustomValues<'InputEvent, 'OutputEvent>)
        (getCommonEvent: GetCommonEvent<'InputEvent, 'OutputEvent>)
        (OutputStreamName (StreamName outputStream))
        event =
        let event = Output event
        let commonEvent = event |> getCommonEvent

        [
            ("event", commonEvent.Event |> EventName.value)
            ("output_stream", outputStream)
        ]
        @ createCustomValues event
        |> List.distinctBy fst
        |> SimpleDataSetKeys
