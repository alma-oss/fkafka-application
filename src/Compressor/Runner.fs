namespace Alma.KafkaApplication.Compressor

module internal CompressorRunner =
    open System
    open Microsoft.Extensions.Logging
    open Alma.KafkaApplication
    open Alma.ErrorHandling
    open Alma.Kafka

    let runCompressor: RunPattern<CompressorApplication<'InputEvent, 'OutputEvent, 'Dependencies>, 'InputEvent, 'OutputEvent, 'Dependencies> =
        fun run (CompressorApplication application) ->
            let beforeStart _ = BeforeStart.empty
            let beforeRun compressorApplication: BeforeRun<'OutputEvent, 'Dependencies> = BeforeRun (Some (fun runtimeParts ->
                let logger = compressorApplication.Application.LoggerFactory.CreateLogger "Compressor.BeforeRun"

                match compressorApplication.GetOffset runtimeParts with
                | None ->
                    logger.LogDebug "No GetCheckpoint provided, running with default configuration."
                    runtimeParts
                | Some getOffset ->
                    logger.LogDebug "GetCheckpoint provided, updating default consumer configuration."
                    let updatedConfiguration = { runtimeParts.ConsumerConfigurations[CompressorBuilder.connection] with GetCheckpoint = Some getOffset }

                    { runtimeParts with
                        ConsumerConfigurations = runtimeParts.ConsumerConfigurations.Add(CompressorBuilder.connection, updatedConfiguration)
                        StoreCurrentOffsetInternally = true
                    }
            ))

            application
            |> PatternRunner.runPattern CompressorBuilder.pattern CompressorApplication.application beforeStart beforeRun run
