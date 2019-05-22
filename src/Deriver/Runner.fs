namespace KafkaApplication.Deriver

module DeriverRunner =
    open KafkaApplication
    open KafkaApplication.Pattern

    let runDeriver: RunPattern<DeriverApplication<'InputEvent, 'OutputEvent>, 'InputEvent, 'OutputEvent> =
        fun run (DeriverApplication application) ->
            let beforeRun _ = ignore

            application
            |> PatternRunner.runPattern "Deriver" DeriverApplication.application beforeRun run
