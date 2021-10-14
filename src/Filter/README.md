Filter
======

It filters input stream by specific configuration and then filters out content you want.

```
[InputStream] ───> (Filter<configuration> >> ContentFilter<'UnwantedData>) ───> [OutputStream]
```

## Filter computation expression
It allows you to create a filter application easier. It has build-in a filter consumer, metrics, etc.

Filter computation expression returns `Application of FilterApplication<'InputEvent, 'OutputEvent>` and it is run by `Application.run` function.

| Function | Arguments | Description |
| --- | --- | --- |
| addCustomMetricValues | `CreateCustomValues: InputOrOutputEvent<'InputEvent, 'OutputEvent> -> (string * string) list` | It will _register_ a function to which create a custom values. Those values will be added to metric set key to both input and output events for metrics. |
| filterTo | `connectionName: string`, `FilterContent<'InputEvent, 'OutputEvent>`, `FromDomain<'OutputEvent>` | It will create producer with filter content function. |
| from | `Configuration<'InputEvent, 'OutputEvent>` | It will create a base kafka application parts. This is mandatory and configuration must contain all dependencies. |
| getCommonEventBy | `GetCommonEvent: InputOrOutputEvent<'InputEvent, 'OutputEvent> -> CommonEvent` | It will _register_ a function to get common data out of both input and output events for metrics. |
| getFilterValue | `GetFilterValue: 'InputEvent -> 'FilterValue option` | It will _register_ a function to get a generic 'FilterValue from the Input Event - to be used in Filter. Otherwise 'FilterValue is ignored. |
| parseConfiguration | `parseFilterValue: (RawData -> 'FilterValue)`, `configurationPath: string` | It parses the configuration file from the path. Configuration must have the correct schema (_see below_). |

### FilterContent
It is a function, which is responsible for filtering events.
```fs
type FilterContent<'InputEvent, 'OutputEvent> = ProcessedBy -> 'InputEvent -> 'OutputEvent list
```

## Filter Configuration

Filter will use configuration to filter input events. Values in configuration determines, what is allowed. If section is empty, all values are allowed.

#### Allow everything:
This allows all filter values implicitly.

```json
{
    "filter": {
        "spot": []
    }
}
```

#### Allow everything explicitly:
This allows all filter values explicitly.

```json
{
    "filter": {
        "spot": [],
        "values": []
    }
}
```

#### Allow only specific spot:
And all filter values.

```json
{
    "filter": {
        "spot": [
            { "zone": "prod", "bucket": "all" }
        ]
    }
}
```

#### Allow one spot and 2 values (consent intents)
```json
{
    "filter": {
        "spot": [
            { "zone": "prod", "bucket": "all" }
        ],
        "values": [
            { "purpose": "data_processing", "scope": "lmc_cz" },
            { "purpose": "employers_assessment", "scope": "lmc_cz" }
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
        parseEventWith Parser.parseInputEvent
    })

    filterTo "outputStream" Filter.filterContentFromInputEvent Serializer.fromDomain

    getCommonEventBy (function
        | Input event ->
            match event with
            | InputEvent.NewPersonIdentified newPersonIdentified ->
                newPersonIdentified
                |> NewPersonIdentified.Event.toCommon
            | InputEvent.NotRelevant rawEvent ->
                rawEvent
                |> RawEvent.toCommon

        | Output event ->
            match event with
            | OutputEvent.NewPersonIdentified publicEvent ->
                publicEvent
                |> NewPersonIdentified.PublicEvent.event
                |> Event.toCommon
    )
}
|> run
|> ApplicationShutdown.withStatusCode
```
