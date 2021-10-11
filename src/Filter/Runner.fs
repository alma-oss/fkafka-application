namespace Lmc.KafkaApplication.Filter

module internal FilterRunner =
    open Microsoft.Extensions.Logging
    open Lmc.KafkaApplication

    let runFilter: RunPattern<FilterApplication<'InputEvent, 'OutputEvent>, 'InputEvent, 'OutputEvent> =
        fun run (FilterApplication application) ->
            let beforeRun filterApplication: BeforeRun<'InputEvent, 'OutputEvent> =
                fun app ->
                    app.LoggerFactory
                        .CreateLogger("KafkaApplication.Filter")
                        .LogDebug("Configuration: {configuration}", filterApplication.FilterConfiguration)

            application
            |> PatternRunner.runPattern (PatternName "Filter") FilterApplication.application beforeRun run
