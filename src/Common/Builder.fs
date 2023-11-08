namespace Alma.KafkaApplication

module ApplicationBuilder =
    open System
    open System.Threading
    open Microsoft.Extensions.Logging
    open Alma.Kafka
    open Alma.Kafka.MetaData
    open Alma.KafkaApplication
    open Alma.Metrics
    open Alma.Metrics.ServiceStatus
    open Alma.ServiceIdentification
    open Alma.Environment
    open Alma.ErrorHandling
    open Alma.ErrorHandling.AsyncResult.Operators
    open Alma.ErrorHandling.Option.Operators

    [<AutoOpen>]
    module internal KafkaApplicationBuilder =
        let private traceConfiguration (parts: ConfigurationParts<_, _, _>) =
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
        let mergeConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> currentConfiguration newConfiguration: Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            currentConfiguration >>= fun currentParts ->
                newConfiguration <!> fun newParts ->
                    {
                        LoggerFactory = currentParts.LoggerFactory
                        Environment = newParts.Environment |> Envs.update currentParts.Environment
                        Instance = newParts.Instance <??> currentParts.Instance
                        CurrentEnvironment = newParts.CurrentEnvironment <??> currentParts.CurrentEnvironment
                        Initialize = newParts.Initialize <??> newParts.Initialize
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
                        WebServerPort = newParts.WebServerPort
                    }
                |> Configuration.result

        let private addConsumeHandler<'InputEvent, 'OutputEvent, 'Dependencies> configuration consumeHandler connectionName: Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            configuration <!> fun parts -> { parts with ConsumeHandlers = { Connection = connectionName; Handler = consumeHandler} :: parts.ConsumeHandlers }

        let addDefaultConsumeHandler<'InputEvent, 'OutputEvent, 'Dependencies> consumeHandler configuration: Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            Connections.Default |> addConsumeHandler configuration consumeHandler

        let addConsumeHandlerForConnection<'InputEvent, 'OutputEvent, 'Dependencies> name consumeHandler configuration: Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            ConnectionName name |> addConsumeHandler configuration consumeHandler

        let addProduceTo<'InputEvent, 'OutputEvent, 'Dependencies> name (fromDomain: OutputFromDomain<'OutputEvent>) configuration: Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            configuration <!> fun parts ->
                let connectionName = ConnectionName name
                {
                    parts with
                        ProduceTo = connectionName :: parts.ProduceTo
                        FromDomain = parts.FromDomain.Add(connectionName, fromDomain)
                }

        let addProduceToMany<'InputEvent, 'OutputEvent, 'Dependencies> topics fromDomain configuration: Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
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

        let addCreateInputEventKeys<'InputEvent, 'OutputEvent, 'Dependencies> createInputEventKeys configuration: Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            configuration <!> fun parts -> { parts with CreateInputEventKeys = Some (CreateInputEventKeys createInputEventKeys) }

        let addCreateOutputEventKeys<'InputEvent, 'OutputEvent, 'Dependencies> createOutputEventKeys configuration: Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            configuration <!> fun parts -> { parts with CreateOutputEventKeys = Some (CreateOutputEventKeys createOutputEventKeys) }

        let addParseEvent<'InputEvent, 'OutputEvent, 'Dependencies> parseEvent configuration: Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            configuration <!> fun parts -> { parts with ParseEvent = Some parseEvent }

        let addConnectToMany<'InputEvent, 'OutputEvent, 'Dependencies> connectionConfigurations configuration: Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
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

        let private prepareProducer<'OutputEvent>
            logger
            checker
            markAsDisabled
            prepareProducer
            (produceMessage: ConnectedProducer -> Alma.Tracing.Trace -> MessageToProduce -> unit)
            (connections: Connections)
            fromDomain
            (incrementOutputCount: OutputStreamName -> 'OutputEvent -> unit)
            name: Result<PreparedProducer<'OutputEvent>, ProduceError> =
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

                let fromDomain: FromDomainAsyncResult<'OutputEvent> = fun serialize event ->
                    match fromDomain with
                    | FromDomain fromDomain -> fromDomain serialize event |> AsyncResult.ofSuccess
                    | FromDomainResult fromDomain -> fromDomain serialize event |> AsyncResult.ofResult
                    | FromDomainAsyncResult fromDomain -> fromDomain serialize event

                let produceEvent producer { Trace = trace; Event = event } = asyncResult {
                    let! serializedEvent =
                        event
                        |> fromDomain Serializer.toJson

                    serializedEvent
                    |> produceMessage producer trace

                    event
                    |> incrementOutputCount
                }

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

        let private composeRuntimeConsumeHandlersForConnections<'InputEvent, 'OutputEvent, 'Dependencies>
            runtimeConsumerConfigurations
            (getErrorHandler: ConnectionName -> ConsumeErrorHandler)
            incrementInputCount
            ({ Connection = connection; Handler = handler }: ConsumeHandlerForConnection<'InputEvent, 'OutputEvent, 'Dependencies>) =
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

        let buildApplication createProducer produceMessage (Configuration configuration): KafkaApplication<'InputEvent, 'OutputEvent, 'Dependencies> =
            /// Overriden Result.ofOption operator to return a KafkaApplicationError instead of generic error
            let inline (<?!>) o errorMessage =
                o <?!> (sprintf "[KafkaApplicationBuilder] %s" errorMessage |> ErrorMessage |> KafkaApplicationError)

            result {
                let! configurationParts = configuration

                let logger = LoggerFactory.createLogger configurationParts.LoggerFactory "KafkaApplication.Build"
                ApplicationState.build logger

                //
                // required parts
                //
                let! instance = configurationParts.Instance <?!> "Instance is required."
                let! currentEnvironment = configurationParts.CurrentEnvironment <?!> "Current environment is required."
                let! connections = configurationParts.Connections |> assertNotEmpty (ErrorMessage "At least one connection configuration is required.")
                let! consumeHandlers = configurationParts.ConsumeHandlers |> assertNotEmpty (ErrorMessage "At least one consume handler is required.")
                let! parseEvent = configurationParts.ParseEvent <?!> "Parse event is required."

                //
                // optional parts
                //
                let initialization = configurationParts.Initialize <?=> Initialization id
                let defaultProduceErrorHandler: ProducerErrorHandler = (fun _ _ -> ProducerErrorPolicy.RetryIn 60<Alma.KafkaApplication.Second>)
                let producerErrorHandler = configurationParts.ProducerErrorHandler <?=> defaultProduceErrorHandler

                let defaultConsumeErrorHandler: ConsumeErrorHandler = (fun _ _ -> RetryIn 60<Alma.KafkaApplication.Second>)
                let getErrorHandler connection =
                    configurationParts.OnConsumeErrorHandlers |> Map.tryFind connection
                    <?=> defaultConsumeErrorHandler

                let spot = configurationParts.Spot <?=> { Zone = Zone "all"; Bucket = Bucket "common" }
                let box = Create.Box(instance, spot)

                let environment = configurationParts.Environment
                let defaultGroupId = configurationParts.GroupId <?=> GroupId.Random
                let groupIds = configurationParts.GroupIds
                let defaultCommitMessage = configurationParts.CommitMessage <?=> CommitMessage.Automatically
                let commitMessages = configurationParts.CommitMessages
                let kafkaChecker = configurationParts.KafkaChecker <?=> Checker.defaultChecker
                let kafkaIntervalChecker = IntervalChecker.defaultChecker   // todo<later> - allow passing custom interval checker

                let gitCommit =
                    configurationParts.Git.Commit
                    |> Option.map (fun (Alma.ApplicationStatus.GitCommit g) -> GitCommit g)
                    <?=> GitCommit "unknown"

                let dockerImageVersion = configurationParts.DockerImageVersion <?=> DockerImageVersion "unknown"

                //
                // composed parts
                //

                let cancellation = {
                    Main = new CancellationTokenSource()
                    Children = new CancellationTokenSource()
                }

                // kafka parts
                let kafkaLogger runtimeConnection = LoggerFactory.createLogger configurationParts.LoggerFactory (sprintf "KafkaApplication.Kafka<%s>" runtimeConnection)
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
                                Cancellation = Some cancellation.Children.Token
                                CountLag = false
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
                        let { Connection = (ConnectionName connection); Producer = producer; Produce = produce } = preparedProducer

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
                    LoggerFactory = configurationParts.LoggerFactory
                    IncrementMetric = incrementMetric
                    SetMetric = setMetric
                    Box = box
                    CurrentEnvironment = currentEnvironment
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

                let consume: RuntimeConsumeEvents<'InputEvent, 'OutputEvent, 'Dependencies> =
                    fun runtimeParts ->
                        let parseEvent = Event.parse (parseEvent runtimeParts)
                        fun configuration ->
                            Consumer.consumeAsync configuration parseEvent

                let customTasks =
                    configurationParts.CustomTasks
                    |> CustomTasks.prepare {
                        LoggerFactory = configurationParts.LoggerFactory
                        Box = box
                        Environment = environment
                        IncrementMetric = incrementMetric
                        SetMetric = setMetric
                        EnableResource = enableResource
                        DisableResource = disableResource
                        ConsumerConfigurations = runtimeConsumerConfigurations
                    }

                return {
                    LoggerFactory = configurationParts.LoggerFactory
                    Initialize = initialization
                    Cancellation = cancellation
                    Environment = environment
                    Box = box
                    CurrentEnvironment = currentEnvironment
                    Git = configurationParts.Git
                    DockerImageVersion = configurationParts.DockerImageVersion
                    ConsumerConfigurations = runtimeConsumerConfigurations
                    ConsumeHandlers = runtimeConsumeHandlers
                    ConsumeEvents = consume
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
                    WebServerPort = configurationParts.WebServerPort
                }
            }
            |> KafkaApplication

    //
    // Kafka Application Builder computation expression
    //

    type KafkaApplicationBuilder<'InputEvent, 'OutputEvent, 'Dependencies, 'Application> internal (buildApplication: Configuration<'InputEvent, 'OutputEvent, 'Dependencies> -> 'Application) =
        member __.Yield (_): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            defaultParts
            |> Ok
            |> Configuration

        member __.Run(state: Configuration<'InputEvent, 'OutputEvent, 'Dependencies>) =
            buildApplication state

        [<CustomOperation("useLoggerFactory")>]
        member __.Logger(state, loggerFactory: ILoggerFactory): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun parts -> { parts with LoggerFactory = loggerFactory }

        [<CustomOperation("useInstance")>]
        member __.Instance(state, instance): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun parts -> { parts with Instance = Some instance }

        [<CustomOperation("useCurrentEnvironment")>]
        member __.CurrentEnvironment(state, currentEnvironment): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun parts -> { parts with CurrentEnvironment = Some currentEnvironment }

        [<CustomOperation("initialize")>]
        member __.Initialize(state, initialize): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun parts -> { parts with Initialize = Some (Initialization initialize) }

        [<CustomOperation("initialize")>]
        member __.Initialize(state, initialize): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun parts -> { parts with Initialize = Some (InitializationResult initialize) }

        [<CustomOperation("initialize")>]
        member __.Initialize(state, initialize): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun parts -> { parts with Initialize = Some (InitializationAsyncResult initialize) }

        [<CustomOperation("useGit")>]
        member __.Git(state, git): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun parts -> { parts with Git = git }

        [<CustomOperation("useDockerImageVersion")>]
        member __.DockerImageVersion(state, dockerImageVersion): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun parts -> { parts with DockerImageVersion = Some dockerImageVersion }

        [<CustomOperation("useSpot")>]
        member __.Spot(state, spot): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun parts -> { parts with Spot = Some spot }

        [<CustomOperation("useGroupId")>]
        member __.GroupId(state, groupId): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun parts -> { parts with GroupId = Some groupId }

        [<CustomOperation("useGroupIdFor")>]
        member __.GroupIdFor(state, name, groupId): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun parts -> { parts with GroupIds = parts.GroupIds.Add(ConnectionName name, groupId) }

        [<CustomOperation("useCommitMessage")>]
        member __.CommitMessage(state, commitMessage): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun parts -> { parts with CommitMessage = Some commitMessage }

        [<CustomOperation("useCommitMessageFor")>]
        member __.CommitMessageFor(state, name, commitMessage): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun parts -> { parts with CommitMessages = parts.CommitMessages.Add(ConnectionName name, commitMessage) }

        [<CustomOperation("parseEventWith")>]
        member __.ParseEventWith(state, parseEvent): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state |> addParseEvent (fun _ -> (ParseEvent parseEvent))

        [<CustomOperation("parseEventWith")>]
        member __.ParseEventWith(state, parseEvent): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state |> addParseEvent (fun _ -> (ParseEventResult parseEvent))

        [<CustomOperation("parseEventWith")>]
        member __.ParseEventWith(state, parseEvent): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state |> addParseEvent (fun _ -> (ParseEventAsyncResult parseEvent))

        [<CustomOperation("parseEventAndUseApplicationWith")>]
        member __.ParseEventAndUseApplicationWith(state, parseEvent): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state |> addParseEvent (parseEvent >> ParseEvent)

        [<CustomOperation("parseEventAndUseApplicationWith")>]
        member __.ParseEventAndUseApplicationWith(state, parseEvent): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state |> addParseEvent (parseEvent >> ParseEventResult)

        [<CustomOperation("parseEventAndUseApplicationWith")>]
        member __.ParseEventAndUseApplicationWith(state, parseEvent): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state |> addParseEvent (parseEvent >> ParseEventAsyncResult)

        [<CustomOperation("checkKafkaWith")>]
        member __.CheckKafkaWith(state, checker): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun parts -> { parts with KafkaChecker = Some checker }

        [<CustomOperation("connect")>]
        member __.Connect(state, connectionConfiguration): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun parts -> { parts with Connections = parts.Connections.Add(Connections.Default, connectionConfiguration) }

        [<CustomOperation("connectTo")>]
        member __.ConnectTo(state, name, connectionConfiguration): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun parts -> { parts with Connections = parts.Connections.Add(ConnectionName name, connectionConfiguration) }

        [<CustomOperation("connectManyToBroker")>]
        member __.ConnectManyToBroker(state, connectionConfigurations: ManyTopicsConnectionConfiguration): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state |> addConnectToMany connectionConfigurations

        [<CustomOperation("useSupervision")>]
        member __.Supervision(state, connectionConfiguration): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun parts -> { parts with Connections = parts.Connections.Add(Connections.Supervision, connectionConfiguration) }

        [<CustomOperation("consume")>]
        member __.Consume(state, consumeHandler): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state |> addDefaultConsumeHandler (ConsumeEvents consumeHandler)

        [<CustomOperation("consume")>]
        member __.Consume(state, consumeHandler): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state |> addDefaultConsumeHandler (ConsumeEventsResult consumeHandler)

        [<CustomOperation("consume")>]
        member __.Consume(state, consumeHandler): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state |> addDefaultConsumeHandler (ConsumeEventsAsyncResult consumeHandler)

        [<CustomOperation("consumeFrom")>]
        member __.ConsumeFrom(state, name, consumeHandler): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state |> addConsumeHandlerForConnection name (ConsumeEvents consumeHandler)

        [<CustomOperation("consumeFrom")>]
        member __.ConsumeFrom(state, name, consumeHandler): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state |> addConsumeHandlerForConnection name (ConsumeEventsResult consumeHandler)

        [<CustomOperation("consumeFrom")>]
        member __.ConsumeFrom(state, name, consumeHandler): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state |> addConsumeHandlerForConnection name (ConsumeEventsAsyncResult consumeHandler)

        [<CustomOperation("onConsumeError")>]
        member __.OnConsumeError(state, onConsumeError): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun parts -> { parts with OnConsumeErrorHandlers = parts.OnConsumeErrorHandlers.Add(Connections.Default, onConsumeError) }

        [<CustomOperation("onConsumeErrorFor")>]
        member __.OnConsumeErrorFor(state, name, onConsumeError): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun parts -> { parts with OnConsumeErrorHandlers = parts.OnConsumeErrorHandlers.Add(ConnectionName name, onConsumeError) }

        [<CustomOperation("produceTo")>]
        member __.ProduceTo(state, name, fromDomain): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state |> addProduceTo name (FromDomain fromDomain)

        [<CustomOperation("produceTo")>]
        member __.ProduceTo(state, name, fromDomain): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state |> addProduceTo name (FromDomainResult fromDomain)

        [<CustomOperation("produceTo")>]
        member __.ProduceTo(state, name, fromDomain): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state |> addProduceTo name (FromDomainAsyncResult fromDomain)

        [<CustomOperation("produceToMany")>]
        member __.ProduceToMany(state, topics, fromDomain): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state |> addProduceToMany topics (FromDomain fromDomain)

        [<CustomOperation("produceToMany")>]
        member __.ProduceToMany(state, topics, fromDomain): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state |> addProduceToMany topics (FromDomainResult fromDomain)

        [<CustomOperation("produceToMany")>]
        member __.ProduceToMany(state, topics, fromDomain): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state |> addProduceToMany topics (FromDomainAsyncResult fromDomain)

        [<CustomOperation("onProducerError")>]
        member __.OnProducerError(state, producerErrorHandler): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun parts -> { parts with ProducerErrorHandler = Some producerErrorHandler }

        /// Add other configuration and merge it with current.
        /// New configuration values have higher priority. New values (only those with Some value) will replace already set configuration values.
        /// (Except of logger)
        [<CustomOperation("merge")>]
        member __.Merge(state, configuration): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            configuration |> mergeConfiguration state

        /// Show metrics for prometheus on the internal web server at http://127.0.0.1:8080/metrics.
        [<CustomOperation("showMetrics")>]
        member __.ShowMetrics(state): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun parts -> { parts with ShowMetrics = true }

        /// Show metrics for prometheus on the internal web server at http://127.0.0.1:{PORT}/metrics.
        [<CustomOperation("showMetrics")>]
        member __.ShowMetrics(state, port): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun parts -> { parts with ShowMetrics = true; WebServerPort = WebServer.Port port }

        [<CustomOperation("showInputEventsWith")>]
        member __.ShowInputEventsWith(state, createInputEventKeys): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state |> addCreateInputEventKeys createInputEventKeys

        [<CustomOperation("showOutputEventsWith")>]
        member __.ShowOutputEventsWith(state, createOutputEventKeys): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state |> addCreateOutputEventKeys createOutputEventKeys

        [<CustomOperation("showCustomMetric")>]
        member this.ShowCustomMetric(state, name, metricType, description): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
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
        member __.RegisterCustomMetric(state, customMetric): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun parts -> { parts with CustomMetrics = customMetric :: parts.CustomMetrics }

        [<CustomOperation("checkResourceInInterval")>]
        member __.CheckResourceInInterval(state, checker, resource, interval): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun parts ->
                let resource = {
                    Resource = resource
                    Interval = interval
                    Checker = checker
                }

                { parts with IntervalResourceCheckers = resource :: parts.IntervalResourceCheckers }

        [<CustomOperation("runCustomTask")>]
        member __.RunCustomTask(state, name, restartPolicy, task): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun parts -> { parts with CustomTasks = PreparedCustomTask (CustomTaskName name, restartPolicy, task) :: parts.CustomTasks }

        [<CustomOperation("addHttpHandler")>]
        member __.AddHttpHandler(state, httpHandler): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun parts -> { parts with HttpHandlers = httpHandler :: parts.HttpHandlers }

        [<CustomOperation("showAppRootStatus")>]
        member __.ShowAppRootStatus(state): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun parts -> { parts with ShowAppRootStatus = true }
