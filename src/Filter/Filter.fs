namespace Alma.KafkaApplication.Filter

[<RequireQualifiedAccess>]
module internal Filter =
    open Microsoft.Extensions.Logging
    open Alma.Kafka
    open Alma.ErrorHandling
    open Alma.ServiceIdentification

    [<RequireQualifiedAccess>]
    module Configuration =
        open FSharp.Data
        open System.IO

        type private ConfigurationSchema = JsonProvider<"src/Filter/schema/configuration.json", SampleIsList=true>

        let parse (parseFilterValue: RawData -> 'FilterValue) path = result {
            let configuration =
                path
                |> File.ReadAllText
                |> ConfigurationSchema.Parse

            let! spots =
                configuration.Filter.Spot
                |> Seq.map (fun parsedSpot -> Create.Spot(parsedSpot.Zone, parsedSpot.Bucket))
                |> List.ofSeq
                |> Validation.ofResults
                |> Result.mapError InvalidSpot

            return {
                Spots = spots
                FilterValues =
                    configuration.Filter.Values
                    |> Seq.map (fun value -> value.JsonValue |> RawData |> parseFilterValue)
                    |> List.ofSeq
            }
        }

        let private tee f a =
            f a
            a

        let private isValueAllowed (logger: ILogger) allowedValues = function
            | None ->
                logger.LogTrace("Value is allowed because it is empty.")
                true

            | Some value ->
                match allowedValues with
                | [] ->
                    logger.LogTrace("Value is allowed because it does not have any requirements.")
                    true

                | allowedValues ->
                    allowedValues
                    |> List.contains value
                    |> tee (fun isAllowed -> logger.LogTrace("Value {value} is {is_allowed}", value, if isAllowed then "allowed" else "not allowed"))

        let isAllowedBy (logger: ILogger) { Spots = spots; FilterValues = filterValues } (spot, value) =
            Some spot |> isValueAllowed logger spots
            && value |> isValueAllowed logger filterValues

            |> tee (fun isAllowed -> logger.LogTrace("Event with ({value_spot}, {value_filter_value}) is allowed: {is_allowed}", spot, value, isAllowed))

    [<RequireQualifiedAccess>]
    module Filtering =
        open Alma.KafkaApplication

        let filterByConfiguration<'InputEvent, 'OutputEvent, 'FilterValue when 'FilterValue: equality>
            (loggerFactory: ILoggerFactory)
            (getCommonEvent: GetCommonEvent<'InputEvent, 'OutputEvent>)
            (getFilterValue: GetFilterValue<'InputEvent, 'FilterValue>)
            (configuration: FilterConfiguration<'FilterValue>)
            (tracedEvent: TracedEvent<'InputEvent>) =

            let logger = LoggerFactory.createLogger loggerFactory "FilterEvent"
            let inputEvent = tracedEvent |> TracedEvent.event

            let commonEvent: CommonEvent =
                inputEvent
                |> Input
                |> getCommonEvent

            let spot = Create.Spot(commonEvent.Zone, commonEvent.Bucket)
            let filterValue = inputEvent |> getFilterValue

            logger.LogTrace(
                "Filter event {event_type}({event_id}) from ({event_zone},{event_bucket}) with {filter_value}",
                commonEvent.Event |> EventName.value,
                commonEvent.Id |> EventId.value,
                spot.Zone |> Zone.value,
                spot.Bucket |> Bucket.value,
                filterValue
            )

            if (spot, filterValue) |> Configuration.isAllowedBy logger configuration then Some tracedEvent
            else None
