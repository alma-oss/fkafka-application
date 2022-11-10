namespace Lmc.KafkaApplication.Filter

module FilterBuilder =
    open Lmc.KafkaApplication
    open Lmc.KafkaApplication.PatternBuilder
    open Lmc.KafkaApplication.PatternMetrics
    open ApplicationBuilder
    open Lmc.ErrorHandling
    open Lmc.ErrorHandling.Option.Operators

    let internal pattern = PatternName "FilterContentFilter"

    module internal FilterApplicationBuilder =
        let addFilterConfiguration<'InputEvent, 'OutputEvent, 'Dependencies, 'FilterValue when 'FilterValue: equality>
            filterConfiguration
            (ConnectionName filterOutputStream)
            (filterContentFromInputEvent: FilterContent<'InputEvent, 'OutputEvent>)
            (createCustomValues: CreateCustomValues<'InputEvent, 'OutputEvent>)
            (getCommonEvent: GetCommonEvent<'InputEvent, 'OutputEvent>)
            (getFilterValue: GetFilterValue<'InputEvent, 'FilterValue>)
            (configuration: Configuration<'InputEvent, 'OutputEvent, 'Dependencies>): Configuration<'InputEvent, 'OutputEvent, 'Dependencies> =

            let filterConsumeHandler (app: ConsumeRuntimeParts<'OutputEvent, 'Dependencies>) (event: TracedEvent<'InputEvent>) = asyncResult {
                use eventToFilter = event |> TracedEvent.continueAs "Filter" "Filter event"

                let outputEvent =
                    eventToFilter
                    |> Filter.Filtering.filterByConfiguration app.LoggerFactory getCommonEvent getFilterValue filterConfiguration
                    >>= filterContentFromInputEvent app.ProcessedBy

                match outputEvent with
                | Some event -> do! event |> app.ProduceTo.[filterOutputStream]
                | _ -> ()
            }

            configuration
            |> addDefaultConsumeHandler (ConsumeEventsAsyncResult filterConsumeHandler)
            |> addCreateInputEventKeys (createKeysForInputEvent createCustomValues getCommonEvent)
            |> addCreateOutputEventKeys (createKeysForOutputEvent createCustomValues getCommonEvent)

        let buildFilter<'InputEvent, 'OutputEvent, 'Dependencies, 'FilterValue when 'FilterValue: equality>
            (buildApplication: Configuration<'InputEvent, 'OutputEvent, 'Dependencies> -> KafkaApplication<'InputEvent, 'OutputEvent, 'Dependencies>)
            (FilterApplicationConfiguration state: FilterApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies, 'FilterValue>): FilterApplication<'InputEvent, 'OutputEvent, 'Dependencies, 'FilterValue> =

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

                let getFilterValue = filterParts.GetFilterValue <?=> (fun _ -> None)
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
                    |> addFilterConfiguration filterConfiguration filterTo filterContent createCustomValues getCommonEvent getFilterValue
                    |> buildApplication

                return {
                    Application = kafkaApplication
                    FilterConfiguration = filterConfiguration
                }
            }
            |> FilterApplication

    type FilterBuilder<'InputEvent, 'OutputEvent, 'Dependencies, 'FilterValue, 'Application> internal (buildApplication: FilterApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies, 'FilterValue> -> 'Application) =
        let (>>=) (FilterApplicationConfiguration configuration) f =
            configuration
            |> Result.bind ((tee (debugPatternConfiguration pattern (fun { Configuration = c } -> c ))) >> f)
            |> FilterApplicationConfiguration

        let (<!>) state f =
            state >>= (f >> Ok)

        let addFilterTo state name filterContent fromDomain =
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

        member __.Yield (_): FilterApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies, 'FilterValue> =
            FilterParts.defaultFilter
            |> Ok
            |> FilterApplicationConfiguration

        member __.Run(state: FilterApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies, 'FilterValue>) =
            buildApplication state

        [<CustomOperation("parseConfiguration")>]
        member __.ParseConfiguration(state, parseFilterValue, configurationPath): FilterApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies, 'FilterValue> =
            state >>= fun parts ->
                result {
                    let! filter =
                        configurationPath
                        |> FileParser.parseFromPath
                            (Filter.Configuration.parse parseFilterValue)
                            (sprintf "Filter configuration was not found at \"%s\"." >> NotFound)

                    return { parts with FilterConfiguration = Some filter }
                }
                |> Result.mapError FilterConfigurationError

        [<CustomOperation("from")>]
        member __.From(state, configuration): FilterApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies, 'FilterValue> =
            state >>= fun parts ->
                match parts.Configuration with
                | None -> Ok { parts with Configuration = Some configuration }
                | _ -> AlreadySetConfiguration |> ApplicationConfigurationError |> Error

        [<CustomOperation("filterTo")>]
        member __.FilterTo(state, name, filterContent, fromDomain): FilterApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies, 'FilterValue> =
            addFilterTo state name filterContent (FromDomain fromDomain)

        [<CustomOperation("filterTo")>]
        member __.FilterTo(state, name, filterContent, fromDomain): FilterApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies, 'FilterValue> =
            addFilterTo state name filterContent (FromDomainResult fromDomain)

        [<CustomOperation("filterTo")>]
        member __.FilterTo(state, name, filterContent, fromDomain): FilterApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies, 'FilterValue> =
            addFilterTo state name filterContent (FromDomainAsyncResult fromDomain)

        [<CustomOperation("getCommonEventBy")>]
        member __.GetCommonEventBy(state, getCommonEvent): FilterApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies, 'FilterValue> =
            state <!> fun filterParts -> { filterParts with GetCommonEvent = Some getCommonEvent }

        [<CustomOperation("getFilterBy")>]
        member __.GetFilterValueBy(state, getFilterValue): FilterApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies, 'FilterValue> =
            state <!> fun filterParts -> { filterParts with GetFilterValue = Some getFilterValue }

        [<CustomOperation("addCustomMetricValues")>]
        member __.AddCustomMetricValues(state, createCustomValues): FilterApplicationConfiguration<'InputEvent, 'OutputEvent, 'Dependencies, 'FilterValue> =
            state <!> fun filterParts -> { filterParts with CreateCustomValues = Some createCustomValues }
