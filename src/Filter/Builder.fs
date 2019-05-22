namespace KafkaApplication.Filter

module FilterBuilder =
    open KafkaApplication
    open KafkaApplication.Pattern
    open KafkaApplication.Pattern.PatternBuilder
    open ApplicationBuilder
    open Filter

    module FilterApplicationBuilder =
        let addFilterConfiguration<'InputEvent, 'OutputEvent>
            filterConfiguration
            (ConnectionName filterOutputStream)
            (filterContentFromInputEvent: FilterContent<'InputEvent, 'OutputEvent>)
            (getCommonEventData: GetCommonEventData<'InputEvent, 'OutputEvent>)
            (configuration: Configuration<'InputEvent, 'OutputEvent>): Configuration<'InputEvent, 'OutputEvent> =

            let filterConsumeHandler (app: ConsumeRuntimeParts<'OutputEvent>) (events: 'InputEvent seq) =
                events
                |> Seq.choose (filterByConfiguration getCommonEventData filterConfiguration)
                |> Seq.collect filterContentFromInputEvent
                |> Seq.iter app.ProduceTo.[filterOutputStream]

            configuration
            |> addDefaultConsumeHandler filterConsumeHandler
            |> addCreateInputEventKeys (Metrics.createKeysForInputEvent getCommonEventData)
            |> addCreateOutputEventKeys (Metrics.createKeysForOutputEvent getCommonEventData)

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

                let! getCommonEventData =
                    filterParts.GetCommonEventData
                    |> Result.ofOption MissingGetCommonEventData
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
                    |> addFilterConfiguration filterConfiguration filterTo filterContent getCommonEventData
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

        member __.Bind(state, f): FilterApplicationConfiguration<'InputEvent, 'OutputEvent> =
            state >>= f

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

        [<CustomOperation("getCommonEventDataBy")>]
        member __.GetCommonEventDataBy(state, getCommonEventData): FilterApplicationConfiguration<'InputEvent, 'OutputEvent> =
            state <!> fun filterparts -> { filterparts with GetCommonEventData = Some getCommonEventData }
