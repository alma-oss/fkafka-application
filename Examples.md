todo
====

```fs
// y is depending on x to be available
let (==>) x y =
    // ...
```

```fs
resourceAvailability {
    // group 1
    kafka "kfall-dev1"
    ==> kafkaTopic "kfall-dev1" inputStream

    // group 2
    kafka "kfall-dev1"
    ==> kafkaTopics "kfall-dev1" [ outputStreams ]
}
|> tee (ResourceAvailability.checkInInterval 15s)   // state of the resources will be _only_ checked and reported to the /metrics - it will NOT change the result of `map` function
|> ResourceAvailability.map (runApp ...)            // this will be mapped by the current result of the Run of computation expression - not by checking in interval
```

```fs
type Resource = {
    Type
    Identification
    Location
    Audience
}

type ResourceStatus =
    | Up
    | Down

type ResourceStatus = {
    Resource
    Status: ResourceStatus
}

type ResourceGroup = Resource list

type ResourceState = {
    Groups = ResourceGroup list
    Status // or Statuses - per all resources (groups?)
}

let checkInInterval (interval: seconds) resourceState =
    async {
        while true do
            resourceState.Groups
            |> List.iter checkAvailability  // it will check of EACH resource in the group, but if the "parent" is DOWN, all others will be DOWN right away without checking
            // current status will be save to the State from checkAvailability function

            do! Async.sleep interval
    }
    |> Async.Start
    |> ignore
```


## All in computation expression???

```fs
resourceAvailability {
    // resource group 1
    kafka "kfall-dev1"
    ==> kafkaTopic "kfall-dev1" inputStream

    // resource group 2
    kafka "kfall-dev1"
    ==> kafkaTopics "kfall-dev1" [ outputStreams ]

    checkInInterval 15s

    // run the app, and hold/kill it when resources are not available
    do! runApp ...
}
|> runWithResources {
    KafkaChecker = Kafka.Admin.CheckCluster
    KafkaTopicChecker = Kafka.Admin.TopicExists
    KafkaTopicsChecker = Kafka.Admin.TopicsExists
}
```

## V2
```fs
application {
    useResources = resources {
        // resource group 1
        kafka "kfall-dev1"
        ==> kafkaTopic "kfall-dev1" inputStream

        // resource group 2
        kafka "kfall-dev1"
        ==> kafkaTopics "kfall-dev1" [ outputStreams ]

        checkInInterval 15s
    }

    // run the app, and hold/kill it when resources are not available
    do! runApp ...
}
|> runWithResources {
    KafkaChecker = Kafka.Admin.CheckCluster
    KafkaTopicChecker = Kafka.Admin.TopicExists
    KafkaTopicsChecker = Kafka.Admin.TopicsExists
}
```

## Router
```fs
// Program.fs

try
        showStateOnWebServerAsync "/metrics"
        |> Async.Start

        enableInstance instance

        ("kafka_topic", kafkaConfiguration.Topic, kafkaConfiguration.BrokerList, "sys")
        |> enableResource instance

        runConsuming instance groupId kafkaConfiguration routerPath
        0
    with
    | :? KafkaException as e ->
        Log.error "Kafka" (sprintf "%A" e)
        1

application {
    useResources resources {
        // resource group 1
        kafka "kfall-dev1"
        ==> kafkaTopic "kfall-dev1" inputStream

        // resource group 2
        kafka "kfall-dev1"
        ==> kafkaTopics "kfall-dev1" [ outputStreams ]

        checkInInterval 15s
    }

    showMetricsOnRoute "/metrics"

    consumeStream kafkaReader {
        withConfiguration kafkaConfiguration
        withGroupId groupId kafkaConfiguration

        onRawEvent {
            OnEventToSerialize =
                tee (EventToRoute.raw >> incrementInputCount)
                >> routeEvent
        }

        on NewPersonIdentified {
            OnNewPersonIdentifiedEvent = printfn "log event %A"
        }

        onError Log.Error "Kafka"
    }
}
|> runWithCheckers {
    KafkaChecker = Kafka.Admin.CheckCluster
    KafkaTopicChecker = Kafka.Admin.TopicExists
    KafkaTopicsChecker = Kafka.Admin.TopicsExists
}
```

