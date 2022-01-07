F-Kafka Application
===================

Framework for kafka application.
It contains computation expressions to help with building this kind of application and have in-build metrics, logging, parsing, etc..

## Install

Add following into `paket.dependencies`
```
git ssh://git@bitbucket.lmc.cz:7999/archi/nuget-server.git master Packages: /nuget/
# LMC Nuget dependencies:
nuget Lmc.KafkaApplication
```

Add following into `paket.references`
```
Lmc.KafkaApplication
```

## Application life cycle

    Entry point (your application)
    ├─> Build (computation expression) builds the Application out of your configuration
    └─> Run Application, which might be either Ok or with the Error
              ┌────────────────────────────────┘               └──────────────────────────────────────────<Ends with the Error>───────┐
              ├─> Before Run (Debug pattern specific configuration)                                                                   │
              ├─> Start Custom Tasks                                                                                                  │
              └─> Run Kafka Application                                                                                               │
                   ├─> Debug Configuration          (only with debug verbosity)                                                       │
                   ├─> Enable Context Metric                                                                                          │
                   ├─> Mark instance as disabled    (set service_status to 0)                                                         │
                   ├─> Check additionally registered resources and start interval checking                                            │
                   ├─> Start Metrics on Route       (only if route is set)                                                            │
                   ├─> Connect Producers <─────────────────────────────────────────────────────────┐                                  │
                   │     └─<On Error>───> Producer Error Handler  (Default - RetryIn 60 seconds)   │                                  │
                   │                        └─> One of Following:                                  │                                  │
          <All connected>                       └─> Retry ─────────────────────────────────────────┘                                  │
                   │                            └─> RetryIn X seconds ─────────────────────────────┘                                  │
                   │                            └─> Shutdown ──────────────────────────────────────<Ends with the RuntimeError>────┐  │
                   │                            └─> ShutdownIn X seconds ──────────────────────────<Ends with the RuntimeError>────┐  │
                   ├─> Produce instance_started event to the [Supervision stream]     (only if supervision stream is registered)   │  │
                   ├─> Consume all registered Consume Handlers, one by one <───────────────────────┐                               │  │
                   │     └─<On Error>───> Consume Error Handler   (Default - RetryIn 60 seconds)   │                               │  │
                   │     │                   └─> One of Following:                                 │                               │  │
          <All consumed> │                       └─> Continue (with next Consume Handler) ─────────┘                               │  │
                   │     │                       └─> Retry ────────────────────────────────────────┘                               │  │
                   │     │                       └─> RetryIn X seconds ────────────────────────────┘                               │  │
                   │     │                       └─> Shutdown ─────────────────────────────────────<Ends with the RuntimeError>────┐  │
                   │     │                       └─> ShutdownIn X seconds ─────────────────────────<Ends with the RuntimeError>────┐  │
                   │     └─<On Success>─> Flush all Producers                                                                      │  │
                   ├─> Close all Producers                                                                                         │  │
                   │   (Successfully end) ──────────┐                                                                              │  │
                   └─> Return ApplicationShutdown   │                                                                              │  │
                         └─> One of Following:      │                                                                              │  │
                             └─> Successfully <─────┘                                                                              │  │
                             └─> WithRuntimeError <────────────────────<Application RuntimeError or Unhandled Exception>───────────┘  │
                             └─> WithError <──────────────────────────────────────────────────────────────────────────────────────────┘
        ┌────<ApplicationShutdown>────┘
        └─> ApplicationShutdown.withStatusCode ─┐
    <────────<Status code - [0|1]>──────────────┘

