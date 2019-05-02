namespace KafkaApplication

[<AutoOpen>]
module KafkaApplication =
    open Kafka
    open ApplicationBuilder
    open ApplicationRunner

    let kafkaApplication<'Event> =
        let buildApplication: Configuration<'Event> -> KafkaApplication<'Event> = KafkaApplicationBuilder.buildApplication Producer.createProducer Producer.produce
        KafkaApplicationBuilder(buildApplication)

    let partialKafkaApplication<'Event> =
        let id: Configuration<'Event> -> Configuration<'Event> = id
        KafkaApplicationBuilder(id)

    let run (KafkaApplication application) =
        let consume configuration =
            Consumer.consume configuration RawEvent.parse       // todo - what with parse?

        let consumeLast configuration =
            Consumer.consumeLast configuration RawEvent.parse   // todo - what with parse?

        match application with
        | Ok app -> runApplication consume consumeLast Producer.produceSingle Producer.TopicProducer.flush Producer.TopicProducer.close app
        | Error error -> failwithf "[Application] Error:\n%A" error

    // todo - remove
    let _runDummy (kafka_consume: ConsumerConfiguration -> 'Event seq) (kafka_consumeLast: ConsumerConfiguration -> 'Event option) (KafkaApplication application) =
        match application with
        | Ok app -> runApplication kafka_consume kafka_consumeLast Producer.produceSingle Producer.TopicProducer.flush Producer.TopicProducer.close app
        | Error error -> failwithf "[Application] Error:\n%A" error
