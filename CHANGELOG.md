# Changelog

<!-- There is always Unreleased section on the top. Subsections (Add, Changed, Fix, Removed) should be Add as needed. -->
## Unreleased

## 24.0.0 - 2023-09-11
- [**BC**] Use `Alma` namespace

## 23.0.0 - 2023-08-11
- [**BC**] Use net7.0

## 22.11.0 - 2023-06-29
- Update dependencies

## 22.10.0 - 2023-06-29
- Allow to set custom port for internal webServer
- Add `showMetrics` with a second parameter - port: int

## 22.9.0 - 2023-06-27
- Update dependencies

## 22.8.0 - 2023-06-20
- Update dependencies

## 22.7.0 - 2023-06-20
- Update dependencies

## 22.6.0 - 2023-06-20
- Update dependencies

## 22.5.0 - 2023-01-04
- Add application level debug logs

## 22.4.0 - 2022-11-10
- Update dependencies

## 22.3.0 - 2022-09-01
- Add `CurrentEnvironment` to runtime parts
    - `ConsumeRuntimeParts`
    - `PatternRuntimeParts`

## 22.2.0 - 2022-08-30
- Add `Cancellation` to runtime parts
    - `ConsumeRuntimeParts`
    - `PatternRuntimeParts`

## 22.1.0 - 2022-08-30
- Add `Dependencies` to `PatternRuntimeParts`
- Allow `initialize` to return `Result` and `AsyncResult`

## 22.0.0 - 2022-08-30
- [**BC**] Add generic dependencies to runtime parts
- Add `initialize` keyword

## 21.2.0 - 2022-08-26
- Update dependencies

## 21.1.0 - 2022-06-29
- Update dependencies

## 21.0.0 - 2022-05-19
- Update dependencies
    - [**BC**] Use `OpenTelemetry` tracing

## 20.1.0 - 2022-04-27
- Update dependencies

## 20.0.0 - 2022-03-07
- [**BC**] Require name for a custom task
- Add in-build graceful shutdown
- Add internal Application State
- [**BC**] Change `ErrorMessage` to be more usable
    - Add cases `ErrorMessage`, `RuntimeError` and `Errors`

## 19.1.0 - 2022-02-22
- Update dependencies
- Fix problem with handle trace in consume
- Make `getCommonEvent` in `deriver` pattern optional

## 19.0.0 - 2022-02-18
- Change internal runner to be async instead of messing with current thread
- Catch and trace the error in the consumer handler function
- Fix problem with Kafka.Consumer state when on retry the consume
- Catch all `exceptions` in the consuming events instead of just a `KafkaException`
- [**BC**] Change `ProducerErrorHandler` to take an `exception` instead of just message
- [**BC**] Change `ConsumeErrorHandler` to take an `exception` instead of just message

## 18.3.0 - 2022-02-11
- Allow generic `ConsumeHandler` function
    - for `kafkaApplication` (and `partialKafkaApplication`) in `consume` and `consumeFrom`
- Make some intentionally internal types really `internal`

## 18.2.0 - 2022-02-11
- Allow generic `DeriveEvent` function
    - for `deriver` in `deriveTo` and `deriveToWithApplication`

## 18.1.0 - 2022-02-11
- Allow generic `FromDomain` function
    - for `kafkaApplication` (and `partialKafkaApplication`) in `produceTo` and `produceToMany`
    - for `deriver` in `deriveTo` and `deriveToWithApplication`
    - for `filterContentFilter` in `filterTo`
    - for `contentBasedRouter` in `routeToBrokerFromEnv`
- Allow generic `ParseEvent` function
    - for `kafkaApplication` (and `partialKafkaApplication`) in `parseEventWith` and `parseEventAndUseApplicationWith`

## 18.0.0 - 2022-01-25
- Fix a commit message configuration option not to be overwritten by `merge`
- [**BC**] Use kafka library in version 20
    - Remove `keywords`
        - `consumeLast`
        - `consumeLastFrom`
    - Produce events with key
    - Change `FromDomain` function to return a `MessageToProduce`

## 17.3.0 - 2022-01-20
- Normalize metric names
- Trace output events

## 17.2.0 - 2022-01-12
- Add `parseEventAndUseApplicationWith` keyword to `kafkaApplication` CE

## 17.1.1 - 2022-01-10
- Add missing keywords for `currentEnvironment`
    - `useCurrentEnvironment` in `kafkaApplication` CE
    - `currentEnvironment` in `environment` CE

## 17.1.0 - 2022-01-07
- Update dependencies

## 17.0.0 - 2022-01-07
- [**BC**] Require `CurrentEnvironment` in all kafka applications
- Add `ApplicationStatus`
- Update dependencies
- [**BC**] Use `Giraffe` and `Saturn` instead of `Suave` and use internal `WebServer` instead of Metrics webserver
- [**BC**] Change `showMetricsOn` to `showMetrics` use default `/metrics` route statically
- [**BC**] Change `addRoute` to `addHttpHandler`
- [**BC**] Change `useGitCommit` to `useGit`
- [**BC**] Remove `gitCommit` from `environment` computation expression

## 16.0.0 - 2021-12-17
- [**BC**] Require `Instance` in common `LoggerFactory`
- [**BC**] Use net6.0

## 15.3.1 - 2021-11-29
- Fix `LoggerFactory.common` to allow run without an existing env file

## 15.3.0 - 2021-11-26
- Fix filter pattern to use `and` instead of `or` in filtering so the event is _allowed_ if it is allowed by **both** spot and the filterValue (_unless one of those allowed lists is empty_)
- Add trace logs to filter pattern
- Add `LoggerFactory.common` function to help create a common logger factory for kafka application usage

## 15.2.0 - 2021-10-29
- Update dependencies

## 15.1.0 - 2021-10-26
- Resolve environment variables in loaded file

## 15.0.0 - 2021-10-18
- Fix default spot from `(Zone=common, Bucket=all)` to `(Zone=all, Bucket=common)`
- Update dependencies
- [**BC**] Use `ILogger` instead of `ApplicationLogger`
- [**BC**] Remove Graylog support
    - Remove `logToGraylog`
- [**BC**] Use `LoggerFactory` instead of `ApplicationLogger` and create other loggers by context
    - Rename operation `useLogger` to `useLoggerFactory`
- Make multi-errors as a list instead of returning just a first one
- Add `ApplicationShutdown.withStatusCodeAndLogResult` function
- Allow to set up commit message mode for consuming
    - Add `useCommitMessage` custom operation
    - Add `useCommitMessageFor` custom operation
- [**BC**] Remove dependency on `Consents` libraries
    - [**BC**] Rename `getIntentBy` in `Filter` pattern to more generic `getFilterBy`
    - [**BC**] Change `parseConfiguration` in `Filter` to have a `parseFilterValue` function

## 14.1.0 - 2021-09-20
- Update dependencies

## 14.0.0 - 2021-09-17
- Add tracing to consume events
    - [**BC**] Change `consume handlers` to get a single `TracedEvent` instead of `Event seq`
    - [**BC**] Update dependencies

## 13.1.0 - 2021-02-16
- Update dependencies

## 13.0.0 - 2020-11-24
- [**BC**] Use .netcore 5.0

## 12.0.0 - 2020-11-24
- [**BC**] Update dependencies

## 11.0.0 - 2006-20-17
- [**BC**] Update dependencies
    - `ConsentEvents`

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
