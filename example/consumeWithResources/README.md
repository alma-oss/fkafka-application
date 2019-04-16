Consume with Resources Example
==============================

## Build
```
dotnet build
```

## Run
For example without computed expression add `not-computed` option

### Dummy example
No real resources are used, just a randomized checking and generating an int value, instead of stream reading.

```
dotnet run -- [not-computed]
```

### Real-life example
Consentor stream on dev1-services is used and checked as it would be in real-life application.

```
dotnet run -- reallife [not-computed]
```

---

## Metrics
Metrics are shown at `http://127.0.0.1:8080/metrics`

## Logging
Logging is done to the stdout/stderr.