## Kafka consumer app

### Router
```fs
// all "Items" should be something like ApplicationPart (like WebPart)

application {
    logger Log                              // this registers Logger, which will be used for log debuging, errors, ...

    envFile [ "../.env"; "../.dist.env" ]   // register envFiles -> select first available

    let verbosity = env "VERBOSITY"                 // get env from envFile
    verbosity                                       // since the value is optional, it has explicit map
    |> ApplicationPart.map Log.setVerbosityLevel    // if Some, use it as verbosity level

    let! instanceString = env "INSTANCE"    // get env from envFile
    let! instance = result {                // every "env" should also Log.debug "Dotenv"
        return
            instanceString
            |> Instance.parse               // Instance must be set, but it also must be in right format
            |> Result.ofOption
            |> Result.mapError InstanceFormatError
    }
    |> ApplicationPart.ofResult

    useInstance instance                    // register current application instance, to enable it in metrics, ...

    let! groupId = env "GROUP_ID"
    let groupId =
        groupId
        |> ApplicationPart.map GroupId.fromString   // it should check whether it has value, and if not, it will use GroupId.Random

    let! brokerList = env "KAFKA_BROKER"
    let! inputStream = env "INPUT_STREAM"

    useKafka {                              // this should automatically register resource { kafka brokerList ==> kafkaTopic brokerList inputStream }
        BrokerList = BrokerList brokerList  // also Log.debug "Kafka", if logger is set
        Topic = inputStream
    }

    let! router = result {
        let! routerPath = findConfiguration [ "configuration"; "../configuration" ] "routing.json"  // finds the first existence of configuration file

        return Router.parseRouter routerPath    // this must be here, to register resources
    }
    |> Result.mapError ConfigurationNotFoundError
    |> ApplicationPart.ofResult

    showMetricsOn "/metrics"                    // run web server which shows current metrics

    dependsOnResources resources {              // this will register more resources (some resources are registered automatically)
        kafka brokerList
        ==> kafkaTopics brokerList (Router.getStreams |> List.distinct)
    }

    //
    // === Router logic ===
    //

    // metrics
    let incrementOuputCount = incrementTotalOutputEventCount instance       // output metric should be in application itself (input is internal, since whole application is consumer which logs input events)

    // prepare producer
    Log.normal "Kafka" "Create producer ..."
    use producer = Producer.createProducer kafkaConfiguration.BrokerList

    // routing
    let routeEvent =
        routeEvent
            (Log.veryVerbose "Routing")
            incrementOuputCount
            producer
            router

    // consume stream
    consumeStream consentsEventReader {         // this will run consuming with checking resources (on every event?)
        OnEventToSerialize =
            tee (EventToRoute.raw >> incrementInputCount)
            >> routeEvent
    }
}
// whole running should be in try/catch to log errors and keeps application running (or maybe set some policies, what to do on error, ...)
|> run
```

