Deriver
=======

It reads input stream and derives events to the derived events into output stream.

```
[InputStream] ───> (Deriver) ───> [OutputStream]
```

## Deriver computed expression
It allows you to create a deriver application easier. It has build-in a deriver consumer, metrics, etc.

Deriver computed expression returns `Application of DeriverApplication<'InputEvent, 'OutputEvent>` and it is run by `Application.run` function.

| Function | Arguments | --- |
| --- | --- | --- |
| deriveTo | `connectionName: string`, `DeriveEvent<'InputEvent, 'OutputEvent>`, `FromDomain<'OutputEvent>` | It will create producer with derive event function. |
| from | `Configuration<'InputEvent, 'OutputEvent>` | It will create a base kafka application parts. This is mandatory and configuration must contain all dependencies. |
| getCommonEventBy | `GetCommonEvent<'InputEvent, 'OutputEvent>` | It will _register_ a function to get common data out of both input and output events for metrics. |

## Deriver Configuration

The only configuration for deriver is the one in the `DeriveEvent` function.

## Example
```fs
deriver {
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

    deriveTo "outputStream" Deriver.deriveInputEvent Serializer.fromDomain

    getCommonEventBy (function
        | Input event ->
            match event with
            | InputEvent.PersonGaveConsentToIntent personGaveConsentToIntent ->
                personGaveConsentToIntent
                |> PersonGaveConsentToIntent.Event.toCommon
            | InputEvent.PersonWithdrewConsentToIntent personWithdrewConsentToIntent ->
                personWithdrewConsentToIntent
                |> PersonWithdrewConsentToIntent.Event.toCommon
            | InputEvent.NotRelevant rawEvent ->
                rawEvent
                |> RawEvent.toCommon

        | Output event ->
            match event with
            | OutputEvent.ConsentAcquired consentAcquired ->
                consentAcquired
                |> ConsentAcquired.InternalEvent.event
                |> Event.toCommon
            | OutputEvent.ConsentLost consentLost ->
                consentLost
                |> ConsentLost.InternalEvent.event
                |> Event.toCommon
    )
}
|> run Parser.parseInputEvent
|> ApplicationShutdown.withStatusCode
```
