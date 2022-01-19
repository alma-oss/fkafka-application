namespace Lmc.KafkaApplication

[<AutoOpen>]
module KafkaApplication =
    open Lmc.Kafka
    open ApplicationBuilder
    open ApplicationRunner
    open Lmc.KafkaApplication.Filter
    open Lmc.KafkaApplication.Filter.FilterBuilder
    open Lmc.KafkaApplication.Router
    open Lmc.KafkaApplication.Router.ContentBasedRouterBuilder
    open Lmc.KafkaApplication.Deriver
    open Lmc.KafkaApplication.Deriver.DeriverBuilder

    //
    // Applications
    //

    type Application<'InputEvent, 'OutputEvent, 'FilterValue> =
        | CustomApplication of KafkaApplication<'InputEvent, 'OutputEvent>
        | FilterContentFilter of FilterApplication<'InputEvent, 'OutputEvent, 'FilterValue>
        | ContentBasedRouter of ContentBasedRouterApplication<'InputEvent, 'OutputEvent>
        | Deriver of DeriverApplication<'InputEvent, 'OutputEvent>

    //
    // Application builders
    //

    let kafkaApplication<'InputEvent, 'OutputEvent> =
        let buildApplication: Configuration<'InputEvent, 'OutputEvent> -> KafkaApplication<'InputEvent, 'OutputEvent> = KafkaApplicationBuilder.buildApplication Producer.prepare Producer.produceWithTrace
        KafkaApplicationBuilder(buildApplication >> CustomApplication)

    let partialKafkaApplication<'InputEvent, 'OutputEvent> =
        let id: Configuration<'InputEvent, 'OutputEvent> -> Configuration<'InputEvent, 'OutputEvent> = id
        KafkaApplicationBuilder(id)

    let filterContentFilter<'InputEvent, 'OutputEvent, 'FilterValue when 'FilterValue: equality> =
        let buildApplication: Configuration<'InputEvent, 'OutputEvent> -> KafkaApplication<'InputEvent, 'OutputEvent> = KafkaApplicationBuilder.buildApplication Producer.prepare Producer.produceWithTrace
        let buildFilter: FilterApplicationConfiguration<'InputEvent, 'OutputEvent, 'FilterValue> -> FilterApplication<'InputEvent, 'OutputEvent, 'FilterValue> = FilterApplicationBuilder.buildFilter buildApplication
        FilterBuilder(buildFilter >> FilterContentFilter)

    let contentBasedRouter<'InputEvent, 'OutputEvent> =
        let buildApplication: Configuration<'InputEvent, 'OutputEvent> -> KafkaApplication<'InputEvent, 'OutputEvent> = KafkaApplicationBuilder.buildApplication Producer.prepare Producer.produceWithTrace
        let buildRouter: ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent> -> ContentBasedRouterApplication<'InputEvent, 'OutputEvent> = ContentBasedRouterApplicationBuilder.build buildApplication
        ContentBasedRouterBuilder(buildRouter >> ContentBasedRouter)

    let deriver<'InputEvent, 'OutputEvent> =
        let buildApplication: Configuration<'InputEvent, 'OutputEvent> -> KafkaApplication<'InputEvent, 'OutputEvent> = KafkaApplicationBuilder.buildApplication Producer.prepare Producer.produceWithTrace
        let buildDeriver: DeriverApplicationConfiguration<'InputEvent, 'OutputEvent> -> DeriverApplication<'InputEvent, 'OutputEvent> = DeriverApplicationBuilder.buildDeriver buildApplication
        DeriverBuilder(buildDeriver >> Deriver)

    //
    // Run applications
    //

    let run<'InputEvent, 'OutputEvent, 'FilterValue> (application: Application<'InputEvent, 'OutputEvent, 'FilterValue>) =
        match application with
        | CustomApplication kafkaApplication -> runKafkaApplication ignore kafkaApplication
        | FilterContentFilter filterApplication -> FilterRunner.runFilter runKafkaApplication filterApplication
        | ContentBasedRouter routerApplication -> ContentBasedRouterRunner.runRouter runKafkaApplication routerApplication
        | Deriver deriverApplication -> DeriverRunner.runDeriver runKafkaApplication deriverApplication
