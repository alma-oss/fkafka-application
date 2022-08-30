namespace Lmc.KafkaApplication.Deriver

module internal DeriverRunner =
    open Lmc.KafkaApplication

    let runDeriver: RunPattern<DeriverApplication<'InputEvent, 'OutputEvent, 'Dependencies>, 'InputEvent, 'OutputEvent, 'Dependencies> =
        fun run (DeriverApplication application) ->
            let beforeRun _: BeforeRun<'InputEvent, 'OutputEvent, 'Dependencies> =
                ignore

            application
            |> PatternRunner.runPattern DeriverBuilder.pattern DeriverApplication.application beforeRun run
