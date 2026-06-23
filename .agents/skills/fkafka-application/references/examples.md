# Examples

All code for the skill lives here. Examples are ordered by increasing complexity and use neutral placeholders only.

## Basic — Single Connection

The smallest custom application: one input connection, print each event.

```fs
open Alma.Kafka
open Alma.ServiceIdentification
open Alma.KafkaApplication

[<EntryPoint>]
let main argv =
    kafkaApplication {
        useInstance { Domain = Domain "demo"; Context = Context "basic"; Purpose = Purpose "example"; Version = Version "local" }
        useCurrentEnvironment environment

        connect {
            BrokerList = BrokerList "127.0.0.1:9092"
            Topic = StreamName "input-stream"
        }

        parseEventWith RawEvent.parse

        consume (fun _ events ->
            events
            |> Seq.iter (printfn "%A")
        )
    }
    |> run
    |> ApplicationShutdown.withStatusCode
```

## Multiple Connections

Order of `connect` does not matter; order of `consume` does. A bounded handler runs first, the perpetual one last.

```fs
open Alma.Kafka
open Alma.ServiceIdentification
open Alma.KafkaApplication

[<EntryPoint>]
let main argv =
    let brokerList = BrokerList "127.0.0.1:9092"

    kafkaApplication {
        useInstance { Domain = Domain "demo"; Context = Context "multi"; Purpose = Purpose "example"; Version = Version "local" }
        useCurrentEnvironment environment

        connect { BrokerList = brokerList; Topic = StreamName "input-stream" }
        connectTo "secondary" { BrokerList = brokerList; Topic = StreamName "secondary-stream" }

        parseEventWith RawEvent.parse

        // bounded: consumes only the first 10 events, then returns
        consumeFrom "secondary" (fun _ events ->
            events
            |> Seq.take 10
            |> Seq.iter (printfn "%A")
        )

        // perpetual: registered last so it does not block the handler above
        consume (fun _ events ->
            events
            |> Seq.iter (printfn "%A")
        )
    }
    |> run
    |> ApplicationShutdown.withStatusCode
```

## Realistic — Group Id, Commit Mode, Supervision, Metrics

Explicit group id and manual commit for resumable, once-per-message processing, with a supervision stream and a Prometheus route.

```fs
open Alma.Kafka
open Alma.ServiceIdentification
open Alma.KafkaApplication

[<EntryPoint>]
let main argv =
    let brokerList = BrokerList "127.0.0.1:9092"

    kafkaApplication {
        useInstance { Domain = Domain "demo"; Context = Context "realistic"; Purpose = Purpose "example"; Version = Version "local" }
        useCurrentEnvironment environment

        useGroupId (GroupId "demo-consumer")
        useCommitMessage CommitMessage.Manually

        connect { BrokerList = brokerList; Topic = StreamName "input-stream" }
        useSupervision { BrokerList = brokerList; Topic = StreamName "supervision-stream" }

        parseEventWith RawEvent.parse

        showMetrics

        onConsumeError (fun logger message ->
            logger.LogError("Consume failed: {Message}", message)
            ConsumeErrorPolicy.RetryIn 30<Second>
        )

        consume (fun app events ->
            events
            |> Seq.iter (fun event ->
                app.IncrementMetric (MetricName "processed") (SimpleDataSetKeys [])
                printfn "%A" event
            )
        )
    }
    |> run
    |> ApplicationShutdown.withStatusCode
```

## Initialization and Dependencies

Build an external client once with `initialize`, require its configuration variable, and use it inside the handler.

```fs
open Alma.Kafka
open Alma.ServiceIdentification
open Alma.KafkaApplication
open Feather.ErrorHandling

type Dependencies = {
    ExampleApi: ExampleApi
}

and ExampleApi = ExampleApi of string

[<EntryPoint>]
let main argv =
    kafkaApplication {
        useInstance { Domain = Domain "demo"; Context = Context "deps"; Purpose = Purpose "example"; Version = Version "local" }
        useCurrentEnvironment environment

        require [ "EXAMPLE_API" ]

        initialize (fun app ->
            { app with Dependencies = Some { ExampleApi = app.Environment.["EXAMPLE_API"] |> ExampleApi } }
        )

        connect {
            BrokerList = BrokerList "127.0.0.1:9092"
            Topic = StreamName "input-stream"
        }

        parseEventWith RawEvent.parse

        consume (fun app ->
            let { ExampleApi = exampleApi } = app.Dependencies.Value

            fun events ->
                events
                |> Seq.iter (fun event -> printfn "%A: %A" exampleApi event)
        )
    }
    |> run
    |> ApplicationShutdown.withStatusCode
```

## Environment Composition

Use `partialKafkaApplication` + `merge` + the `environment` computation expression to read `.env` files and variables instead of hard-coding configuration.

