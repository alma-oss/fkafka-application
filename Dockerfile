FROM dcreg.service.consul/prod/development-dotnet-core-sdk-common:latest

# build scripts
COPY ./fake.sh /fkafka-application/
COPY ./build.fsx /fkafka-application/
COPY ./paket.dependencies /fkafka-application/
COPY ./paket.references /fkafka-application/
COPY ./paket.lock /fkafka-application/

# sources
COPY ./KafkaApplication.fsproj /fkafka-application/
COPY ./src /fkafka-application/src

# others
COPY ./.git /fkafka-application/.git
COPY ./CHANGELOG.md /fkafka-application/

WORKDIR /fkafka-application

RUN \
    ./fake.sh build target Build no-clean

CMD ["./fake.sh", "build", "target", "Tests", "no-clean"]
