FROM dcreg.service.consul/prod/development-dotnet-core-sdk-common:latest

# build scripts
COPY ./fake.sh /kafka-application/
COPY ./build.fsx /kafka-application/
COPY ./paket.dependencies /kafka-application/
COPY ./paket.references /kafka-application/
COPY ./paket.lock /kafka-application/

# sources
COPY ./KafkaApplication.fsproj /kafka-application/
COPY ./src /kafka-application/src

# others
COPY ./CHANGELOG.md /kafka-application/

# copy tests
COPY ./tests /kafka-application/tests

WORKDIR /kafka-application

RUN \
    ./fake.sh build target Build no-clean

CMD ["./fake.sh", "build", "target", "Tests", "no-clean"]