```fs
open Alma.KafkaApplication

let buildConfiguration () =
    partialKafkaApplication {
        merge (environment {
            file [ "./.env"; "./.dist.env" ]

            instance "INSTANCE"
            currentEnvironment "ENVIRONMENT"
            groupId "GROUP_ID"

            connect {
                BrokerList = "KAFKA_BROKER"
                Topic = "INPUT_STREAM"
            }

            connectTo "outputStream" {
                BrokerList = "KAFKA_BROKER"
                Topic = "OUTPUT_STREAM"
            }

            supervision {
                BrokerList = "KAFKA_BROKER"
                Topic = "SUPERVISION_STREAM"
            }

            require [ "KAFKA_BROKER"; "INPUT_STREAM" ]
        })

        showMetrics
        parseEventWith InputEvent.parse
    }
```

## Pattern — Deriver

Read an input stream and emit derived events to an output stream.

```fs
open Alma.KafkaApplication

deriver {
    from (partialKafkaApplication {
        merge (environment {
            file [ "./.env"; "./.dist.env" ]

            instance "INSTANCE"
            groupId "GROUP_ID"

            connect {
                BrokerList = "KAFKA_BROKER"
                Topic = "INPUT_STREAM"
            }

            connectTo "outputStream" {
                BrokerList = "KAFKA_BROKER"
                Topic = "OUTPUT_STREAM"
            }

            supervision {
                BrokerList = "KAFKA_BROKER"
                Topic = "SUPERVISION_STREAM"
            }
        })

        showMetrics
        parseEventWith InputEvent.parse
    })

    deriveTo "outputStream" Deriver.deriveInputEvent OutputEvent.fromDomain

    getCommonEventBy (function
        | Input event -> event |> InputEvent.toCommon
        | Output event -> event |> OutputEvent.toCommon
    )
}
|> run
|> ApplicationShutdown.withStatusCode
```

## Pattern — Content-Based Router

Route input events to different output streams from a JSON routing configuration.

Routing file (`routing.json`):

```json
{
    "route": [
        { "event": "event_1", "targetStream": "output-stream-1" },
        { "event": "event_2", "targetStream": "output-stream-2" },
        { "event": "event_3", "targetStream": "output-stream-3" }
    ]
}
```

```fs
open Alma.KafkaApplication

contentBasedRouter {
    parseConfiguration "./configuration/routing.json"

    from (partialKafkaApplication {
        merge (environment {
            file [ "./.env"; "./.dist.env" ]

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
        })

        showMetrics
        parseEventWith InputEvent.parse
    })

    routeToBrokerFromEnv "KAFKA_BROKER" OutputEvent.fromDomain
    route InputEvent.route

    getCommonEventBy (function
        | Input event -> event |> InputEvent.toCommon
        | Output event -> event |> OutputEvent.toCommon
    )
}
|> run
|> ApplicationShutdown.withStatusCode
```

## Pattern — Filter

Filter an input stream by a JSON configuration, then filter out unwanted content.

Filter file (`configuration.json`) — empty section means "allow everything":

```json
{
    "filter": {
        "spot": [
            { "zone": "prod", "bucket": "all" }
        ],
        "values": [
            { "purpose": "demo_a", "scope": "scope_a" },
            { "purpose": "demo_b", "scope": "scope_a" }
        ]
    }
}
```

```fs
open Alma.KafkaApplication

filterContentFilter {
    parseConfiguration FilterValue.parse "./configuration/configuration.json"

    from (partialKafkaApplication {
        merge (environment {
            file [ "./.env"; "./.dist.env" ]

            instance "INSTANCE"
            groupId "GROUP_ID"

            connect {
                BrokerList = "KAFKA_BROKER"
                Topic = "INPUT_STREAM"
            }

            connectTo "outputStream" {
                BrokerList = "KAFKA_BROKER"
                Topic = "OUTPUT_STREAM"
            }

            supervision {
                BrokerList = "KAFKA_BROKER"
                Topic = "SUPERVISION_STREAM"
            }
        })

        showMetrics
        parseEventWith InputEvent.parse
    })

    filterTo "outputStream" Filter.filterContentFromInputEvent OutputEvent.serialize

    getCommonEventBy (function
        | Input event -> event |> InputEvent.toCommon
        | Output event -> event |> OutputEvent.toCommon
    )

    getFilterBy InputEvent.filterValue
}
|> run
|> ApplicationShutdown.withStatusCode
```

## Pattern — Compressor (Basic)

Accumulate events into a batch and process the batch when the threshold is reached.

```fs
open Alma.KafkaApplication.Compressor
open Feather.ErrorHandling

type InputEvent = string
type OutputEvent = string

[<EntryPoint>]
let main argv =
    compressor {
        from (partialKafkaApplication {
            useInstance { Domain = Domain "demo"; Context = Context "compressor"; Purpose = Purpose "example"; Version = Version "local" }
            useCurrentEnvironment environment

            connect {
                BrokerList = BrokerList "127.0.0.1:9092"
                Topic = StreamName "input-stream"
            }

            parseEventWith id
            showMetrics
        })

        batchSize 10

        pickEvent (fun _ { Event = event } ->
            Some { Event = sprintf "Processed: %s" event }
        )

        sendBatch (fun batch -> asyncResult {
            printfn "Processing batch of %d events" (batch |> List.length)
            batch |> List.iter (fun { Event = event } -> printfn "Sending: %s" event)
            return ()
        })
    }
    |> run
    |> ApplicationShutdown.withStatusCode
```

