namespace Alma.KafkaApplication.Filter

module internal FilterRunner =
    open Microsoft.Extensions.Logging
    open Alma.KafkaApplication

    let runFilter: RunPattern<FilterApplication<'InputEvent, 'OutputEvent, 'Dependencies, 'FilterValue>, 'InputEvent, 'OutputEvent, 'Dependencies> =
        fun run (FilterApplication application) ->
            let beforeStart filterApplication = BeforeStart (fun app ->
                (patternLogger FilterBuilder.pattern app.LoggerFactory)
                    .LogDebug("Configuration: {configuration}", filterApplication.FilterConfiguration)
            )

            let beforeRun _ = BeforeRun.empty

            application
            |> PatternRunner.runPattern FilterBuilder.pattern FilterApplication.application beforeStart beforeRun run
