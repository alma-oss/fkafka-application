namespace Lmc.KafkaApplication.Router

module internal ContentBasedRouterRunner =
    open Microsoft.Extensions.Logging
    open Lmc.KafkaApplication

    let runRouter: RunPattern<ContentBasedRouterApplication<'InputEvent, 'OutputEvent>, 'InputEvent, 'OutputEvent> =
        fun run (ContentBasedRouterApplication application) ->
            let beforeRun routerApplication: BeforeRun<'InputEvent, 'OutputEvent> =
                fun app ->
                    app.LoggerFactory
                        .CreateLogger("KafkaApplication.Router")
                        .LogDebug("Configuration: {configuration}", routerApplication.RouterConfiguration)

            application
            |> PatternRunner.runPattern (PatternName "Router") ContentBasedRouterApplication.application beforeRun run
