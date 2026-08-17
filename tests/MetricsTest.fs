module Alma.KafkaApplication.Test.Metrics

open Expecto
open Alma.Metrics
open Alma.ServiceIdentification
open Alma.KafkaApplication
open Alma.KafkaApplication.Compressor

let private okOrFail = function
    | Ok ok -> ok
    | Error error -> failtestf "Fail on %A" error

let private instance (value: string) = Create.Instance(value) |> okOrFail

let private configurationParts = function
    | Configuration (Ok parts) -> parts
    | Configuration (Error error) -> failtestf "Fail on %A" error

[<Tests>]
let metricsTest =
    testList "KafkaApplication - metrics" [
        testCase "should register a simple custom metric when showCustomMetric is used" <| fun _ ->
            let configuration =
                partialKafkaApplication {
                    showCustomMetric "test_registered_counter_total" SimpleMetricType.Counter "Test counter."
                }

            let parts = configuration |> configurationParts

            let expected = [
                CustomMetric.Simple {
                    Name = "test_registered_counter_total" |> MetricName.createOrFail
                    Type = SimpleMetricType.Counter
                    Description = "Test counter."
                }
            ]
            Expect.equal parts.CustomMetrics expected "Simple custom metric should be registered"
            Expect.isTrue parts.ShowMetrics "Metrics should be shown"

        testCase "should register a histogram custom metric when showCustomHistogramMetric is used" <| fun _ ->
            let configuration =
                partialKafkaApplication {
                    showCustomHistogramMetric "test_registered_histogram_seconds" [ 0.5; 1.0 ] "Test histogram."
                }

            let parts = configuration |> configurationParts

            let expected = [
                CustomMetric.Histogram {
                    Metric =
                        HistogramBuckets.create [ 0.5; 1.0 ]
                        |> HistogramMetric.createOrFail "test_registered_histogram_seconds"
                    Description = "Test histogram."
                }
            ]
            Expect.equal parts.CustomMetrics expected "Histogram custom metric should be registered"
            Expect.isTrue parts.ShowMetrics "Metrics should be shown"

        testCase "should return InvalidMetricName error when histogram metric name is empty" <| fun _ ->
            let (Configuration configuration) =
                partialKafkaApplication {
                    showCustomHistogramMetric "" [ 0.5 ] "Test histogram."
                }

            match configuration with
            | Error (MetricsError (InvalidMetricName _)) -> ()
            | other -> failtestf "Expected InvalidMetricName error, got %A" other

        testCase "should format prometheus histogram samples when histogram values are observed" <| fun _ ->
            let instance = instance "development-kafkaApplication-metricsHistogram-test"
            let histogramMetric =
                HistogramBuckets.create [ 0.5; 1.0 ]
                |> HistogramMetric.createOrFail "test_formatted_histogram_seconds"
            let customMetrics = [
                CustomMetric.Histogram {
                    Metric = histogramMetric
                    Description = "Test histogram."
                }
            ]

            ApplicationMetrics.observeCustomHistogramValue instance histogramMetric (SimpleDataSetKeys []) 0.25
            ApplicationMetrics.observeCustomHistogramValue instance histogramMetric (SimpleDataSetKeys []) 2.0
            let formatted = ApplicationMetrics.getMetricsState instance customMetrics

            let labels = "svc_domain=\"development\", svc_context=\"kafkaApplication\", svc_purpose=\"metricsHistogram\", svc_version=\"test\""
            Expect.stringContains formatted "# HELP test_formatted_histogram_seconds Test histogram." "Description should be in the help header"
            Expect.stringContains formatted "# TYPE test_formatted_histogram_seconds histogram" "Metric should be typed as histogram"
            Expect.stringContains formatted (sprintf "test_formatted_histogram_seconds_bucket{le=\"0.5\", %s} 1" labels) "First bucket should count one observation"
            Expect.stringContains formatted (sprintf "test_formatted_histogram_seconds_bucket{le=\"1\", %s} 1" labels) "Second bucket should count one observation"
            Expect.stringContains formatted (sprintf "test_formatted_histogram_seconds_bucket{le=\"+Inf\", %s} 2" labels) "Infinite bucket should count all observations"
            Expect.stringContains formatted (sprintf "test_formatted_histogram_seconds_sum{%s} 2.25" labels) "Sum should be the total of observed values"
            Expect.stringContains formatted (sprintf "test_formatted_histogram_seconds_count{%s} 2" labels) "Count should be the number of observations"

        testCase "should format prometheus counter sample when simple custom metric is incremented" <| fun _ ->
            let instance = instance "development-kafkaApplication-metricsCounter-test"
            let metricName = "test_formatted_counter_total" |> MetricName.createOrFail
            let customMetrics = [
                CustomMetric.Simple {
                    Name = metricName
                    Type = SimpleMetricType.Counter
                    Description = "Test counter."
                }
            ]

            ApplicationMetrics.incrementCustomMetricCount instance metricName (SimpleDataSetKeys [])
            ApplicationMetrics.incrementCustomMetricCount instance metricName (SimpleDataSetKeys [])
            let formatted = ApplicationMetrics.getMetricsState instance customMetrics

            let labels = "svc_domain=\"development\", svc_context=\"kafkaApplication\", svc_purpose=\"metricsCounter\", svc_version=\"test\""
            Expect.stringContains formatted "# TYPE test_formatted_counter_total counter" "Metric should be typed as counter"
            Expect.stringContains formatted (sprintf "test_formatted_counter_total {%s} 2" labels) "Counter sample should count both increments"

        testCase "should publish compressor batch size as gauge" <| fun _ ->
            let instance = instance "development-kafkaApplication-compressorBatchSize-test"
            let batchThreshold = BatchThreshold.tryCreate 10 |> Option.get

            CompressorMetrics.observeBatchSize instance batchThreshold
            let formatted = ApplicationMetrics.getMetricsState instance CompressorMetrics.metrics

            let labels = "svc_domain=\"development\", svc_context=\"kafkaApplication\", svc_purpose=\"compressorBatchSize\", svc_version=\"test\""
            Expect.stringContains formatted "# TYPE compressor_batch_size gauge" "Batch size should be typed as gauge"
            Expect.stringContains formatted (sprintf "compressor_batch_size {%s} 10" labels) "Gauge sample should hold the batch threshold"
    ]
