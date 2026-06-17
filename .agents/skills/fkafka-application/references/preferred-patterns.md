# Preferred Patterns

How to use `Alma.KafkaApplication` correctly. All code lives in `examples.md`; this file describes principles and points to examples by name.

## Core Principles

- **Declare, don't orchestrate.** Each builder operation registers a piece of configuration; the framework owns the lifecycle (connect, consume, flush, commit, shutdown). Application code supplies handlers and configuration only.
- **Mandatory configuration.** Every application must set `useInstance`, `useCurrentEnvironment`, at least one `connect` + `consume` pair, and `parseEventWith`. Patterns supply their own consume handler, so under a pattern builder you provide the base config (via `from`) plus the pattern operations instead of a raw `consume`.
- **One handler signature.** Consume handlers and pattern handlers always receive a runtime-parts record as their first argument, then the event(s). Read connections, producers, metric setters, logger factory, and injected dependencies from that record rather than from globals.
- **Pick the right entry builder.** Use `kafkaApplication` for fully custom consume logic; use a pattern builder (`filterContentFilter`, `contentBasedRouter`, `deriver`, `compressor`) when the work matches that pattern; use `partialKafkaApplication` only to produce a base configuration consumed by a pattern's `from`.

## Recommended API Usage

- **Connections.** Use `connect` for the single default connection, `connectTo "name"` for additional named connections, and `connectManyToBroker` to register many topics that share a broker. All connections are available at runtime via `ConsumeRuntimeParts.Connections`. See `examples.md` → Multiple Connections.
- **Consuming.** `consume` / `consumeFrom "name"` receive a sequence of parsed events; `consumeLast` / `consumeLastFrom "name"` receive only the latest event (handler is skipped when the stream is empty). Handlers run in registration order, each to completion before the next starts.
- **Producing.** Register an output with `produceTo "name" fromDomain` (or `produceToMany`). At runtime, produce through `ConsumeRuntimeParts.ProduceTo.["name"]`. The `fromDomain` function serializes a domain event into a message and may return plain, `Result`, or `AsyncResult`.
- **Dependencies.** Use `initialize` to build application dependencies once (e.g. external clients) and attach them to `ConsumeRuntimeParts.Dependencies`; read them inside handlers. See `examples.md` → Initialization and Dependencies.
- **Metrics.** Call `showMetrics` (default port 8080) or `showMetrics port` to expose a Prometheus route; register custom metrics with `showCustomMetric`/`registerCustomMetric` and update them through the `IncrementMetric`/`IncrementMetricBy`/`SetMetric` functions on the runtime parts. Count input/output events with `showInputEventsWith`/`showOutputEventsWith`.
- **Environment.** Build configuration from `.env` files and environment variables with the `environment` computation expression (`file`, `instance`, `currentEnvironment`, `groupId`, `spot`, `connect`, `supervision`, `require`, `check`, `ifSetDo`), then fold it into the application with `merge`. See `examples.md` → Environment Composition.
- **Supervision.** Register a supervision stream with `useSupervision` (or `supervision` in the environment CE) to emit lifecycle events such as `instance_started`.

## Error Handling

- Override the consumer policy with `onConsumeError` / `onConsumeErrorFor "name"`, returning a `ConsumeErrorPolicy` (`Retry`, `RetryIn n<Second>`, `Shutdown`, `ShutdownIn n<Second>`, or `Continue` to skip to the next handler). Override the producer policy with `onProducerError` returning a `ProducerErrorPolicy` (same cases without `Continue`).
- Both policies default to `RetryIn 60<Second>` when not set.
- Prefer railway-oriented handlers: parsers, initializers, `fromDomain`, and pattern handlers can return `Result`/`AsyncResult`, and the framework routes failures into the configured error policy. See `examples.md` → Initialization and Dependencies for `AsyncResult` usage.

## Composition

- Build shared base configuration with `partialKafkaApplication { ... }` and feed it to a pattern via `from`.
- Combine configuration fragments with `merge`; later values (only those that are `Some`) win, except the logger which is preserved. Consume handlers and producers are appended rather than replaced, so order of registration is retained.
- Keep environment parsing in an `environment { ... }` block and `merge` it, rather than reading environment variables manually inside handlers.

## Integration with Other Libraries

- Construct identity with `Alma.ServiceIdentification` (`Instance` from `Domain`/`Context`/`Purpose`/`Version`, plus `Spot`).
- Construct connections with `Alma.Kafka` types (`BrokerList`, `StreamName`).
- Use `Feather.ErrorHandling` `result`/`asyncResult` computation expressions and `Result.orFail` when bootstrapping a logger factory. See `examples.md` → Logger Factory and Dependencies.
- Add custom Giraffe routes with `addHttpHandler`; expose internal state with `showInternalState "/path"` (the path must start with `/`).

## Naming Conventions

- Builder operations are camelCase keywords (`useInstance`, `consumeFrom`, `produceTo`, `parseEventWith`).
- Connection names are arbitrary strings; the framework reserves `__default` and `__supervision`.
- Pattern handler variants follow the `*WithApplication` suffix when they need `PatternRuntimeParts` (e.g. `routeWithApplication`, `deriveToWithApplication`).

## Testing Recommendations

- Tests use Expecto. Assert on configuration assembly and handler behavior rather than spinning up a real broker.
- Keep handlers pure where possible (transform input to output) so they can be unit-tested in isolation from the consume loop. See `examples.md` → Test.
