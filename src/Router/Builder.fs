namespace Lmc.KafkaApplication.Router

module ContentBasedRouterBuilder =
    open Lmc.Kafka
    open Lmc.KafkaApplication
    open Lmc.KafkaApplication.PatternBuilder
    open Lmc.KafkaApplication.PatternMetrics
    open Lmc.ErrorHandling
    open Lmc.ErrorHandling.Option.Operators
    open Lmc.ErrorHandling.Result.Operators
    open ApplicationBuilder

    module internal ContentBasedRouterApplicationBuilder =
        let private addRouterConfiguration<'InputEvent, 'OutputEvent>
            router
            routeToBrokerList
            (routeEventHandler: RouteEventHandler<'InputEvent, 'OutputEvent>)
            (fromDomain: FromDomain<'OutputEvent>)
            (createCustomValues: CreateCustomValues<'InputEvent, 'OutputEvent>)
            (getCommonEvent: GetCommonEvent<'InputEvent, 'OutputEvent>)
            (configuration: Configuration<'InputEvent, 'OutputEvent>): Result<Configuration<'InputEvent, 'OutputEvent>, ContentBasedRouterApplicationError> =
            result {
                let outputStreams = router |> Router.Configuration.getOutputStreams

                let! outputStreamTopics =
                    outputStreams
                    |> List.map (function
                        | StreamName streamName -> Error (RouterError.StreamNameIsNotInstance streamName)
                        | Instance instance -> Ok instance
                    )
                    |> Result.sequence <@> RouterError

                let outputStreamNames =
                    outputStreams
                    |> List.map StreamName.value

                let routerConsumeHandler (app: ConsumeRuntimeParts<'OutputEvent>) (event: TracedEvent<'InputEvent>) =
                    use eventToRoute = event |> TracedEvent.continueAs "Router" "Route event"

                    let routeEvent =
                        match routeEventHandler with
                        | Simple routeEvent -> routeEvent
                        | WithApplication routeEvent -> routeEvent (app |> PatternRuntimeParts.fromConsumeParts)

                    let produceRoutedEvent =
                        Router.Routing.routeEvent
                            (app.Logger.VeryVerbose "Routing")
                            (Output >> getCommonEvent >> CommonEvent.eventType)
                            (fun stream -> app.ProduceTo.[stream |> StreamName.value])
                            router

                    eventToRoute
                    |> routeEvent app.ProcessedBy
                    |> Option.iter produceRoutedEvent

                return
                    configuration
                    |> addConnectToMany { BrokerList = routeToBrokerList; Topics = outputStreamTopics }
                    |> addProduceToMany outputStreamNames fromDomain
                    |> addDefaultConsumeHandler routerConsumeHandler
                    |> addCreateInputEventKeys (createKeysForInputEvent createCustomValues getCommonEvent)
                    |> addCreateOutputEventKeys (createKeysForOutputEvent createCustomValues getCommonEvent)
            }
            <@> RouterConfigurationError

        let build
            (buildApplication: Configuration<'InputEvent, 'OutputEvent> -> KafkaApplication<'InputEvent, 'OutputEvent>)
            (ContentBasedRouterApplicationConfiguration state: ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent>): ContentBasedRouterApplication<'InputEvent, 'OutputEvent> =

            result {
                let! routerParts = state

                let createCustomValues = routerParts.CreateCustomValues <?=> (fun _ -> [])

                let! router = routerParts.RouterConfiguration <?!> NotSet <@> RouterConfigurationError
                let! routeToBrokerList = routerParts.RouteToBrokerList <?!> OutputBrokerListNotSet <@> RouterConfigurationError
                let! fromDomain = routerParts.FromDomain <?!> MissingFromDomain <@> RouterConfigurationError
                let! routeEvent = routerParts.RouteEvent <?!> MissingRouteEvent <@> RouterConfigurationError
                let! getCommonEventData = routerParts.GetCommonEvent <?!> MissingGetCommonEvent <@> RouterConfigurationError

                let! configuration = routerParts.Configuration <?!> ConfigurationNotSet <@> ApplicationConfigurationError

                let! routerConfiguration =
                    configuration
                    |> addRouterConfiguration router routeToBrokerList routeEvent fromDomain createCustomValues getCommonEventData

                let kafkaApplication =
                    routerConfiguration
                    |> buildApplication

                return {
                    Application = kafkaApplication
                    RouterConfiguration = router
                }
            }
            |> ContentBasedRouterApplication

    type ContentBasedRouterBuilder<'InputEvent, 'OutputEvent, 'a> internal (buildApplication: ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent> -> 'a) =
        let (>>=) (ContentBasedRouterApplicationConfiguration configuration) f =
            configuration
            |> Result.bind ((tee (debugPatternConfiguration (PatternName "ContentBasedRouter") (fun { Configuration = c } -> c))) >> f)
            |> ContentBasedRouterApplicationConfiguration

        let (<!>) state f =
            state >>= (f >> Ok)

        member __.Yield (_): ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent> =
            RouterParts.defaultRouter
            |> Ok
            |> ContentBasedRouterApplicationConfiguration

        member __.Run(state: ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent>) =
            buildApplication state

        [<CustomOperation("parseConfiguration")>]
        member __.ParseConfiguration(state, configurationPath): ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent> =
            state >>= fun routerParts ->
                result {
                    let! routerPath =
                        configurationPath
                        |> FileParser.parseFromPath id (sprintf "Routing configuration was not found at \"%s\".") <@> NotFound

                    let! router =
                        routerPath
                        |> Router.Configuration.parse <@> RouterError

                    return { routerParts with RouterConfiguration = Some router }
                }
                <@> RouterConfigurationError

        [<CustomOperation("from")>]
        member __.From(state, configuration): ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent> =
            state >>= fun routerParts ->
                match routerParts.Configuration with
                | None -> Ok { routerParts with Configuration = Some configuration }
                | _ -> AlreadySetConfiguration |> ApplicationConfigurationError |> Error

        [<CustomOperation("routeToBrokerFromEnv")>]
        member __.RouteToBrokerFromEnv(state, brokerListEnvironmentKey, fromDomain): ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent> =
            state >>= fun routerParts ->
                result {
                    let! configuration = routerParts.Configuration <?!> ConfigurationNotSet <@> ApplicationConfigurationError

                    let! configurationParts =
                        configuration
                        |> Configuration.result <@> InvalidConfiguration <@> ApplicationConfigurationError

                    let! brokerList =
                        brokerListEnvironmentKey
                        |> getEnvironmentValue configurationParts BrokerList ConnectionConfigurationError.VariableNotFoundError <@> ContentBasedRouterApplicationError.ConnectionConfigurationError

                    return { routerParts with RouteToBrokerList = Some brokerList; FromDomain = Some fromDomain }
                }

        [<CustomOperation("route")>]
        member __.Route(state, routeEvent): ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent> =
            state <!> fun routerParts -> { routerParts with RouteEvent = Some (Simple routeEvent) }

        [<CustomOperation("routeWithApplication")>]
        member __.RouteWithApp(state, routeEvent): ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent> =
            state <!> fun routerParts -> { routerParts with RouteEvent = Some (WithApplication routeEvent) }

        [<CustomOperation("getCommonEventBy")>]
        member __.GetCommonEventBy(state, getCommonEvent): ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent> =
            state <!> fun routerParts -> { routerParts with GetCommonEvent = Some getCommonEvent }

        [<CustomOperation("addCustomMetricValues")>]
        member __.AddCustomMetricValues(state, createCustomValues): ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent> =
            state <!> fun routerParts -> { routerParts with CreateCustomValues = Some createCustomValues }