### Filter>>ContentFilter
```fs
// all "Items" should be something like ApplicationPart (like WebPart)

application {
    logger Log                              // this registers Logger, which will be used for log debuging, errors, ...

    envFile [ "../.env"; "../.dist.env" ]   // register envFiles -> select first available

    let verbosity = env "VERBOSITY"                 // get env from envFile
    verbosity                                       // since the value is optional, it has explicit map
    |> ApplicationPart.map Log.setVerbosityLevel    // if Some, use it as verbosity level

    let! instanceString = env "INSTANCE"    // get env from envFile
    let! instance = result {                // every "env" should also Log.debug "Dotenv"
        return
            instanceString
            |> Instance.parse               // Instance must be set, but it also must be in right format
            |> Result.ofOption
            |> Result.mapError InstanceFormatError
    }
    |> ApplicationPart.ofResult

    useInstance instance                    // register current application instance, to enable it in metrics, ...

    let! groupId = env "GROUP_ID"
    let groupId =
        groupId
        |> ApplicationPart.map GroupId.fromString   // it should check whether it has value, and if not, it will use GroupId.Random

    let! brokerList = env "KAFKA_BROKER"
    let! inputStream = env "INPUT_STREAM"

    useKafka {                              // this should automatically register resource { kafka brokerList ==> kafkaTopic brokerList inputStream }
        BrokerList = BrokerList brokerList  // also Log.debug "Kafka", if logger is set
        Topic = inputStream
    }

    let! outputStream = env "OUTPUT_STREAM"
    let outputStream =
        outputStream
        |> StreamName
        |> OutputStreamName

    let! configuration = configuration {        // configuration computation expression to help parsing configuration
        file "configuration.json"
        findIn [ "configuration"; "../configuration" ]
        parseBy Filter.parseFilterConfiguration
    }

    showMetricsOn "/metrics"                    // run web server which shows current metrics

    dependsOnResources resources {              // this will register more resources (some resources are registered automatically)
        kafka brokerList
        ==> kafkaTopic brokerList outputStream
    }

    //
    // === Filter logic ===
    //

    // metrics
    let incrementOuputCount = incrementTotalOutputEventCount instance outputStream       // output metric should be in application itself (input is internal, since whole application is consumer which logs input events)

    // prepare producer
    Log.normal "Kafka" "Create producer ..."
    let (OutputStreamName (StreamName outputStream)) = outputStream
    use producer = Producer.createProducer kafkaConfiguration.BrokerList
    let produce = Producer.produceMessage producer outputStream

    let produceEvent event =
        event
        |> tee (Serializer.serialize >> produce)
        |> incrementOutputCount
    let produceOutputEvents events =
        events
        |> List.iter produceEvent

    // consume stream
    consumeStream consentsEventReader {         // this will run consuming with checking resources (on every event?)
        OnEventToSerialize =
            tee incrementInputCount
            >> filterByConfiguration configuration
            >> Option.map (filterContentFromInputEvent >> produceOutputEvents)
    }
}
// whole running should be in try/catch to log errors and keeps application running (or maybe set some policies, what to do on error, ...)
|> run
```

### Deriver
```fs
// all "Items" should be something like ApplicationPart (like WebPart)

application {
    logger Log                              // this registers Logger, which will be used for log debuging, errors, ...

    envFile [ "../.env"; "../.dist.env" ]   // register envFiles -> select first available

    let verbosity = env "VERBOSITY"                 // get env from envFile
    verbosity                                       // since the value is optional, it has explicit map
    |> ApplicationPart.map Log.setVerbosityLevel    // if Some, use it as verbosity level

    let! instance = instance {              // computation expression to help with parsing instance
        fromEnv "INSTANCE"
    }

    useInstance instance                    // register current application instance, to enable it in metrics, ...

    let! groupId = env "GROUP_ID"
    let groupId =
        groupId
        |> ApplicationPart.map GroupId.fromString   // it should check whether it has value, and if not, it will use GroupId.Random

    let! brokerList = env "KAFKA_BROKER"
    let! inputStream = env "INPUT_STREAM"

    useKafka {                              // this should automatically register resource { kafka brokerList ==> kafkaTopic brokerList inputStream }
        BrokerList = BrokerList brokerList  // also Log.debug "Kafka", if logger is set
        Topic = inputStream
    }

    let! outputStream = env "OUTPUT_STREAM"
    let outputStream =
        outputStream
        |> StreamName
        |> OutputStreamName

    showMetricsOn "/metrics"                    // run web server which shows current metrics

    dependsOnResources resources {              // this will register more resources (some resources are registered automatically)
        kafka brokerList
        ==> kafkaTopic brokerList outputStream
    }

    //
    // === Deriver logic ===
    //

    // metrics
    let incrementOuputCount = incrementTotalOutputEventCount instance outputStream       // output metric should be in application itself (input is internal, since whole application is consumer which logs input events)

    // prepare producer
    use! produce = producer {               // computation expression to help create producer and produce function
        toStream outputStream
        serialize Serializer.serialize
        increment incrementOutputCount
    }
    let produceOutputEvents events =
        events
        |> List.iter produce

    // consume stream
    consumeStream consentsEventReader {         // this will run consuming with checking resources (on every event?)
        OnEventToSerialize =
            tee incrementInputCount
            >> deriveContentFromInputEvent
            >> produceOutputEvents
    }
}
// whole running should be in try/catch to log errors and keeps application running (or maybe set some policies, what to do on error, ...)
|> run
```

