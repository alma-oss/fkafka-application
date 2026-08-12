namespace Alma.KafkaApplication.Compressor

module internal CompressorMetrics =
    open System
    open Alma.Metrics
    open Feather.ErrorHandling
    open Alma.KafkaApplication

    let private metricBatchCreatedTotal = "compressor_batch_created_total" |> MetricName.createOrFail
    let private metricBatchSize =
        HistogramBuckets.create [ 1.0; 5.0; 10.0; 25.0; 50.0; 100.0; 250.0; 500.0; 1000.0; 2500.0; 5000.0; 10000.0 ]
        |> HistogramMetric.createOrFail "compressor_batch_size"
    let private metricBatchSentTotal = "compressor_batch_sent_total" |> MetricName.createOrFail
    let private metricBatchSendDurationSeconds =
        HistogramBuckets.create [ 0.005; 0.01; 0.025; 0.05; 0.1; 0.25; 0.5; 1.0; 2.5; 5.0; 10.0 ]
        |> HistogramMetric.createOrFail "compressor_batch_send_duration_seconds"
    let private metricBatchSendFailuresTotal = "compressor_batch_send_failures_total" |> MetricName.createOrFail

    let metrics = [
        CustomMetric.Simple {
            Name = metricBatchCreatedTotal
            Type = SimpleMetricType.Counter
            Description = "Counts the total number of batches created by the compressor, regardless of success or failure."
        }
        CustomMetric.Histogram {
            Metric = metricBatchSize
            Description = "Observes the size of batches being created (number of events per batch)."
        }
        CustomMetric.Simple {
            Name = metricBatchSentTotal
            Type = SimpleMetricType.Counter
            Description = "Counts how many times a batch was sent because the size  was reached."
        }
        CustomMetric.Histogram {
            Metric = metricBatchSendDurationSeconds
            Description = "Measures the time it takes to send (HTTP POST) a batch to the target service."
        }
        CustomMetric.Simple {
            Name = metricBatchSendFailuresTotal
            Type = SimpleMetricType.Counter
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
        State.observeHistogramSetValue metricBatchSize (batchThreshold |> BatchThreshold.float) dataSetKey

    let incrementBatchSent instance size =
        let dataSetKey = createKey instance [ "size", size |> BatchSize.value |> string ]
        State.incrementMetricSetValue (Int 1) metricBatchSentTotal dataSetKey
        |> ignore

    let observeBatchSendDuration instance (duration: TimeSpan) =
        let dataSetKey = createKey instance []
        State.observeHistogramSetValue metricBatchSendDurationSeconds duration.TotalSeconds dataSetKey

    let incrementBatchSendFailure instance status =
        let dataSetKey = createKey instance [ "status", status ]
        State.incrementMetricSetValue (Int 1) metricBatchSendFailuresTotal dataSetKey
        |> ignore
