Content-Based Router
====================

It routes events from `inputStream` to the different streams based on configuration.

```
                           ┌─────> [OutputStream-1]
[InputStream] ───> (CBRouter) ───> [OutputStream-2]
                           └─────> [OutputStream-3]
```

## Content-Based Router computation expression
It allows you to create a Content-Based router application easier. It has build-in a routing, metrics, etc.

Content-Based Router computation expression returns `Application of ContentBasedRouterApplication<'InputEvent, 'OutputEvent>` and it is run by `Application.run` function.

| Function | Arguments | Description |
| --- | --- | --- |
| addCustomMetricValues | `CreateCustomValues: InputOrOutputEvent<'InputEvent, 'OutputEvent> -> (string * string) list` | It will _register_ a function to which create a custom values. Those values will be added to metric set key to both input and output events for metrics. |
| from | `Configuration<'InputEvent, 'OutputEvent>` | It will create a base kafka application parts. This is mandatory and configuration must contain all dependencies. |
| getCommonEventBy | `GetCommonEvent: InputOrOutputEvent<'InputEvent, 'OutputEvent> -> CommonEvent` | It will _register_ a function to get common data out of both input and output events for metrics. |
| parseConfiguration | `configurationPath: string` | It parses the configuration file from the path. Configuration must have the correct schema (_see below_). |
| route | `RouteEvent<'InputEvent, 'OutputEvent>` | It will _register_ a route event function. |
| routeToBrokerFromEnv | `brokerListEnvironmentKey: string`, `FromDomain<'OutputEvent>` | It checks the configuration for environment value and use it for default routing broker list. |
| routeWithApplication | `PatternRuntimeParts -> RouteEvent<'InputEvent, 'OutputEvent>` | It will inject `PatternRuntimeParts` into routeEvent and then it will _register_ a route event function. |

### RouteEvent
It is a function, which is responsible for routing events.
```fs
type RouteEvent<'InputEvent, 'OutputEvent> = ProcessedBy -> 'InputEvent -> 'OutputEvent option
```

## Configuration
Routing configuration must be defined in `routing.json` file.

It looks like this:
```json
{
    "route": [
        {
            "event": "event_1",
            "targetStream": "OutputStream-1"
        },
        {
            "event": "event_2",
            "targetStream": "OutputStream-2"
        },
        {
            "event": "event_3",
            "targetStream": "OutputStream-3"
        }
    ]
}
```

## Example
```fs
contentBasedRouter {
    parseConfiguration "./configuration/routing.json"

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
        })

        showMetricsOn "/metrics"
        parseEventWith InputEvent.parse
    })

    routeToBrokerFromEnv "KAFKA_BROKER" OutputEvent.fromDomain
    route InputEvent.route

    getCommonEventBy (function
        | Input event -> event |> InputEvent.commonEvent
        | Output event -> event |> Output.commonEvent
    )
}
|> run
|> ApplicationShutdown.withStatusCode
```
