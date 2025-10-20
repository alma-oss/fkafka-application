namespace Alma.KafkaApplication.Router

module internal ContentBasedRouterRunner =
    open Microsoft.Extensions.Logging
    open Alma.KafkaApplication

    let runRouter: RunPattern<ContentBasedRouterApplication<'InputEvent, 'OutputEvent, 'Dependencies>, 'InputEvent, 'OutputEvent, 'Dependencies> =
        fun run (ContentBasedRouterApplication application) ->
            let beforeStart routerApplication = BeforeStart (fun app ->
                (patternLogger ContentBasedRouterBuilder.pattern app.LoggerFactory)
                    .LogDebug("Configuration: {configuration}", routerApplication.RouterConfiguration)
            )

            let beforeRun _ = BeforeRun.empty

            application
            |> PatternRunner.runPattern ContentBasedRouterBuilder.pattern ContentBasedRouterApplication.application beforeStart beforeRun run
