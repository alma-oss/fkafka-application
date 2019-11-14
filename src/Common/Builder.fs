namespace KafkaApplication

module ApplicationBuilder =
    open System
    open Kafka
    open KafkaApplication
    open Metrics
    open Logging
    open Metrics.ServiceStatus
    open ServiceIdentification
    open OptionOperators
    open Events

    [<AutoOpen>]
    module internal KafkaApplicationBuilder =
        let private debugConfiguration (parts: ConfigurationParts<_, _>) =
            parts
            |> sprintf "%A"
            |> parts.Logger.Debug "Configuration"

        let (>>=) (Configuration configuration) f =
            configuration
            |> Result.bind ((tee debugConfiguration) >> f)
            |> Configuration

        let (<!>) state f =
            state >>= (f >> Ok)

        let logger (Configuration configuration) =
            configuration
            |> Result.map (fun parts -> parts.Logger)

        /// Add other configuration and merge it with current.
        /// New configuration values have higher priority. New values (only those with Some value) will replace already set configuration values.
        /// (Except of logger)
        let mergeConfiguration<'InputEvent, 'OutputEvent> currentConfiguration newConfiguration: Configuration<'InputEvent, 'OutputEvent> =
            currentConfiguration >>= fun currentParts ->
                newConfiguration <!> fun newParts ->
                    {
                        Logger = currentParts.Logger
                        Environment = newParts.Environment |> Environment.update currentParts.Environment
                        Instance = newParts.Instance <??> currentParts.Instance
                        GitCommit = newParts.GitCommit <??> currentParts.GitCommit
                        DockerImageVersion = newParts.DockerImageVersion <??> currentParts.DockerImageVersion
                        Spot = newParts.Spot <??> currentParts.Spot
                        GroupId = newParts.GroupId <??> currentParts.GroupId
                        GroupIds = newParts.GroupIds |> Map.merge currentParts.GroupIds
                        ParseEvent = newParts.ParseEvent <??> currentParts.ParseEvent
                        Connections = newParts.Connections |> Map.merge currentParts.Connections
                        ConsumeHandlers = currentParts.ConsumeHandlers @ newParts.ConsumeHandlers
                        OnConsumeErrorHandlers = newParts.OnConsumeErrorHandlers |> Map.merge currentParts.OnConsumeErrorHandlers
                        ProduceTo = currentParts.ProduceTo @ newParts.ProduceTo
                        ProducerErrorHandler = currentParts.ProducerErrorHandler <??> newParts.ProducerErrorHandler
                        FromDomain = newParts.FromDomain |> Map.merge currentParts.FromDomain
                        MetricsRoute = newParts.MetricsRoute <??> currentParts.MetricsRoute
                        CustomMetrics = currentParts.CustomMetrics @ newParts.CustomMetrics
                        IntervalResourceCheckers = currentParts.IntervalResourceCheckers @ newParts.IntervalResourceCheckers
                        CreateInputEventKeys = newParts.CreateInputEventKeys <??> currentParts.CreateInputEventKeys
                        CreateOutputEventKeys = newParts.CreateOutputEventKeys <??> currentParts.CreateOutputEventKeys
                        KafkaChecker = newParts.KafkaChecker <??> currentParts.KafkaChecker
                        GraylogConnections = currentParts.GraylogConnections @ newParts.GraylogConnections
                        CustomTasks = currentParts.CustomTasks @ newParts.CustomTasks
                        WebServerSettings = currentParts.WebServerSettings @ newParts.WebServerSettings
                    }
                |> Configuration.result

        let addGraylogToParts<'InputEvent, 'OutputEvent> (parts: ConfigurationParts<'InputEvent, 'OutputEvent>) (graylog: string, graylogService: string) =
            result {
                let! hostsPorts =
                    graylog.Split ","
                    |> Array.filter (String.IsNullOrEmpty >> not)
                    |> Array.map (fun graylog ->
                        match graylog.Split ":" with
                        | [| host |] -> Ok (host, None)
                        | [| host; port |] -> Ok (host, Some port)
                        | _ -> Error (LoggingError.InvalidGraylogConnectionString graylog)
                    )
                    |> Array.toList
                    |> Result.sequence

                let! graylogHostsPorts =
                    hostsPorts
                    |> List.map (fun (host, port) ->
                        result {
                            let! host =
                                host
                                |> Graylog.Host.create
                                |> Result.mapError LoggingError.InvalidGraylogHost

                            let! port =
                                match port with
                                | Some port ->
                                    match Int32.TryParse port with
                                    | true, port -> Ok <| Some (Graylog.Port port)
                                    | _ -> Error (LoggingError.InvalidPort port)
                                | _ -> Ok None

                            return (host, port)
                        }
                    )
                    |> Result.sequence

                if graylogHostsPorts |> List.isEmpty then
                    return! Error (LoggingError.InvalidGraylogConnectionString graylog)

                let resource = {
                    Resource = ResourceAvailability.createFromStrings "graylog" graylogService graylog Audience.Sys
                    Interval = 30<KafkaApplication.Second>
                    Checker = fun () ->
                        graylogService
                        |> Graylog.Diagnostics.isAliveResult
                        |> Async.RunSynchronously
                        |> function
                            | Ok isAlive when isAlive -> Up
                            | Ok _ -> Down
                            | Error e ->
                                parts.Logger.Error "Graylog Resource" <| sprintf "%A" e
                                Down
                }

                return { parts
                    with
                        GraylogConnections = parts.GraylogConnections @ graylogHostsPorts
                        IntervalResourceCheckers = resource :: parts.IntervalResourceCheckers
                }
            }

        let private addConsumeHandler<'InputEvent, 'OutputEvent> configuration consumeHandler connectionName: Configuration<'InputEvent, 'OutputEvent> =
            configuration <!> fun parts -> { parts with ConsumeHandlers = { Connection = connectionName; Handler = ConsumeHandler.Events consumeHandler} :: parts.ConsumeHandlers }

        let addDefaultConsumeHandler<'InputEvent, 'OutputEvent> consumeHandler configuration: Configuration<'InputEvent, 'OutputEvent> =
            Connections.Default |> addConsumeHandler configuration consumeHandler

        let addConsumeHandlerForConnection<'InputEvent, 'OutputEvent> name consumeHandler configuration: Configuration<'InputEvent, 'OutputEvent> =
            ConnectionName name |> addConsumeHandler configuration consumeHandler

        let addProduceTo<'InputEvent, 'OutputEvent> name fromDomain configuration: Configuration<'InputEvent, 'OutputEvent> =
            configuration <!> fun parts ->
                let connectionName = ConnectionName name
                {
                    parts with
                        ProduceTo = connectionName :: parts.ProduceTo
                        FromDomain = parts.FromDomain.Add(connectionName, fromDomain)
                }

        let addProduceToMany<'InputEvent, 'OutputEvent> topics fromDomain configuration: Configuration<'InputEvent, 'OutputEvent> =
            configuration <!> fun parts ->
                let connectionNames =
                    topics
                    |> List.map ConnectionName

                let fromDomain =
                    connectionNames
                    |> List.map (fun name -> (name, fromDomain))
                    |> Map.ofList

                {
                    parts with
                        ProduceTo = parts.ProduceTo @ connectionNames
                        FromDomain = parts.FromDomain |> Map.merge fromDomain
                }

        let addCreateInputEventKeys<'InputEvent, 'OutputEvent> createInputEventKeys configuration: Configuration<'InputEvent, 'OutputEvent> =
            configuration <!> fun parts -> { parts with CreateInputEventKeys = Some (CreateInputEventKeys createInputEventKeys) }

        let addCreateOutputEventKeys<'InputEvent, 'OutputEvent> createOutputEventKeys configuration: Configuration<'InputEvent, 'OutputEvent> =
            configuration <!> fun parts -> { parts with CreateOutputEventKeys = Some (CreateOutputEventKeys createOutputEventKeys) }

        let addParseEvent<'InputEvent, 'OutputEvent> parseEvent configuration: Configuration<'InputEvent, 'OutputEvent> =
            configuration <!> fun parts -> { parts with ParseEvent = Some parseEvent }

        let addConnectToMany<'InputEvent, 'OutputEvent> connectionConfigurations configuration: Configuration<'InputEvent, 'OutputEvent> =
            configuration <!> fun parts ->
                let configurationConnections: Connections =
                    connectionConfigurations.Topics
                    |> List.map (fun topic ->
                        (
                            topic |> Instance.concat "-" |> ConnectionName,
                            { BrokerList = connectionConfigurations.BrokerList; Topic = topic }
                        )
                    )
                    |> Map.ofList
                { parts with Connections = parts.Connections |> Map.merge configurationConnections }

        let private assertNotEmpty error collection =
            if collection |> Seq.isEmpty then Error (KafkaApplicationError error)
            else Ok collection

        let private prepareProducer
            logger
            checker
            markAsDisabled
            prepareProducer
            produceMessage
            (connections: Connections)
            fromDomain
            incrementOutputCount
            name =
            result {
                let! connection =
                    connections
                    |> Map.tryFind name
                    |> Result.ofOption (ProduceError.MissingConnectionConfiguration name)

                let! fromDomain =
                    fromDomain
                    |> Map.tryFind name
                    |> Result.ofOption (ProduceError.MissingFromDomainConfiguration name)

                let producer = prepareProducer {
                    Connection = connection |> ConnectionConfiguration.toKafkaConnectionConfiguration
                    Logger = name |> ConnectionName.runtimeName |> logger |> Some
                    Checker = checker connection.BrokerList |> Some
                    MarkAsDisabled = markAsDisabled |> Some
                }
                let incrementOutputCount = incrementOutputCount (OutputStreamName (connection.Topic |> StreamName.Instance))

                let produceEvent producer event =
                    event
                    |> tee (fromDomain Serializer.toJson >> produceMessage producer)
                    |> incrementOutputCount

                return {
                    Connection = name
                    Producer = producer
                    Produce = produceEvent
                }
            }

        let private prepareSupervisionProduce logger checker markAsDisabled prepareProducer supervisionConnection =
            prepareProducer {
                Connection = supervisionConnection |> ConnectionConfiguration.toKafkaConnectionConfiguration
                Logger = "Supervision" |> logger |> Some
                Checker = checker supervisionConnection.BrokerList |> Some
                MarkAsDisabled = markAsDisabled |> Some
            }

        let private composeRuntimeConsumeHandlersForConnections<'InputEvent, 'OutputEvent>
            runtimeConsumerConfigurations
            (getErrorHandler: ConnectionName -> ConsumeErrorHandler)
            incrementInputCount
            ({ Connection = connection; Handler = handler }: ConsumeHandlerForConnection<'InputEvent, 'OutputEvent>) =
            result {
                let runtimeConnectionName = connection |> ConnectionName.runtimeName

                let! configuration =
                    runtimeConsumerConfigurations
                    |> Map.tryFind runtimeConnectionName
                    |> Result.ofOption (ConsumeHandlerError.MissingConfiguration connection)

                return {
                    Connection = runtimeConnectionName
                    Configuration = configuration
                    Handler = handler
                    OnError = connection |> getErrorHandler
                    IncrementInputCount =
                        match incrementInputCount with
                        | Some incrementInputCount -> incrementInputCount (InputStreamName configuration.Connection.Topic)
                        | None -> ignore
                }
            }

        let buildApplication createProducer produceMessage (Configuration configuration): KafkaApplication<'InputEvent, 'OutputEvent> =
            result {
                let! configurationParts = configuration

                //
                // required parts
                //
                let! instance = configurationParts.Instance <?!> "Instance is required."
                let! connections = configurationParts.Connections |> assertNotEmpty "At least one connection configuration is required."
                let! consumeHandlers = configurationParts.ConsumeHandlers |> assertNotEmpty "At least one consume handler is required."
                let! parseEvent = configurationParts.ParseEvent <?!> "Parse event is required."

                //
                // optional parts
                //
                let defaultProduceErrorHandler: ProducerErrorHandler = (fun _ _ -> ProducerErrorPolicy.RetryIn 60<KafkaApplication.Second>)
                let producerErrorHandler = configurationParts.ProducerErrorHandler <?=> defaultProduceErrorHandler

                let defaultConsumeErrorHandler: ConsumeErrorHandler = (fun _ _ -> RetryIn 60<KafkaApplication.Second>)
                let getErrorHandler connection =
                    configurationParts.OnConsumeErrorHandlers |> Map.tryFind connection
                    <?=> defaultConsumeErrorHandler

                let spot = configurationParts.Spot <?=> { Zone = Zone "common"; Bucket = Bucket "all" }
                let box = Box.createFromValues instance.Domain instance.Context instance.Purpose instance.Version spot.Zone spot.Bucket

                let logger = configurationParts.Logger
                let environment = configurationParts.Environment
                let defaultGroupId = configurationParts.GroupId <?=> Kafka.GroupId.Random
                let groupIds = configurationParts.GroupIds
                let kafkaChecker = configurationParts.KafkaChecker <?=> Kafka.Checker.defaultChecker
                let kafkaIntervalChecker = Kafka.IntervalChecker.defaultChecker   // todo - allow passing custom interval checker

                let gitCommit = configurationParts.GitCommit <?=> GitCommit "unknown"
                let dockerImageVersion = configurationParts.DockerImageVersion <?=> DockerImageVersion "unknown"

                //
                // composed parts
                //

                // logging
                let logger =
                    match configurationParts.GraylogConnections with
                    | [] -> logger
                    | connections ->
                        connections
                        |> List.map (fun (host, port) ->
                            (host, port <?=> Graylog.Port Graylog.DefaultPort)
                        )
                        |> ApplicationLogger.graylogLogger instance
                        |> ApplicationLogger.combine logger

                // kafka parts
                let kafkaLogger runtimeConnection = { Log = logger.Verbose (sprintf "Kafka<%s>" runtimeConnection ) }
                let kafkaChecker brokerList = kafkaChecker |> ResourceChecker.updateResourceStatusOnCheck instance brokerList
                let kafkaIntervalChecker brokerList = kafkaIntervalChecker |> ResourceChecker.updateResourceStatusOnIntervalCheck instance brokerList

                // service status
                let! markAsEnabled =
                    Metrics.ServiceStatus.markAsEnabled instance Metrics.Audience.Sys
                    |> Result.mapError (MetricError >> MetricsError)
                let! markAsDisabled =
                    Metrics.ServiceStatus.markAsDisabled instance Metrics.Audience.Sys
                    |> Result.mapError (MetricError >> MetricsError)

                let serviceStatus = { MarkAsEnabled = markAsEnabled; MarkAsDisabled = markAsDisabled }

                let runtimeConsumerConfigurations =
                    connections
                    |> Map.fold (fun (runtimeConsumerConfigurations: Map<RuntimeConnectionName, ConsumerConfiguration>) name connection ->
                        let runtimeConnection = name |> ConnectionName.runtimeName

                        runtimeConsumerConfigurations.Add(
                            runtimeConnection,
                            {
                                Connection = connection |> ConnectionConfiguration.toKafkaConnectionConfiguration
                                GroupId = groupIds.TryFind name <?=> defaultGroupId
                                Configure = None
                                Logger = kafkaLogger runtimeConnection |> Some
                                Checker = kafkaChecker connection.BrokerList |> Some
                                IntervalChecker = kafkaIntervalChecker connection.BrokerList |> Some
                                ServiceStatus = serviceStatus |> Some
                            }
                        )
                    ) Map.empty

                // input/output metrics
                let incrementInputCount =
                    configurationParts.CreateInputEventKeys
                    |> Option.map (fun createInputKeys ->
                        ApplicationMetrics.incrementTotalInputEventCount createInputKeys instance
                    )
                let incrementOutputCount =
                    match configurationParts.CreateOutputEventKeys with
                    | Some createOutputKeys -> ApplicationMetrics.incrementTotalOutputEventCount createOutputKeys instance
                    | _ -> fun _ -> ignore

                // producers
                let prepareProducer = prepareProducer kafkaLogger kafkaChecker serviceStatus.MarkAsDisabled createProducer produceMessage connections configurationParts.FromDomain incrementOutputCount

                let! preparedProducers =
                    configurationParts.ProduceTo
                    |> List.map prepareProducer
                    |> Result.sequence
                    |> Result.mapError ProduceError

                let (producers, produces) =
                    preparedProducers
                    |> List.fold (fun (producers: Map<RuntimeConnectionName, NotConnectedProducer>, produces: Map<RuntimeConnectionName, PreparedProduceEvent<'OutputEvent>>) preparedProducer ->
                        let { Connection = (ConnectionName connection); Producer = producer; Produce = produce} = preparedProducer

                        ( producers.Add(connection, producer), produces.Add(connection, produce) )
                    ) (Map.empty, Map.empty)

                let supervisionProducer =
                    connections
                    |> Map.tryFind Connections.Supervision
                    |> Option.map (prepareSupervisionProduce kafkaLogger kafkaChecker markAsDisabled createProducer)

                let producers =
                    match supervisionProducer with
                    | Some supervisionProducer -> producers.Add(Connections.Supervision |> ConnectionName.runtimeName, supervisionProducer)
                    | _ -> producers

                //
                // runtime parts
                //
                let enableResource = ResourceAvailability.enable instance >> ignore
                let disableResource = ResourceAvailability.disable instance >> ignore
                let incrementMetric = ApplicationMetrics.incrementCustomMetricCount instance
                let setMetric = ApplicationMetrics.setCustomMetricValue instance

                let preparedRuntimeParts: PreparedConsumeRuntimeParts<'OutputEvent> = {
                    Logger = logger
                    IncrementMetric = incrementMetric
                    SetMetric = setMetric
                    Box = box
                    GitCommit = gitCommit
                    DockerImageVersion = dockerImageVersion
                    Environment = environment
                    Connections = connections
                    ConsumerConfigurations = runtimeConsumerConfigurations
                    ProduceTo = produces
                    EnableResource = enableResource
                    DisableResource = disableResource
                }

                let composeRuntimeHandler = composeRuntimeConsumeHandlersForConnections runtimeConsumerConfigurations getErrorHandler incrementInputCount

                let! runtimeConsumeHandlers =
                    consumeHandlers
                    |> List.map composeRuntimeHandler
                    |> Result.sequence
                    |> Result.mapError ConsumeHandlerError

                let customTasks =
                    configurationParts.CustomTasks
                    |> CustomTasks.prepare {
                        Logger = logger
                        Box = box
                        Environment = environment
                        IncrementMetric = incrementMetric
                        SetMetric = setMetric
                        EnableResource = enableResource
                        DisableResource = disableResource
                    }

                return {
                    Logger = logger
                    Environment = environment
                    Box = box
                    ParseEvent = parseEvent
                    ConsumerConfigurations = runtimeConsumerConfigurations
                    ConsumeHandlers = runtimeConsumeHandlers
                    Producers = producers
                    ProducerErrorHandler = producerErrorHandler
                    ServiceStatus = serviceStatus
                    MetricsRoute = configurationParts.MetricsRoute
                    CustomMetrics = configurationParts.CustomMetrics
                    IntervalResourceCheckers = configurationParts.IntervalResourceCheckers
                    PreparedRuntimeParts = preparedRuntimeParts
                    CustomTasks = customTasks
                    WebServerSettings = configurationParts.WebServerSettings
                }
            }
            |> KafkaApplication

    //
    // Kafka Application Builder computation expression
    //

    type KafkaApplicationBuilder<'InputEvent, 'OutputEvent, 'a> internal (buildApplication: Configuration<'InputEvent, 'OutputEvent> -> 'a) =
        member __.Yield (_): Configuration<'InputEvent, 'OutputEvent> =
            defaultParts
            |> Ok
            |> Configuration

        member __.Run(state: Configuration<'InputEvent, 'OutputEvent>) =
            buildApplication state

        [<CustomOperation("useLogger")>]
        member __.Logger(state, logger: ApplicationLogger): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts -> { parts with Logger = logger }

        [<CustomOperation("logToGraylog")>]
        member __.LogToGraylog(state, graylog, graylogService): Configuration<'InputEvent, 'OutputEvent> =
            state >>= fun parts ->
                (graylog, graylogService)
                |> addGraylogToParts parts
                |> Result.mapError LoggingError

        [<CustomOperation("useInstance")>]
        member __.Instance(state, instance): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts -> { parts with Instance = Some instance }

        [<CustomOperation("useGitCommit")>]
        member __.GitCommit(state, gitCommit): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts -> { parts with GitCommit = Some gitCommit }

        [<CustomOperation("useDockerImageVersion")>]
        member __.DockerImageVersion(state, dockerImageVersion): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts -> { parts with DockerImageVersion = Some dockerImageVersion }

        [<CustomOperation("useSpot")>]
        member __.Spot(state, spot): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts -> { parts with Spot = Some spot }

        [<CustomOperation("useGroupId")>]
        member __.GroupId(state, groupId): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts -> { parts with GroupId = Some groupId }

        [<CustomOperation("useGroupIdFor")>]
        member __.GroupIdFor(state, name, groupId): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts -> { parts with GroupIds = parts.GroupIds.Add(ConnectionName name, groupId) }

        [<CustomOperation("parseEventWith")>]
        member __.ParseEventWith(state, parseEvent): Configuration<'InputEvent, 'OutputEvent> =
            state |> addParseEvent parseEvent

        [<CustomOperation("checkKafkaWith")>]
        member __.CheckKafkaWith(state, checker): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts -> { parts with KafkaChecker = Some checker }

        [<CustomOperation("connect")>]
        member __.Connect(state, connectionConfiguration): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts -> { parts with Connections = parts.Connections.Add(Connections.Default, connectionConfiguration) }

        [<CustomOperation("connectTo")>]
        member __.ConnectTo(state, name, connectionConfiguration): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts -> { parts with Connections = parts.Connections.Add(ConnectionName name, connectionConfiguration) }

        [<CustomOperation("connectManyToBroker")>]
        member __.ConnectManyToBroker(state, connectionConfigurations: ManyTopicsConnectionConfiguration): Configuration<'InputEvent, 'OutputEvent> =
            state |> addConnectToMany connectionConfigurations

        [<CustomOperation("useSupervision")>]
        member __.Supervision(state, connectionConfiguration): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts -> { parts with Connections = parts.Connections.Add(Connections.Supervision, connectionConfiguration) }

        [<CustomOperation("consume")>]
        member __.Consume(state, consumeHandler): Configuration<'InputEvent, 'OutputEvent> =
            state |> addDefaultConsumeHandler consumeHandler

        [<CustomOperation("consumeFrom")>]
        member __.ConsumeFrom(state, name, consumeHandler): Configuration<'InputEvent, 'OutputEvent> =
            state |> addConsumeHandlerForConnection name consumeHandler

        [<CustomOperation("consumeLast")>]
        member __.ConsumeLast(state, consumeHandler): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts -> { parts with ConsumeHandlers = { Connection = Connections.Default; Handler = ConsumeHandler.LastEvent consumeHandler } :: parts.ConsumeHandlers }

        [<CustomOperation("consumeLastFrom")>]
        member __.ConsumeLastFrom(state, name, consumeHandler): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts -> { parts with ConsumeHandlers = { Connection = ConnectionName name; Handler = ConsumeHandler.LastEvent consumeHandler } :: parts.ConsumeHandlers }

        [<CustomOperation("onConsumeError")>]
        member __.OnConsumeError(state, onConsumeError): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts -> { parts with OnConsumeErrorHandlers = parts.OnConsumeErrorHandlers.Add(Connections.Default, onConsumeError) }

        [<CustomOperation("onConsumeErrorFor")>]
        member __.OnConsumeErrorFor(state, name, onConsumeError): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts -> { parts with OnConsumeErrorHandlers = parts.OnConsumeErrorHandlers.Add(ConnectionName name, onConsumeError) }

        [<CustomOperation("produceTo")>]
        member __.ProduceTo(state, name, fromDomain): Configuration<'InputEvent, 'OutputEvent> =
            state |> addProduceTo name fromDomain

        [<CustomOperation("produceToMany")>]
        member __.ProduceToMany(state, topics, fromDomain): Configuration<'InputEvent, 'OutputEvent> =
            state |> addProduceToMany topics fromDomain

        [<CustomOperation("onProducerError")>]
        member __.OnProducerError(state, producerErrorHandler): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts -> { parts with ProducerErrorHandler = Some producerErrorHandler }

        /// Add other configuration and merge it with current.
        /// New configuration values have higher priority. New values (only those with Some value) will replace already set configuration values.
        /// (Except of logger)
        [<CustomOperation("merge")>]
        member __.Merge(state, configuration): Configuration<'InputEvent, 'OutputEvent> =
            configuration |> mergeConfiguration state

        /// Start an asynchronous web server on http://127.0.0.1:8080 and shows metrics for prometheus.
        [<CustomOperation("showMetricsOn")>]
        member __.ShowMetricsOn(state, route): Configuration<'InputEvent, 'OutputEvent> =
            state >>= fun parts ->
                result {
                    let! route =
                        route
                        |> MetricsRoute.create
                        |> Result.mapError InvalidRoute

                    return { parts with MetricsRoute = Some route }
                }
                |> Result.mapError MetricsError

        [<CustomOperation("showInputEventsWith")>]
        member __.ShowInputEventsWith(state, createInputEventKeys): Configuration<'InputEvent, 'OutputEvent> =
            state |> addCreateInputEventKeys createInputEventKeys

        [<CustomOperation("showOutputEventsWith")>]
        member __.ShowOutputEventsWith(state, createOutputEventKeys): Configuration<'InputEvent, 'OutputEvent> =
            state |> addCreateOutputEventKeys createOutputEventKeys

        [<CustomOperation("showCustomMetric")>]
        member __.ShowCustomMetric(state, name, metricType, description): Configuration<'InputEvent, 'OutputEvent> =
            state >>= fun parts ->
                result {
                    let! metricName =
                        name
                        |> MetricName.create
                        |> Result.mapError InvalidMetricName

                    let customMetric = {
                        Name = metricName
                        Type = metricType
                        Description = description
                    }

                    return { parts with CustomMetrics = customMetric :: parts.CustomMetrics }
                }
                |> Result.mapError MetricsError

        [<CustomOperation("registerCustomMetric")>]
        member __.RegisterCustomMetric(state, customMetric): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts -> { parts with CustomMetrics = customMetric :: parts.CustomMetrics }

        [<CustomOperation("checkResourceInInterval")>]
        member __.CheckResourceInInterval(state, checker, resource, interval): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts ->
                let resource = {
                    Resource = resource
                    Interval = interval
                    Checker = checker
                }

                { parts with IntervalResourceCheckers = resource :: parts.IntervalResourceCheckers }

        [<CustomOperation("runCustomTask")>]
        member __.RunCustomTask(state, restartPolicy, task): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts -> { parts with CustomTasks = PreparedCustomTask (restartPolicy, task) :: parts.CustomTasks }

        [<CustomOperation("addRoute")>]
        member __.AddRoute(state, route): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts -> { parts with WebServerSettings = route :: parts.WebServerSettings }