## Integration — Compressor with Offset Management and External System

Inject an external batch processor via `initialize`, read batch size from an environment variable, and manage offsets manually for exactly-once processing. `setOffset` and `getOffset` must both be present.

```fs
open Alma.KafkaApplication.Compressor
open Feather.ErrorHandling
open System
open System.IO

type Dependencies = {
    BatchProcessor: BatchProcessor
}

and BatchProcessor = BatchProcessor of (string list -> Async<Result<unit, string>>)

let parseEvent: ParseEvent<InputEvent> = id

let externalBatchProcessor (events: string list) = async {
    do! Async.Sleep 100
    printfn "External system processed %d events" events.Length
    return Ok ()
}

[<EntryPoint>]
let main argv =
    compressor {
        from (partialKafkaApplication {
            useInstance { Domain = Domain "demo"; Context = Context "compressor"; Purpose = Purpose "integration"; Version = Version "local" }
            useCurrentEnvironment environment

            connect {
                BrokerList = BrokerList "127.0.0.1:9092"
                Topic = StreamName "input-stream"
            }

            initialize (fun app ->
                { app with Dependencies = Some { BatchProcessor = BatchProcessor externalBatchProcessor } })

            parseEventWith parseEvent
            showMetrics
            showInternalState "/internal-state"
        })

        batchSize "BATCH_SIZE"

        setOffset (fun _ tpo -> asyncResult {
            let offsetFile =
                sprintf "offsets/%s_%d.txt"
                    (tpo.TopicPartition.Topic |> StreamName.value)
                    tpo.TopicPartition.Partition

            match tpo.Offset with
            | Some (Offset o) -> File.WriteAllText(offsetFile, string o)
            | None -> ()
        })

        getOffset (fun _ tp -> asyncResult {
            let offsetFile =
                sprintf "offsets/%s_%d.txt"
                    (tp.Topic |> StreamName.value)
                    tp.Partition

            if File.Exists offsetFile then
                match File.ReadAllText offsetFile |> Int64.TryParse with
                | true, offset -> return { TopicPartition = tp; Offset = Some (Offset (offset + 1L)) }
                | false, _ -> return { TopicPartition = tp; Offset = None }
            else
                return { TopicPartition = tp; Offset = None }
        })

        pickEvent (fun _ { Event = event; Trace = trace } ->
            if event.Contains("keep") then Some { Event = event.ToUpper(); Trace = trace }
            else None
        )

        sendBatch (fun app batch -> asyncResult {
            let { BatchProcessor = (BatchProcessor processor) } = app.Dependencies.Value
            let events = batch |> List.map (fun { Event = event } -> event)
            do! processor events |> AsyncResult.ofAsync
            return ()
        })
    }
    |> run
    |> ApplicationShutdown.withStatusCode
```

## Logger Factory and Dependencies

Bootstrap a logger factory from environment variables and attach it with `useLoggerFactory`, mapping the result to an exit code that also logs.

```fs
open Alma.Kafka
open Alma.ServiceIdentification
open Alma.KafkaApplication
open Feather.ErrorHandling

[<EntryPoint>]
let main argv =
    let envFiles = [ "./.env"; "./.dist.env" ]

    use loggerFactory =
        envFiles
        |> LoggerFactory.common {
            LogTo = "LOG_TO"
            Verbosity = "VERBOSITY"
            LoggerTags = "LOGGER_TAGS"
            EnableTraceProvider = true
        }
        |> Result.orFail

    kafkaApplication {
        useInstance { Domain = Domain "demo"; Context = Context "logging"; Purpose = Purpose "example"; Version = Version "local" }
        useCurrentEnvironment environment
        useLoggerFactory loggerFactory

        connect {
            BrokerList = BrokerList "127.0.0.1:9092"
            Topic = StreamName "input-stream"
        }

        parseEventWith RawEvent.parse

        consume (fun _ events ->
            events |> Seq.iter (printfn "%A")
        )
    }
    |> run
    |> ApplicationShutdown.withStatusCodeAndLogResult loggerFactory
```

## Custom HTTP Route

Register an additional Giraffe route on the metrics web server.

```fs
open Giraffe
open Alma.KafkaApplication

kafkaApplication {
    addHttpHandler (
        route "/my-new-route"
        >=> warbler (fun _ -> text "OK")
    )
}
```

## Test

Unit-test a pure handler in isolation from the consume loop, using Expecto.

```fs
open Expecto

let deriveInputEvent processedBy event =
    [ { Event = sprintf "derived:%s" event.Event; Trace = event.Trace } ]

[<Tests>]
let tests =
    testList "Deriver" [
        test "derives a single output event from one input" {
            let input = { Event = "value"; Trace = Trace.empty }

            let result = deriveInputEvent ProcessedBy.empty input

            Expect.hasLength result 1 "one output event"
            Expect.equal result.[0].Event "derived:value" "transformed payload"
        }
    ]
```