## Patterns
Definitions for patterns could is in the [Confluence](https://confluence.int.lmc.cz/display/ARCH/Event+Driven+Architecture).
You can simply use predefined patterns for application you want.

- [FilterContentFilter](https://bitbucket.lmc.cz/projects/ARCHI/repos/fkafka-application/browse/src/Filter/README.md)
- [ContentBasedRouter](https://bitbucket.lmc.cz/projects/ARCHI/repos/fkafka-application/browse/src/Router/README.md)
- [Deriver](https://bitbucket.lmc.cz/projects/ARCHI/repos/fkafka-application/browse/src/Deriver/README.md)

## Custom Kafka Application functions
_NOTE: All functions has the first argument for the `state: Configuration<'Event>`, but this is current state of the application and it is passed implicitly in the background by computation expression._

| Function | Arguments | Description |
| --- | --- | --- |
| addHttpHandler | `httpHandler: Giraffe.HttpHandler` | It will register additional HttpHandler to the WebServer. |
| checkKafkaWith | `checker: Kafka.Checker` | It will register a checker, which will be passed to the Consumer Configuration and used by Kafka library to check resources. |
| checkResourceInInterval | `checker: unit -> Metrics.ResourceStatus`, `ResourceAvailability`, `interval: int<second>` | It will register a resource which will be checked in runtime of the application in given interval. |
| connect | `Kafka.ConnectionConfiguration` | It will register a default connection for Kafka. |
| connectManyToBroker | `ManyTopicsConnectionConfiguration` | It will register a named connections for Kafka. Connection name will be the same as the topic name. |
| connectTo | `connectionName: string`, `Kafka.ConnectionConfiguration` | It will register a named connection for Kafka. |
| consume | `handler: ConsumeRuntimeParts -> seq<'Event> -> unit` | It will register a handler, which will be called with events consumed from the default Kafka connection. |
| consumeFrom | `connectionName: string`, `handler: ConsumeRuntimeParts -> seq<'Event> -> unit` | It will register a handler, which will be called with events consumed from the Kafka connection. |
| consumeLast | `handler: ConsumeRuntimeParts -> 'Event -> unit` | It will register a handler, which will be called if there is a last message (event), in the default connection. |
| consumeLastFrom | `connectionName: string`, `handler: ConsumeRuntimeParts -> 'Event -> unit` | It will register a handler, which will be called if there is a last message (event), in the connection. |
| merge | `configuration: Configuration<'Event>` | Add other configuration and merge it with current. New configuration values have higher priority. New values (only those with Some value) will replace already set configuration values. (Except of logger) |
| onConsumeError | `ErrorHandler = Logger -> (errorMessage: string) -> ConsumeErrorPolicy` | It will register an error handler, which will be called on error while consuming a default connection. And it determines what will happen next. |
| onConsumeErrorFor | `connectionName: string`, `ErrorHandler = Logger -> (errorMessage: string) -> ConsumeErrorPolicy` | It will register an error handler, which will be called on error while consuming a connection. And it determines what will happen next. |
| onProducerError | `ErrorHandler = Logger -> (errorMessage: string) -> ProducerErrorPolicy` | It will register an error handler, which will be called on error while connecting producers. And it determines what will happen next. |
| parseEventWith | `ParseEvent<'InputEvent>` | It will register a parser for input events. |
| produceTo | `connectionName: string`, `FromDomain<'OutputEvent>` | This will register both a Kafka Producer and a produce event function. |
| produceToMany | `topics: string list`, `FromDomain<'OutputEvent>` | This will register both a Kafka Producer and a produce event function for all topics with the one `fromDomain` function. |
| registerCustomMetric | `CustomMetric` | It will register a custom metric, which will be shown (_if it has a value_) amongst other metrics on metrics route. (_see also `showMetrics`, `ConsumeRuntimeParts.IncrementMetric`, etc._) |
| runCustomTask | `TaskErrorPolicy`, `CustomTaskRuntimeParts -> Async<unit>` | Register a CustomTask, which will be start with the application. |
| showCustomMetric | `name: string`, `MetricType`, `description: string` | It will register a custom metric, which will be shown (_if it has a value_) amongst other metrics on metrics route. (_see also `showMetrics`, `ConsumeRuntimeParts.IncrementMetric`, etc._) |
| showInputEventsWith | `createInputEventKeys: InputStreamName -> 'Event -> SimpleDataSetKey` | If this function is set, all Input events will be counted and the count will be shown on metrics. (_Created keys will be added to the default ones, like `Instance`, etc._) |
| showMetricsOn | `route: string` | It will asynchronously run a web server (`http://127.0.0.1:8080`) and show metrics (_for Prometheus_) on the route. Route must start with `/`. |
| showOutputEventsWith | `createOutputEventKeys: OutputStreamName -> 'Event -> SimpleDataSetKey` | If this function is set, all Output events will be counted and the count will be shown on metrics. (_Created keys will be added to the default ones, like `Instance`, etc._) |
| useDockerImageVersion | `DockerImageVersion` | |
| useGit | `Git` | |
| useGroupId | `GroupId` | It is optional with default `GroupId.Random`. |
| useGroupIdFor | `connectionName: string`, `GroupId` | Set group id for connection. |
| useCommitMessage | `CommitMessage` | It is optional with default `CommitMessage.Automatically`. |
| useCommitMessageFor | `connectionName: string`, `CommitMessage` | Set commit message mode for connection. |
| useInstance | `Instance` | |
| useLogger | `logger: Logger` | It is optional. |
| useSpot | `Spot` | It is optional with default `Zone = "common"; Bucket = "all"` |
| useSupervision | `Kafka.ConnectionConfiguration` | It will register a supervision connection for Kafka. This connection will be used to produce a supervision events (like `instance_started`) |

### Mandatory
- Instance of the application is required.
- You have to define at least one consume handler and its connection.
- You have to define `ParseEvent<'InputEvent>` function.

### Defaults
- Default error handler for consuming is set to `RetryIn 60 seconds` on error.
- Default error handler for connecting producers is set to `RetryIn 60 seconds` on error.
- Default `GroupId` is `Random`. And if you define group id without `connection` it will be used for all connections unless you explicitly set other group id for them.
- Default `Spot` is `Zone = "common"; Bucket = "all"`.
- Default `Checker` for kafka is default checker defined in Kafka library.
- Default `GitCommit` is `unknown`.
- Default `DockerImageVersion` is `unknown`.

### Add Route example
```fs
open Giraffe
open Lmc.KafkaApplication

kafkaApplication {
    addHttpHandler (
        route "/my-new-route"
        >=> warbler (fun _ -> text "OK")
    )
}
```

### Runtime parts for Consume Handler

In every Consume Handler, the first parameter you will receive is the `ConsumeRuntimeParts`. There are all _parts_ of the application, you might need on runtime.

```fs
type ConsumeRuntimeParts<'OutputEvent> = {
    Logger: ApplicationLogger
    Box: Box
    ProcessedBy: Events.ProcessedBy
    Environment: Map<string, string>
    Connections: Connections
    ConsumerConfigurations: Map<RuntimeConnectionName, ConsumerConfiguration>
    ProduceTo: Map<RuntimeConnectionName, ProduceEvent<'OutputEvent>>
    IncrementMetric: Metrics.MetricName -> SimpleDataSetKeys -> unit
    SetMetric: Metrics.MetricName -> SimpleDataSetKeys -> Metrics.MetricValue -> unit
    EnableResource: ResourceAvailability -> unit
    DisableResource: ResourceAvailability -> unit
}
```

### Runtime parts for Custom task

In every Custom task, the first parameter you will receive is the `CustomTaskRuntimeParts`. There all _parts_ of the application, you might need on runtime.

```fs
type CustomTaskRuntimeParts = {
    Logger: ApplicationLogger
    Box: Box
    Environment: Map<string, string>
    IncrementMetric: MetricName -> SimpleDataSetKeys -> unit
    SetMetric: MetricName -> SimpleDataSetKeys -> MetricValue -> unit
    EnableResource: ResourceAvailability -> unit
    DisableResource: ResourceAvailability -> unit
}
```

### Environment computation expression
It allows you to parse .env files and get other environment variables to use in your application workflow.

Environment computation expression returns `Configuration<'Event>` so you can `merge` it to the Kafka Application.

| Function | Arguments | Description |
| --- | --- | --- |
| check | `variable name: string`, `checker: string -> 'a option` | If the variable name is defined it is forwarded to the checker and it passes when `Some` is returned. |
| connect | `connection configuration: EnvironmentConnectionConfiguration` | It will register a default connection for Kafka. Environment Connection configuration looks the same as Connection Configuration for Kafka, but it just has the variable names of the BrokerList and Topic. |
| connectManyToBroker | `EnvironmentManyTopicsConnectionConfiguration` | It will register a named connections for Kafka. Connection name will be the same as the topic name. |
| connectTo | `connectionName: string`, `connection configuration: EnvironmentConnectionConfiguration` | It will register a named connection for Kafka. |
| dockerImageVersion | `variable name: string` | It will parse DockerImageVersion from the environment variable. |
| file | `paths: string list` | It will parse the first existing file and add variables to others defined Environment variables. If no file is parse, it will still reads all other environment variables. |
| groupId | `variable name: string` | It will parse GroupId from the environment variable. |
| ifSetDo | `variable name: string`, `action: string -> unit` | It will try to parse a variable and if it is defined, the `action` is called with the value. |
| instance | `variable name: string` | It will parse Instance from the environment variable. (_Separator is `-`_) |
| require | `variables: string list` | It will check whether all required variables are already defined. |
| spot | `variable name: string` | It will parse Spot from the environment variable. (_Separator is `-`_) |
| supervision | `connection configuration: EnvironmentConnectionConfiguration` | It will register a supervision connection for Kafka. This connection will be used to produce a supervision events (like `instance_started`) |

## Examples

### Connect to kafka stream
This is the most elemental reason for this application framework, so there are simple ways for simple cases.
But there are also more complex, for more complex applications, where you need more connections.

This is also mandatory, to define at least one connection.

#### Simple one connection
Following example is the easiest setup, you can get.

```fs
open Lmc.Kafka
open Lmc.ServiceIdentification
open Lmc.KafkaApplication

[<EntryPoint>]
let main argv =
    kafkaApplication {
        useInstance { Domain = Domain "my"; Context = Context "simple"; Purpose = Purpose "example"; Version = Version "local" }
        useGit {
            Commit = Some (GitCommit "abc123")
            Branch = None
            Repository = None
        }
        useDockerImageVersion (DockerImageVersion "42-2019")

        connect {
            BrokerList = BrokerList "127.0.0.1:9092"
            Topic = StreamName "my-input-stream"
        }

        consume (fun _ events ->
            events
            |> Seq.iter (printfn "%A")
        )
    }
    |> run
    |> ApplicationShutdown.withStatusCode
```

#### Simple one connection with logger
Following example is the easiest setup, you can get but uses a logger factory to log internal events.

```fs
open Lmc.Kafka
open Lmc.ServiceIdentification
open Lmc.KafkaApplication
open Lmc.ErrorHandling

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
        useInstance { Domain = Domain "my"; Context = Context "simple"; Purpose = Purpose "example"; Version = Version "local" }
        useGit {
            Commit = Some (GitCommit "abc123")
            Branch = None
            Repository = None
        }
        useDockerImageVersion (DockerImageVersion "42-2019")

        useLoggerFactory loggerFactory

        connect {
            BrokerList = BrokerList "127.0.0.1:9092"
            Topic = StreamName "my-input-stream"
        }

        consume (fun _ events ->
            events
            |> Seq.iter (printfn "%A")
        )
    }
    |> run
    |> ApplicationShutdown.withStatusCodeAndLogResult loggerFactory
```

#### More than one connection
Instead of previous example, where our application uses only one connections, there are cases, where we need more than that.

```fs
open Lmc.Kafka
open Lmc.ServiceIdentification
open Lmc.KafkaApplication

[<EntryPoint>]
let main argv =
    let brokerList = BrokerList "127.0.0.1:9092"

    kafkaApplication {
        useInstance { Domain = Domain "my"; Context = Context "simple"; Purpose = Purpose "example"; Version = Version "local" }

        // this will create only default connection, which can be consumed by the default `consume` function only
        connect {
            BrokerList = brokerList
            Topic = StreamName "my-input-stream"
        }

        // this will create a second connections - called "secondary" which will be available in application parts by this key, and can be connect to
        connectTo "secondary" {
            BrokerList = brokerList
            Topic = StreamName "my-secondary-stream"
        }

        // first we consume first 10 events from the "secondary" stream
        consumeFrom "secondary" (fun _ events ->
            events
            |> Seq.take 10
            |> Seq.iter (printfn "%A")
        )

        // ONLY after 10 events are consumed, this consume will be active and remain active, since it will wait indefinitely to next events
        consume (fun _ events ->
            events
            |> Seq.iter (printfn "%A")
        )
    }
    |> run
    |> ApplicationShutdown.withStatusCode
```
Notes:
- It does NOT matter in which order you create connections - they are just _registered_.
- It does matter in which order you consume events - they are run in the same order of registration and the next consume will be run ONLY when the previous ended. So make sure it is not infinite, if you want to consume the other ones too.
- All connections are available in the `ConsumeRuntimeParts.Connections` of the consume handler function

#### Even more connections
Now we have even more connection and different relationships between them.
Keep in mind, that this example is simplified and it is missing the parsing logic (which is passed to the `run` function)

```fs
open Lmc.Kafka
open Lmc.ServiceIdentification
open Lmc.KafkaApplication

[<EntryPoint>]
let main argv =
    let brokerList = BrokerList "127.0.0.1:9092"

    kafkaApplication {
        useInstance { Domain = Domain "my"; Context = Context "simple"; Purpose = Purpose "example"; Version = Version "local" }

        connect { BrokerList = brokerList; Topic = StreamName "my-input-stream" }   // default connection
        connectTo "application" { BrokerList = brokerList; Topic = StreamName "my-application-stream" }
        connectTo "output"      { BrokerList = brokerList; Topic = StreamName "my-output-stream" }

        // first we consume the last event from the "application" stream, then we will consume the output stream to the last event (if there is None, handler won't be called at all)
        consumeLastFrom "application" (fun application lastEvent ->
            let hasCorrelationId event =
                event.CorrelationId = lastEvent.CorrelationId

            Consumer.consume application.Connections.["output"] RawEvent.parse
            |> Seq.map (fun event ->
                printfn "%A" event  // do something with the event (we just print it)
                event
            )
            |> Seq.takeWhile (hasCorrelationId >> not)  // this will consume until event with last correlation id occurs, then it will stop
            |> Seq.iter ignore
        )

        // then we consume the default connection
        consume (fun _ events ->
            events
            |> Seq.iter (printfn "%A")
        )
    }
    |> run
    |> ApplicationShutdown.withStatusCode
```

#### Tips, Notes, ...

##### GroupId
You can use different group id for every connection, but you have to explicitly define it, otherwise `GroupId.Random` will be used.
If you just define `useGroupId "foo"`, it will be shared for all connections as default.
You can define specific group id for a connection by
```fs
useGroupIdFor "connection" "groupId-for-connection"
```

##### Commit Message
You can use different commit message mode for every connection, but you have to explicitly define it, otherwise `CommitMessage.Automatically` will be used.
If you just define `useCommitMessage CommitMessage.Manually`, it will be shared for all connections as default.
You can define specific commit message for a connection by
```fs
useCommitMessageFor "connection" CommitMessage.Manually
```
**NOTE**: Kafka application will also Manually commit the handled message for you - so there is nothing else to do, than just set a commit message mode.

## Release
1. Increment version in `KafkaApplication.fsproj`
2. Update `CHANGELOG.md`
3. Commit new version and tag it
4. Run `$ fake build target release`
5. Go to `nuget-server` repo, run `faket build target copyAll` and push new versions

## Development
### Requirements
- [dotnet core](https://dotnet.microsoft.com/learn/dotnet/hello-world-tutorial)
- [FAKE](https://fake.build/fake-gettingstarted.html)

### Build
```bash
fake build
```