### Aggregator
```fs
// all "Items" should be something like ApplicationPart (like WebPart)

application {
    logger Log                              // this registers Logger, which will be used for log debuging, errors, ...

    envFile [ "../.env"; "../.dist.env" ]   // register envFiles -> select first available

    let verbosity = envAs "VERBOSITY" Log.setVerbosityLevel                 // get env from envFile

    let! instance = instance {              // computation expression to help with parsing instance
        fromEnv "INSTANCE"                  // it also calls `useInstance`
    }

    let! groupId = envAs "GROUP_ID" GroupId.fromString
    let! brokerList = env "KAFKA_BROKER"
    let! inputStream = env "INPUT_STREAM"

    useKafka {                              // this should automatically register resource { kafka brokerList ==> kafkaTopic brokerList inputStream }
        BrokerList = BrokerList brokerList  // also Log.debug "Kafka", if logger is set
        Topic = inputStream
    }

    let! dataCenter = envAs "DATA_CENTER" DataCenter                        // envAs to map env with consturctor directly
    let! outputStream = envAs "OUTPUT_STREAM" (StreamName >> OutputStream)

    let! contractApi = env "CONTRACT_API"
    let! contractApiStatus = env "CONTRACT_API_STATUS"
    let getContract = getContract (Log.debug "Contract") (Log.error "Contract") contractApi     // todo - how to use `getContract` and automatically check httpApi as resource

    showMetricsOn "/metrics"                    // run web server which shows current metrics

    dependsOnResources resources {              // this will register more resources (some resources are registered automatically)
        kafka brokerList
        ==> kafkaTopic brokerList outputStream
    }

    //
    // === Aggregator logic ===
    //

    // metrics
    let incrementOuputCount = incrementTotalOutputEventCount instance outputStream       // output metric should be in application itself (input is internal, since whole application is consumer which logs input events)

    // state
    let stateStorage = create<UniqueKey, LastInteraction.StateValue>()

    let getStatePerIntent createKey intent =
        intent
        |> createKey
        |> getState stateStorage

    let setStatePerIntent createKey (intent, state) =
        let key =
            intent
            |> createKey
        setState stateStorage key state

    let countTotalStates () =
        countAll stateStorage

    // rule
    let createResourceHref = Resource.createHref dataCenter
    let rule = LastInteraction.rule getContract (createOutputEvent createResourceHref)

    // fill state to the offset
    let (fillStatePerEvent, cancelationToken) = startFillStateToOffset kafkaConfiguration rule getStatePerIntent setStatePerIntent countTotalStates // this will start showing the current progress

    // todo - this is tricky because kafkaConfiguration is "inside" the computation expression and not visible here, so all consuming should be done with application functions
    consumeStreamToOffset offset consentsInputEventReader {
        OnInputEvent = fillStatePerEvent
    }
    cancelationToken |> cancel // cancel showing status on async

    // prepare producer
    use! produce = producer {               // computation expression to help create producer and produce function
        toStream outputStream
        serialize Serializer.serialize
        increment incrementOutputCount
    }

    // aggregate
    let aggregateInputEvent = aggregateInputEvent rule getStatePerIntent setStatePerIntent produceEvent

    // consume stream
    consumeStream consentsEventReader {         // this will run consuming with checking resources (on every event?)
        OnEventToSerialize =
            tee incrementInputCount
            >> aggregateInputEvent
    }
}
// whole running should be in try/catch to log errors and keeps application running (or maybe set some policies, what to do on error, ...)
|> run
```

