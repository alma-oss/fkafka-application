namespace Lmc.KafkaApplication.Filter


[<RequireQualifiedAccess>]
module internal Filter =
    open Lmc.Consents.Intent
    open Lmc.ErrorHandling
    open Lmc.ServiceIdentification

    [<RequireQualifiedAccess>]
    module Configuration =
        open FSharp.Data
        open System.IO

        type private ConfigurationSchema = JsonProvider<"src/Filter/schema/configuration.json", SampleIsList=true>

        let parse path = result {
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
                Intents =
                    configuration.Filter.Intents
                    |> Seq.map (fun intent -> { Purpose = IntentPurpose intent.Purpose; Scope = IntentScope intent.Scope } )
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

        let isAllowedBy { Spots = spots; Intents = intents } (spot, intent) =
            Some spot |> isValueAllowed spots
            || intent |> isValueAllowed intents

    [<RequireQualifiedAccess>]
    module Filtering =
        open Lmc.Kafka
        open Lmc.KafkaApplication

        let filterByConfiguration getCommonEvent getIntent configuration tracedEvent =
            let inputEvent = tracedEvent |> TracedEvent.event

            let commonEvent: CommonEvent =
                inputEvent
                |> Input
                |> getCommonEvent
            let spot = Create.Spot(commonEvent.Zone, commonEvent.Bucket)

            let intent: Intent option =
                inputEvent
                |> getIntent

            if (spot, intent) |> Configuration.isAllowedBy configuration then Some tracedEvent
            else None
