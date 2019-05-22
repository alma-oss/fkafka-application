namespace KafkaApplication.Filter

module FilterRunner =
    open KafkaApplication
    open KafkaApplication.Pattern

    let runFilter<'InputEvent, 'OutputEvent> run (FilterApplication application: FilterApplication<'InputEvent, 'OutputEvent>): unit =
        let beforeRun filterApplication app =
            filterApplication.FilterConfiguration
            |> sprintf "%A"
            |> app.Logger.VeryVerbose "Filter"

        application
        |> PatternRunner.runPattern "Filter" FilterApplication.application beforeRun run
