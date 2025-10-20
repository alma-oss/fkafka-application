namespace Alma.KafkaApplication.Compressor

module CompressorBuilder =
    open Microsoft.Extensions.Logging
    open Alma.KafkaApplication
    open Alma.KafkaApplication.PatternBuilder
    open Alma.KafkaApplication.PatternMetrics
    open ApplicationBuilder
    open Alma.ErrorHandling
    open Alma.ServiceIdentification
    open Alma.ErrorHandling.Option.Operators
    open Alma.ErrorHandling.Result.Operators
    open Alma.Tracing

    let internal pattern = PatternName "Compressor"

    /// Connection used by compressor pattern, currently only the default connection could be compressed
    let internal connection = Connections.Default |> ConnectionName.runtimeName

    [<AutoOpen>]
    module internal CompressorApplicationBuilder =
        let private registerMetrics configuration =
            CompressorMetrics.metrics
            |> List.fold (fun c m -> registerCustomMetric m c) configuration

        [<RequireQualifiedAccess>]
        module private SendBatch =
            open System.Diagnostics
            open Microsoft.Extensions.Logging
            open AsyncResult.Operators

            let prepare (sendBatch: ConsumeRuntimeParts<'OutputEvent, 'Dependencies> -> SendBatch<'OutputEvent>) (app: ConsumeRuntimeParts<'OutputEvent, 'Dependencies>): SendBatch<'OutputEvent> =
                let instance = app.Box |> Box.instance
                let logger = app.LoggerFactory.CreateLogger "Compressor.SendBatch"
                let observeBatchSendDuration = CompressorMetrics.observeBatchSendDuration instance

                let sendBatch =
                    sendBatch app
                    >@@> (
                        ErrorMessage.format
                        >> tee (CompressorMetrics.incrementBatchSendFailure instance)
                        >> fun e -> logger.LogError("Failed to send batch: {Error}", e)
                    )

                fun batch ->
                    async {
                        let stopwatch = Stopwatch.StartNew()
                        let! result =
                            batch
                            |> sendBatch
                            |> AsyncResult.ofAsyncCatch RuntimeError
                        stopwatch.Stop()

                        observeBatchSendDuration stopwatch.Elapsed

                        return result |> Result.concat
                    }
                    |> AsyncResult.retryWithExponential logger.LogError 500 5

        let addCompressorConfiguration<'InputEvent, 'OutputEvent, 'Dependencies>
            batch
            batchThreshold
            (setOffset: ConsumeRuntimeParts<'OutputEvent, 'Dependencies> -> SetOffset)
            (pickEventHandler: PickEventHandler<'InputEvent, 'OutputEvent, 'Dependencies>)
            (sendBatch: SendBatchHandler<'OutputEvent, 'Dependencies>)
            (createCustomValues: CreateCustomValues<'InputEvent, 'OutputEvent>)
            (getCommonEvent: GetCommonEvent<'InputEvent, 'OutputEvent> option)
            (configuration: Configuration<'InputEvent, 'OutputEvent, 'Dependencies>): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =

            let sendBatch (app: ConsumeRuntimeParts<'OutputEvent, 'Dependencies>) =
                (app.LoggerFactory.CreateLogger "Compressor.init").LogDebug "prepare sendBatch"
                match sendBatch with
                | SendBatchHandler.Simple sendBatch -> sendBatch
                | SendBatchHandler.WithApplication sendBatch -> sendBatch (app |> PatternRuntimeParts.fromConsumeParts pattern)

            let pickEvent (app: ConsumeRuntimeParts<'OutputEvent, 'Dependencies>) processedBy =
                (app.LoggerFactory.CreateLogger "Compressor.init").LogDebug "prepare pickEvent"
                let pickEvent =
                    match pickEventHandler with
                    | Simple pickEvent -> pickEvent
                    | WithApplication pickEvent -> pickEvent (app |> PatternRuntimeParts.fromConsumeParts pattern)

                match pickEvent with
                | PickEvent pickEvent -> pickEvent processedBy >> AsyncResult.ofSuccess
                | PickEventResult pickEvent -> pickEvent processedBy >> AsyncResult.ofResult
                | PickEventAsyncResult pickEvent -> pickEvent processedBy

            let pickEventHandler (app: ConsumeRuntimeParts<'OutputEvent, 'Dependencies>) =
                (app.LoggerFactory.CreateLogger "Compressor.init").LogDebug "prepare pickEventHandler"
                let sendBatch = SendBatch.prepare sendBatch app
                let pickEvent = pickEvent app app.ProcessedBy
                let setOffset = setOffset app
                let instance = app.Box |> Box.instance
                let incrementBatchCreated () = CompressorMetrics.incrementBatchCreated instance
                let observerBatchSize = CompressorMetrics.observeBatchSize instance
                let incrementBatchSent = CompressorMetrics.incrementBatchSent instance
                let groupId = app.ConsumerConfigurations[connection].GroupId

                fun (event: TracedEvent<'InputEvent>) -> asyncResult {
                    use inputEvent = event |> TracedEvent.continueAs "Compressor" "Pick event"
                    let! outputEvent = inputEvent |> pickEvent

                    outputEvent
                    |> Option.iter (fun ({ Trace = trace } as event) ->
                        trace |> Trace.addEvent "Add to batch" |> ignore
                        match event |> Batch.add batch with
                        | CreatedAndAdded ->
                            incrementBatchCreated ()
                            observerBatchSize batchThreshold
                        | Added -> ()
                    )

                    match! batch |> Batch.check batchThreshold sendBatch with
                    | Filling -> ()
                    | Sent currentSize ->
                        do!
                            InternalState.getCurrentOffsets()
                            |> List.map (setOffset groupId)
                            |> AsyncResult.ofParallelAsyncResults RuntimeError
                            |> AsyncResult.mapError Errors
                            |> AsyncResult.ignore

                        incrementBatchSent currentSize
                }

            let configuration =
                configuration
                |> addDefaultConsumeHandler (ConsumeEventsAsyncResult pickEventHandler)
                |> registerMetrics

            match getCommonEvent with
            | Some getCommonEvent ->
                configuration
                |> addCreateInputEventKeys (createKeysForInputEvent createCustomValues getCommonEvent)
            | _ ->
                configuration

        let buildCompressor<'InputEvent, 'OutputEvent, 'Dependencies>
            (buildApplication: Configuration<'InputEvent, 'OutputEvent, 'Dependencies> -> KafkaApplication<'InputEvent, 'OutputEvent, 'Dependencies>)
            (CompressorApplicationConfiguration state: CompressorApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies>): CompressorApplication<'InputEvent, 'OutputEvent, 'Dependencies> =

            result {
                let! compressorParts = state

                let! batchSize =
                    compressorParts.BatchSize
                    |> Result.ofOption (CompressorConfigurationError MissingBatchThreshold)

                let! pickEvent =
                    compressorParts.PickEvent
                    |> Result.ofOption (CompressorConfigurationError MissingPickEvent)

                let! sendBatch =
                    compressorParts.SendBatch
                    |> Result.ofOption (CompressorConfigurationError MissingSendBatch)

                let createCustomValues = compressorParts.CreateCustomValues <?=> (fun _ -> [])

                let getCommonEvent =
                    compressorParts.GetCommonEvent

                let! configuration =
                    compressorParts.Configuration
                    |> Result.ofOption (ApplicationConfigurationError ConfigurationNotSet)

                do!
                    match compressorParts.SetOffset, compressorParts.GetOffset with
                    | None, None | Some _, Some _ -> Ok ()
                    | _ -> Error (CompressorConfigurationError IncompleteOffsetHandlers)

                let setOffset app: SetOffset =
                    compressorParts.SetOffset
                    |> Option.map (function
                        | SetOffsetHandler.Simple setOffset -> setOffset
                        | SetOffsetHandler.WithApplication setOffset -> setOffset (app |> PatternRuntimeParts.fromConsumeParts pattern)
                    )
                    |> Option.defaultValue SetOffset.ignore

                let getOffset app: GetOffset option =
                    compressorParts.GetOffset
                    |> Option.map (function
                        | GetOffsetHandler.Simple getOffset -> getOffset
                        | GetOffsetHandler.WithApplication getOffset -> getOffset (app |> PatternRuntimeParts.fromConsumeParts pattern)
                    )

                let batch = Batch.init<TracedEvent<'OutputEvent>>()

                let kafkaApplication =
                    configuration
                    |> addCompressorConfiguration batch batchSize setOffset pickEvent sendBatch createCustomValues getCommonEvent
                    |> buildApplication

                return {
                    Application = kafkaApplication
                    GetOffset = getOffset
                }
            }
            |> CompressorApplication

    type CompressorBuilder<'InputEvent, 'OutputEvent, 'Dependencies, 'Application> internal (buildApplication: CompressorApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> -> 'Application) =
        let (>>=) (CompressorApplicationConfiguration configuration) f =
            configuration
            |> Result.bind ((tee (debugPatternConfiguration pattern (fun { Configuration = c } -> c))) >> f)
            |> CompressorApplicationConfiguration

        let (<!>) state f =
            state >>= (f >> Ok)

        member __.Yield (_): CompressorApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            CompressorParts.defaultCompressor
            |> Ok
            |> CompressorApplicationConfiguration

        member __.Run(state: CompressorApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies>) =
            buildApplication state

        [<CustomOperation("from")>]
        member __.From(state, configuration): CompressorApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state >>= fun compressorParts ->
                match compressorParts.Configuration with
                | None -> Ok { compressorParts with Configuration = Some configuration }
                | _ -> AlreadySetConfiguration |> ApplicationConfigurationError |> Error

        [<CustomOperation("getCommonEventBy")>]
        member __.GetCommonEventBy(state, getCommonEvent): CompressorApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun compressorParts -> { compressorParts with GetCommonEvent = Some getCommonEvent }

        [<CustomOperation("addCustomMetricValues")>]
        member __.AddCustomMetricValues(state, createCustomValues): CompressorApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun compressorParts -> { compressorParts with CreateCustomValues = Some createCustomValues }

        [<CustomOperation("batchSize")>]
        member __.AddBatchSize(state, batchSize): CompressorApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state >>= fun compressorParts ->
                match BatchThreshold.tryCreate batchSize with
                | Some batchThreshold -> Ok { compressorParts with BatchSize = Some batchThreshold }
                | None -> InvalidBatchThresholdValue batchSize |> Error

        [<CustomOperation("batchSize")>]
        member __.AddBatchSize(state, batchSizeEnvironmentKey): CompressorApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state >>= fun compressorParts ->
                result {
                    let! configuration = compressorParts.Configuration <?!> ConfigurationNotSet <@> ApplicationConfigurationError

                    let! configurationParts =
                        configuration
                        |> Configuration.result <@> (InvalidConfiguration >> ApplicationConfigurationError)

                    let! batchSize =
                        batchSizeEnvironmentKey
                        |> getEnvironmentValue configurationParts BatchThreshold.tryParse BatchThresholdVariableNotSet

                    let! batchThreshold = batchSize |> Result.ofOption (InvalidBatchThresholdVariable batchSizeEnvironmentKey)

                    return { compressorParts with BatchSize = Some batchThreshold }
                }

        /// Base method to add a generic pickEvent and fromDomain functions
        member internal __.AddPickEvent(state, pickEvent): CompressorApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun compressorParts -> { compressorParts with PickEvent = Some pickEvent }

        // Derive to plain methods

        [<CustomOperation("pickEvent")>]
        member this.PickEvent(state, pickEvent): CompressorApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            this.AddPickEvent(state, Simple (PickEvent pickEvent))

        [<CustomOperation("pickEvent")>]
        member this.PickEvent(state, pickEvent): CompressorApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            this.AddPickEvent(state, WithApplication (pickEvent >> PickEvent))

        // Derive to Result overloads

        [<CustomOperation("pickEvent")>]
        member this.PickEvent(state, pickEvent): CompressorApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            this.AddPickEvent(state, Simple (PickEventResult pickEvent))

        [<CustomOperation("pickEvent")>]
        member this.PickEvent(state, pickEvent): CompressorApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            this.AddPickEvent(state, WithApplication (pickEvent >> PickEventResult))

        // Derive to AsyncResult overloads

        [<CustomOperation("pickEvent")>]
        member this.PickEvent(state, pickEvent): CompressorApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            this.AddPickEvent(state, Simple (PickEventAsyncResult pickEvent))

        [<CustomOperation("pickEvent")>]
        member this.PickEvent(state, pickEvent): CompressorApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            this.AddPickEvent(state, WithApplication (pickEvent >> PickEventAsyncResult))

        /// Base method to add a generic sendBatch
        member internal __.AddSendBatch(state, sendBatch): CompressorApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun compressorParts -> { compressorParts with SendBatch = Some sendBatch }

        // Send batch to plain methods

        [<CustomOperation("sendBatch")>]
        member this.SendBatch(state, sendBatch): CompressorApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            this.AddSendBatch(state, SendBatchHandler.Simple sendBatch)

        [<CustomOperation("sendBatch")>]
        member this.SendBatch(state, sendBatch): CompressorApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            this.AddSendBatch(state, SendBatchHandler.WithApplication sendBatch)

        /// Base method to add a generic setOffset
        member internal __.AddSetOffset(state, setOffset): CompressorApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun compressorParts -> { compressorParts with SetOffset = Some setOffset }

        // Set Offset methods

        [<CustomOperation("setOffset")>]
        member this.SetOffset(state, setOffset): CompressorApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            this.AddSetOffset(state, SetOffsetHandler.Simple setOffset)

        [<CustomOperation("setOffset")>]
        member this.SetOffset(state, setOffset): CompressorApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            this.AddSetOffset(state, SetOffsetHandler.WithApplication setOffset)

        /// Base method to add a generic setOffset
        member internal __.AddGetOffset(state, setOffset): CompressorApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun compressorParts -> { compressorParts with GetOffset = Some setOffset }

        // Get Offset methods

        [<CustomOperation("getOffset")>]
        member this.GetOffset(state, getOffset): CompressorApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            this.AddGetOffset(state, GetOffsetHandler.Simple getOffset)

        [<CustomOperation("getOffset")>]
        member this.GetOffset(state, getOffset): CompressorApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            this.AddGetOffset(state, GetOffsetHandler.WithApplication getOffset)
