namespace Alma.KafkaApplication

open Alma.Kafka

[<RequireQualifiedAccess>]
module internal InternalState =
    open Alma.State
    open Alma.State.ConcurrentStorage
    open Giraffe

    type InternalState =
        | CurrentOffsets

    let private internalState: State<InternalState, TopicPartitionOffset list> = State.empty()

    let getCurrentOffsets () =
        internalState
        |> State.tryFind (Key CurrentOffsets)
        |> Option.defaultValue []

    let updateCurrentOffset topic (message: Consumer.Message) =
        message.Partition
        |> Option.iter (fun partition ->
            let latest = {
                TopicPartition = {
                    Topic = topic
                    Partition = partition
                }
                Offset = message.Offset |> Option.map Offset
            }
            let currentState =
                getCurrentOffsets()
                |> List.filter (fun toff -> toff.TopicPartition <> latest.TopicPartition)

            internalState
            |> State.set (Key CurrentOffsets) (latest :: currentState)
        )

    let httpHandler path: HttpHandler =
        GET
        >=> route path
        >=> warbler (fun _ ->
            let offsets =
                getCurrentOffsets()
                |> List.sortBy (fun tpo -> tpo.TopicPartition.Topic |> StreamName.value, tpo.TopicPartition.Partition)
                |> List.map (fun tpo ->
                    sprintf " - %s[%d]: %s"
                        (tpo.TopicPartition.Topic |> StreamName.value)
                        tpo.TopicPartition.Partition
                        (tpo.Offset |> Option.map (fun (Offset o) -> string o) |> Option.defaultValue "none")
                )
                |> String.concat "\n"

            sprintf "Current internal offsets:\n%s" (if offsets = "" then " - No internal offsets stored." else offsets)
            |> text
        )
