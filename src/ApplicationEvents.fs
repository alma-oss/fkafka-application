namespace KafkaApplication

module ApplicationEvents =
    open System
    open Kafka
    open ServiceIdentification

    //
    // Constants
    //

    [<LiteralAttribute>]
    let InstanceStartedEventName = "instance_started"

    //
    // Events data
    //

    type EmptyData = EmptyData

    type MetaData = {
        CreatedAt: string
    }

    type InstanceStartedEvent = InstanceStartedEvent of Event<EmptyData, MetaData, EmptyData>

    //
    // Transformation
    //

    let createInstanceStarted (box: Box) =
        let id = Guid.NewGuid()
        let now = (DateTime.Now).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'")

        InstanceStartedEvent {
            Schema = 1
            Id = EventId id
            CorrelationId = CorrelationId id
            CausationId = CausationId id
            Timestamp = now
            Event = EventName InstanceStartedEventName
            Domain = box.Domain
            Context = box.Context
            Purpose = box.Purpose
            Version = box.Version
            Zone = box.Zone
            Bucket = box.Bucket
            MetaData = {
                CreatedAt = now
            }
            Resource = None
            KeyData = EmptyData
            DomainData = EmptyData
        }

    type InstanceStartedDtoMetaData = {
        created_at: string
    }

    type EmptyDataDto = Map<unit, unit>

    type InstanceStartedDto = {
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
        meta_data: InstanceStartedDtoMetaData
        key_data: EmptyDataDto
        domain_data: EmptyDataDto
    }

    let serialize (InstanceStartedEvent event) =
        {
            schema = 1
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
            meta_data = {
                created_at = event.MetaData.CreatedAt
            }
            key_data = Map.empty
            domain_data = Map.empty
        }
        |> Serializer.serialize
