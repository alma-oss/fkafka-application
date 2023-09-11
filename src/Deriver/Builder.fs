namespace Alma.KafkaApplication.Deriver

module DeriverBuilder =
    open Alma.KafkaApplication
    open Alma.KafkaApplication.PatternBuilder
    open Alma.KafkaApplication.PatternMetrics
    open ApplicationBuilder
    open Alma.ErrorHandling
    open Alma.ErrorHandling.Option.Operators

    let internal pattern = PatternName "Deriver"

    [<AutoOpen>]
    module internal DeriverApplicationBuilder =
        let addDeriverConfiguration<'InputEvent, 'OutputEvent, 'Dependencies>
            (ConnectionName deriverOutputStream)
            (deriveEventHandler: DeriveEventHandler<'InputEvent, 'OutputEvent, 'Dependencies>)
            (createCustomValues: CreateCustomValues<'InputEvent, 'OutputEvent>)
            (getCommonEvent: GetCommonEvent<'InputEvent, 'OutputEvent> option)
            (configuration: Configuration<'InputEvent, 'OutputEvent, 'Dependencies>): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =

            let deriveEventHandler (app: ConsumeRuntimeParts<'OutputEvent, 'Dependencies>) (event: TracedEvent<'InputEvent>) = asyncResult {
                use eventToDerive = event |> TracedEvent.continueAs "Deriver" "Derive event"

                let deriveEvent processedBy =
                    let deriveEvent =
                        match deriveEventHandler with
                        | Simple deriveEvent -> deriveEvent
                        | WithApplication deriveEvent -> deriveEvent (app |> PatternRuntimeParts.fromConsumeParts pattern)

                    match deriveEvent with
                    | DeriveEvent deriveEvent -> deriveEvent processedBy >> AsyncResult.ofSuccess
                    | DeriveEventResult deriveEvent -> deriveEvent processedBy >> AsyncResult.ofResult
                    | DeriveEventAsyncResult deriveEvent -> deriveEvent processedBy

                let! outputEvents =
                    eventToDerive
                    |> deriveEvent app.ProcessedBy

                do!
                    outputEvents
                    |> List.map app.ProduceTo.[deriverOutputStream]
                    |> IO.runList
            }

            let configuration =
                configuration
                |> addDefaultConsumeHandler (ConsumeEventsAsyncResult deriveEventHandler)

            match getCommonEvent with
            | Some getCommonEvent ->
                configuration
                |> addCreateInputEventKeys (createKeysForInputEvent createCustomValues getCommonEvent)
                |> addCreateOutputEventKeys (createKeysForOutputEvent createCustomValues getCommonEvent)
            | _ ->
                configuration

        let addDeriveTo<'InputEvent, 'OutputEvent, 'Dependencies> name deriveEventHandler fromDomain (parts: DeriverParts<'InputEvent, 'OutputEvent, 'Dependencies>) =
            result {
                let! configuration =
                    parts.Configuration
                    |> Result.ofOption ConfigurationNotSet
                    |> Result.mapError ApplicationConfigurationError

                return {
                    parts with
                        Configuration = Some (configuration |> addProduceTo name fromDomain)
                        DeriveTo = Some (ConnectionName name)
                        DeriveEvent = Some deriveEventHandler
                }
            }

        let buildDeriver<'InputEvent, 'OutputEvent, 'Dependencies>
            (buildApplication: Configuration<'InputEvent, 'OutputEvent, 'Dependencies> -> KafkaApplication<'InputEvent, 'OutputEvent, 'Dependencies>)
            (DeriverApplicationConfiguration state: DeriverApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies>): DeriverApplication<'InputEvent, 'OutputEvent, 'Dependencies> =

            result {
                let! deriverParts = state

                let! deriveTo =
                    deriverParts.DeriveTo
                    |> Result.ofOption MissingOutputStream
                    |> Result.mapError DeriverConfigurationError

                let createCustomValues = deriverParts.CreateCustomValues <?=> (fun _ -> [])

                let getCommonEvent =
                    deriverParts.GetCommonEvent

                let! deriveEvent =
                    deriverParts.DeriveEvent
                    |> Result.ofOption MissingDeriveEvent
                    |> Result.mapError DeriverConfigurationError

                let! configuration =
                    deriverParts.Configuration
                    |> Result.ofOption ConfigurationNotSet
                    |> Result.mapError ApplicationConfigurationError

                let kafkaApplication =
                    configuration
                    |> addDeriverConfiguration deriveTo deriveEvent createCustomValues getCommonEvent
                    |> buildApplication

                return {
                    Application = kafkaApplication
                }
            }
            |> DeriverApplication

    type DeriverBuilder<'InputEvent, 'OutputEvent, 'Dependencies, 'Application> internal (buildApplication: DeriverApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> -> 'Application) =
        let (>>=) (DeriverApplicationConfiguration configuration) f =
            configuration
            |> Result.bind ((tee (debugPatternConfiguration pattern (fun { Configuration = c } -> c))) >> f)
            |> DeriverApplicationConfiguration

        let (<!>) state f =
            state >>= (f >> Ok)

        member __.Yield (_): DeriverApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            DeriverParts.defaultDeriver
            |> Ok
            |> DeriverApplicationConfiguration

        member __.Run(state: DeriverApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies>) =
            buildApplication state

        [<CustomOperation("from")>]
        member __.From(state, configuration): DeriverApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state >>= fun deriverParts ->
                match deriverParts.Configuration with
                | None -> Ok { deriverParts with Configuration = Some configuration }
                | _ -> AlreadySetConfiguration |> ApplicationConfigurationError |> Error

        [<CustomOperation("getCommonEventBy")>]
        member __.GetCommonEventBy(state, getCommonEvent): DeriverApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun deriverParts -> { deriverParts with GetCommonEvent = Some getCommonEvent }

        [<CustomOperation("addCustomMetricValues")>]
        member __.AddCustomMetricValues(state, createCustomValues): DeriverApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state <!> fun deriverParts -> { deriverParts with CreateCustomValues = Some createCustomValues }

        /// Base method to add a generic deriveEvent and fromDomain functions
        member internal __.AddDeriveTo(state, name, deriveEvent, fromDomain): DeriverApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            state >>= addDeriveTo name deriveEvent fromDomain

        // Derive to plain methods

        [<CustomOperation("deriveTo")>]
        member this.DeriveTo(state, name, deriveEvent, fromDomain): DeriverApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            this.AddDeriveTo(state, name, Simple (DeriveEvent deriveEvent), FromDomain fromDomain)

        [<CustomOperation("deriveToWithApplication")>]
        member this.DeriveToWithApp(state, name, deriveEvent, fromDomain): DeriverApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            this.AddDeriveTo(state, name, WithApplication (deriveEvent >> DeriveEvent), FromDomain fromDomain)

        // Derive to Result overloads

        [<CustomOperation("deriveTo")>]
        member this.DeriveTo(state, name, deriveEvent, fromDomain): DeriverApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            this.AddDeriveTo(state, name, Simple (DeriveEventResult deriveEvent), FromDomain fromDomain)

        [<CustomOperation("deriveTo")>]
        member this.DeriveTo(state, name, deriveEvent, fromDomain): DeriverApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            this.AddDeriveTo(state, name, Simple (DeriveEvent deriveEvent), FromDomainResult fromDomain)

        [<CustomOperation("deriveTo")>]
        member this.DeriveTo(state, name, deriveEvent, fromDomain): DeriverApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            this.AddDeriveTo(state, name, Simple (DeriveEventResult deriveEvent), FromDomainResult fromDomain)

        [<CustomOperation("deriveToWithApplication")>]
        member this.DeriveToWithApp(state, name, deriveEvent, fromDomain): DeriverApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            this.AddDeriveTo(state, name, WithApplication (deriveEvent >> DeriveEventResult), FromDomain fromDomain)

        [<CustomOperation("deriveToWithApplication")>]
        member this.DeriveToWithApp(state, name, deriveEvent, fromDomain): DeriverApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            this.AddDeriveTo(state, name, WithApplication (deriveEvent >> DeriveEvent), FromDomainResult fromDomain)

        [<CustomOperation("deriveToWithApplication")>]
        member this.DeriveToWithApp(state, name, deriveEvent, fromDomain): DeriverApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            this.AddDeriveTo(state, name, WithApplication (deriveEvent >> DeriveEventResult), FromDomainResult fromDomain)

        // Derive to AsyncResult overloads

        [<CustomOperation("deriveTo")>]
        member this.DeriveTo(state, name, deriveEvent, fromDomain): DeriverApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            this.AddDeriveTo(state, name, Simple (DeriveEventAsyncResult deriveEvent), FromDomain fromDomain)

        [<CustomOperation("deriveTo")>]
        member this.DeriveTo(state, name, deriveEvent, fromDomain): DeriverApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            this.AddDeriveTo(state, name, Simple (DeriveEvent deriveEvent), FromDomainAsyncResult fromDomain)

        [<CustomOperation("deriveTo")>]
        member this.DeriveTo(state, name, deriveEvent, fromDomain): DeriverApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            this.AddDeriveTo(state, name, Simple (DeriveEventAsyncResult deriveEvent), FromDomainAsyncResult fromDomain)

        [<CustomOperation("deriveToWithApplication")>]
        member this.DeriveToWithApp(state, name, deriveEvent, fromDomain): DeriverApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            this.AddDeriveTo(state, name, WithApplication (deriveEvent >> DeriveEventAsyncResult), FromDomain fromDomain)

        [<CustomOperation("deriveToWithApplication")>]
        member this.DeriveToWithApp(state, name, deriveEvent, fromDomain): DeriverApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            this.AddDeriveTo(state, name, WithApplication (deriveEvent >> DeriveEvent), FromDomainAsyncResult fromDomain)

        [<CustomOperation("deriveToWithApplication")>]
        member this.DeriveToWithApp(state, name, deriveEvent, fromDomain): DeriverApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            this.AddDeriveTo(state, name, WithApplication (deriveEvent >> DeriveEventAsyncResult), FromDomainAsyncResult fromDomain)

        // Derive to AsyncResult x Result overloads

        [<CustomOperation("deriveTo")>]
        member this.DeriveTo(state, name, deriveEvent, fromDomain): DeriverApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            this.AddDeriveTo(state, name, Simple (DeriveEventResult deriveEvent), FromDomainAsyncResult fromDomain)

        [<CustomOperation("deriveTo")>]
        member this.DeriveTo(state, name, deriveEvent, fromDomain): DeriverApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            this.AddDeriveTo(state, name, Simple (DeriveEventAsyncResult deriveEvent), FromDomainResult fromDomain)

        [<CustomOperation("deriveToWithApplication")>]
        member this.DeriveToWithApp(state, name, deriveEvent, fromDomain): DeriverApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            this.AddDeriveTo(state, name, WithApplication (deriveEvent >> DeriveEventResult), FromDomainAsyncResult fromDomain)

        [<CustomOperation("deriveToWithApplication")>]
        member this.DeriveToWithApp(state, name, deriveEvent, fromDomain): DeriverApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies> =
            this.AddDeriveTo(state, name, WithApplication (deriveEvent >> DeriveEventAsyncResult), FromDomainResult fromDomain)
