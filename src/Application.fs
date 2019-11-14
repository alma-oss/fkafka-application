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
    open KafkaApplication.Deriver
    open KafkaApplication.Deriver.DeriverBuilder

    //
    // Applications
    //

    type Application<'InputEvent, 'OutputEvent> =
        | CustomApplication of KafkaApplication<'InputEvent, 'OutputEvent>
        | FilterContentFilter of FilterApplication<'InputEvent, 'OutputEvent>
        | ContentBasedRouter of ContentBasedRouterApplication<'InputEvent, 'OutputEvent>
        | Deriver of DeriverApplication<'InputEvent, 'OutputEvent>

    //
    // Application builders
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

    let contentBasedRouter<'InputEvent, 'OutputEvent> =
        let buildApplication: Configuration<'InputEvent, 'OutputEvent> -> KafkaApplication<'InputEvent, 'OutputEvent> = KafkaApplicationBuilder.buildApplication Producer.prepareProducer Producer.produce
        let buildRouter: ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent> -> ContentBasedRouterApplication<'InputEvent, 'OutputEvent> = ContentBasedRouterApplicationBuilder.build buildApplication
        ContentBasedRouterBuilder(buildRouter >> ContentBasedRouter)

    let deriver<'InputEvent, 'OutputEvent> =
        let buildApplication: Configuration<'InputEvent, 'OutputEvent> -> KafkaApplication<'InputEvent, 'OutputEvent> = KafkaApplicationBuilder.buildApplication Producer.prepareProducer Producer.produce
        let buildDeriver: DeriverApplicationConfiguration<'InputEvent, 'OutputEvent> -> DeriverApplication<'InputEvent, 'OutputEvent> = DeriverApplicationBuilder.buildDeriver buildApplication
        DeriverBuilder(buildDeriver >> Deriver)

    //
    // Run applications
    //

    let run<'InputEvent, 'OutputEvent> (application: Application<'InputEvent, 'OutputEvent>) =
        match application with
        | CustomApplication kafkaApplication -> runKafkaApplication ignore kafkaApplication
        | FilterContentFilter filterApplication -> FilterRunner.runFilter runKafkaApplication filterApplication
        | ContentBasedRouter routerApplication -> ContentBasedRouterRunner.runRouter runKafkaApplication routerApplication
        | Deriver deriverApplication -> DeriverRunner.runDeriver runKafkaApplication deriverApplication
