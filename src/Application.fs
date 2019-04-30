namespace KafkaApplication

[<AutoOpen>]
module KafkaApplication =
    open Kafka
    open ApplicationBuilder
    open ApplicationRunner

    let kafkaApplication = KafkaApplicationBuilder()

    let run (KafkaApplication application) =
        let consume configuration =
            Consumer.consume configuration RawEvent.parse       // todo - what with parse?

        let consumeLast configuration =
            Consumer.consumeLast configuration RawEvent.parse   // todo - what with parse?

        match application with
        | Ok app -> runApplication consume consumeLast app
        | Error error -> failwithf "[Application] Error:\n%A" error

    // todo - remove
    let _runDummy (kafka_consume: ConsumerConfiguration -> 'Event seq) (kafka_consumeLast: ConsumerConfiguration -> 'Event option) (KafkaApplication application) =
        match application with
        | Ok app -> runApplication kafka_consume kafka_consumeLast app
        | Error error -> failwithf "[Application] Error:\n%A" error
