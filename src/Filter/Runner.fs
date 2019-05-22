namespace KafkaApplication.Filter

module FilterRunner =
    open KafkaApplication
    open KafkaApplication.Pattern

    let runFilter: RunPattern<FilterApplication<'InputEvent, 'OutputEvent>, 'InputEvent, 'OutputEvent> =
        fun run (FilterApplication application) ->
            let beforeRun filterApplication app =
                filterApplication.FilterConfiguration
                |> sprintf "%A"
                |> app.Logger.VeryVerbose "Filter"

            application
            |> PatternRunner.runPattern (PatternName "Filter") FilterApplication.application beforeRun run
