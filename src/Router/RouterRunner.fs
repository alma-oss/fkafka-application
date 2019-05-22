namespace KafkaApplication.Router

module ContentBasedRouterRunner =
    open KafkaApplication

    let runRouter<'InputEvent, 'OutputEvent> run (ContentBasedRouterApplication application: ContentBasedRouterApplication<'InputEvent, 'OutputEvent>): unit =
        match application with
        | Ok filterApplication ->   // todo - this could be common too
            let (KafkaApplication kafkaApplication) = filterApplication.Application

            match kafkaApplication with
            | Ok app ->
                // log filter configuration
                app.Logger.VeryVerbose "Router" (sprintf "%A" filterApplication.RouterConfiguration)

                // run router application
                filterApplication.Application |> run
            | Error error -> failwithf "[Router Application] Error:\n%A" error
        | Error error -> failwithf "[Router Application] Error:\n%A" error
