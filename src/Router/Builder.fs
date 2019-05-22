namespace KafkaApplication.Router

module ContentBasedRouterBuilder =
    open Kafka
    open KafkaApplication
    open ApplicationBuilder
    open OptionOperators
    open Router

    module ContentBasedRouterApplicationBuilder =
        let private addRouterConfiguration
            router
            routeToBrokerList
            (configuration: Configuration<EventToRoute, EventToRoute>): Configuration<EventToRoute, EventToRoute> =

            let outputStreams = router |> Router.getOutputStreams
            let outputStreamNames = outputStreams |> List.map StreamName.value

            let routerConsumeHandler (app: ConsumeRuntimeParts<EventToRoute>) (events: EventToRoute seq) =
                let routeEvent =
                    ContentBasedRouter.routeEvent
                        (app.Logger.VeryVerbose "Routing")
                        (fun (StreamName topic) -> app.ProduceTo.[topic])
                        router

                events
                |> Seq.iter routeEvent

            let fromDomain: FromDomain<EventToRoute> =
                fun _ -> EventToRoute.serialized

            configuration
            |> addConnectToMany { BrokerList = routeToBrokerList; Topics = outputStreams }
            |> addProduceToMany outputStreamNames fromDomain
            |> addDefaultConsumeHandler routerConsumeHandler
            |> addCreateInputEventKeys Metrics.createKeysForInputEvent
            |> addCreateOutputEventKeys Metrics.createKeysForOutputEvent

        let build
            (buildApplication: Configuration<EventToRoute, EventToRoute> -> KafkaApplication<EventToRoute, EventToRoute>)
            (ContentBasedRouterApplicationConfiguration state: ContentBasedRouterApplicationConfiguration<EventToRoute, EventToRoute>): ContentBasedRouterApplication<EventToRoute, EventToRoute> =

            result {
                let! routerParts = state

                let! router =
                    routerParts.RouterConfiguration
                    |> Result.ofOption NotSet
                    |> Result.mapError RouterConfigurationError

                let! routeToBrokerList =
                    routerParts.RouteToBrokerList
                    |> Result.ofOption OutputBrokerListNotSet
                    |> Result.mapError RouterConfigurationError

                let! configuration =
                    routerParts.Configuration
                    |> Result.ofOption ConfigurationNotSet
                    |> Result.mapError ApplicationConfigurationError

                let kafkaApplication =
                    configuration
                    |> addRouterConfiguration router routeToBrokerList
                    |> buildApplication

                return {
                    Application = kafkaApplication
                    RouterConfiguration = router
                }
            }
            |> ContentBasedRouterApplication

    type ContentBasedRouterBuilder<'InputEvent, 'OutputEvent, 'a> internal (buildApplication: ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent> -> 'a) =
        let debugConfiguration (parts: RouterParts<'InputEvent, 'OutputEvent>) =    // todo - this could be common if it is parametrized
            parts.Configuration
            |>! fun configuration ->
                configuration <!> tee (fun configurationParts ->
                    parts
                    |> sprintf "%A"
                    |> configurationParts.Logger.Debug "ContentBasedRouter"
                )
                |> ignore

        let (>>=) (ContentBasedRouterApplicationConfiguration configuration) f =
            configuration
            |> Result.bind ((tee debugConfiguration) >> f)
            |> ContentBasedRouterApplicationConfiguration

        let (<!>) state f =
            state >>= (f >> Ok)

        member __.Yield (_): ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent> =
            RouterParts.defaultRouter
            |> Ok
            |> ContentBasedRouterApplicationConfiguration

        member __.Bind(state, f): ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent> =
            state >>= f

        member __.Run(state: ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent>) =
            buildApplication state

        [<CustomOperation("parseConfiguration")>]
        member __.ParseConfiguration(state, configurationPath): ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent> =
            state >>= fun parts ->
                result {
                    if not (System.IO.File.Exists(configurationPath)) then  // todo - could be common
                        return!
                            configurationPath
                            |> sprintf "Routing configuration was not found at \"%s\"."
                            |> NotFound
                            |> Error

                    let configuration = Router.parse configurationPath

                    return { parts with RouterConfiguration = Some configuration }
                }
                |> Result.mapError RouterConfigurationError

        [<CustomOperation("from")>]
        member __.From(state, configuration): ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent> =
            state >>= fun parts ->  // todo - could be common
                match parts.Configuration with
                | None -> Ok { parts with Configuration = Some configuration }
                | _ -> AlreadySetConfiguration |> ApplicationConfigurationError |> Error

        [<CustomOperation("routeToBrokerFromEnv")>]
        member __.RouteToBrokerFromEnv(state, brokerListEnvironmentKey): ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent> =
            state >>= fun parts ->
                result {
                    let! configuration =
                        parts.Configuration
                        |> Result.ofOption ConfigurationNotSet
                        |> Result.mapError ApplicationConfigurationError

                    let! configurationParts =
                        configuration
                        |> Configuration.result
                        |> Result.mapError InvalidConfiguration
                        |> Result.mapError ApplicationConfigurationError

                    let! brokerList =
                        brokerListEnvironmentKey
                        |> getEnvironmentValue configurationParts Kafka.BrokerList ConnectionConfigurationError.VariableNotFoundError
                        |> Result.mapError ConnectionConfigurationError

                    return { parts with RouteToBrokerList = Some brokerList }
                }
