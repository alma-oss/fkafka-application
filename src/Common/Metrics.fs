namespace KafkaApplication

module internal ApplicationMetrics =
    open Metrics
    open ServiceIdentification

    type private Count = Count of int

    module private Count =
        let value (Count count) = count

    module private SimpleDataSetKeys =
        let value (SimpleDataSetKeys dataSetKeys) = dataSetKeys

    type private ApplicationMetric<'InputEvent, 'OutputEvent> =
        | InputEventsTotal of Instance * InputStreamName * 'InputEvent
        | OutputEventsTotal of Instance * OutputStreamName * 'OutputEvent
        | CustomMetricWithDataSetKey of Instance * MetricName * SimpleDataSetKeys

    [<AutoOpen>]
    module private InternalState =
        let private failOnError = function
            | Ok success -> success
            | Error error -> failwithf "Error: %A" error

        let createKey instance labels =
            DataSetKey.createFromInstance instance labels
            |> failOnError

        let private createKeyForStatus instance =
            createKey instance []

        let private createKeyForInputEvent (CreateInputEventKeys createKeys) instance inputStream event =
            createKeys inputStream event
            |> SimpleDataSetKeys.value
            |> createKey instance

        let private createKeyForOutputEvent (CreateOutputEventKeys createKeys) instance outputStream event =
            createKeys outputStream event
            |> SimpleDataSetKeys.value
            |> createKey instance

        let private metricStatus (Context context) = context |> sprintf "%s_status" |> MetricName.createOrFail
        let private metricTotalInputEvent (Context context) = context |> sprintf "%s_input_events_total" |> MetricName.createOrFail
        let private metricTotalOutputEvent (Context context) = context |> sprintf "%s_output_events_total" |> MetricName.createOrFail

        let private metricValueToCount = function
            | Int int -> Count int
            | _ -> Count 0

        let incrementState createInputKeys createOutputKeys = function
            | InputEventsTotal (instance, inputStream, event) ->
                event
                |> createKeyForInputEvent createInputKeys instance inputStream
                |> State.incrementMetricSetValue (Int 1) (metricTotalInputEvent instance.Context)
                |> metricValueToCount
            | OutputEventsTotal (instance, outputStream, event) ->
                event
                |> createKeyForOutputEvent createOutputKeys instance outputStream
                |> State.incrementMetricSetValue (Int 1) (metricTotalOutputEvent instance.Context)
                |> metricValueToCount
            | CustomMetricWithDataSetKey (instance, metricName, (SimpleDataSetKeys labels)) ->
                labels
                |> createKey instance
                |> State.incrementMetricSetValue (Int 1) metricName
                |> metricValueToCount

        let enableContextStatus (instance: Instance) =
            let metricName = metricStatus instance.Context
            let dataSetKey =
                instance
                |> createKeyForStatus

            State.enableStatusMetric metricName dataSetKey

        let getFormattedMetricsForPrometheus (customMetrics: CustomMetric list) (instance: Instance) _ =
            let formatMetric metricType description metricName =
                metricName
                |> State.getMetric
                |> function
                    | Some metric ->
                        { metric with
                            Description = Some description
                            Type = Some metricType
                        }
                        |> Metric.format
                    | _ -> ""
            let formatCounter = formatMetric MetricType.Counter

            let customMetrics =
                customMetrics
                |> List.rev
                |> List.map (fun customMetric ->
                    formatMetric customMetric.Type customMetric.Description customMetric.Name
                )

            [
                instance.Context |> metricStatus |> formatMetric MetricType.Gauge "Current instance status."
                ServiceStatus.getFormattedValue()
                ResourceAvailability.getFormattedValue()
                instance.Context |> metricTotalInputEvent |> formatCounter "Total input event count."
                instance.Context |> metricTotalOutputEvent |> formatCounter "Total output event count."
            ]
            @ customMetrics
            |> String.concat ""

    //
    // Public state api
    //

    // Instance status

    let private createNoKeys _ _ = SimpleDataSetKeys []
    let private createNoInputKeys = CreateInputEventKeys createNoKeys
    let private createNoOutputKeys = CreateOutputEventKeys createNoKeys

    let enableContext instance =
        instance
        |> enableContextStatus

    // Changing state

    let incrementTotalInputEventCount createKeys instance inputStream event =
        (instance, inputStream, event)
        |> InputEventsTotal
        |> incrementState createKeys createNoOutputKeys
        |> ignore

    let incrementTotalOutputEventCount createKeys instance outputStream event =
        (instance, outputStream, event)
        |> OutputEventsTotal
        |> incrementState createNoInputKeys createKeys
        |> ignore

    let incrementCustomMetricCount instance metricName labels =
        (instance, metricName, labels)
        |> CustomMetricWithDataSetKey
        |> incrementState createNoInputKeys createNoOutputKeys
        |> ignore

    let setCustomMetricValue instance metricName (SimpleDataSetKeys labels) value =
        labels
        |> createKey instance
        |> State.setMetricSetValue value metricName

    // Showing state

    let showStateOnWebServerAsync instance customMetrics (settings: WebServerPart list) route =
        let route =
            route
            |> MetricsRoute.value

        let getMetrics =
            instance
            |> getFormattedMetricsForPrometheus customMetrics

        settings
        |> WebServer.runStateAsync route getMetrics
