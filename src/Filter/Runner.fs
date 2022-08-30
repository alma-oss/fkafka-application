namespace Lmc.KafkaApplication.Filter

module internal FilterRunner =
    open Microsoft.Extensions.Logging
    open Lmc.KafkaApplication

    let runFilter: RunPattern<FilterApplication<'InputEvent, 'OutputEvent, 'Dependencies, 'FilterValue>, 'InputEvent, 'OutputEvent, 'Dependencies> =
        fun run (FilterApplication application) ->
            let beforeRun filterApplication: BeforeRun<'InputEvent, 'OutputEvent, 'Dependencies> =
                fun app ->
                    (patternLogger FilterBuilder.pattern app.LoggerFactory)
                        .LogDebug("Configuration: {configuration}", filterApplication.FilterConfiguration)

            application
            |> PatternRunner.runPattern FilterBuilder.pattern FilterApplication.application beforeRun run
