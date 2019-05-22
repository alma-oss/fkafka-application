namespace KafkaApplication.Pattern

open KafkaApplication

// Errors

type ApplicationConfigurationError =
    | ConfigurationNotSet
    | AlreadySetConfiguration
    | InvalidConfiguration of KafkaApplicationError

// Run Patterns

type RunPatternApplication<'InputEvent, 'OutputEvent> = BeforeRun<'InputEvent, 'OutputEvent> -> Run<'InputEvent, 'OutputEvent>
type RunPattern<'Pattern, 'InputEvent, 'OutputEvent> = RunPatternApplication<'InputEvent, 'OutputEvent> -> 'Pattern -> unit

module internal PatternRunner =
    let runPattern<'PatternParts, 'PatternError, 'InputEvent, 'OutputEvent>
        pattern
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
