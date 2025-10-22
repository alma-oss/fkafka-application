namespace RealLifeExample

open System
open System.IO
open Microsoft.Extensions.Logging
open Alma.ServiceIdentification
open Alma.Kafka
open Alma.Kafka.MetaData
open Alma.KafkaApplication
open Alma.ErrorHandling
open Alma.Tracing

type InputEvent = string
type OutputEvent = string

[<RequireQualifiedAccess>]
module Dice =
    let roll () = System.Random().Next(1, 7)
    let isRollOver i =
        use rollTrace = "Dice rool" |> Trace.ChildOf.startFromActive
        printfn "[Dice] roll with trace: %A" (rollTrace |> Trace.id)

        let roll = roll()

        rollTrace
        |> Trace.addTags [ "rool", string roll ]
        |> ignore

        roll >= i

[<RequireQualifiedAccess>]
module Fail =

    let on threshold =
        let mutable counter = 0

        fun () ->
            counter <- counter + 1
            if counter = threshold then
                printfn "Threshold %d reached, failing now!" threshold

                failwithf "Crashed after %d calls" threshold

                Error (ErrorMessage (sprintf "Failed after %d calls" threshold))
            else Ok ()

[<RequireQualifiedAccess>]
module App =
    let formatTp (tp: TopicPartition) =
        sprintf "%s[%d]"
            (tp.Topic |> StreamName.value)
            tp.Partition

    let formatTpo (tpo: TopicPartitionOffset) =
        sprintf "%s: %s"
            (formatTp tpo.TopicPartition)
            (tpo.Offset |> Option.map (fun (Offset o) -> string o) |> Option.defaultValue "none")

    let tpFile groupId (tp: TopicPartition) =
        let key = groupId |> Checkpoint.key tp |> Result.orFail
        let name = sprintf "%s.txt" key
        Path.Combine("offset", name)

    let parseEvent app =
        printfn "⚠️ Parse event"
        fun event -> event

    open Alma.KafkaApplication.Compressor

    type Dependencies = Todo

    let compressorExample envFiles loggerFactory: Application<InputEvent, OutputEvent, Dependencies, _> =
        let failOn2 = Fail.on 2
        let failOn5 = Fail.on 5
        let mutable batchCounter = 0

        let send (app: PatternRuntimeParts<Dependencies>) =
            printfn "⚠️ Send event"
            let file = app.Environment["BATCH_FILE"]

            fun (CompressedBatch batch) -> asyncResult {
                let batchSize = batch |> List.length
                printfn "Starting sending batch[%d] ..." batchSize
                //failOn5()
                printfn "Sending batch of %d events" batchSize

                batchCounter <- batchCounter + 1

                let lines =
                    batch
                    |> List.map (TracedEvent.event >> sprintf " - %s")

                failwith "Simulated failure during send"
                do! AsyncResult.sleep (Dice.roll() * 1000)
                do! failOn2()

                File.AppendAllLines(file, $"Batch[{batchCounter}]: " :: lines)

                return ()
            }

        let pickEventHandler app =
                printfn "⚠️ Pickup event"

                fun { Trace = trace; Event = event } ->
                    printfn "Picking event: %s" event
                    // failOn2()

                    Some { Trace = trace; Event = sprintf "O: %s" event }

        let storeOffset app =
            printfn "⚠️ Store offset"
            fun groupId tpo -> asyncResult {
                printfn "[Offset] Set: %s" (formatTpo tpo)

                match tpo with
                | { Offset = Some (Offset o) } ->
                    let file = tpFile groupId tpo.TopicPartition
                    File.WriteAllText(file, string o)

                | _ -> printfn " - No offset to store -> skipped"
            }

        let retreiveOffset app =
            printfn "⚠️ Retrieve offset"
            fun groupId tp -> asyncResult {
                printfn "[Offset] Get: %s" (formatTp tp)
                let file = tpFile groupId tp

                if file |> File.Exists |> not then
                    printfn " - File %s not found, returning None" file
                    return { TopicPartition = tp; Offset = None }

                else
                    match File.ReadAllText file |> Int64.TryParse with
                    | true, o -> return { TopicPartition = tp; Offset = Some (Offset (o + 1L)) }
                    | _ -> return { TopicPartition = tp; Offset = None }
            }

        compressor {
            from (partialKafkaApplication {
                useLoggerFactory loggerFactory
                useCommitMessage (CommitMessage.Manually FailOnNotCommittedMessage.WithException)

                merge (environment {
                    file envFiles

                    instance "INSTANCE"
                    currentEnvironment "ENVIRONMENT"

                    connect {
                        BrokerList = "KAFKA_BROKER"
                        Topic = "INPUT_STREAM"
                    }

                    require [
                        "BATCH_FILE"
                    ]

                    groupId "GROUP_ID"
                })

                parseEventAndUseApplicationWith parseEvent

                showMetrics
                showAppRootStatus
                showInternalState "/internal-state"
            })

            batchSize "BATCH_SIZE"

            setOffset storeOffset

            getOffset retreiveOffset

            pickEvent pickEventHandler
            sendBatch send
        }

module Program =
    let run envFiles loggerFactory =
        App.compressorExample
            envFiles
            loggerFactory
        |> run
