Consume with Resources Example
==============================

## Build
```
dotnet build
```

## Run
For example without computation expression add `not-computation` option

### Dummy example
No real resources are used, just a randomized checking and generating an int value, instead of stream reading.

```
dotnet run -- [not-computation]
```

### Real-life example
Consentor stream on dev1-services is used and checked as it would be in real-life application.

```
dotnet run -- reallife [not-computation]
```

---

## Metrics
Metrics are shown at `http://127.0.0.1:8080/metrics`

## Logging
Logging is done to the stdout/stderr.
