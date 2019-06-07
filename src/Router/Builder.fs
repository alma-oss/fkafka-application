namespace KafkaApplication.Router

module ContentBasedRouterBuilder =
    open Kafka
    open KafkaApplication
    open KafkaApplication.PatternBuilder
    open ApplicationBuilder
    open Router

    module ContentBasedRouterApplicationBuilder =
        let private addRouterConfiguration
            router
            routeToBrokerList
            (configuration: Configuration<EventToRoute, EventToRoute>): Result<Configuration<EventToRoute, EventToRoute>, ContentBasedRouterApplicationError> =
            result {
                let outputStreams = router |> Router.getOutputStreams

                let! outputStreamTopics =
                    outputStreams
                    |> List.map (function
                        | (StreamName streamName) -> Error (RouterError.StreamNameIsNotInstance streamName)
                        | Instance instance -> Ok instance
                    )
                    |> Result.sequence
                    |> Result.mapError RouterError

                let outputStreamNames =
                    outputStreams
                    |> List.map StreamName.value

                let routerConsumeHandler (app: ConsumeRuntimeParts<EventToRoute>) (events: EventToRoute seq) =
                    let routeEvent =
                        ContentBasedRouter.routeEvent
                            (app.Logger.VeryVerbose "Routing")
                            (fun topic -> app.ProduceTo.[topic |> StreamName.value])
                            router

                    events
                    |> Seq.iter routeEvent

                let fromDomain: FromDomain<EventToRoute> =
                    fun _ -> EventToRoute.serialized

                return
                    configuration
                    |> addParseEvent EventToRoute.parse
                    |> addConnectToMany { BrokerList = routeToBrokerList; Topics = outputStreamTopics }
                    |> addProduceToMany outputStreamNames fromDomain
                    |> addDefaultConsumeHandler routerConsumeHandler
                    |> addCreateInputEventKeys Metrics.createKeysForInputEvent
                    |> addCreateOutputEventKeys Metrics.createKeysForOutputEvent
            }
            |> Result.mapError RouterConfigurationError

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

                let! routerConfiguration =
                    configuration
                    |> addRouterConfiguration router routeToBrokerList

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

        member __.Bind(state, f): ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent> =
            state >>= f

        member __.Run(state: ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent>) =
            buildApplication state

        [<CustomOperation("parseConfiguration")>]
        member __.ParseConfiguration(state, configurationPath): ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent> =
            state >>= fun parts ->
                result {
                    let! routerPath =
                        configurationPath
                        |> FileParser.parseFromPath id (sprintf "Routing configuration was not found at \"%s\".")
                        |> Result.mapError NotFound

                    let! router =
                        routerPath
                        |> Router.parse
                        |> Result.mapError RouterError

                    return { parts with RouterConfiguration = Some router }
                }
                |> Result.mapError RouterConfigurationError

        [<CustomOperation("from")>]
        member __.From(state, configuration): ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent> =
            state >>= fun parts ->
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
