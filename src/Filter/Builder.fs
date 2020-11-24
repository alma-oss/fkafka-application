namespace Lmc.KafkaApplication.Filter

module FilterBuilder =
    open Lmc.KafkaApplication
    open Lmc.KafkaApplication.PatternBuilder
    open Lmc.KafkaApplication.PatternMetrics
    open Lmc.ErrorHandling
    open ApplicationBuilder
    open OptionOperators
    open Filter

    module internal FilterApplicationBuilder =
        let addFilterConfiguration<'InputEvent, 'OutputEvent>
            filterConfiguration
            (ConnectionName filterOutputStream)
            (filterContentFromInputEvent: FilterContent<'InputEvent, 'OutputEvent>)
            (createCustomValues: CreateCustomValues<'InputEvent, 'OutputEvent>)
            (getCommonEvent: GetCommonEvent<'InputEvent, 'OutputEvent>)
            (getIntent: GetIntent<'InputEvent>)
            (configuration: Configuration<'InputEvent, 'OutputEvent>): Configuration<'InputEvent, 'OutputEvent> =

            let filterConsumeHandler (app: ConsumeRuntimeParts<'OutputEvent>) (events: 'InputEvent seq) =
                events
                |> Seq.choose (filterByConfiguration getCommonEvent getIntent filterConfiguration)
                |> Seq.collect (filterContentFromInputEvent app.ProcessedBy)
                |> Seq.iter app.ProduceTo.[filterOutputStream]

            configuration
            |> addDefaultConsumeHandler filterConsumeHandler
            |> addCreateInputEventKeys (createKeysForInputEvent createCustomValues getCommonEvent)
            |> addCreateOutputEventKeys (createKeysForOutputEvent createCustomValues getCommonEvent)

        let buildFilter<'InputEvent, 'OutputEvent>
            (buildApplication: Configuration<'InputEvent, 'OutputEvent> -> KafkaApplication<'InputEvent, 'OutputEvent>)
            (FilterApplicationConfiguration state: FilterApplicationConfiguration<'InputEvent, 'OutputEvent>): FilterApplication<'InputEvent, 'OutputEvent> =

            result {
                let! filterParts = state

                let! filterConfiguration =
                    filterParts.FilterConfiguration
                    |> Result.ofOption NotSet
                    |> Result.mapError FilterConfigurationError

                let! filterTo =
                    filterParts.FilterTo
                    |> Result.ofOption MissingOutputStream
                    |> Result.mapError FilterConfigurationError

                let getIntent = filterParts.GetIntent <?=> (fun _ -> None)
                let createCustomValues = filterParts.CreateCustomValues <?=> (fun _ -> [])

                let! getCommonEvent =
                    filterParts.GetCommonEvent
                    |> Result.ofOption MissingGetCommonEvent
                    |> Result.mapError FilterConfigurationError

                let! filterContent =
                    filterParts.FilterContent
                    |> Result.ofOption MissingFilterContent
                    |> Result.mapError FilterConfigurationError

                let! configuration =
                    filterParts.Configuration
                    |> Result.ofOption ConfigurationNotSet
                    |> Result.mapError ApplicationConfigurationError

                let kafkaApplication =
                    configuration
                    |> addFilterConfiguration filterConfiguration filterTo filterContent createCustomValues getCommonEvent getIntent
                    |> buildApplication

                return {
                    Application = kafkaApplication
                    FilterConfiguration = filterConfiguration
                }
            }
            |> FilterApplication

    type FilterBuilder<'InputEvent, 'OutputEvent, 'a> internal (buildApplication: FilterApplicationConfiguration<'InputEvent, 'OutputEvent> -> 'a) =
        let (>>=) (FilterApplicationConfiguration configuration) f =
            configuration
            |> Result.bind ((tee (debugPatternConfiguration (PatternName "FilterContentFilter") (fun { Configuration = c } -> c ))) >> f)
            |> FilterApplicationConfiguration

        let (<!>) state f =
            state >>= (f >> Ok)

        member __.Yield (_): FilterApplicationConfiguration<'InputEvent, 'OutputEvent> =
            FilterParts.defaultFilter
            |> Ok
            |> FilterApplicationConfiguration

        member __.Run(state: FilterApplicationConfiguration<'InputEvent, 'OutputEvent>) =
            buildApplication state

        [<CustomOperation("parseConfiguration")>]
        member __.ParseConfiguration(state, configurationPath): FilterApplicationConfiguration<'InputEvent, 'OutputEvent> =
            state >>= fun parts ->
                result {
                    let! filter =
                        configurationPath
                        |> FileParser.parseFromPath Filter.parseFilterConfiguration (sprintf "Filter configuration was not found at \"%s\".")
                        |> Result.mapError NotFound

                    return { parts with FilterConfiguration = Some filter }
                }
                |> Result.mapError FilterConfigurationError

        [<CustomOperation("from")>]
        member __.From(state, configuration): FilterApplicationConfiguration<'InputEvent, 'OutputEvent> =
            state >>= fun parts ->
                match parts.Configuration with
                | None -> Ok { parts with Configuration = Some configuration }
                | _ -> AlreadySetConfiguration |> ApplicationConfigurationError |> Error

        [<CustomOperation("filterTo")>]
        member __.FilterTo(state, name, filterContent, fromDomain): FilterApplicationConfiguration<'InputEvent, 'OutputEvent> =
            state >>= fun filterParts ->
                result {
                    let! configuration =
                        filterParts.Configuration
                        |> Result.ofOption ConfigurationNotSet
                        |> Result.mapError ApplicationConfigurationError

                    return {
                        filterParts with
                            Configuration = Some (configuration |> addProduceTo name fromDomain)
                            FilterTo = Some (ConnectionName name)
                            FilterContent = Some filterContent
                    }
                }

        [<CustomOperation("getCommonEventBy")>]
        member __.GetCommonEventBy(state, getCommonEvent): FilterApplicationConfiguration<'InputEvent, 'OutputEvent> =
            state <!> fun filterparts -> { filterparts with GetCommonEvent = Some getCommonEvent }

        [<CustomOperation("getIntentBy")>]
        member __.GetIntentBy(state, getIntent): FilterApplicationConfiguration<'InputEvent, 'OutputEvent> =
            state <!> fun filterparts -> { filterparts with GetIntent = Some getIntent }

        [<CustomOperation("addCustomMetricValues")>]
        member __.AddCustomMetricValues(state, createCustomValues): FilterApplicationConfiguration<'InputEvent, 'OutputEvent> =
            state <!> fun filterparts -> { filterparts with CreateCustomValues = Some createCustomValues }
