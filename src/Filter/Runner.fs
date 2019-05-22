namespace KafkaApplication.Filter

module FilterRunner =
    open KafkaApplication

    let runFilter<'InputEvent, 'OutputEvent> run (FilterApplication application: FilterApplication<'InputEvent, 'OutputEvent>): unit =
        match application with
        | Ok filterApplication ->
            let (KafkaApplication kafkaApplication) = filterApplication.Application

            match kafkaApplication with
            | Ok app ->
                // log filter configuration
                app.Logger.VeryVerbose "Filter" (sprintf "%A" filterApplication.FilterConfiguration)

                // run filter application
                filterApplication.Application |> run
            | Error error -> failwithf "[Filter Application] Error:\n%A" error
        | Error error -> failwithf "[Filter Application] Error:\n%A" error
