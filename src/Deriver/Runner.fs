namespace Lmc.KafkaApplication.Deriver

module internal DeriverRunner =
    open Lmc.KafkaApplication

    let runDeriver: RunPattern<DeriverApplication<'InputEvent, 'OutputEvent>, 'InputEvent, 'OutputEvent> =
        fun run (DeriverApplication application) ->
            let beforeRun _: BeforeRun<'InputEvent, 'OutputEvent> =
                ignore

            application
            |> PatternRunner.runPattern DeriverBuilder.pattern DeriverApplication.application beforeRun run
