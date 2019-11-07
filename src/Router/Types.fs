namespace KafkaApplication.Router

open Kafka
open KafkaApplication
open Events

//
// Event data
//

type SerializedEvent = SerializedEvent of string

type EventToRoute = {
    Serialized: SerializedEvent // todo - deprecated? to remove?
    Raw: RawEvent
}

type ProcessedEventToRoute = ProcessedEventToRoute of EventToRoute * ProcessedBy

[<RequireQualifiedAccess>]
module internal EventToRoute =
    let raw { Raw = raw } = raw
    let serialized { Serialized = (SerializedEvent serialized) } = serialized

    let parse message =
        {
            Serialized = SerializedEvent message
            Raw = message |> RawEvent.parse
        }

    let route processedBy event =
        ProcessedEventToRoute (event, processedBy)


open System
type RawEventDto = {
    schema: int
    id: Guid
    correlation_id: Guid
    causation_id: Guid
    timestamp: string
    event: string
    domain: string
    context: string
    purpose: string
    version: string
    zone: string
    bucket: string
    meta_data: string
    resource: string
    key_data: string
    domain_data: string
}

module Resource =
    let toDto (resource: Resource) =
        {
            name = resource.Name
            href = resource.Href
        }

module RawData =
    open FSharp.Data

    let toJson (RawData data) =
        data.ToString(JsonSaveOptions.DisableFormatting)

[<RequireQualifiedAccess>]
module internal ProcessedEventToRoute =
    open System
    open FSharp.Data

    open Kafka
    open ServiceIdentification

    let private toDto (Serialize serialize) (ProcessedEventToRoute (event, processedBy)) =
        let serializeMetaData serialize metaData =
            match metaData with
            | Some rawMetaData ->
                let meta =
                    rawMetaData
                    |> MetaData.parse
                    |> Result.orFail

                match meta with
                | OnlyCreatedAt createdAt
                | CreatedAndProcessed (createdAt, _) ->
                    (
                        createdAt,
                        {
                            ProcessedAt = DateTime.Now
                            ProcessedBy = processedBy
                        }
                    )
                    |> MetaDataDto.fromProcessed
                |> serialize
            | _ -> null

        let event = event.Raw

        ["meta_data"; "resource"; "domain_data"],
        {
            schema = event.Schema
            id = event.Id |> EventId.value
            correlation_id = event.CorrelationId |> CorrelationId.value
            causation_id = event.CausationId |> CausationId.value
            timestamp = event.Timestamp
            event = event.Event |> EventName.value
            domain = event.Domain |> Domain.value
            context = event.Context |> Context.value
            purpose = event.Purpose |> Purpose.value
            version = event.Version |> Version.value
            zone = event.Zone |> Zone.value
            bucket = event.Bucket |> Bucket.value
            meta_data = event.MetaData |> serializeMetaData serialize
            resource =
                match event.Resource with
                | Some resource -> resource |> Resource.toDto |> serialize
                | _ -> null
            key_data = event.KeyData |> RawData.toJson
            domain_data =
                match event.DomainData with
                | Some domainData -> domainData |> RawData.toJson
                | _ -> null
        }

    let fromDomain: FromDomain<ProcessedEventToRoute> =
        fun (Serialize serialize) event ->
            let (nullableFields, dto) = event |> toDto (Serialize serialize)

            dto
            |> serialize
            |> Serializer.fixJsonSerializedInnerData
            |> Serializer.removeNullFields nullableFields

//
// Router
//

type RouterError =
    | StreamNameIsNotInstance of string

type Router = private Router of Map<EventName, StreamName>

module internal Router =
    open FSharp.Data
    open System.IO
    open ServiceIdentification

    type private RoutingSchema = JsonProvider<"src/Router/schema/routingSchema.json">

    let parse path =
        result {
            let rawRouting =
                path
                |> File.ReadAllText
                |> RoutingSchema.Parse

            let! routing =
                rawRouting.Route
                |> Seq.map (fun route ->
                    result {
                        let! topicInstance =
                            route.TargetStream
                            |> Instance.parse "-"
                            |> Result.ofOption (StreamNameIsNotInstance route.TargetStream)

                        return (EventName route.Event, StreamName.Instance topicInstance)
                    }
                )
                |> Seq.toList
                |> Result.sequence

            return
                routing
                |> Map.ofList
                |> Router
        }

    let getStreamFor event (Router router) =
        router
        |> Map.tryFind event

    let getOutputStreams (Router router) =
        router
        |> Map.toList
        |> List.map snd

// Errors

type RouterConfigurationError =
    | NotFound of string
    | NotSet
    | OutputBrokerListNotSet
    | RouterError of RouterError

type ContentBasedRouterApplicationError =
    | ApplicationConfigurationError of ApplicationConfigurationError
    | RouterConfigurationError of RouterConfigurationError
    | ConnectionConfigurationError of ConnectionConfigurationError

// Content-Based Router Application Configuration

type internal RouterParts<'InputEvent, 'OutputEvent> = {
    Configuration: Configuration<'InputEvent, 'OutputEvent> option
    RouterConfiguration: Router option
    RouteToBrokerList: BrokerList option
}

[<RequireQualifiedAccess>]
module internal RouterParts =
    let defaultRouter = {
        Configuration = None
        RouterConfiguration = None
        RouteToBrokerList = None
    }

type ContentBasedRouterApplicationConfiguration<'InputEvent, 'OutputEvent> = private ContentBasedRouterApplicationConfiguration of Result<RouterParts<'InputEvent, 'OutputEvent>, ContentBasedRouterApplicationError>

type internal ContentBasedRouterApplicationParts<'InputEvent, 'OutputEvent> = {
    Application: KafkaApplication<'InputEvent, 'OutputEvent>
    RouterConfiguration: Router
}

type ContentBasedRouterApplication<'InputEvent, 'OutputEvent> = internal ContentBasedRouterApplication of Result<ContentBasedRouterApplicationParts<'InputEvent, 'OutputEvent>, ContentBasedRouterApplicationError>

[<RequireQualifiedAccess>]
module internal ContentBasedRouterApplication =
    let application { Application = application } = application
