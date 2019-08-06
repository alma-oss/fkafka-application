namespace KafkaApplication.Filter

module internal Filter =
    open System.IO
    open FSharp.Data
    open ServiceIdentification
    open ContractAggregate.Intent
    open Kafka
    open KafkaApplication

    type private ConfigurationSchema = JsonProvider<"src/Filter/schema/configuration.json", SampleIsList=true>

    let parseFilterConfiguration path =
        let configuration =
            path
            |> File.ReadAllText
            |> ConfigurationSchema.Parse

        {
            Spots =
                configuration.Filter.Spot
                |> Seq.map (fun spot -> { Zone = Zone spot.Zone; Bucket = Bucket spot.Bucket } )
                |> List.ofSeq
            Intents =
                configuration.Filter.Intents
                |> Seq.map (fun intent -> { Purpose = IntentPurpose intent.Purpose; Scope = IntentScope intent.Scope } )
                |> List.ofSeq
        }

    let private isValueAllowed allowedValues = function
        | None -> true
        | Some value ->
            match allowedValues with
            | [] -> true
            | allowedValues ->
                allowedValues
                |> List.contains value

    let private isAllowedBy { Spots = spots; Intents = intents } (spot, intent) =
        Some spot |> isValueAllowed spots
        || intent |> isValueAllowed intents

    let filterByConfiguration getCommonEvent getIntent configuration inputEvent =
        let commonEvent: CommonEvent =
            inputEvent
            |> Input
            |> getCommonEvent
        let spot = { Zone = commonEvent.Zone; Bucket = commonEvent.Bucket}

        let intent: Intent option =
            inputEvent
            |> getIntent

        if (spot, intent) |> isAllowedBy configuration then Some inputEvent
        else None
