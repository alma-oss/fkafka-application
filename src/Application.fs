namespace KafkaApplication

[<AutoOpen>]
module KafkaApplication =
    open Kafka
    open ApplicationBuilder
    open ApplicationRunner
    open KafkaApplication.Filter
    open KafkaApplication.Filter.FilterBuilder
    open KafkaApplication.Router
    open KafkaApplication.Router.ContentBasedRouterBuilder

    //
    // Applications
    //

    type Application<'InputEvent, 'OutputEvent> =
        | CustomApplication of KafkaApplication<'InputEvent, 'OutputEvent>
        | FilterContentFilter of FilterApplication<'InputEvent, 'OutputEvent>
        | ContentBasedRouter of ContentBasedRouterApplication<'InputEvent, 'OutputEvent>

    //
    // Build applications
    //

    let kafkaApplication<'InputEvent, 'OutputEvent> =
        let buildApplication: Configuration<'InputEvent, 'OutputEvent> -> KafkaApplication<'InputEvent, 'OutputEvent> = KafkaApplicationBuilder.buildApplication Producer.prepareProducer Producer.produce
        KafkaApplicationBuilder(buildApplication >> CustomApplication)

    let partialKafkaApplication<'InputEvent, 'OutputEvent> =
        let id: Configuration<'InputEvent, 'OutputEvent> -> Configuration<'InputEvent, 'OutputEvent> = id
        KafkaApplicationBuilder(id)

    let filterContentFilter<'InputEvent, 'OutputEvent> =
        let buildApplication: Configuration<'InputEvent, 'OutputEvent> -> KafkaApplication<'InputEvent, 'OutputEvent> = KafkaApplicationBuilder.buildApplication Producer.prepareProducer Producer.produce
        let buildFilter: FilterApplicationConfiguration<'InputEvent, 'OutputEvent> -> FilterApplication<'InputEvent, 'OutputEvent> = FilterApplicationBuilder.buildFilter buildApplication
        FilterBuilder(buildFilter >> FilterContentFilter)

    let contentBasedRouter =
        let buildApplication: Configuration<EventToRoute, EventToRoute> -> KafkaApplication<EventToRoute, EventToRoute> = KafkaApplicationBuilder.buildApplication Producer.prepareProducer Producer.produce
        let buildRouter: ContentBasedRouterApplicationConfiguration<EventToRoute, EventToRoute> -> ContentBasedRouterApplication<EventToRoute, EventToRoute> = ContentBasedRouterApplicationBuilder.build buildApplication
        ContentBasedRouterBuilder(buildRouter >> ContentBasedRouter)

    //
    // Run applications
    //

    let private runKafkaApplication<'InputEvent, 'OutputEvent>
        (KafkaApplication application: KafkaApplication<'InputEvent, 'OutputEvent>)
        (parseEvent: ParseEvent<'InputEvent>) =

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

    let run<'InputEvent, 'OutputEvent>
        (parseEvent: ParseEvent<'InputEvent>)
        (application: Application<'InputEvent, 'OutputEvent>) =
        let runApplication kafkaApplication =
            runKafkaApplication kafkaApplication parseEvent

        match application with
        | CustomApplication kafkaApplication -> runApplication kafkaApplication
        | FilterContentFilter filterApplication -> FilterRunner.runFilter runApplication filterApplication
        | ContentBasedRouter routerApplication -> ContentBasedRouterRunner.runRouter runApplication routerApplication

    let runRouter (application: Application<EventToRoute, EventToRoute>) =
        application |> run EventToRoute.parse

    // todo - remove
    let _runDummy (kafka_consume: ConsumerConfiguration -> 'InputEvent seq) (kafka_consumeLast: ConsumerConfiguration -> 'InputEvent option) = function
        | CustomApplication (KafkaApplication application) ->
            match application with
            | Ok app -> runApplication kafka_consume kafka_consumeLast Producer.connect Producer.produceSingle Producer.TopicProducer.flush Producer.TopicProducer.close app
            | Error error -> failwithf "[Application] Error:\n%A" error
        | app -> failwithf "Run Dummy is not implemented for %A." app
