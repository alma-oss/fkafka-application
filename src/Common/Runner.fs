namespace Lmc.KafkaApplication

module internal ApplicationRunner =
    open Microsoft.Extensions.Logging
    open Lmc.Kafka
    open Lmc.ServiceIdentification
    open Lmc.ErrorHandling
    open Lmc.ErrorHandling.Option.Operators

    module private KafkaApplicationRunner =
        open System
        open Lmc.Metrics
        open Lmc.Metrics.ServiceStatus
        open Lmc.Tracing
        open Lmc.KafkaApplication.ApplicationEvents

        let private checkResources (application: KafkaApplicationParts<_, _>) =
            let instance = application.Box |> Box.instance
            let enable = ResourceAvailability.enable instance >> ignore
            let disable = ResourceAvailability.disable instance >> ignore

            let resourceLogger = application.LoggerFactory.CreateLogger "KafkaApplication.Resource"
            let debugResourceResult resource = sprintf "Checked %A with %A" resource >> resourceLogger.LogTrace

            application.IntervalResourceCheckers
            |> List.rev
            |> List.map (fun { Resource = resource; Interval = interval; Checker = checker } ->
                async {
                    let intervalInMilliseconds = (int interval) * 1000
                    resourceLogger.LogDebug("Start checking for {resource} in interval of {interval}s", resource, interval)

                    while true do
                        resourceLogger.LogTrace("Check {resource}", resource)
                        let result = checker()
                        resourceLogger.LogTrace("Checked {resource} with {result}", resource, result)

                        match result with
                        | Up -> enable resource
                        | Down -> disable resource

                        do! Async.Sleep intervalInMilliseconds
                }
            )
            |> List.iter Async.Start

        let internal wait (seconds: int<Lmc.KafkaApplication.Second>) =
            Threading.Thread.Sleep(TimeSpan.FromSeconds (float seconds))

        let rec private connectProducersWithErrorHandling connectProducer application =
            try
                application.Producers |> Map.map (fun _ -> connectProducer)
            with
            | e ->
                let applicationLogger = application.LoggerFactory.CreateLogger "KafkaApplication.ConnectProducers"

                applicationLogger.LogError("Producer error: {error}", e)
                let log = applicationLogger.LogInformation

                match application.ProducerErrorHandler applicationLogger e.Message with
                | ProducerErrorPolicy.Shutdown ->
                    log "Producer could not connect, application will shutdown."
                    failwithf "Producer could not connect due to:\n\t%A" e.Message
                | ProducerErrorPolicy.ShutdownIn seconds ->
                    log <| sprintf "Producer could not connect, application will shutdown in %i seconds." seconds
                    wait seconds
                    failwithf "Producer could not connect due to:\n\t%A" e.Message
                | ProducerErrorPolicy.Retry ->
                    log "Producer could not connect, try again."
                    connectProducersWithErrorHandling connectProducer application
                | ProducerErrorPolicy.RetryIn seconds ->
                    log <| sprintf "Producer could not connect, try again in %i seconds." seconds
                    wait seconds
                    connectProducersWithErrorHandling connectProducer application

        let private produceInstanceStarted produceSingleMessage (loggerFactory: ILoggerFactory) box (supervisionProducer: ConnectedProducer) =
            box
            |> InstanceStartedEvent.create
            |> InstanceStartedEvent.fromDomain Serializer.toJson
            |> produceSingleMessage supervisionProducer

            loggerFactory
                .CreateLogger("KafkaApplication.Supervision")
                .LogDebug "Instance started produced."

        let private consume<'InputEvent>
            (logger: ILogger)
            (consumeEvents: ConsumerConfiguration -> ParsedEventResult<'InputEvent> seq)
            (incrementInputCount: 'InputEvent -> unit)
            (configuration: ConsumerConfiguration)
            : RuntimeConsumeHandler<'InputEvent> -> unit =

            let handleEvent handle (event: ParsedEventResult<'InputEvent>): unit =
                match event with
                | Ok event ->
                    use handledEvent =
                        event.Message
                        |> TracedEvent.startHandle
                        |> tee (TracedEvent.event >> incrementInputCount)
                        |> tee handle

                    match event.Commit |> ManualCommit.execute with
                    | Ok () -> ()
                    | Error commitError ->
                        logger.LogError("Event was handled but manual commit failed with {manualCommitError}", commitError)

                        handledEvent.Trace
                        |> Trace.addError (TracedError.ofError (sprintf "%A") commitError)
                        |> ignore

                        match commitError with
                        | ManualCommitError.KafkaException e -> raise e
                        | e -> failwithf "%A" e

                | Error ConsumeError.PreviousMessageWasNotCommited ->
                    raise (Confluent.Kafka.KafkaException(Confluent.Kafka.Error(Confluent.Kafka.ErrorCode.Local_Fail, "PreviousMessageWasNotCommited")))
                | Error (ConsumeError.KafkaException e) -> raise e
                | Error e -> failwithf "%A" e

            function
            | Events eventsHandler ->
                configuration
                |> consumeEvents
                |> Seq.iter (handleEvent eventsHandler)

        let private consumeWithErrorHandling<'InputEvent, 'OutputEvent>
            (loggerFactory: ILoggerFactory)
            (runtimeParts: ConsumeRuntimeParts<'OutputEvent>)
            flushProducers
            (consumeEvents: ConsumerConfiguration -> ParsedEventResult<'InputEvent> seq)
            (consumeHandler: RuntimeConsumeHandlerForConnection<'InputEvent, 'OutputEvent>) =
            let logger = loggerFactory.CreateLogger (sprintf "KafkaApplication.Kafka<%s>" consumeHandler.Connection)

            let mutable runConsuming = true
            while runConsuming do
                try
                    try
                        runConsuming <- false

                        consumeHandler.Handler
                        |> ConsumeHandler.toRuntime runtimeParts
                        |> consume logger consumeEvents consumeHandler.IncrementInputCount consumeHandler.Configuration
                    with
                    | :? Confluent.Kafka.KafkaException as e ->
                        logger.LogError("Consume events ends with {error}", e)
                        let log = logger.LogInformation

                        match consumeHandler.OnError logger e.Message with
                        | Retry ->
                            runConsuming <- true
                            log "Retry current consume ..."
                        | RetryIn seconds ->
                            runConsuming <- true
                            log <| sprintf "Retry current consume in %i seconds ..." seconds
                            wait seconds
                        | Continue ->
                            log "Continue to next consume ..."
                            runConsuming <- false
                        | Shutdown ->
                            log "Shutting down the application ..."
                            raise e
                        | ShutdownIn seconds ->
                            log <| sprintf "Shutting down the application in %i seconds ..." seconds
                            wait seconds
                            raise e
                finally
                    logger.LogDebug "Flush all producers ..."
                    flushProducers()

        let private doWithAllProducers producers action =
            producers
            |> Map.toList
            |> List.map snd
            |> List.iter action

        let run<'InputEvent, 'OutputEvent>
            (consumeEvents: ParseInputEvent<'InputEvent> -> ConsumerConfiguration -> ParsedEventResult<'InputEvent> seq)
            connectProducer
            produceSingleMessage
            flushProducer
            closeProducer
            (application: KafkaApplicationParts<'InputEvent, 'OutputEvent>) =

            let applicationLogger = application.LoggerFactory.CreateLogger "KafkaApplication"
            applicationLogger.LogInformation "Starts ..."
            applicationLogger.LogDebug("With configuration: {configuration}", application)

            let instance =
                application.Box
                |> Box.instance
                |> tee (ApplicationMetrics.enableContext)

            application.ServiceStatus.MarkAsDisabled
            |> MarkAsDisabled.execute

            application |> checkResources

            async {
                let showStatus: WebServer.Show<Lmc.ApplicationStatus.ApplicationStatus> =
                    if application.ShowAppRootStatus
                    then Some (fun () -> AppRootStatus.status instance application.CurrentEnvironment application.Git application.DockerImageVersion)
                    else None

                let showMetrics: WebServer.Show<string> =
                    if application.ShowMetrics
                    then Some (fun () -> ApplicationMetrics.getMetricsState instance application.CustomMetrics)
                    else None

                application.HttpHandlers
                |> WebServer.web application.LoggerFactory showMetrics showStatus
                |> Saturn.Application.run
            }
            |> Async.Start

            applicationLogger.LogDebug "Connect producers ..."
            let connectedProducers =
                application
                |> connectProducersWithErrorHandling connectProducer
            applicationLogger.LogDebug "All producers are connected."

            let doWithAllProducers = doWithAllProducers connectedProducers

            let runtimeParts =
                application.PreparedRuntimeParts
                |> PreparedConsumeRuntimeParts.toRuntimeParts connectedProducers

            connectedProducers
            |> Map.tryFind (Connections.Supervision |> ConnectionName.runtimeName)
            |>! produceInstanceStarted produceSingleMessage application.LoggerFactory application.Box

            let flushAllProducers () = flushProducer |> doWithAllProducers

            let parseEvent = application.ParseEvent runtimeParts
            let consumeEvents = consumeEvents parseEvent

            try
                application.ConsumeHandlers
                |> List.rev
                |> List.iter (consumeWithErrorHandling application.LoggerFactory runtimeParts flushAllProducers consumeEvents)
            finally
                applicationLogger.LogDebug "Close producers ..."
                closeProducer |> doWithAllProducers

        let runCustomTasks (app: KafkaApplicationParts<_,_>) =
            let customTaskLogger = app.LoggerFactory.CreateLogger "KafkaApplication.CustomTask"

            let rec runSafely (CustomTask (restartPolicy, task)) =
                let onError (error: exn) =
                    customTaskLogger.LogError("Custom task ends with {error}", error)

                    match restartPolicy with
                    | TaskErrorPolicy.Ignore -> ()
                    | TaskErrorPolicy.Restart -> CustomTask (restartPolicy, task) |> runSafely

                task
                |> Async.Catch
                |> Async.map (Result.ofChoice >> Result.teeError onError)
                |> Async.Ignore
                |> Async.Start

            app.CustomTasks
            |> List.iter runSafely

    let runKafkaApplication: RunKafkaApplication<'InputEvent, 'OutputEvent> =
        fun beforeRun (KafkaApplication application) ->
            match application with
            | Ok app ->
                try
                    let consume parseEvent configuration: ParsedEventResult<'InputEvent> seq =
                        Consumer.consume configuration (Event.parse parseEvent)

                    app
                    |> tee beforeRun
                    |> tee KafkaApplicationRunner.runCustomTasks
                    |> KafkaApplicationRunner.run
                        consume
                        Producer.connect
                        Producer.produceSingle
                        Producer.flush
                        Producer.close
                    Successfully
                with
                | error ->
                    let logger = app.LoggerFactory.CreateLogger "KafkaApplication"
                    error
                    |> sprintf "Exception:\n%A"
                    |> tee (logger.LogError >> fun _ ->
                        logger.LogInformation "Shutting down with runtime error ..."
                        KafkaApplicationRunner.wait 2<Lmc.KafkaApplication.Second>  // Main thread waits till logger logs error message
                    )
                    |> WithRuntimeError

            | Error error ->
                error
                |> sprintf "[Critical Error] Application cannot start because of %A"
                |> WithCriticalError
