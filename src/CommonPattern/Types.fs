namespace KafkaApplication.Pattern

open KafkaApplication

// Errors

type ApplicationConfigurationError =
    | ConfigurationNotSet
    | AlreadySetConfiguration
    | InvalidConfiguration of KafkaApplicationError

module internal PatternRunner =
    let runPattern<'PatternParts, 'PatternError, 'InputEvent, 'OutputEvent>
        pattern
        (getKafkaApplication: 'PatternParts -> KafkaApplication<'InputEvent,'OutputEvent>)
        (beforeRun: 'PatternParts -> KafkaApplicationParts<'InputEvent, 'OutputEvent> -> unit)
        (run: (KafkaApplicationParts<'InputEvent, 'OutputEvent> -> unit) -> KafkaApplication<'InputEvent, 'OutputEvent> -> unit)
        (patternApplication: Result<'PatternParts, 'PatternError>) =

        match patternApplication with
        | Ok patternParts ->
            patternParts
            |> getKafkaApplication
            |> run (beforeRun patternParts)
        | Error error -> failwithf "[%s Application] Error:\n%A" pattern error
