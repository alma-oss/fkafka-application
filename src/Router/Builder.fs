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

    let internal pattern = PatternName "ContentBasedRouter"

    module internal ContentBasedRouterApplicationBuilder =
        let private addRouterConfiguration<'InputEvent, 'OutputEvent, 'Dependencies>
            router
            routeToBrokerList
            (routeEventHandler: RouteEventHandler<'InputEvent, 'OutputEvent, 'Dependencies>)
            (fromDomain: OutputFromDomain<'OutputEvent>)
            (createCustomValues: CreateCustomValues<'InputEvent, 'OutputEvent>)
            (getCommonEvent: GetCommonEvent<'InputEvent, 'OutputEvent>)
            (configuration: Configuration<'InputEvent, 'OutputEvent, 'Dependencies>): Result<Configuration<'InputEvent, 'OutputEvent, 'Dependencies>, ContentBasedRouterApplicationError> =
            result {
                let outputStreams = router |> Router.Configuration.getOutputStreams

                let! outputStreamTopics =
                    outputStreams
                    |> List.map (function
                        | StreamName streamName -> Error (StreamNameIsNotInstance (Lmc.ServiceIdentification.InstanceError.InvalidFormat streamName))
                        | Instance instance -> Ok instance
                    )
                    |> Validation.ofResults <@> RouterErrors

                let outputStreamNames =
                    outputStreams
                    |> List.map StreamName.value

                let routerConsumeHandler (app: ConsumeRuntimeParts<'OutputEvent, 'Dependencies>) (event: TracedEvent<'InputEvent>) = asyncResult {
                    use eventToRoute = event |> TracedEvent.continueAs "Router" "Route event"

                    let routeEvent =
                        match routeEventHandler with
                        | Simple routeEvent -> routeEvent
                        | WithApplication routeEvent -> routeEvent (app |> PatternRuntimeParts.fromConsumeParts pattern)

                    let produceRoutedEvent =
                        Router.Routing.routeEvent
                            (LoggerFactory.createLogger app.LoggerFactory "KafkaApplication.Router")
                            (TracedEvent.event >> Output >> getCommonEvent >> CommonEvent.eventType)
                            (fun stream -> app.ProduceTo.[stream |> StreamName.value])
                            router

                    let outputEvent =
                        eventToRoute
                        |> routeEvent app.ProcessedBy

                    match outputEvent with
                    | Some outputEvent -> do! outputEvent |> produceRoutedEvent
                    | _ -> ()
                }

                return
                    configuration
                    |> addConnectToMany { BrokerList = routeToBrokerList; Topics = outputStreamTopics }
                    |> addProduceToMany outputStreamNames fromDomain
                    |> addDefaultConsumeHandler (ConsumeEventsAsyncResult routerConsumeHandler)
                    |> addCreateInputEventKeys (createKeysForInputEvent createCustomValues getCommonEvent)
                    |> addCreateOutputEventKeys (createKeysForOutputEvent createCustomValues getCommonEvent)
            }
            <@> RouterConfigurationError

        let build
            (buildApplication: Configuration<'InputEvent, 'OutputEvent, 'Dependencies> -> KafkaApplication<'InputEvent, 'OutputEvent, 'Dependencies>)
            (ContentBasedRouterApplicationConfiguration state: ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies>): ContentBasedRouterApplication<'InputEvent, 'OutputEvent, 'Dependencies> =

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

    type ContentBasedRouterBuilder<'InputEvent, 'OutputEvent, 'Dependencies, 'a> internal (buildApplication: ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> -> 'a) =
        let (>>=) (ContentBasedRouterApplicationConfiguration configuration) f =
            configuration
            |> Result.bind ((tee (debugPatternConfiguration pattern (fun { Configuration = c } -> c))) >> f)
            |> ContentBasedRouterApplicationConfiguration

        let (<!>) state f =
            state >>= (f >> Ok)

        let routeToBroker state brokerListEnvironmentKey fromDomain =
            state >>= fun routerParts ->
                result {
                    let! configuration = routerParts.Configuration <?!> ConfigurationNotSet <@> ApplicationConfigurationError

                    let! configurationParts =
                        configuration
                        |> Configuration.result <@> (InvalidConfiguration >> ApplicationConfigurationError)

                    let! brokerList =
                        brokerListEnvironmentKey
                        |> getEnvironmentValue configurationParts BrokerList ConnectionConfigurationError.VariableNotFoundError <@> ContentBasedRouterApplicationError.ConnectionConfigurationError

                    return { routerParts with RouteToBrokerList = Some brokerList; FromDomain = Some fromDomain }
                }

        member __.Yield (_): ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            RouterParts.defaultRouter
            |> Ok
            |> ContentBasedRouterApplicationConfiguration

        member __.Run(state: ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies>) =
            buildApplication state

        [<CustomOperation("parseConfiguration")>]
        member __.ParseConfiguration(state, configurationPath): ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state >>= fun routerParts ->
                result {
                    let! router =
                        configurationPath
                        |> FileParser.parseFromPath
                            Router.Configuration.parse
                            (sprintf "Routing configuration was not found at \"%s\"." >> NotFound)

                    return { routerParts with RouterConfiguration = Some router }
                }
                <@> RouterConfigurationError

        [<CustomOperation("from")>]
        member __.From(state, configuration): ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state >>= fun routerParts ->
                match routerParts.Configuration with
                | None -> Ok { routerParts with Configuration = Some configuration }
                | _ -> AlreadySetConfiguration |> ApplicationConfigurationError |> Error

        [<CustomOperation("routeToBrokerFromEnv")>]
        member __.RouteToBrokerFromEnv(state, brokerListEnvironmentKey, fromDomain): ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            routeToBroker state brokerListEnvironmentKey (FromDomain fromDomain)

        [<CustomOperation("routeToBrokerFromEnv")>]
        member __.RouteToBrokerFromEnv(state, brokerListEnvironmentKey, fromDomain): ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            routeToBroker state brokerListEnvironmentKey (FromDomainResult fromDomain)

        [<CustomOperation("routeToBrokerFromEnv")>]
        member __.RouteToBrokerFromEnv(state, brokerListEnvironmentKey, fromDomain): ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            routeToBroker state brokerListEnvironmentKey (FromDomainAsyncResult fromDomain)

        [<CustomOperation("route")>]
        member __.Route(state, routeEvent): ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun routerParts -> { routerParts with RouteEvent = Some (Simple routeEvent) }

        [<CustomOperation("routeWithApplication")>]
        member __.RouteWithApp(state, routeEvent): ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun routerParts -> { routerParts with RouteEvent = Some (WithApplication routeEvent) }

        [<CustomOperation("getCommonEventBy")>]
        member __.GetCommonEventBy(state, getCommonEvent): ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun routerParts -> { routerParts with GetCommonEvent = Some getCommonEvent }

        [<CustomOperation("addCustomMetricValues")>]
        member __.AddCustomMetricValues(state, createCustomValues): ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun routerParts -> { routerParts with CreateCustomValues = Some createCustomValues }
