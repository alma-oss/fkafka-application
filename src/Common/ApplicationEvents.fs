namespace Lmc.KafkaApplication

open System
open Lmc.Kafka
open Lmc.ServiceIdentification
open Lmc.Serializer

type public InstanceStartedDtoMetaData = {
    CreatedAt: string
}

type public EmptyDataDto = Map<unit, unit>

type public InstanceStartedDto = EventWithoutResourceDto<EmptyDataDto, InstanceStartedDtoMetaData, EmptyDataDto>

module internal ApplicationEvents =
    //
    // Events data
    //

    type EmptyData = EmptyData

    type MetaData = {
        CreatedAt: string
    }

    type InstanceStartedEvent = InstanceStartedEvent of Event<EmptyData, MetaData, EmptyData>

    [<RequireQualifiedAccess>]
    module InstanceStartedEvent =
        //
        // Constants
        //

        [<Literal>]
        let InstanceStartedEventName = "instance_started"

        //
        // Transformation
        //

        let create (box: Box) =
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

        let fromDomain: FromDomain<InstanceStartedEvent> =
            fun (Serialize serialize) (InstanceStartedEvent event) ->
                let key = MessageKey.Delimited [
                    event.Zone |> Zone.value
                    event.Bucket |> Bucket.value
                ]

                let emptyData _ = Ok Map.empty

                let serialized =
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

                MessageToProduce.create (key, serialized)
