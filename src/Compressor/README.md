Compressor
==========

The compressor pattern allows you to batch multiple events together before processing them as a group. This is useful for optimizing throughput when you need to send events in batches to external systems, or when processing events in groups is more efficient than individual processing.

```
[InputStream] ───> (Compressor) ───> [CompressedBatch] ───> (TargetService)
```

Key features:
- **Batch accumulation**: Events are collected until a batch size threshold is reached
- **Event transformation**: Optional transformation of input events before adding to batch
- **Batch processing**: Process entire batches with custom logic
- **Offset management**: Manual control over Kafka offset commits
- **Metrics integration**: Built-in metrics for batch creation, size, send duration, and failures

## Compressor computation expression

It allows you to create a compressor application easier. It has build-in a consumer, buffering a batch, metrics, etc.

Compressor computation expression returns `Application of CompressorApplication<'InputEvent, 'OutputEvent>` and it is run by `Application.run` function.

### Functions

| Function | Arguments | Description |
| --- | --- | --- |
| `batchSize` | `int` or `string` | Sets the batch size threshold. When this many events are accumulated, the batch is sent. Can be an integer or environment variable name. |
| `pickEvent` | `ProcessedBy -> TracedEvent<'InputEvent> -> TracedEvent<'OutputEvent> option` | Transforms input events to output events and optionally filters them. Return `None` to skip the event. |
| `sendBatch` | `TracedEvent<'OutputEvent> list -> AsyncResult<unit, string>` | Processes a complete batch of events. Called when batch size threshold is reached. |
| `setOffset` | `TopicPartitionOffset -> AsyncResult<unit, string>` | Custom offset storage logic for exactly-once processing. Called after successful batch processing. |
| `getOffset` | `TopicPartition -> AsyncResult<TopicPartitionOffset, string>` | Custom offset retrieval logic. Called on startup to determine where to start consuming. |
| `getCommonEventBy` | `'InputEvent -> CommonEvent` | Extracts common event information for metrics and monitoring. |
| `addCustomMetricValues` | `'InputEvent -> 'OutputEvent -> SimpleDataSetKey list` | Adds custom metric dimensions for enhanced monitoring. |
| `from` | `Configuration<'InputEvent, 'OutputEvent>` | It will create a base kafka application parts. This is mandatory and configuration must contain all dependencies. |

### PickEvent

It is a function, which is responsible for picking events for the batch.
```fs
type PickEvent<'InputEvent, 'OutputEvent> = ProcessedBy -> TracedEvent<'InputEvent> -> TracedEvent<'OutputEvent> option
```

#### Generic variants
- `PickEvent<'InputEvent, 'OutputEvent>` could return one of following:
    - `TracedEvent<'OutputEvent> option`
    - `Result<TracedEvent<'OutputEvent> option, string>`
    - `AsyncResult<TracedEvent<'OutputEvent> option, string>`

### SendBatch

It is a function, which is responsible for processing the complete batch of events.
```fs
type SendBatch<'OutputEvent> = TracedEvent<'OutputEvent> list -> AsyncResult<unit, string>
```

#### Generic variants
- `SendBatch<'OutputEvent>` could return one of following:
    - `unit`
    - `Result<unit, string>`
    - `AsyncResult<unit, string>`

### SetOffset

It is a function, which is responsible for storing offset information after successful batch processing.
```fs
type SetOffset = TopicPartitionOffset -> AsyncResult<unit, string>
```

#### Generic variants
- `SetOffset` could return one of following:
    - `unit`
    - `Result<unit, string>`
    - `AsyncResult<unit, string>`

### GetOffset

It is a function, which is responsible for retrieving stored offset information on startup.
```fs
type GetOffset = TopicPartition -> AsyncResult<TopicPartitionOffset, string>
```

#### Generic variants
- `GetOffset` could return one of following:
    - `TopicPartitionOffset`
    - `Result<TopicPartitionOffset, string>`
    - `AsyncResult<TopicPartitionOffset, string>`

