F-Kafka Application
===================

Framework for kafka application.
It contains computed expressions to help with building this kind of application and have in-build metrics, logging, parsing, etc..

## Install
```
dotnet add package -s $NUGET_SERVER_PATH Lmc.FkafkaApplication
```
Where `$NUGET_SERVER_PATH` is the URL of nuget server
- it should be http://development-nugetserver-common-stable.service.devel1-services.consul:{PORT} (_make sure you have a correct port, since it changes with deployment_)
- see http://consul-1.infra.pprod/ui/devel1-services/services/development-nugetServer-common-stable for detailed information (and port)

## Use

### Connect to kafka stream
This is the most elemental reason for this application framework, so there are simple ways for simple cases.
But there are also more complex, for moc complex applications, where you need more connections.

This is also mandatory, to define at least one connection.

#### Simple one connection
Following example is the easiest setup, you can get.

```fs
open Kafka
open ServiceIdentification
open KafkaApplication

[<EntryPoint>]
let main argv =
    kafkaApplication {
        instance { Domain="my"; Context="simple"; Purpose="example"; Version="local" }

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

    0
```

#### More than one connection
Instead of previous example, where our application uses only one connections, there are cases, where we need more than that.

```fs
open Kafka
open ServiceIdentification
open KafkaApplication

[<EntryPoint>]
let main argv =
    let brokerList = BrokerList "127.0.0.1:9092"

    kafkaApplication {
        instance { Domain="my"; Context="simple"; Purpose="example"; Version="local" }

        // this will create only default connection, which can be consumed by the default `consume` funcion only
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

    0
```
Notes:
- It does NOT matter in which order you create connections - they are just _registered_.
- It does matter in which order you consume events - they are run in the same order of registration and the next consume will be run ONLY when the previous ended. So make sure it is not infinite, if you want to consume the other ones too.
- All connections are available in the `ApplicationRuntimeParts.Connections` of the consume handler function ([__TODO__ - link to example])

#### Event more connections
Now we have even more connection and different relationships between them.
Keep in mind, that this example is simplified and it is missing the parsing logic ([__TODO__ - link to example])

```fs
open Kafka
open ServiceIdentification
open KafkaApplication

[<EntryPoint>]
let main argv =
    let brokerList = BrokerList "127.0.0.1:9092"

    kafkaApplication {
        instance { Domain="my"; Context="simple"; Purpose="example"; Version="local" }

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

    0
```

## Release
1. Increment version in `KafkaApplication.fsproj`
2. Update `CHANGELOG.md`
3. Commit new version and tag it
4. Run `$ fake build target release`

## Development
### Requirements
- [dotnet core](https://dotnet.microsoft.com/learn/dotnet/hello-world-tutorial)
- [FAKE](https://fake.build/fake-gettingstarted.html)

### Build
```bash
fake build
```

### Watch
```bash
fake build target watch
```