### Simplified?
```fs
// all "Items" should be something like ApplicationPart (like WebPart)

application {
    logger Log                              // this registers Logger, which will be used for log debuging, errors, ...

    envFile [ "../.env"; "../.dist.env" ]   // register envFiles -> select first available

    //loggerVerbosityBy "VERBOSITY"
    //let verbosity = envAs "VERBOSITY" Log.setVerbosityLevel                 // get env from envFile

    instanceFromEnv "INSTANCE"  // calls Instance.parse and stores the value in ApplicationConfiguration
    groupIdFromEnv "GROUP_ID"   // calls GroupId.fromString and stores the value in ApplicationConfiguration

    useKafkaFromEnv {                   // it is not a ConnectionConfiguration, but ConnectionConfigurationFromEnvironemnt (same keys, but it only has strings with env names)
        BrokerList = "KAFKA_BROKER"     // also Log.debug "Kafka", if logger is set
        Topic = "INPUT_STREAM"
    }

    // this could be used to pre-register env variables into some map, which could be later passed to "consume" function
    envs [
        "DATA_CENTER"
        "OUTPUT_STREAM" // how to do mapping?
        "CONTRACT_API"
        "CONTRACT_API_STATUS"
    ]

    showMetricsOn "/metrics"            // run web server which shows current metrics

    consume (fun events application ->  // application can contain all needed parts, like envs, logger, ...
        let { Logger = log; Environment = envs; IncrementOutputCount = incrementOutputCount; ConsumerConfiguration = consumerConfiguration } = application

        let dataCenter = envs.["DATA_CENTER"] |> DataCenter
        let outputStream = envs.["OUTPUT_STREAM"] |> StreamName |> OutputStream

        // metrics                                                          // output metric should be in application itself (input is internal, since whole application is consumer which logs
        let incrementOutputCount = incrementOutputCount outputStream        // it will already have instance injected

        let contractApiStatus = envs.["CONTRACT_API_STATUS"]
        let getContract =
            envs.["CONTRACT_API"]
            |> getContract (log.Debug "Contract") (log.Error "Contract")    // todo - how to use `getContract` and

        //
        // === Aggregator logic ===
        //

        // state
        let stateStorage = create<UniqueKey, LastInteraction.StateValue>()

        let getStatePerIntent createKey intent =
            intent
            |> createKey
            |> getState stateStorage

        let setStatePerIntent createKey (intent, state) =
            let key =
                intent
                |> createKey
            setState stateStorage key state

        let countTotalStates () =
            countAll stateStorage

        // rule
        let createResourceHref = Resource.createHref dataCenter
        let rule = LastInteraction.rule getContract (createOutputEvent createResourceHref)

        // fill state to the offset
        let (fillStatePerEvent, cancelationToken) = startFillStateToOffset kafkaConfiguration rule getStatePerIntent setStatePerIntent countTotalStates // this will start showing the current progress

        {
            OnInputEvent = fillStatePerEvent
        }
        |> consentsInputEventReader
        |> Kafka.consumeStreamToOffset consumerConfiguration offset

        // cancel cancelationToken to cance async showing of filling progress

        // prepare producer
        let produce = producer {               // computation expression to help create producer and produce function
            toStream outputStream
            serialize Serializer.serialize
            increment incrementOutputCount
        }

        // aggregate
        let aggregateInputEvent = aggregateInputEvent rule getStatePerIntent setStatePerIntent produceEvent

        events
        |> Seq.map (tee incrementInputCount)
        |> Seq.iter aggregateInputEvent
    )
}
// whole running should be in try/catch to log errors and keeps application running (or maybe set some policies, what to do on error, ...)
|> run
```
