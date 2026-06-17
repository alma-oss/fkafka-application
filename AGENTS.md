# AGENTS.md — F-Kafka Application

This repo ships Agent Skill for the `Alma.KafkaApplication` library. Compatible agents discover it automatically; see `.agents/skills/fkafka-application/SKILL.md`.

## Project Purpose

F-Kafka Application (`Alma.KafkaApplication`) is an open-source F# framework for building Kafka-based event-driven applications. It provides computation expressions (DSL) for common EDA patterns — Filter, Content-Based Router, Deriver, and Compressor — with built-in metrics (Prometheus), logging, event parsing, error handling, and graceful shutdown. Published as a NuGet package consumed by downstream microservices.

## Tech Stack

| Layer | Technology |
|---|---|
| Language | F# 10, .NET 10.0 |
| Web | Saturn + Giraffe (metrics/status HTTP endpoints) |
| Messaging | Apache Kafka via `Alma.Kafka` |
| Metrics | Prometheus (via `Alma.Metrics`) |
| Tracing | OpenTelemetry (via `Alma.Tracing`) |
| Error handling | Railway-oriented programming (`Feather.ErrorHandling`) |
| JSON parsing | `FSharp.Data` (type providers), `Newtonsoft.Json` |
| Testing | Expecto |
| Build | FAKE (F# Make) via `build.sh` |
| Packages | Paket (dependencies + references) |
| CI/CD | GitHub Actions |
| Linting | fsharplint (`fsharplint.json`) |

## Commands

```bash
# Restore tools and packages
dotnet tool restore && dotnet tool run paket restore

# Build
./build.sh build

# Run tests
./build.sh -t tests

# Lint
dotnet fsharplint lint KafkaApplication.fsproj

# Full pipeline: clean → build → lint → tests
./build.sh -t tests

# Publish (requires NUGET_API_KEY)
./build.sh -t publish
```

### Build options (passed as env-like flags)
- `no-clean` — skip cleaning bin/obj dirs
- `no-lint` — run lint but don't fail on warnings

## Project Structure

```
├── KafkaApplication.fsproj        # Main library project
├── build.sh                       # Entry point for all build commands
├── build/                         # FAKE build system (shared across projects)
│   ├── Build.fs                   # Build definition (Library type, NuGet API key)
│   ├── Targets.fs                 # FAKE targets: Clean, Build, Lint, Tests, Release, Publish
│   ├── Utils.fs                   # Build utilities
│   └── SafeBuildHelpers.fs        # SAFE stack helpers (not used here)
├── src/
│   ├── Application.fs             # Top-level Application DU: Custom, Filter, Router, Deriver, Compressor
│   ├── Common/                    # Core framework
│   │   ├── Types.fs               # All core types: events, connections, handlers, errors, config
│   │   ├── Utils.fs               # Graceful shutdown, JSON helpers, app state
│   │   ├── Builder.fs             # KafkaApplicationBuilder computation expression (50+ operations)
│   │   ├── Environment.fs         # EnvironmentBuilder CE for .env file parsing
│   │   ├── Runner.fs              # Application execution engine
│   │   ├── Pattern.fs             # Pattern infrastructure and metrics utilities
│   │   ├── Resource.fs            # Resource availability checking
│   │   ├── Metrics.fs             # Prometheus metrics state
│   │   ├── InternalState.fs       # Internal state management
│   │   └── ApplicationEvents.fs   # Instance started event
│   ├── Filter/                    # Filter Content Filter pattern
│   │   ├── Types.fs, Filter.fs, Builder.fs, Runner.fs
│   │   └── README.md              # Pattern docs with JSON config schema
│   ├── Router/                    # Content-Based Router pattern
│   │   ├── Types.fs, Router.fs, Builder.fs, Runner.fs
│   │   └── README.md
│   ├── Deriver/                   # Deriver pattern (event derivation)
│   │   ├── Types.fs, Builder.fs, Runner.fs
│   │   └── README.md
│   └── Compressor/                # Compressor pattern (batch accumulation)
│       ├── Batch.fs, Types.fs, Metrics.fs, Builder.fs, Runner.fs
│       └── README.md
├── tests/                         # Expecto tests
│   ├── Tests.fs                   # Test entry point
│   ├── GenericApplicationTest.fs
│   ├── CommitMessageTest.fs
│   └── Utils.fs
├── example/                       # Example app (Deriver pattern)
│   ├── Program.fs, RealLife.fs
│   └── README.md
├── example-compressor/            # Example app (Compressor pattern)
│   ├── Program.fs, Compressor.fs
│   └── README.md
├── paket.dependencies            # Package sources and versions
├── paket.references              # Packages used by the main project
├── fsharplint.json               # Lint config (genericTypesNames disabled)
├── global.json                   # .NET SDK 10.0
└── CHANGELOG.md                  # Version history
```

## Key Dependencies (Alma Ecosystem)

- **Alma.Kafka** — Kafka consumer/producer abstractions, connection configuration
- **Alma.ServiceIdentification** — Instance, Domain, Context, Purpose, Version, Spot identification
- **Alma.Metrics** — Prometheus metric primitives
- **Alma.Logging** — Structured logging with LoggerFactory
- **Alma.Tracing** — OpenTelemetry tracing integration
- **Alma.Environment** — Environment variable parsing
- **Alma.ApplicationStatus** — Service status reporting
- **Alma.WebApplication** — Saturn/Giraffe web app bootstrapping
- **Feather.ErrorHandling** — `Result`, `AsyncResult`, `maybe` CE, railway operators (`<@>`, `>>=`)

## Architecture & Patterns

### Application types (discriminated union)
```
Application = CustomApplication | FilterContentFilter | ContentBasedRouter | Deriver | Compressor
```

### Core concepts
1. **Computation expressions** — `kafkaApplication { ... }`, `environment { ... }`, and pattern-specific builders
2. **Consume handlers** — registered in order; each receives `ConsumeRuntimeParts` and a sequence of parsed events
3. **Error policies** — `Retry`, `RetryIn`, `Shutdown`, `ShutdownIn`, `Continue` for both producer and consumer errors
4. **Generic variants** — handlers, parsers, and producers accept `unit`, `Result<_,_>`, or `AsyncResult<_,_>` return types
5. **Metrics route** — optional HTTP endpoint (default port 8080) for Prometheus scraping

### Mandatory configuration
- `useInstance` — service identification
- `useCurrentEnvironment` — deployment environment
- At least one `connect` + `consume` pair
- `parseEventWith` — event parser function

## Conventions

- **Namespace**: `Alma.KafkaApplication` (and sub-namespaces for patterns)
- **Module-per-concern**: Types.fs, Builder.fs, Runner.fs in each pattern directory
- **F# idioms**: discriminated unions, single-case DUs for type safety, `[<RequireQualifiedAccess>]` on modules
- **Error handling**: Railway-oriented with `result { ... }` and `asyncResult { ... }` CEs
- **No mutable state** outside of metrics and thread-safe batch management (Compressor)
- **Computation expression keywords** use camelCase (e.g., `useInstance`, `consumeFrom`, `produceTo`)

## CI/CD Workflows (GitHub Actions)

| Workflow | Trigger | What it does |
|---|---|---|
| `tests.yaml` | PR + daily schedule | Build → Tests on ubuntu-latest, .NET 10.x |
| `publish.yaml` | Version tag push (`X.Y.Z`) | Pack → Publish to NuGet.org |
| `pr-check.yaml` | PR | Block fixup commits + ShellCheck |

## Release Process

1. Increment `<Version>` in `KafkaApplication.fsproj`
2. Update `CHANGELOG.md`
3. Commit and tag with the version number (e.g., `28.0.0`)
4. Push tag → GitHub Actions publishes to NuGet.org

## Pitfalls & Things to Know

- **This is a library, not a runnable service** — there is no `docker-compose.yaml` for local dev (the `__docker-compose.yaml` is for the example app only)
- **Consume handler order matters** — handlers run sequentially; infinite consumers block subsequent handlers
- **GroupId defaults to Random** — always set explicitly for production use
- **CommitMessage defaults to Automatically** — use `CommitMessage.Manually` for exactly-once processing
- **Pattern READMEs** in `src/Filter/`, `src/Router/`, `src/Deriver/`, `src/Compressor/` contain JSON configuration schemas — read them for pattern-specific details
- **`fsharplint.json`** only disables `genericTypesNames` — all other rules are active
- **Private feed access** — tests in CI use `GITHUB_TOKEN`; local dev may need `PRIVATE_FEED_USER` / `PRIVATE_FEED_PASS` for Alma packages from GitHub Packages
- **`build/` directory** is a shared FAKE build system used identically across all Alma F# projects — avoid modifying unless you understand the full project family
- **`example/` and `example-compressor/`** are separate `.fsproj` apps for demonstration — useful as reference but not part of the published package
