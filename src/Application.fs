namespace KafkaApplication

[<AutoOpen>]
module KafkaApplication =
    open Kafka
    open ApplicationBuilder
    open ApplicationRunner
    open KafkaApplication.Filter
    open KafkaApplication.Filter.FilterBuilder

    //
    // Build applications
    //

    let kafkaApplication<'InputEvent, 'OutputEvent> =
        let buildApplication: Configuration<'InputEvent, 'OutputEvent> -> KafkaApplication<'InputEvent, 'OutputEvent> = KafkaApplicationBuilder.buildApplication Producer.prepareProducer Producer.produce
        KafkaApplicationBuilder(buildApplication)

    let partialKafkaApplication<'InputEvent, 'OutputEvent> =
        let id: Configuration<'InputEvent, 'OutputEvent> -> Configuration<'InputEvent, 'OutputEvent> = id
        KafkaApplicationBuilder(id)

    let filter<'InputEvent, 'OutputEvent> =
        let buildApplication: Configuration<'InputEvent, 'OutputEvent> -> KafkaApplication<'InputEvent, 'OutputEvent> = KafkaApplicationBuilder.buildApplication Producer.prepareProducer Producer.produce
        let buildFilter: FilterApplicationConfiguration<'InputEvent, 'OutputEvent> -> FilterApplication<'InputEvent, 'OutputEvent> = FilterApplicationBuilder.buildFilter buildApplication
        FilterBuilder(buildFilter)

    //
    // Run applications
    //

    let run<'InputEvent, 'OutputEvent>
        (parseEvent: ParseEvent<'InputEvent>)
        (KafkaApplication application: KafkaApplication<'InputEvent, 'OutputEvent>) =

        let consume configuration =
            Consumer.consume configuration parseEvent

        let consumeLast configuration =
            Consumer.consumeLast configuration parseEvent

        match application with
        | Ok app ->
            runApplication
                consume
                consumeLast
                Producer.connect
                Producer.produceSingle
                Producer.TopicProducer.flush
                Producer.TopicProducer.close
                app
        | Error error -> failwithf "[Application] Error:\n%A" error

    let runFilter<'InputEvent, 'OutputEvent>
        (parseEvent: ParseEvent<'InputEvent>)
        (filterApplication: FilterApplication<'InputEvent, 'OutputEvent>) =
        FilterRunner.runFilter (run parseEvent) filterApplication

    // todo - remove
    let _runDummy (kafka_consume: ConsumerConfiguration -> 'InputEvent seq) (kafka_consumeLast: ConsumerConfiguration -> 'InputEvent option) (KafkaApplication application) =
        match application with
        | Ok app -> runApplication kafka_consume kafka_consumeLast Producer.connect Producer.produceSingle Producer.TopicProducer.flush Producer.TopicProducer.close app
        | Error error -> failwithf "[Application] Error:\n%A" error
