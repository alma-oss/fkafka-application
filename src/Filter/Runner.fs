namespace Lmc.KafkaApplication.Filter

module internal FilterRunner =
    open Lmc.KafkaApplication

    let runFilter: RunPattern<FilterApplication<'InputEvent, 'OutputEvent>, 'InputEvent, 'OutputEvent> =
        fun run (FilterApplication application) ->
            let beforeRun filterApplication: BeforeRun<'InputEvent, 'OutputEvent> =
                fun app ->
                    filterApplication.FilterConfiguration
                    |> sprintf "%A"
                    |> app.Logger.VeryVerbose "Filter"

            application
            |> PatternRunner.runPattern (PatternName "Filter") FilterApplication.application beforeRun run