## Built-in Metrics

The compressor pattern automatically provides these Prometheus metrics:

- `compressor_batch_created_total`: Total number of batches created
- `compressor_batch_size`: Size of batches (observed when batch is created)
- `compressor_batch_threshold_triggered_total`: Number of times batch size threshold was triggered
- `compressor_batch_send_duration_seconds`: Time taken to send batches
- `compressor_batch_send_failures_total`: Number of batch send failures

## Simple Example

```fs
open Alma.KafkaApplication.Compressor
open Alma.ErrorHandling

type InputEvent = string
type OutputEvent = string

[<EntryPoint>]
let main argv =
    compressor {
        from (partialKafkaApplication {
            useInstance { Domain = Domain "my"; Context = Context "simple"; Purpose = Purpose "example"; Version = Version "local" }
            useCurrentEnvironment environment

            connect {
                BrokerList = BrokerList "127.0.0.1:9092"
                Topic = StreamName "my-input-stream"
            }

            parseEventWith id
            showMetrics
        })

        batchSize 10

        pickEvent (fun _ { Event = event } ->
            // Transform input event to output event (optional)
            Some { Event = sprintf "Processed: %s" event }
        )

        sendBatch (fun batch -> asyncResult {
            // Process the entire batch
            printfn "Processing batch of %d events" (batch |> List.length)

            batch
            |> List.iter (fun { Event = event } ->
                printfn "Sending: %s" event)

            return ()
        })
    }
    |> run
    |> ApplicationShutdown.withStatusCode
```

## Advanced Example with Offset Management

```fs
open Alma.KafkaApplication.Compressor
open Alma.ErrorHandling
open System.IO

type Dependencies = {
    BatchProcessor: BatchProcessor
}

and BatchProcessor = BatchProcessor of (string list -> Async<Result<unit, string>>)

let parseEvent: ParseEvent<InputEvent> = id

let externalBatchProcessor (events: string list) = async {
    // Simulate external batch processing
    do! Async.Sleep 100
    printfn "External system processed %d events" events.Length
    return Ok ()
}

[<EntryPoint>]
let main argv =
    compressor {
        from (partialKafkaApplication {
            useInstance { Domain = Domain "my"; Context = Context "compressor"; Purpose = Purpose "example"; Version = Version "local" }
            useCurrentEnvironment environment

            connect {
                BrokerList = BrokerList "127.0.0.1:9092"
                Topic = StreamName "my-input-stream"
            }

            initialize (fun app ->
                { app with Dependencies = Some { BatchProcessor = BatchProcessor externalBatchProcessor } })

            parseEventWith parseEvent
            showMetrics
            showInternalState "/internal-state"
        })

        batchSize "BATCH_SIZE"  // Read from environment variable

        // Custom offset management for exactly-once processing
        setOffset (fun app tpo -> asyncResult {
            let offsetFile = sprintf "offsets/%s_%d.txt"
                (tpo.TopicPartition.Topic |> StreamName.value)
                tpo.TopicPartition.Partition

            match tpo.Offset with
            | Some (Offset o) ->
                File.WriteAllText(offsetFile, string o)
                printfn "Stored offset: %d" o
            | None ->
                printfn "No offset to store"
        })

        getOffset (fun app tp -> asyncResult {
            let offsetFile = sprintf "offsets/%s_%d.txt"
                (tp.Topic |> StreamName.value)
                tp.Partition

            if File.Exists offsetFile then
                match File.ReadAllText offsetFile |> Int64.TryParse with
                | true, offset ->
                    return { TopicPartition = tp; Offset = Some (Offset (offset + 1L)) }
                | false, _ ->
                    return { TopicPartition = tp; Offset = None }
            else
                return { TopicPartition = tp; Offset = None }
        })

        // Transform and filter events
        pickEvent (fun app { Event = event; Trace = trace } ->
            let { BatchProcessor = processor } = app.Dependencies.Value

            // Optional filtering and transformation
            if event.Contains("important") then
                Some { Event = event.ToUpper(); Trace = trace }
            else
                None
        )

        // Process batches with external system
        sendBatch (fun app batch -> asyncResult {
            let { BatchProcessor = (BatchProcessor processor) } = app.Dependencies.Value

            let events = batch |> List.map (fun { Event = event } -> event)

            do! processor events |> AsyncResult.ofAsync

            printfn "Successfully processed batch of %d events" (batch |> List.length)

            return ()
        })
    }
    |> run
    |> ApplicationShutdown.withStatusCode
```

