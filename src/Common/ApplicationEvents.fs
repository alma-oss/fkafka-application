namespace Lmc.KafkaApplication

module internal ApplicationEvents =
    open System
    open Kafka
    open ServiceIdentification
    open Lmc.Serializer

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
        let now = DateTime.Now |> Serialize.dateTime

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
        CreatedAt: string
    }

    type EmptyDataDto = Map<unit, unit>

    type InstanceStartedDto = Kafka.EventWithoutResourceDto<EmptyDataDto, InstanceStartedDtoMetaData, EmptyDataDto>

    let fromDomain: FromDomain<InstanceStartedEvent> =
        fun (Serialize serialize) (InstanceStartedEvent event) ->
            let emptyData _ = Ok Map.empty

            event
            |> Event.withoutResourceToDto
                (function
                    | { Event = EventName InstanceStartedEventName } -> Ok ()
                    | { Event = EventName wrong } -> Error (sprintf "Wrong event name given %A." wrong)
                )
                (fun meta -> Ok { CreatedAt = meta.CreatedAt })
                emptyData
                emptyData
            |> serialize
