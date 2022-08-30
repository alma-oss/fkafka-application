namespace Lmc.KafkaApplication.Router

module internal ContentBasedRouterRunner =
    open Microsoft.Extensions.Logging
    open Lmc.KafkaApplication

    let runRouter: RunPattern<ContentBasedRouterApplication<'InputEvent, 'OutputEvent, 'Dependencies>, 'InputEvent, 'OutputEvent, 'Dependencies> =
        fun run (ContentBasedRouterApplication application) ->
            let beforeRun routerApplication: BeforeRun<'InputEvent, 'OutputEvent, 'Dependencies> =
                fun app ->
                    (patternLogger ContentBasedRouterBuilder.pattern app.LoggerFactory)
                        .LogDebug("Configuration: {configuration}", routerApplication.RouterConfiguration)

            application
            |> PatternRunner.runPattern ContentBasedRouterBuilder.pattern ContentBasedRouterApplication.application beforeRun run