## Real-world Example

```fs
compressor {
    from (partialKafkaApplication {
        merge (environment {
            file ["./.env"; "./.dist.env"]

            instance "INSTANCE"
            groupId "GROUP_ID"

            connect {
                BrokerList = "KAFKA_BROKER"
                Topic = "INPUT_STREAM"
            }

            supervision {
                BrokerList = "KAFKA_BROKER"
                Topic = "SUPERVISION_STREAM"
            }

            require [
                "CLIENT_CONNECTION"
                "EVENT_SOURCE"
                "TARGET_SPOT"
                "KV_STORE_CONNECTION"
            ]
        })

        showMetrics
        parseEventWith Parser.parseInputEvent

        initialize (fun app -> asyncResult {
            let! targetServiceClient = SomeClient.connect app.Environment["CLIENT_CONNECTION"]
            let source = EventSource app.Environment["EVENT_SOURCE"]
            let! spot = Spot.create app.Environment["TARGET_SPOT"]
            let! kvStoreClient = KVStoreClient.connect app.Environment["KV_STORE_CONNECTION"]

            let dependencies = {
                TargetClient = targetServiceClient
                Source = source
                TargetSpot = spot
                KVStoreClient = kvStoreClient
            }

            return { app with Dependencies = Some dependencies }
        })
    })

    batchSize "BATCH_SIZE"

    setOffset (fun app ->
        let { KVStoreClient = kvStoreClient } = app.Dependencies.Value

        fun sentBatch -> kvStoreClient.Set(sentBatch.TopicPartition, sentBatch.Offset)
    )

    getOffset (fun app ->
        let { KVStoreClient = kvStoreClient } = app.Dependencies.Value

        Get = fun lastSentBatch -> kvStoreClient.TryGet(lastSentBatch.TopicPartition)
    )

    pickEvent (fun app ->
        let { TargetSpot = spot; Source = source } = app.Dependencies.Value

        function
        | InputEvent.LocalDataAcceptationReceived event when event |> LocalDataAcceptationReceived.Event.box |> Box.spot >> (=) spot ->
            maybe {
                let! cloudEvent =
                    outputEventsBatch
                    |> LocalDataAcceptationReceived.CloudEvent.create source
                    |> LocalDataAcceptationReceived.CloudEvent.toDto
                    |> Result.toOption

                return OutputEvent.LocalDataAcceptationReceived (event, cloudEvent)
            }

        | _ -> None
    )

    sendBatch (fun app ->
        let { TargetService = targetServiceClient } = app.Dependencies.Value

        targetServiceClient.sendBatch
    )

    getCommonEventBy (function
        | Input event ->
            match event with
            | InputEvent.LocalDataAcceptationReceived localDataAcceptationReceived ->
                localDataAcceptationReceived
                |> LocalDataAcceptationReceived.Event.toCommon
            | InputEvent.NotRelevant rawEvent ->
                rawEvent
                |> RawEvent.toCommon

        | Output event ->
            match event with
            | OutputEvent.LocalDataAcceptationReceived (localDataAcceptationReceived, _) ->
                localDataAcceptationReceived
                |> LocalDataAcceptationReceived.Event.toCommon
    )
}
|> run
|> ApplicationShutdown.withStatusCode
```