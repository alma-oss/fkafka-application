namespace KafkaApplication.Deriver

module DeriverRunner =
    open KafkaApplication
    open KafkaApplication.Pattern

    let runDeriver<'InputEvent, 'OutputEvent> run (DeriverApplication application: DeriverApplication<'InputEvent, 'OutputEvent>): unit =
        let beforeRun _ = ignore

        application
        |> PatternRunner.runPattern "Deriver" DeriverApplication.application beforeRun run
