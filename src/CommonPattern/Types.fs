namespace KafkaApplication.Pattern

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

module internal PatternBuilder =
    open ApplicationBuilder
    open OptionOperators

    type DebugConfiguration<'PatternParts, 'InputEvent, 'OutputEvent> = PatternName -> ('PatternParts -> Configuration<'InputEvent, 'OutputEvent> option) -> 'PatternParts -> unit

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
