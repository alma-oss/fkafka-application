namespace KafkaApplication.Router

module ContentBasedRouterRunner =
    open KafkaApplication
    open KafkaApplication.Pattern

    let runRouter<'InputEvent, 'OutputEvent> run (ContentBasedRouterApplication application: ContentBasedRouterApplication<'InputEvent, 'OutputEvent>): unit =
        let beforeRun routerApplication app =
            routerApplication.RouterConfiguration
            |> sprintf "%A"
            |> app.Logger.VeryVerbose "Router"

        application
        |> PatternRunner.runPattern "Router" ContentBasedRouterApplication.application beforeRun run
