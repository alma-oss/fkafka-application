# Changelog

<!-- There is always Unreleased section on the top. Subsections (Add, Changed, Fix, Removed) should be Add as needed. -->
## Unreleased

## 10.0.0 - 2020-05-25
- [**BC**] Use .netcore 3.1
- [**BC**] Update dependencies
- [**BC**] Change `DTO` format to PascalCase
- Use `Lmc.Serializer`
- [**BC**] Use `ApplicationLogger` from `Lmc.Logging` lib
- [**BC**] Change namespace to `Lmc.KafkaApplication`

## 9.2.0 - 2019-11-20
- Update dependencies

## 9.1.1 - 2019-11-15
- Fix `PreparedConsumeRuntimeParts` to contain a `GitCommit` and `DockerImageVersion` values

## 9.1.0 - 2019-11-14
- Fix `ContentBasedRouter` type `RouteEvent` to return a `OutputEvent option` instead of `OutputEvent`
- Update dependencies

## 9.0.0 - 2019-11-14
- Add `GitCommit` to the configuration
- Add `DockerImageVersion` to the configuration
- [**BC**] Change patterns to add `processedBy` information to the processed events
- Add `AssemblyInfo`
- [**BC**] Change Router pattern to work as others with generic `InputEvent`/`OutputEvent`, since a routing itself is also a generating of a new events
    - `ContentBasedRouter` computation expression
        - Add `route`, `routeWithApplication`, `getCommonEventBy`, `addCustomMetricValues`
        - [**BC**] Change `routeToBrokerFromEnv` to require a new parameter of `FromDomain`

## 8.0.0 - 2019-11-05
- [**BC**] Update ConsentsEvents and other dependencies

## 7.2.0 - 2019-10-25
- Update dependencies
- Allow to `addRoute` to the _metrics_ WebServer

## 7.1.0 - 2019-08-08
- Allow `registerCustomMetric` directly
- Allow running custom tasks

## 7.0.0 - 2019-08-07
- [**BC**] Replace `deriveToWithLog` with `deriveToWithApplication`, to allow more actions then just a logging.
- [**BC**] Make some internal types `internal`
- [**BC**] Remove `Bind` of the builders, since it is not possible to use it anyway.
- Add `SetMetric` function to the _RuntimeParts_ (_both `ConsumeRuntimeParts` and `PatternRuntimeParts`_)

## 6.0.0 - 2019-08-06
- Update `ErrorHandling` to unify operators
- [**BC**] Use `Contract` and `Intent` from `ContractAggregate` library

## 5.4.0 - 2019-08-05
- Add `deriveToWithLog` function in `Deriver` pattern, to allow logging while deriving.

## 5.3.0 - 2019-08-05
- Add `ConsentExpireSet` event

## 5.2.1 - 2019-07-16
- Fix parsing graylog connection, to ignore empty hosts (_after trailing commas_)

## 5.2.0 - 2019-07-16
- Allow graylog connection to be single `host[:port]` or a list of them (_cluster_) separated by `,` (_one random node will be used for cluster_)

## 5.1.1 - 2019-07-16
- Fix consul host in graylog diagnostics

## 5.1.0 - 2019-07-15
- Fix graylog resource identification from host to graylog service
- Log error on graylog resource check fail

## 5.0.0 - 2019-07-15
- Parse `graylog` string not just as a host, but as a `host[:port]`
- Use `graylog` consul service as identification for resource health check
- [**BC**] Change `logToGraylog` function - now it requires both `graylog` (`host[:port]`) and `graylogService` (_service name in consul_)

## 4.1.0 - 2019-06-27
- Update `ConsentEvents` lib, to parse phone always as a string

## 4.0.0 - 2019-06-26
- Use lint
    - Rename types `second` and `attempt` to _PascalCase_
- Update dependencies

## 3.4.0 - 2019-06-13
- Fix `Phone` number format for parsing, to allow int64 for big numbers
- Catch runtime unhandled exceptions and treat them as Runtime Error

## 3.3.0 - 2019-06-12
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
