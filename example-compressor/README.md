Consume with Resources Example
==============================

## Run local panda for testing/example
> https://docs.redpanda.com/current/get-started/quick-start/?tab=tabs-1-three-brokers

1. docker compose up
2. docker exec -it redpanda-0 rpk cluster info
3. docker exec -it redpanda-0 rpk topic create XXX -p 10
4. use port from panda starting with `1` - for kafka `19092` for admin `19644`

panda console will run on http://127.0.0.1:8000

## Build
```
dotnet build
```

## Run

### Real-life example
Consentor stream on dev1-services is used and checked as it would be in real-life application.

```
dotnet run
```

---

## Metrics
Metrics are shown at `http://127.0.0.1:8080/metrics`

## Logging
Logging is done to the stdout/stderr.
