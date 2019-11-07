module KafkaApplication.Test.Router

open Expecto
open Events
open KafkaApplication
open KafkaApplication.Router
open ServiceIdentification

type ProcessTestItem = {
    Event: EventToRoute
    ProcessedBy: ProcessedBy
    Expected: string
    Description: string
}

let provideEvents =
    [
        {
            Description = "NewPersonIdentified"
            Event =
                """
                {"schema":1,"id":"fe884cd7-b182-4abd-8956-f480af733535","correlation_id":"fe884cd7-b182-4abd-8956-f480af733535","causation_id":"fe884cd7-b182-4abd-8956-f480af733535","timestamp":"2019-06-27T09:43:51.789+02:00","event":"new_person_identified","domain":"consents","context":"consentor","purpose":"devel","version":"v1v0v0","zone":"all","bucket":"devel","meta_data":{"created_at":"2019-06-27T09:43:51.799+02:00"},"resource":{"name":"persons","href":"http:\/\/consents-consentor-devel-v1v0v0.service.devel1-services.consul\/v2\/persons\/3277d9fc-9331-4758-9749-cd991f49bacc?spot=%28all%2Cdevel%29&intent=%28data_processing%2Clmc_cz%29"},"key_data":{"person_id":"3277d9fc-9331-4758-9749-cd991f49bacc"},"domain_data":{"first_name":null,"last_name":null,"email":"seduo@student.cz","phone":null,"purpose":"data_processing","scope":"lmc_cz"}}
                """
                |> EventToRoute.parse
            ProcessedBy = {
                Instance = { Domain = Domain "domain"; Context = Context "context"; Purpose = Purpose "purpose"; Version = Version "version" }
                Commit = GitCommit "g123"
                ImageVersion = DockerImageVersion "42-2019"
            }
            Expected =
                """
                {"schema":1,"id":"fe884cd7-b182-4abd-8956-f480af733535","correlation_id":"fe884cd7-b182-4abd-8956-f480af733535","causation_id":"fe884cd7-b182-4abd-8956-f480af733535","timestamp":"2019-06-27T09:43:51.789+02:00","event":"new_person_identified","domain":"consents","context":"consentor","purpose":"devel","version":"v1v0v0","zone":"all","bucket":"devel","meta_data":{"created_at":"2019-06-27T09:43:51.799+02:00","processed_at":"<NORMALIZED-TIME>","processed_by":{"instance":"domain-context-purpose-version","commit":"g123","image_version":"42-2019"}},"resource":{"name":"persons","href":"http://consents-consentor-devel-v1v0v0.service.devel1-services.consul/v2/persons/3277d9fc-9331-4758-9749-cd991f49bacc?spot=%28all%2Cdevel%29&intent=%28data_processing%2Clmc_cz%29"},"key_data":{"person_id":"3277d9fc-9331-4758-9749-cd991f49bacc"},"domain_data":{"first_name":null,"last_name":null,"email":"seduo@student.cz","phone":null,"purpose":"data_processing","scope":"lmc_cz"}}
                """
        }
    ]

let assertSerializedEvents description (expected: string) (actual: string) =
    let normalize (event: string) =
        event.Trim()
        |> Regex.replace "processed_at\":\"(\d{4}\-\d{2}\-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z)\"" "processed_at\":\"<NORMALIZED-TIME>\""
        |> String.replace "+01:00" "Z"
        |> String.replace "+02:00" "Z"

    Expect.equal (normalize actual) (normalize expected) description

[<Tests>]
let routerSerializeTests =
    testList "Router" [
        testCase "process event" <| fun _ ->
            provideEvents
            |> List.iter (fun ({ Event = event; ProcessedBy = processedBy; Expected = expected; Description = description }) ->
                event
                |> EventToRoute.route processedBy
                |> ProcessedEventToRoute.fromDomain Serializer.toJson
                |> assertSerializedEvents description expected
            )
    ]
