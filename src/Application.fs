namespace Alma.KafkaApplication

[<AutoOpen>]
module KafkaApplication =
    open System
    open Microsoft.Extensions.Logging
    open Alma.Kafka
    open Alma.Tracing
    open ApplicationBuilder
    open ApplicationRunner
    open Alma.KafkaApplication.Filter
    open Alma.KafkaApplication.Filter.FilterBuilder
    open Alma.KafkaApplication.Router
    open Alma.KafkaApplication.Router.ContentBasedRouterBuilder
    open Alma.KafkaApplication.Deriver
    open Alma.KafkaApplication.Deriver.DeriverBuilder

    //
    // Applications
    //

    type Application<'InputEvent, 'OutputEvent, 'Dependencies, 'FilterValue> =
        | CustomApplication of KafkaApplication<'InputEvent, 'OutputEvent, 'Dependencies>
        | FilterContentFilter of FilterApplication<'InputEvent, 'OutputEvent, 'Dependencies, 'FilterValue>
        | ContentBasedRouter of ContentBasedRouterApplication<'InputEvent, 'OutputEvent, 'Dependencies>
        | Deriver of DeriverApplication<'InputEvent, 'OutputEvent, 'Dependencies>

        with
            member this.Logger
                with get () =
                    match this with
                    | CustomApplication app -> LoggerFactory.createLogger app.LoggerFactory "CustomApplication"
                    | FilterContentFilter (FilterApplication (Ok { Application = app })) -> LoggerFactory.createLogger app.LoggerFactory "FilterContentFilter"
                    | ContentBasedRouter (ContentBasedRouterApplication (Ok { Application = app })) -> LoggerFactory.createLogger app.LoggerFactory "ContentBasedRouter"
                    | Deriver (DeriverApplication (Ok { Application = app })) -> LoggerFactory.createLogger app.LoggerFactory "Deriver"
                    | _ -> LoggerFactory.createLogger defaultLoggerFactory "Application"

    //
    // Application builders
    //

    let kafkaApplication<'InputEvent, 'OutputEvent, 'Dependencies> =
        let buildApplication: Configuration<'InputEvent, 'OutputEvent, 'Dependencies> -> KafkaApplication<'InputEvent, 'OutputEvent, 'Dependencies> = KafkaApplicationBuilder.buildApplication Producer.prepare Producer.produceWithTrace
        KafkaApplicationBuilder(buildApplication >> CustomApplication)

    let partialKafkaApplication<'InputEvent, 'OutputEvent, 'Dependencies> =
        let id: Configuration<'InputEvent, 'OutputEvent, 'Dependencies> -> Configuration<'InputEvent, 'OutputEvent, 'Dependencies> = id
        KafkaApplicationBuilder(id)

    let filterContentFilter<'InputEvent, 'OutputEvent, 'Dependencies, 'FilterValue when 'FilterValue: equality> =
        let buildApplication: Configuration<'InputEvent, 'OutputEvent, 'Dependencies> -> KafkaApplication<'InputEvent, 'OutputEvent, 'Dependencies> = KafkaApplicationBuilder.buildApplication Producer.prepare Producer.produceWithTrace
        let buildFilter: FilterApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies, 'FilterValue> -> FilterApplication<'InputEvent, 'OutputEvent, 'Dependencies, 'FilterValue> = FilterApplicationBuilder.buildFilter buildApplication
        FilterBuilder(buildFilter >> FilterContentFilter)

    let contentBasedRouter<'InputEvent, 'OutputEvent, 'Dependencies> =
        let buildApplication: Configuration<'InputEvent, 'OutputEvent, 'Dependencies> -> KafkaApplication<'InputEvent, 'OutputEvent, 'Dependencies> = KafkaApplicationBuilder.buildApplication Producer.prepare Producer.produceWithTrace
        let buildRouter: ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> -> ContentBasedRouterApplication<'InputEvent, 'OutputEvent, 'Dependencies> = ContentBasedRouterApplicationBuilder.build buildApplication
        ContentBasedRouterBuilder(buildRouter >> ContentBasedRouter)

    let deriver<'InputEvent, 'OutputEvent, 'Dependencies> =
        let buildApplication: Configuration<'InputEvent, 'OutputEvent, 'Dependencies> -> KafkaApplication<'InputEvent, 'OutputEvent, 'Dependencies> = KafkaApplicationBuilder.buildApplication Producer.prepare Producer.produceWithTrace
        let buildDeriver: DeriverApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> -> DeriverApplication<'InputEvent, 'OutputEvent, 'Dependencies> = DeriverApplicationBuilder.buildDeriver buildApplication
        DeriverBuilder(buildDeriver >> Deriver)

    //
    // Run applications
    //

    let run<'InputEvent, 'OutputEvent, 'Dependencies, 'FilterValue> (application: Application<'InputEvent, 'OutputEvent, 'Dependencies, 'FilterValue>) =
        try
            match application with
            | CustomApplication kafkaApplication -> startKafkaApplication ignore kafkaApplication
            | FilterContentFilter filterApplication -> FilterRunner.runFilter startKafkaApplication filterApplication
            | ContentBasedRouter routerApplication -> ContentBasedRouterRunner.runRouter startKafkaApplication routerApplication
            | Deriver deriverApplication -> DeriverRunner.runDeriver startKafkaApplication deriverApplication
        finally
            ApplicationState.finish application.Logger
            Tracer.finishTracerProvider()
            System.Threading.Thread.Sleep(TimeSpan.FromSeconds 2.) // Main thread waits till logger logs error message
