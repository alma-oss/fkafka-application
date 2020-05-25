namespace Lmc.KafkaApplication.Deriver

module DeriverBuilder =
    open Lmc.KafkaApplication
    open Lmc.KafkaApplication.PatternBuilder
    open Lmc.KafkaApplication.PatternMetrics
    open ApplicationBuilder
    open OptionOperators

    [<AutoOpen>]
    module internal DeriverApplicationBuilder =
        let addDeriverConfiguration<'InputEvent, 'OutputEvent>
            (ConnectionName deriverOutputStream)
            (deriveEventHandler: DeriveEventHandler<'InputEvent, 'OutputEvent>)
            (createCustomValues: CreateCustomValues<'InputEvent, 'OutputEvent>)
            (getCommonEvent: GetCommonEvent<'InputEvent, 'OutputEvent>)
            (configuration: Configuration<'InputEvent, 'OutputEvent>): Configuration<'InputEvent, 'OutputEvent> =

            let deriveEventHandler (app: ConsumeRuntimeParts<'OutputEvent>) (events: 'InputEvent seq) =
                let deriveEvent =
                    match deriveEventHandler with
                    | Simple deriveEvent -> deriveEvent
                    | WithApplication deriveEvent -> deriveEvent (app |> PatternRuntimeParts.fromConsumeParts)

                events
                |> Seq.collect (deriveEvent app.ProcessedBy)
                |> Seq.iter app.ProduceTo.[deriverOutputStream]

            configuration
            |> addDefaultConsumeHandler deriveEventHandler
            |> addCreateInputEventKeys (createKeysForInputEvent createCustomValues getCommonEvent)
            |> addCreateOutputEventKeys (createKeysForOutputEvent createCustomValues getCommonEvent)

        let addDeriveTo<'InputEvent, 'OutputEvent> name deriveEventHandler fromDomain (parts: DeriverParts<'InputEvent, 'OutputEvent>) =
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

        let buildDeriver<'InputEvent, 'OutputEvent>
            (buildApplication: Configuration<'InputEvent, 'OutputEvent> -> KafkaApplication<'InputEvent, 'OutputEvent>)
            (DeriverApplicationConfiguration state: DeriverApplicationConfiguration<'InputEvent, 'OutputEvent>): DeriverApplication<'InputEvent, 'OutputEvent> =

            result {
                let! deriverParts = state

                let! deriveTo =
                    deriverParts.DeriveTo
                    |> Result.ofOption MissingOutputStream
                    |> Result.mapError DeriverConfigurationError

                let createCustomValues = deriverParts.CreateCustomValues <?=> (fun _ -> [])

                let! getCommonEvent =
                    deriverParts.GetCommonEvent
                    |> Result.ofOption MissingGetCommonEvent
                    |> Result.mapError DeriverConfigurationError

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

    type DeriverBuilder<'InputEvent, 'OutputEvent, 'a> internal (buildApplication: DeriverApplicationConfiguration<'InputEvent, 'OutputEvent> -> 'a) =
        let (>>=) (DeriverApplicationConfiguration configuration) f =
            configuration
            |> Result.bind ((tee (debugPatternConfiguration (PatternName "Deriver") (fun { Configuration = c } -> c))) >> f)
            |> DeriverApplicationConfiguration

        let (<!>) state f =
            state >>= (f >> Ok)

        member __.Yield (_): DeriverApplicationConfiguration<'InputEvent, 'OutputEvent> =
            DeriverParts.defaultDeriver
            |> Ok
            |> DeriverApplicationConfiguration

        member __.Run(state: DeriverApplicationConfiguration<'InputEvent, 'OutputEvent>) =
            buildApplication state

        [<CustomOperation("from")>]
        member __.From(state, configuration): DeriverApplicationConfiguration<'InputEvent, 'OutputEvent> =
            state >>= fun deriverParts ->
                match deriverParts.Configuration with
                | None -> Ok { deriverParts with Configuration = Some configuration }
                | _ -> AlreadySetConfiguration |> ApplicationConfigurationError |> Error

        [<CustomOperation("deriveTo")>]
        member __.DeriveTo(state, name, deriveEvent, fromDomain): DeriverApplicationConfiguration<'InputEvent, 'OutputEvent> =
            state >>= addDeriveTo name (Simple deriveEvent) fromDomain

        [<CustomOperation("deriveToWithApplication")>]
        member __.DeriveToWithApp(state, name, deriveEvent, fromDomain): DeriverApplicationConfiguration<'InputEvent, 'OutputEvent> =
            state >>= addDeriveTo name (WithApplication deriveEvent) fromDomain

        [<CustomOperation("getCommonEventBy")>]
        member __.GetCommonEventBy(state, getCommonEvent): DeriverApplicationConfiguration<'InputEvent, 'OutputEvent> =
            state <!> fun deriverParts -> { deriverParts with GetCommonEvent = Some getCommonEvent }

        [<CustomOperation("addCustomMetricValues")>]
        member __.AddCustomMetricValues(state, createCustomValues): DeriverApplicationConfiguration<'InputEvent, 'OutputEvent> =
            state <!> fun deriverParts -> { deriverParts with CreateCustomValues = Some createCustomValues }
