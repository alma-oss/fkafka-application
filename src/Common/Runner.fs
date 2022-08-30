namespace Lmc.KafkaApplication

module internal ApplicationRunner =
    open System
    open System.Threading
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

        let private checkResources (application: KafkaApplicationParts<_, _, _>) =
            let instance = application.Box |> Box.instance
            let enable = ResourceAvailability.enable instance >> ignore
            let disable = ResourceAvailability.disable instance >> ignore

            let logger = LoggerFactory.createLogger application.LoggerFactory "KafkaApplication.Resource"
            let debugResourceResult resource = sprintf "Checked %A with %A" resource >> logger.LogTrace

            application.IntervalResourceCheckers
            |> List.rev
            |> List.map (fun { Resource = resource; Interval = interval; Checker = checker } ->
                resource, async {
                    let intervalInMilliseconds = (int interval) * 1000
                    logger.LogDebug("Start checking for {resource} in interval of {interval}s", resource, interval)

                    while true do
                        logger.LogTrace("Check {resource}", resource)
                        let result = checker()
                        logger.LogTrace("Checked {resource} with {result}", resource, result)

                        match result with
                        | Up -> enable resource
                        | Down -> disable resource

                        do! Async.Sleep intervalInMilliseconds
                }
            )
            |> List.iter (fun (resource, check) ->
                check
                |> Async.startAndAllowCancellation logger $"Check resource {resource}" application.Cancellation.Children
            )

        let internal wait (seconds: int<Lmc.KafkaApplication.Second>) =
            Async.Sleep(TimeSpan.FromSeconds (float seconds))

        let rec private connectProducersWithErrorHandling connectProducer application = async {
            try
                return application.Producers |> Map.map (fun _ -> connectProducer)
            with
            | e ->
                let logger = LoggerFactory.createLogger application.LoggerFactory "KafkaApplication.ConnectProducers"

                logger.LogError("Producer error: {error}", e)
                let log = logger.LogInformation
                let isApplicationRunning = ApplicationState.isRunning logger ()

                match application.ProducerErrorHandler logger e with
                | ProducerErrorPolicy.Retry | ProducerErrorPolicy.RetryIn _ when not isApplicationRunning ->
                    log "Application is not running, so retry is not possible."
                    return raise e

                | ProducerErrorPolicy.Shutdown ->
                    log "Producer could not connect, application will shutdown."
                    return failwithf "Producer could not connect due to:\n\t%A" e.Message

                | ProducerErrorPolicy.ShutdownIn seconds ->
                    log <| sprintf "Producer could not connect, application will shutdown in %i seconds." seconds
                    ApplicationState.shutDown logger
                    do! wait seconds
                    return failwithf "Producer could not connect due to:\n\t%A" e.Message

                | ProducerErrorPolicy.Retry ->
                    log "Producer could not connect, try again."
                    return! connectProducersWithErrorHandling connectProducer application

                | ProducerErrorPolicy.RetryIn seconds ->
                    log <| sprintf "Producer could not connect, try again in %i seconds." seconds
                    ApplicationState.retry logger
                    do! wait seconds
                    ApplicationState.run logger
                    return! connectProducersWithErrorHandling connectProducer application
        }

        let private produceInstanceStarted produceSingleMessage (loggerFactory: ILoggerFactory) box (supervisionProducer: ConnectedProducer) =
            box
            |> InstanceStartedEvent.create
            |> InstanceStartedEvent.fromDomain Serializer.toJson
            |> produceSingleMessage supervisionProducer

            (LoggerFactory.createLogger loggerFactory "KafkaApplication.Supervision")
                .LogDebug "Instance started produced."

        let private consume<'InputEvent>
            (logger: ILogger)
            (consumeEvents: ConsumeEventsWithConfiguration<'InputEvent>)
            (incrementInputCount: 'InputEvent -> unit)
            (configuration: ConsumerConfiguration)
            : RuntimeConsumeHandler<'InputEvent> -> unit =

            let handleEvent (handle: TracedEvent<'InputEvent> -> IO<unit>) (event: ParsedEventAsyncResult<'InputEvent>): Async<unit> = async {
                match! event with
                | Ok event ->
                    let handledEvent =
                        event.Message
                        |> TracedEvent.startHandle
                        |> tee (TracedEvent.event >> incrementInputCount)

                    try
                        match! handle handledEvent with
                        | Ok () ->
                            match event.Commit |> ManualCommit.execute with
                            | Ok () -> handledEvent.Finish()
                            | Error commitError ->
                                logger.LogError("Event was handled but manual commit failed with {manualCommitError}", commitError)

                                handledEvent.Trace
                                |> Trace.addError (TracedError.ofError (sprintf "%A") commitError)
                                |> Trace.finish

                                match commitError with
                                | ManualCommitError.KafkaException e -> raise e
                                | e -> failwithf "%A" e

                        | Error (RuntimeError e) -> raise e
                        | Error error ->
                            error
                            |> ErrorMessage.value
                            |> String.concat "\n"
                            |> failwithf "Handle event failed on error: %s"

                    with e ->
                        handledEvent.Trace
                        |> Trace.addError (TracedError.ofExn e)
                        |> Trace.finish
                        raise e

                | Error ConsumeError.PreviousMessageWasNotCommited ->
                    raise (Confluent.Kafka.KafkaException(Confluent.Kafka.Error(Confluent.Kafka.ErrorCode.Local_Fail, "PreviousMessageWasNotCommited")))
                | Error (ConsumeError.KafkaException e) -> raise e
                | Error e -> failwithf "%A" e
            }

            function
            | Events eventsHandler ->
                let topic = configuration.Connection.Topic |> StreamName.value
                logger.LogDebug("Starting to consume events from {topic}", topic)

                configuration
                |> consumeEvents
                |> Seq.takeWhile (ignore >> ApplicationState.isRunning logger)
                |> Seq.iter (handleEvent eventsHandler >> Async.runSynchronouslyAndFinishTheTask logger $"Consume events from {topic}")

        let rec private consumeWithErrorHandling<'InputEvent, 'OutputEvent, 'Dependencies>
            (loggerFactory: ILoggerFactory)
            (runtimeParts: ConsumeRuntimeParts<'OutputEvent, 'Dependencies>)
            flushProducers
            consumeEvents
            (consumeHandler: RuntimeConsumeHandlerForConnection<'InputEvent, 'OutputEvent, 'Dependencies>) = async {

            let logger = LoggerFactory.createLogger loggerFactory (sprintf "KafkaApplication.Kafka<%s>" consumeHandler.Connection)
            try
                try
                    consumeHandler.Handler
                    |> ConsumeHandler.toRuntime runtimeParts
                    |> consume logger (consumeEvents runtimeParts) consumeHandler.IncrementInputCount consumeHandler.Configuration
                with
                | e ->
                    logger.LogError("Consume events ends with {error}", e)
                    let log = logger.LogInformation
                    let isApplicationRunning = ApplicationState.isRunning logger ()

                    match consumeHandler.OnError logger e with
                    | Retry | RetryIn _ when not isApplicationRunning ->
                        log "Application is not running, so retry is not possible."

                    | Continue when not isApplicationRunning ->
                        log "Application is not running, so retry is not possible."

                    | Retry ->
                        log "Retry current consume ..."
                        return! consumeWithErrorHandling loggerFactory runtimeParts flushProducers consumeEvents consumeHandler

                    | RetryIn seconds ->
                        log <| sprintf "Retry current consume in %i seconds ..." seconds
                        ApplicationState.retry logger
                        do! wait seconds
                        ApplicationState.run logger
                        return! consumeWithErrorHandling loggerFactory runtimeParts flushProducers consumeEvents consumeHandler

                    | Continue ->
                        log "Continue to next consume ..."

                    | Shutdown ->
                        log "Shutting down the application ..."
                        ApplicationState.shutDown logger
                        raise e

                    | ShutdownIn seconds ->
                        log <| sprintf "Shutting down the application in %i seconds ..." seconds
                        ApplicationState.shutDown logger
                        do! wait seconds
                        raise e
            finally
                logger.LogDebug "Flush all producers ..."
                flushProducers()
        }

        let private doWithAllProducers producers action =
            producers
            |> Map.toList
            |> List.map snd
            |> List.iter action

        let run<'InputEvent, 'OutputEvent, 'Dependencies>
            connectProducer
            produceSingleMessage
            flushProducer
            closeProducer
            (application: KafkaApplicationParts<'InputEvent, 'OutputEvent, 'Dependencies>): AsyncResult<unit, ErrorMessage> = asyncResult {
                let logger = LoggerFactory.createLogger application.LoggerFactory "KafkaApplication"
                ApplicationState.run logger
                logger.LogDebug("With configuration: {configuration}", application)

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
                |> Async.startAndAllowCancellation logger "WebServer" application.Cancellation.Children

                logger.LogDebug "Connect producers ..."
                let! connectedProducers =
                    application
                    |> connectProducersWithErrorHandling connectProducer
                    |> AsyncResult.ofAsyncCatch RuntimeError
                logger.LogDebug "All producers are connected."

                let doWithAllProducers = doWithAllProducers connectedProducers

                let! runtimeParts =
                    application.PreparedRuntimeParts
                    |> PreparedConsumeRuntimeParts.toRuntimeParts connectedProducers
                    |> ApplicationInitialization.initialize application.Initialize

                connectedProducers
                |> Map.tryFind (Connections.Supervision |> ConnectionName.runtimeName)
                |>! produceInstanceStarted produceSingleMessage application.LoggerFactory application.Box

                let flushAllProducers () = flushProducer |> doWithAllProducers

                try
                    do!
                        application.ConsumeHandlers
                        |> List.rev
                        |> List.map (consumeWithErrorHandling application.LoggerFactory runtimeParts flushAllProducers application.ConsumeEvents)
                        |> AsyncResult.ofSequentialAsyncs RuntimeError
                        |> AsyncResult.ignore
                        |> AsyncResult.mapError Errors
                finally
                    ApplicationState.shutDown logger
                    logger.LogDebug "Close producers ..."
                    closeProducer |> doWithAllProducers
        }

        let runCustomTasks (app: KafkaApplicationParts<_,_, _>) =
            let logger = LoggerFactory.createLogger app.LoggerFactory "KafkaApplication.CustomTask"

            let rec runSafely (CustomTask (CustomTaskName name, restartPolicy, task) as currentTask) =
                let onError (error: exn) =
                    logger.LogError("Custom task ends with {error}", error)

                    match restartPolicy with
                    | TaskErrorPolicy.Ignore -> ()
                    | TaskErrorPolicy.Restart -> currentTask |> runSafely

                task
                |> AsyncResult.ofAsyncCatch id
                |> AsyncResult.teeError onError
                |> Async.Ignore
                |> Async.startAndAllowCancellation logger $"Custom Task {name}" app.Cancellation.Children

            app.CustomTasks
            |> List.iter runSafely

    let startKafkaApplication: RunKafkaApplication<'InputEvent, 'OutputEvent, 'Dependencies> =
        fun beforeRun (KafkaApplication application) ->
            match application with
            | Ok app ->
                let logger = LoggerFactory.createLogger app.LoggerFactory "KafkaApplication"

                try
                    use cancellation = app.Cancellation

                    use _ = GracefulShutdown.enable logger cancellation
                    ApplicationState.start logger

                    app
                    |> tee beforeRun
                    |> tee KafkaApplicationRunner.runCustomTasks
                    |> KafkaApplicationRunner.run
                        Producer.connect
                        Producer.produceSingle
                        Producer.flush
                        Producer.close
                    |> Async.runSynchronouslyAndAllowCancellation logger "Application Run" cancellation.Main
                    |> Result.mapError ErrorMessage.value
                    |> Result.orFail

                    Successfully
                with
                | error ->
                    error
                    |> tee (
                        sprintf "Exception:\n%A"
                        >> logger.LogError
                        >> fun _ -> logger.LogInformation "Shutting down with runtime error ..."
                    )
                    |> RuntimeError
                    |> WithRuntimeError

            | Error error ->
                error
                |> sprintf "[Critical Error] Application cannot start because of %A"
                |> ErrorMessage
                |> WithCriticalError
