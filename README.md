F-Kafka Application
===================

Framework for kafka application.
It contains computed expressions to help with building this kind of application and have in-build metrics, logging, parsing, etc..

## Install
```
dotnet add package -s $NUGET_SERVER_PATH Lmc.FkafkaApplication
```
Where `$NUGET_SERVER_PATH` is the URL of nuget server
- it should be http://development-nugetserver-common-stable.service.devel1-services.consul:{PORT} (_make sure you have a correct port, since it changes with deployment_)
- see http://consul-1.infra.pprod/ui/devel1-services/services/development-nugetServer-common-stable for detailed information (and port)

## Use
_todo_

## Release
1. Increment version in `KafkaApplication.fsproj`
2. Update `CHANGELOG.md`
3. Commit new version and tag it
4. Run `$ fake build target release`

## Development
### Requirements
- [dotnet core](https://dotnet.microsoft.com/learn/dotnet/hello-world-tutorial)
- [FAKE](https://fake.build/fake-gettingstarted.html)

### Build
```bash
fake build
```

### Watch
```bash
fake build target watch
```
