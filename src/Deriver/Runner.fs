namespace KafkaApplication.Deriver

module DeriverRunner =
    open KafkaApplication

    let runDeriver: RunPattern<DeriverApplication<'InputEvent, 'OutputEvent>, 'InputEvent, 'OutputEvent> =
        fun run (DeriverApplication application) ->
            let beforeRun _: BeforeRun<'InputEvent, 'OutputEvent> =
                ignore

            application
            |> PatternRunner.runPattern (PatternName "Deriver") DeriverApplication.application beforeRun run
