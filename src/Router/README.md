Content-Based Router
====================

It routes events from `inputStream` to the different streams based on configuration.

```
                           ┌─────> [OutputStream-1]
[InputStream] ───> (CBRouter) ───> [OutputStream-2]
                           └─────> [OutputStream-3]
```

## Content-Based Router computed expression
It allows you to create a Content-Based router application easier. It has build-in a routing, metrics, etc.
Keep in mind, that `RouterApplication` is not generic as the other patterns, because it uses its own type `EventToRoute` which is specifically designed for the router.

Router computed expression returns `Application of RouterApplication<'InputEvent, 'OutputEvent>` and it is run by `Application.run` function.

| Function | Arguments | --- |
| --- | --- | --- |
| from | `Configuration<'InputEvent, 'OutputEvent>` | It will create a base kafka application parts. This is mandatory and configuration must contain all dependencies. |
| parseConfiguration | `configurationPath: string` | It parses the configuration file from the path. Configuration must have the correct schema (_see below_). |
| routeToBrokerFromEnv | `brokerListEnvironmentKey: string` | It checks the configuration for environment value and use it for default routing broker list. |

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
            ifSetDo "VERBOSITY" Log.setVerbosityLevel

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
    })

    routeToBrokerFromEnv "KAFKA_BROKER"
}
|> run
|> ApplicationShutdown.withStatusCode
```
