Filter
======

It filters input stream by specific configuration and then filters out content you want.

```
[InputStream] ───> (Filter<configuration> >> ContentFilter<'UnwantedData>) ───> [OutputStream]
```

## Filter computed expression
It allows you to create a filter application easier. It has build-in a filter consumer, metrics, etc.

Filter computed expression returns `FilterApplication<'InputEvent, 'OutputEvent>` and it is run by `Application.run` function.

| Function | Arguments | --- |
| --- | --- | --- |
| filterTo | `connectionName: string`, `FilterContent<'InputEvent, 'OutputEvent>`, `FromDomain<'OutputEvent>` | It will create producer with filter content function. |
| from | `Configuration<'InputEvent, 'OutputEvent>` | It will create a base kafka application parts. This is mandatory and configuration must contain all dependencies. |
| getCommonEventDataBy | `GetCommonEventData<'InputEvent, 'OutputEvent>` | It will _register_ a function to get common data out of both input and output events for metrics. |
| parseConfiguration | `configurationPath: string` | It parses the configuration file from the path. Configuration must have the correct schema (_see below_). |

## Filter Configuration

Filter will use configuration to filter input events. Values in configuration determines, what is allowed. If section is empty, all values are allowed.

#### Allow everything:
```json
{
    "filter": {
        "spot": []
    }
}
```

#### Allow only specific spot:
```json
{
    "filter": {
        "spot": [
            { "zone": "prod", "bucket": "all" }
        ]
    }
}
```

## Example
```fs
filterContentFilter {
    parseConfiguration "./configuration/configuration.json"

    from (partialKafkaApplication {
        merge (environment {
            file ["./.env"; "./.dist.env"]
            ifSetDo "VERBOSITY" Log.setVerbosityLevel

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

        showMetricsOn "/metrics"
    })

    filterTo "outputStream" filterContentFromInputEvent fromDomain

    getCommonEventDataBy (function
        | Input event ->
            match event with
            | InputEvent.NewPersonIdentified (NewPersonIdentified e) -> { Event = e.Event; Spot = { Zone = e.Zone; Bucket = e.Bucket } }
            | InputEvent.NotRelevant e -> { Event = e.Event; Spot = { Zone = e.Zone; Bucket = e.Bucket } }
        | Output event ->
            match event with
            | OutputEvent.NewPersonIdentified (FilteredNewPersonIdentified e) -> { Event = e.Event; Spot = { Zone = e.Zone; Bucket = e.Bucket } }
    )
}
|> runFilter parseInputEvent
```
