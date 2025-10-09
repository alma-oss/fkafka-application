namespace Alma.KafkaApplication.Deriver

module internal DeriverRunner =
    open Alma.KafkaApplication

    let runDeriver: RunPattern<DeriverApplication<'InputEvent, 'OutputEvent, 'Dependencies>, 'InputEvent, 'OutputEvent, 'Dependencies> =
        fun run (DeriverApplication application) ->
            let beforeStart _ = BeforeStart.empty
            let beforeRun _ = BeforeRun.empty

            application
            |> PatternRunner.runPattern DeriverBuilder.pattern DeriverApplication.application beforeStart beforeRun run
