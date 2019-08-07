namespace KafkaApplication.Router

module internal ContentBasedRouterRunner =
    open KafkaApplication

    let runRouter: RunPattern<ContentBasedRouterApplication<'InputEvent, 'OutputEvent>, 'InputEvent, 'OutputEvent> =
        fun run (ContentBasedRouterApplication application) ->
            let beforeRun routerApplication: BeforeRun<'InputEvent, 'OutputEvent> =
                fun app ->
                    routerApplication.RouterConfiguration
                    |> sprintf "%A"
                    |> app.Logger.VeryVerbose "Router"

            application
            |> PatternRunner.runPattern (PatternName "Router") ContentBasedRouterApplication.application beforeRun run
