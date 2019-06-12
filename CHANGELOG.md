# Changelog

<!-- There is always Unreleased section on the top. Subsections (Add, Changed, Fix, Removed) should be Add as needed. -->
## Unreleased
- Add `Service` (domain, context) to graylog logs, as additional fields

## 3.2.0 - 2019-06-11
- Check kafka resources in interval while consuming

## 3.1.1 - 2019-06-10
- Fix reading environment variables, when no file is found.

## 3.1.0 - 2019-06-10
- Log very verbosely all environment variables after reading a dotenv file

## 3.0.0 - 2019-06-10
- Update Kafka library
    - [**BC**] Use new version of `Confluent.Kafka` client
    - [**BC**] Allow `StreamName` to be defined by `Instance`
- [**BC**] Use `KafkaApplication.ConnectionConfiguration` instead of `Kafka.ConnectionConfiguration` to allow only `StreamName.Instance`
- [**BC**] Use `Instance` instead of `StreamName` in `ManyTopicsConnectionConfiguration` to allow only `StreamName.Instance`
- [**BC**] Use only `StreamName.Instance` in Router pattern

## 2.0.0 - 2019-06-07
- Update Dependencies
    - [**BC**] Allow `Metrics.ResourceAvailability` to be `Common`, `Service` or `MultiTenantService`

## 1.3.0 - 2019-06-06
- Add `Environment.logToGraylog` function to allow getting Graylog host from environment variable
- Add `CheckResourceInInterval` function
- Mark application as disabled on start up

## 1.2.0 - 2019-06-05
- Fix debug logging to the Graylog by replacing `{`, `}` characters to `(`, `)`
- Use `Logging.Log` verbosity in the Graylog Logger
- Change Graylog message field `context` to `application_context` to prevent misleading with Service Identification naming

## 1.1.0 - 2019-06-05
- Add `IncrementMetric` to Consume Runtime Parts to allow custom metrics
- Add `CustomMetrics`
- Allow Enable/Disable Resource Availability from `ConsumeRuntimeParts`
- Add `Graylog` logger for runtime

## 1.0.0 - 2019-05-29
- Initial implementation
