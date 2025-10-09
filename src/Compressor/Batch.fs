namespace Alma.KafkaApplication.Compressor

type CompressedBatch<'T> = CompressedBatch of 'T list

[<AutoOpen>]
module internal CompressorBatch =
    open System
    open System.Collections.Concurrent
    open Alma.ErrorHandling

    type Batch<'Item> = Batch of ConcurrentQueue<'Item>
    type BatchThreshold = BatchThreshold of int

    type BatchAddResult =
        | CreatedAndAdded
        | Added

    type BatchSize = BatchSize of int
    type BatchCheckResult =
        | Filling
        | Sent of currentSize: BatchSize

    [<RequireQualifiedAccess>]
    module BatchSize =
        let value (BatchSize size) = size

    [<RequireQualifiedAccess>]
    module BatchThreshold =
        let value (BatchThreshold threshold) = threshold
        let float = value >> float

        let tryCreate (value: int) =
            if value > 0 then Some (BatchThreshold value)
            else None

        let tryParse (value: string) =
            match Int32.TryParse value with
            | true, parsed -> tryCreate parsed
            | _ -> None

    [<RequireQualifiedAccess>]
    module Batch =
        let init<'Item> () = ConcurrentQueue<'Item>() |> Batch

        /// <summary>
        /// Gets the current number of items in the batch.
        /// Thread-safe operation that provides a snapshot count.
        /// </summary>
        let count (Batch queue) = queue.Count

        let add (Batch queue) item =
            let wasEmpty = queue.Count = 0
            queue.Enqueue item
            if wasEmpty then CreatedAndAdded else Added

        /// <summary>
        /// Returns all current items as a list AND removes them from the queue atomically.
        /// Thread-safe operation that ensures no items are lost during concurrent access.
        /// Each item is individually dequeued, guaranteeing consistent state.
        /// Use this instead of 'items' + 'clear' in multi-threaded scenarios.
        /// </summary>
        let drain (Batch queue) =
            let rec dequeueAll acc =
                match queue.TryDequeue() with
                | true, item -> dequeueAll (item :: acc)
                | false, _ -> List.rev acc

            dequeueAll []
            |> CompressedBatch

        let check (BatchThreshold threshold) sendBatch batch = asyncResult {
            let currentSize = batch |> count
            if currentSize >= threshold then
                do! batch |> drain |> sendBatch
                return Sent (BatchSize currentSize)

            else return Filling
        }
