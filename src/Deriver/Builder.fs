namespace KafkaApplication.Deriver

module DeriverBuilder =
    open KafkaApplication
    open KafkaApplication.Pattern
    open KafkaApplication.Pattern.PatternBuilder
    open ApplicationBuilder

    module DeriverApplicationBuilder =
        let addDeriverConfiguration<'InputEvent, 'OutputEvent>
            (ConnectionName deriverOutputStream)
            (deriveEvent: DeriveEvent<'InputEvent, 'OutputEvent>)
            (getCommonEventData: GetCommonEventData<'InputEvent, 'OutputEvent>)
            (configuration: Configuration<'InputEvent, 'OutputEvent>): Configuration<'InputEvent, 'OutputEvent> =

            //let filterConsumeHandler (app: ConsumeRuntimeParts<'OutputEvent>) (events: 'InputEvent seq) =
            //    events
            //    |> Seq.choose (filterByConfiguration getCommonEventData filterConfiguration)
            //    |> Seq.collect filterContentFromInputEvent
            //    |> Seq.iter app.ProduceTo.[filterOutputStream]

            configuration
            //|> addDefaultConsumeHandler filterConsumeHandler
            //|> addCreateInputEventKeys (Metrics.createKeysForInputEvent getCommonEventData)
            //|> addCreateOutputEventKeys (Metrics.createKeysForOutputEvent getCommonEventData)

        let buildDeriver<'InputEvent, 'OutputEvent>
            (buildApplication: Configuration<'InputEvent, 'OutputEvent> -> KafkaApplication<'InputEvent, 'OutputEvent>)
            (DeriverApplicationConfiguration state: DeriverApplicationConfiguration<'InputEvent, 'OutputEvent>): DeriverApplication<'InputEvent, 'OutputEvent> =

            result {
                let! deriverParts = state

                let! deriveTo =
                    deriverParts.DeriveTo
                    |> Result.ofOption MissingOutputStream
                    |> Result.mapError DeriverConfigurationError

                let! getCommonEventData =
                    deriverParts.GetCommonEventData
                    |> Result.ofOption MissingGetCommonEventData
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
                    |> addDeriverConfiguration deriveTo deriveEvent getCommonEventData
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

        member __.Bind(state, f): DeriverApplicationConfiguration<'InputEvent, 'OutputEvent> =
            state >>= f

        member __.Run(state: DeriverApplicationConfiguration<'InputEvent, 'OutputEvent>) =
            buildApplication state

        [<CustomOperation("from")>]
        member __.From(state, configuration): DeriverApplicationConfiguration<'InputEvent, 'OutputEvent> =
            state >>= fun parts ->
                match parts.Configuration with
                | None -> Ok { parts with Configuration = Some configuration }
                | _ -> AlreadySetConfiguration |> ApplicationConfigurationError |> Error

        [<CustomOperation("deriveTo")>]
        member __.DeriveTo(state, name, deriveEvent, fromDomain): DeriverApplicationConfiguration<'InputEvent, 'OutputEvent> =
            state >>= fun parts ->
                result {
                    let! configuration =
                        parts.Configuration
                        |> Result.ofOption ConfigurationNotSet
                        |> Result.mapError ApplicationConfigurationError

                    return {
                        parts with
                            Configuration = Some (configuration |> addProduceTo name fromDomain)
                            DeriveTo = Some (ConnectionName name)
                            DeriveEvent = Some deriveEvent
                    }
                }

        [<CustomOperation("getCommonEventDataBy")>]
        member __.GetCommonEventDataBy(state, getCommonEventData): DeriverApplicationConfiguration<'InputEvent, 'OutputEvent> =
            state <!> fun filterparts -> { filterparts with GetCommonEventData = Some getCommonEventData }
