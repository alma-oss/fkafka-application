namespace Lmc.KafkaApplication

module ApplicationBuilder =
    open System
    open Microsoft.Extensions.Logging
    open Lmc.Kafka
    open Lmc.Kafka.MetaData
    open Lmc.KafkaApplication
    open Lmc.Metrics
    open Lmc.Metrics.ServiceStatus
    open Lmc.ServiceIdentification
    open Lmc.Environment
    open Lmc.ErrorHandling
    open Lmc.ErrorHandling.Option.Operators

    [<AutoOpen>]
    module internal KafkaApplicationBuilder =
        let private traceConfiguration (parts: ConfigurationParts<_, _>) =
            parts.LoggerFactory
                .CreateLogger("KafkaApplication.Configuration")
                .LogTrace("Configuration: {configuration}", parts)

        let (>>=) (Configuration configuration) f =
            configuration
            |> Result.bind (f >> (Result.tee traceConfiguration))
            |> Configuration

        let (<!>) state f =
            state >>= (f >> Ok)

        /// Add other configuration and merge it with current.
        /// New configuration values have higher priority. New values (only those with Some value) will replace already set configuration values.
        /// (Except of logger)
        let mergeConfiguration<'InputEvent, 'OutputEvent> currentConfiguration newConfiguration: Configuration<'InputEvent, 'OutputEvent> =
            currentConfiguration >>= fun currentParts ->
                newConfiguration <!> fun newParts ->
                    {
                        LoggerFactory = currentParts.LoggerFactory
                        Environment = newParts.Environment |> Envs.update currentParts.Environment
                        Instance = newParts.Instance <??> currentParts.Instance
                        CurrentEnvironment = newParts.CurrentEnvironment <??> currentParts.CurrentEnvironment
                        Git = {
                            Branch = newParts.Git.Branch <??> currentParts.Git.Branch
                            Commit = newParts.Git.Commit <??> currentParts.Git.Commit
                            Repository = newParts.Git.Repository <??> currentParts.Git.Repository
                        }
                        DockerImageVersion = newParts.DockerImageVersion <??> currentParts.DockerImageVersion
                        Spot = newParts.Spot <??> currentParts.Spot
                        GroupId = newParts.GroupId <??> currentParts.GroupId
                        GroupIds = newParts.GroupIds |> Map.merge currentParts.GroupIds
                        CommitMessage = newParts.CommitMessage <??> currentParts.CommitMessage
                        CommitMessages = newParts.CommitMessages |> Map.merge currentParts.CommitMessages
                        ParseEvent = newParts.ParseEvent <??> currentParts.ParseEvent
                        Connections = newParts.Connections |> Map.merge currentParts.Connections
                        ConsumeHandlers = currentParts.ConsumeHandlers @ newParts.ConsumeHandlers
                        OnConsumeErrorHandlers = newParts.OnConsumeErrorHandlers |> Map.merge currentParts.OnConsumeErrorHandlers
                        ProduceTo = currentParts.ProduceTo @ newParts.ProduceTo
                        ProducerErrorHandler = currentParts.ProducerErrorHandler <??> newParts.ProducerErrorHandler
                        FromDomain = newParts.FromDomain |> Map.merge currentParts.FromDomain
                        ShowMetrics = newParts.ShowMetrics
                        ShowAppRootStatus = newParts.ShowAppRootStatus
                        CustomMetrics = currentParts.CustomMetrics @ newParts.CustomMetrics
                        IntervalResourceCheckers = currentParts.IntervalResourceCheckers @ newParts.IntervalResourceCheckers
                        CreateInputEventKeys = newParts.CreateInputEventKeys <??> currentParts.CreateInputEventKeys
                        CreateOutputEventKeys = newParts.CreateOutputEventKeys <??> currentParts.CreateOutputEventKeys
                        KafkaChecker = newParts.KafkaChecker <??> currentParts.KafkaChecker
                        CustomTasks = currentParts.CustomTasks @ newParts.CustomTasks
                        HttpHandlers = currentParts.HttpHandlers @ newParts.HttpHandlers
                    }
                |> Configuration.result

        let private addConsumeHandler<'InputEvent, 'OutputEvent> configuration consumeHandler connectionName: Configuration<'InputEvent, 'OutputEvent> =
            configuration <!> fun parts -> { parts with ConsumeHandlers = { Connection = connectionName; Handler = ConsumeHandler.Events consumeHandler} :: parts.ConsumeHandlers }

        let addDefaultConsumeHandler<'InputEvent, 'OutputEvent> consumeHandler configuration: Configuration<'InputEvent, 'OutputEvent> =
            Connections.Default |> addConsumeHandler configuration consumeHandler

        let addConsumeHandlerForConnection<'InputEvent, 'OutputEvent> name consumeHandler configuration: Configuration<'InputEvent, 'OutputEvent> =
            ConnectionName name |> addConsumeHandler configuration consumeHandler

        let addProduceTo<'InputEvent, 'OutputEvent> name (fromDomain: OutputFromDomain<'OutputEvent>) configuration: Configuration<'InputEvent, 'OutputEvent> =
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
            (produceMessage: ConnectedProducer -> Lmc.Tracing.Trace -> MessageToProduce -> unit)
            (connections: Connections)
            fromDomain
            incrementOutputCount
            name =
            result {
                let! connection =
                    connections
                    |> Map.tryFind name
                    |> Result.ofOption (ProduceError.MissingConnectionConfiguration name)

                let! (fromDomain: OutputFromDomain<'OutputEvent>) =
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

                let fromDomain: FromDomain<'OutputEvent> = fun serialize event ->
                    match fromDomain with
                    | FromDomain fromDomain -> fromDomain serialize event
                    | FromDomainResult fromDomain -> fromDomain serialize event |> Result.orFail
                    | FromDomainAsyncResult fromDomain -> fromDomain serialize event |> Async.RunSynchronously |> Result.orFail

                let produceEvent producer { Trace = trace; Event = event } =
                    event
                    |> tee (fromDomain Serializer.toJson >> produceMessage producer trace)
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
            /// Overriden Result.ofOption operator to return a KafkaApplicationError instead of generic error
            let inline (<?!>) o errorMessage =
                o <?!> (sprintf "[KafkaApplicationBuilder] %s" errorMessage |> KafkaApplicationError)

            result {
                let! configurationParts = configuration

                //
                // required parts
                //
                let! instance = configurationParts.Instance <?!> "Instance is required."
                let! currentEnvironment = configurationParts.CurrentEnvironment <?!> "Current environment is required."
                let! connections = configurationParts.Connections |> assertNotEmpty "At least one connection configuration is required."
                let! consumeHandlers = configurationParts.ConsumeHandlers |> assertNotEmpty "At least one consume handler is required."
                let! parseEvent = configurationParts.ParseEvent <?!> "Parse event is required."

                //
                // optional parts
                //
                let defaultProduceErrorHandler: ProducerErrorHandler = (fun _ _ -> ProducerErrorPolicy.RetryIn 60<Lmc.KafkaApplication.Second>)
                let producerErrorHandler = configurationParts.ProducerErrorHandler <?=> defaultProduceErrorHandler

                let defaultConsumeErrorHandler: ConsumeErrorHandler = (fun _ _ -> RetryIn 60<Lmc.KafkaApplication.Second>)
                let getErrorHandler connection =
                    configurationParts.OnConsumeErrorHandlers |> Map.tryFind connection
                    <?=> defaultConsumeErrorHandler

                let spot = configurationParts.Spot <?=> { Zone = Zone "all"; Bucket = Bucket "common" }
                let box = Create.Box(instance, spot)

                let loggerFactory = configurationParts.LoggerFactory
                let environment = configurationParts.Environment
                let defaultGroupId = configurationParts.GroupId <?=> GroupId.Random
                let groupIds = configurationParts.GroupIds
                let defaultCommitMessage = configurationParts.CommitMessage <?=> CommitMessage.Automatically
                let commitMessages = configurationParts.CommitMessages
                let kafkaChecker = configurationParts.KafkaChecker <?=> Checker.defaultChecker
                let kafkaIntervalChecker = IntervalChecker.defaultChecker   // todo<later> - allow passing custom interval checker

                let gitCommit =
                    configurationParts.Git.Commit
                    |> Option.map (fun (Lmc.ApplicationStatus.GitCommit g) -> GitCommit g)
                    <?=> GitCommit "unknown"

                let dockerImageVersion = configurationParts.DockerImageVersion <?=> DockerImageVersion "unknown"

                //
                // composed parts
                //

                // kafka parts
                let kafkaLogger runtimeConnection = loggerFactory.CreateLogger (sprintf "KafkaApplication.Kafka<%s>" runtimeConnection)
                let kafkaChecker brokerList = kafkaChecker |> ResourceChecker.updateResourceStatusOnCheck instance brokerList
                let kafkaIntervalChecker brokerList = kafkaIntervalChecker |> ResourceChecker.updateResourceStatusOnIntervalCheck instance brokerList

                // service status
                let! markAsEnabled =
                    ServiceStatus.markAsEnabled instance Audience.Sys
                    |> Result.mapError (MetricError >> MetricsError)
                let! markAsDisabled =
                    ServiceStatus.markAsDisabled instance Audience.Sys
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
                                Logger = kafkaLogger runtimeConnection |> Some
                                Checker = kafkaChecker connection.BrokerList |> Some
                                IntervalChecker = kafkaIntervalChecker connection.BrokerList |> Some
                                ServiceStatus = serviceStatus |> Some
                                CommitMessage = commitMessages.TryFind name <?=> defaultCommitMessage
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
                    |> Validation.ofResults
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
                    LoggerFactory = loggerFactory
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
                    |> Validation.ofResults
                    |> Result.mapError ConsumeHandlerError

                let customTasks =
                    configurationParts.CustomTasks
                    |> CustomTasks.prepare {
                        LoggerFactory = loggerFactory
                        Box = box
                        Environment = environment
                        IncrementMetric = incrementMetric
                        SetMetric = setMetric
                        EnableResource = enableResource
                        DisableResource = disableResource
                    }

                return {
                    LoggerFactory = loggerFactory
                    Environment = environment
                    Box = box
                    CurrentEnvironment = currentEnvironment
                    Git = configurationParts.Git
                    DockerImageVersion = configurationParts.DockerImageVersion
                    ParseEvent = parseEvent
                    ConsumerConfigurations = runtimeConsumerConfigurations
                    ConsumeHandlers = runtimeConsumeHandlers
                    Producers = producers
                    ProducerErrorHandler = producerErrorHandler
                    ServiceStatus = serviceStatus
                    ShowMetrics = configurationParts.ShowMetrics
                    ShowAppRootStatus = configurationParts.ShowAppRootStatus
                    CustomMetrics = configurationParts.CustomMetrics
                    IntervalResourceCheckers = configurationParts.IntervalResourceCheckers
                    PreparedRuntimeParts = preparedRuntimeParts
                    CustomTasks = customTasks
                    HttpHandlers = configurationParts.HttpHandlers
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

        [<CustomOperation("useLoggerFactory")>]
        member __.Logger(state, loggerFactory: ILoggerFactory): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts -> { parts with LoggerFactory = loggerFactory }

        [<CustomOperation("useInstance")>]
        member __.Instance(state, instance): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts -> { parts with Instance = Some instance }

        [<CustomOperation("useCurrentEnvironment")>]
        member __.CurrentEnvironment(state, currentEnvironment): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts -> { parts with CurrentEnvironment = Some currentEnvironment }

        [<CustomOperation("useGit")>]
        member __.Git(state, git): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts -> { parts with Git = git }

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

        [<CustomOperation("useCommitMessage")>]
        member __.CommitMessage(state, commitMessage): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts -> { parts with CommitMessage = Some commitMessage }

        [<CustomOperation("useCommitMessageFor")>]
        member __.CommitMessageFor(state, name, commitMessage): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts -> { parts with CommitMessages = parts.CommitMessages.Add(ConnectionName name, commitMessage) }

        [<CustomOperation("parseEventWith")>]
        member __.ParseEventWith(state, parseEvent): Configuration<'InputEvent, 'OutputEvent> =
            state |> addParseEvent (fun _ -> parseEvent)

        [<CustomOperation("parseEventAndUseApplicationWith")>]
        member __.ParseEventAndUseApplicationWith(state, parseEvent): Configuration<'InputEvent, 'OutputEvent> =
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

        [<CustomOperation("onConsumeError")>]
        member __.OnConsumeError(state, onConsumeError): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts -> { parts with OnConsumeErrorHandlers = parts.OnConsumeErrorHandlers.Add(Connections.Default, onConsumeError) }

        [<CustomOperation("onConsumeErrorFor")>]
        member __.OnConsumeErrorFor(state, name, onConsumeError): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts -> { parts with OnConsumeErrorHandlers = parts.OnConsumeErrorHandlers.Add(ConnectionName name, onConsumeError) }

        [<CustomOperation("produceTo")>]
        member __.ProduceTo(state, name, fromDomain): Configuration<'InputEvent, 'OutputEvent> =
            state |> addProduceTo name (FromDomain fromDomain)

        [<CustomOperation("produceTo")>]
        member __.ProduceTo(state, name, fromDomain): Configuration<'InputEvent, 'OutputEvent> =
            state |> addProduceTo name (FromDomainResult fromDomain)

        [<CustomOperation("produceTo")>]
        member __.ProduceTo(state, name, fromDomain): Configuration<'InputEvent, 'OutputEvent> =
            state |> addProduceTo name (FromDomainAsyncResult fromDomain)

        [<CustomOperation("produceToMany")>]
        member __.ProduceToMany(state, topics, fromDomain): Configuration<'InputEvent, 'OutputEvent> =
            state |> addProduceToMany topics (FromDomain fromDomain)

        [<CustomOperation("produceToMany")>]
        member __.ProduceToMany(state, topics, fromDomain): Configuration<'InputEvent, 'OutputEvent> =
            state |> addProduceToMany topics (FromDomainResult fromDomain)

        [<CustomOperation("produceToMany")>]
        member __.ProduceToMany(state, topics, fromDomain): Configuration<'InputEvent, 'OutputEvent> =
            state |> addProduceToMany topics (FromDomainAsyncResult fromDomain)

        [<CustomOperation("onProducerError")>]
        member __.OnProducerError(state, producerErrorHandler): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts -> { parts with ProducerErrorHandler = Some producerErrorHandler }

        /// Add other configuration and merge it with current.
        /// New configuration values have higher priority. New values (only those with Some value) will replace already set configuration values.
        /// (Except of logger)
        [<CustomOperation("merge")>]
        member __.Merge(state, configuration): Configuration<'InputEvent, 'OutputEvent> =
            configuration |> mergeConfiguration state

        /// Show metrics for prometheus on the internal web server at http://127.0.0.1:8080/metrics.
        [<CustomOperation("showMetrics")>]
        member __.ShowMetrics(state): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts -> { parts with ShowMetrics = true }

        [<CustomOperation("showInputEventsWith")>]
        member __.ShowInputEventsWith(state, createInputEventKeys): Configuration<'InputEvent, 'OutputEvent> =
            state |> addCreateInputEventKeys createInputEventKeys

        [<CustomOperation("showOutputEventsWith")>]
        member __.ShowOutputEventsWith(state, createOutputEventKeys): Configuration<'InputEvent, 'OutputEvent> =
            state |> addCreateOutputEventKeys createOutputEventKeys

        [<CustomOperation("showCustomMetric")>]
        member this.ShowCustomMetric(state, name, metricType, description): Configuration<'InputEvent, 'OutputEvent> =
            this.ShowMetrics(state) >>= fun parts ->
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

        [<CustomOperation("addHttpHandler")>]
        member __.AddHttpHandler(state, httpHandler): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts -> { parts with HttpHandlers = httpHandler :: parts.HttpHandlers }

        [<CustomOperation("showAppRootStatus")>]
        member __.ShowAppRootStatus(state): Configuration<'InputEvent, 'OutputEvent> =
            state <!> fun parts -> { parts with ShowAppRootStatus = true }
