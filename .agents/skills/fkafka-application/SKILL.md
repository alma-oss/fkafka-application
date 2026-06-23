---
name: fkafka-application
description: Use whenever generating or reviewing F# code that builds Kafka event-driven applications with Alma.KafkaApplication — the `kafkaApplication`, `partialKafkaApplication`, `environment`, `filterContentFilter`, `contentBasedRouter`, `deriver`, or `compressor` computation expressions. Trigger on mentions of consuming/producing Kafka streams in F#, `connect`/`consume`/`produceTo`/`parseEventWith`/`initialize`/`merge`, EDA patterns (Filter, Content-Based Router, Deriver, Compressor), `ConsumeRuntimeParts`, `ProducerErrorPolicy`/`ConsumeErrorPolicy`, supervision streams, Prometheus metrics routes, `useInstance`/`useGroupId`/`useCommitMessage`, or `run >> ApplicationShutdown.withStatusCode`.
---

# F-Kafka-Application

Library: [alma-oss/fkafka-application](https://github.com/alma-oss/fkafka-application)
NuGet: `Alma.KafkaApplication`

## Purpose

`Alma.KafkaApplication` is an F# framework for building Kafka-based event-driven applications. It exposes computation expressions (a DSL) that wire up consumers, producers, event parsing, error policies, graceful shutdown, structured logging, and Prometheus metrics, so application code only declares configuration and per-event handlers. It also ships four ready-made pattern builders (Filter, Content-Based Router, Deriver, Compressor) on top of the generic custom application.

## When to Use

- Building any service that consumes and/or produces Apache Kafka streams in F#.
- Implementing one of the supported EDA patterns: content filtering, content-based routing, event derivation, or batch compression.
- Adding metrics, supervision events, custom HTTP routes, resource checks, or custom background tasks to a Kafka consumer.

## When NOT to Use

- Non-Kafka messaging, or projects not on the Alma .NET ecosystem.
- Pure request/response HTTP services with no stream processing (use a web framework directly).
- When you only need a raw Kafka client without the application lifecycle — use `Alma.Kafka` directly.

## Main Concepts

- **`kafkaApplication`** — the generic computation expression that builds a `CustomApplication`.
- **`partialKafkaApplication`** — builds reusable, not-yet-finalized configuration to feed into a pattern builder's `from`.
- **`environment`** — computation expression that parses `.env` files and environment variables, returning a `Configuration` to `merge`.
- **`Application`** — discriminated union with cases `CustomApplication`, `FilterContentFilter`, `ContentBasedRouter`, `Deriver`, `Compressor`; executed by `run`.
- **Pattern builders** — `filterContentFilter`, `contentBasedRouter`, `deriver`, `compressor`, each layering pattern logic over a base configuration provided via `from`.
- **`Configuration<'InputEvent,'OutputEvent,'Dependencies>`** — the immutable, merge-able state every builder operation transforms.
- **`ConsumeRuntimeParts`** — runtime context passed as the first argument to every consume/handler function (loggers, connections, producers, metric setters, dependencies, cancellation).
- **`CustomTaskRuntimeParts` / `PatternRuntimeParts`** — runtime context for custom background tasks and for pattern handlers using the `*WithApplication` overloads.
- **Connections** — named Kafka endpoints; a `__default` connection plus optional named ones and a `__supervision` stream.
- **Error policies** — `ProducerErrorPolicy` (`Retry`, `RetryIn`, `Shutdown`, `ShutdownIn`) and `ConsumeErrorPolicy` (same plus `Continue`); both default to `RetryIn 60<Second>`.
- **Generic handler variants** — parsers, initializers, producers, and pattern handlers accept plain, `Result<_,_>`, or `AsyncResult<_,_>` return types interchangeably.
- **`ParseEvent`** — mandatory function turning raw Kafka messages into typed `'InputEvent`s.
- **`run` / `ApplicationShutdown.withStatusCode`** — execute the application and map its outcome to a process exit code.

## Related Libraries

- **Alma.Kafka** — underlying consumer/producer, `BrokerList`, `StreamName`, `ConnectionConfiguration`.
- **Alma.ServiceIdentification** — `Instance` (`Domain`/`Context`/`Purpose`/`Version`), `Spot`.
- **Alma.Metrics** — Prometheus metric primitives surfaced on the metrics route.
- **Alma.Logging** / **Alma.Tracing** — `LoggerFactory`, OpenTelemetry tracing.
- **Feather.ErrorHandling** — `Result`, `AsyncResult`, `result`/`asyncResult` CEs, railway operators.

## Keywords for Search

kafkaApplication, partialKafkaApplication, environment computation expression, filterContentFilter, contentBasedRouter, deriver, compressor, connect, connectTo, connectManyToBroker, consume, consumeFrom, consumeLast, produceTo, produceToMany, parseEventWith, initialize, merge, useInstance, useCurrentEnvironment, useGroupId, useCommitMessage, useSupervision, onConsumeError, onProducerError, ConsumeRuntimeParts, ProducerErrorPolicy, ConsumeErrorPolicy, showMetrics, showInternalState, batchSize, pickEvent, sendBatch, deriveTo, route, filterTo, ApplicationShutdown, F# Kafka, event-driven, EDA pattern, supervision stream, Prometheus metrics

## Reference Files

- For composition principles and recommended API usage, read `references/preferred-patterns.md`.
- For known pitfalls and incorrect assumptions, read `references/anti-patterns.md`.
- For worked code examples, read `references/examples.md`.
