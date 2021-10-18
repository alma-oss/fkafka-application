namespace Lmc.KafkaApplication.Filter

[<RequireQualifiedAccess>]
module internal Filter =
    open Lmc.Kafka
    open Lmc.ErrorHandling
    open Lmc.ServiceIdentification

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

        let private isValueAllowed allowedValues = function
            | None -> true
            | Some value ->
                match allowedValues with
                | [] -> true
                | allowedValues ->
                    allowedValues
                    |> List.contains value

        let isAllowedBy { Spots = spots; FilterValues = filterValues } (spot, value) =
            Some spot |> isValueAllowed spots
            || value |> isValueAllowed filterValues

    [<RequireQualifiedAccess>]
    module Filtering =
        open Lmc.KafkaApplication

        let filterByConfiguration<'InputEvent, 'OutputEvent, 'FilterValue when 'FilterValue: equality>
            (getCommonEvent: GetCommonEvent<'InputEvent, 'OutputEvent>)
            (getFilterValue: GetFilterValue<'InputEvent, 'FilterValue>)
            (configuration: FilterConfiguration<'FilterValue>)
            (tracedEvent: TracedEvent<'InputEvent>) =

            let inputEvent = tracedEvent |> TracedEvent.event

            let commonEvent: CommonEvent =
                inputEvent
                |> Input
                |> getCommonEvent
            let spot = Create.Spot(commonEvent.Zone, commonEvent.Bucket)

            let filterValue =
                inputEvent
                |> getFilterValue

            if (spot, filterValue) |> Configuration.isAllowedBy configuration then Some tracedEvent
            else None
