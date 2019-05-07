namespace KafkaApplication.Filter

open ServiceIdentification

module internal Filter =
    open System.IO
    open FSharp.Data

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

    let filterByConfiguration getCommonEventData configuration inputEvent =
        let isAllowedBy = isAllowedBy configuration

        let commonEventData =
            inputEvent
            |> Input
            |> getCommonEventData

        match commonEventData.Spot with
        | { Zone = zone; Bucket = bucket } when isAllowedBy zone bucket -> Some inputEvent
        | _ -> None
