# Changelog

<!-- There is always Unreleased section on the top. Subsections (Add, Changed, Fix, Removed) should be Add as needed. -->
## Unreleased
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
