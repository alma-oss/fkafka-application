namespace KafkaApplication.Filter

module internal Filter =
    open System.IO
    open FSharp.Data
    open ServiceIdentification
    open Kafka
    open KafkaApplication.Pattern

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
        }

    let private isValueAllowed allowedValues value =
        match allowedValues with
        | [] -> true
        | allowedValues ->
            allowedValues
            |> List.contains value

    let private isAllowedBy { Spots = spots } zone bucket =
        { Zone = zone; Bucket = bucket } |> isValueAllowed spots

    let filterByConfiguration getCommonEvent configuration inputEvent =
        let isAllowedBy = isAllowedBy configuration

        let commonEvent: CommonEvent =
            inputEvent
            |> Input
            |> getCommonEvent

        match { Zone = commonEvent.Zone; Bucket = commonEvent.Bucket} with
        | { Zone = zone; Bucket = bucket } when isAllowedBy zone bucket -> Some inputEvent
        | _ -> None
