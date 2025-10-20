namespace Alma.KafkaApplication.Compressor

module internal CompressorMetrics =
    open System
    open Alma.Metrics
    open Alma.ErrorHandling
    open Alma.KafkaApplication

    let private metricBatchCreatedTotal = "compressor_batch_created_total" |> MetricName.createOrFail
    let private metricBatchSize = "compressor_batch_size" |> MetricName.createOrFail
    let private metricBatchSentTotal = "compressor_batch_sent_total" |> MetricName.createOrFail
    let private metricBatchSendDurationSeconds = "compressor_batch_send_duration_seconds" |> MetricName.createOrFail
    let private metricBatchSendFailuresTotal = "compressor_batch_send_failures_total" |> MetricName.createOrFail

    let metrics = [
        {
            Name = metricBatchCreatedTotal
            Type = Counter
            Description = "Counts the total number of batches created by the compressor, regardless of success or failure."
        }
        {
            Name = metricBatchSize
            Type = Histogram
            Description = "Observes the size of batches being created (number of events per batch)."
        }
        {
            Name = metricBatchSentTotal
            Type = Counter
            Description = "Counts how many times a batch was sent because the size  was reached."
        }
        {
            Name = metricBatchSendDurationSeconds
            Type = Histogram
            Description = "Measures the time it takes to send (HTTP POST) a batch to the target service."
        }
        {
            Name = metricBatchSendFailuresTotal
            Type = Counter
            Description = "Counts the number of failed attempts to send batches."
        }
    ]

    [<AutoOpen>]
    module private InternalState =
        let createKey instance labels =
            DataSetKey.createFromInstance instance labels
            |> Result.orFail

    //
    // Public state api
    //

    // Recording batch metrics

    let incrementBatchCreated instance =
        let dataSetKey = createKey instance []
        State.incrementMetricSetValue (Int 1) metricBatchCreatedTotal dataSetKey
        |> ignore

    let observeBatchSize instance batchThreshold =
        let dataSetKey = createKey instance []
        State.setMetricSetValue (Float (batchThreshold |> BatchThreshold.float)) metricBatchSize dataSetKey

    let incrementBatchSent instance size =
        let dataSetKey = createKey instance [ "size", size |> BatchSize.value |> string ]
        State.incrementMetricSetValue (Int 1) metricBatchSentTotal dataSetKey
        |> ignore

    let observeBatchSendDuration instance (duration: TimeSpan) =
        let dataSetKey = createKey instance []
        State.setMetricSetValue (Float duration.TotalSeconds) metricBatchSendDurationSeconds dataSetKey

    let incrementBatchSendFailure instance status =
        let dataSetKey = createKey instance [ "status", status ]
        State.incrementMetricSetValue (Int 1) metricBatchSendFailuresTotal dataSetKey
        |> ignore
