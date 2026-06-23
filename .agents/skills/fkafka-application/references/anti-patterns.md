# Anti-Patterns

Common mistakes when using `Alma.KafkaApplication`, why they bite, and how to fix them. Code examples live in `examples.md`.

## Mistakes

### Registering an infinite consumer before others
- **Mistake:** Putting a `consume`/`consumeFrom` that waits indefinitely for new events before another consume handler.
- **Why:** Consume handlers run sequentially in registration order; the next handler only starts after the previous one returns. An infinite consumer permanently blocks every handler registered after it.
- **Fix:** Register bounded handlers first (e.g. `consumeLastFrom`, or one that stops via `Seq.takeWhile`), and register the perpetual `consume` last. Order of `connect` calls does not matter, but order of consume calls does.

### Relying on the default `GroupId` in production
- **Mistake:** Not setting a group id and assuming consumers resume from their committed offset.
- **Why:** The default `GroupId` is `Random`, so each restart joins a brand-new consumer group and re-reads according to the broker's reset policy instead of continuing where it left off.
- **Fix:** Set `useGroupId` (shared default) or `useGroupIdFor "connection"` (per connection) explicitly for any non-throwaway consumer.

### Expecting exactly-once with the default commit mode
- **Mistake:** Leaving `CommitMessage` at its default and assuming each message is processed once.
- **Why:** The default is `CommitMessage.Automatically`, which commits on a schedule and can replay or skip around failures.
- **Fix:** Set `useCommitMessage CommitMessage.Manually` (or `useCommitMessageFor "connection"`). The framework still commits the handled message for you — you only choose the mode.

### Manually committing offsets in handler code
- **Mistake:** Calling Kafka commit APIs from inside a consume handler.
- **Why:** The application already commits the handled message according to the configured `CommitMessage` mode; manual commits fight the framework and can corrupt offset tracking.
- **Fix:** Choose the commit mode via `useCommitMessage` and let the framework commit. For batch/exactly-once offset control, use the Compressor pattern's `setOffset`/`getOffset` instead.

### Metrics or internal-state route without a leading slash
- **Mistake:** Passing a route like `"metrics"` to `showInternalState` (or any registered route).
- **Why:** Routes are validated to start with `/`; an invalid route fails configuration.
- **Fix:** Always start the path with `/`, e.g. `showInternalState "/internal-state"`.

### Compressor with only one of `setOffset` / `getOffset`
- **Mistake:** Defining `setOffset` without `getOffset` (or vice versa) in a `compressor` builder.
- **Why:** The two form a pair for manual offset management; the builder rejects an incomplete pair (`IncompleteOffsetHandlers`).
- **Fix:** Provide both `setOffset` and `getOffset`, or neither.

### Forgetting a mandatory configuration element
- **Mistake:** Omitting `useInstance`, `useCurrentEnvironment`, `parseEventWith`, or any consume connection.
- **Why:** These are required; the build fails rather than running with defaults.
- **Fix:** Always supply all four. Under a pattern builder, put them in the base configuration passed to `from` (the pattern provides the consume handler itself).

### Producing through the wrong channel
- **Mistake:** Trying to construct a producer manually or producing to a connection that was never registered with `produceTo`.
- **Why:** Producers are created from `produceTo`/`produceToMany` registrations and looked up by name; an unregistered name has no producer or `fromDomain` and fails with a missing-configuration error.
- **Fix:** Register every output with `produceTo "name" fromDomain` and emit via `ConsumeRuntimeParts.ProduceTo.["name"]`.

### Reading environment variables ad hoc inside handlers
- **Mistake:** Calling `Environment.GetEnvironmentVariable` directly in handler code.
- **Why:** Bypasses validation (`require`, `check`) and the merge model, scattering configuration and hiding missing-variable failures until runtime.
- **Fix:** Parse configuration in an `environment { ... }` block (with `require`/`check`) and `merge` it; read values from `ConsumeRuntimeParts.Environment` or injected `Dependencies`.

## Do Not Use / Avoid

- **Avoid mutable shared state** across handlers. Use `initialize` to set up dependencies and the metric setters on the runtime parts; the only framework-managed mutable state is metrics and the Compressor's thread-safe batch.
- **Do not reuse reserved connection names** `__default` or `__supervision` for your own named connections.
- **Avoid blocking synchronous I/O** in handlers when an `AsyncResult` variant is available; prefer the async/result overloads so failures flow into the error policy.
