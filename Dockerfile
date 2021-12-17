FROM dcreg.service.consul/dev/development-dotnet-core-sdk-common:6.0

# build scripts
COPY ./build.sh /lib/
COPY ./build.fsx /lib/
COPY ./paket.dependencies /lib/
COPY ./paket.references /lib/
COPY ./paket.lock /lib/

# sources
COPY ./KafkaApplication.fsproj /lib/
COPY ./src /lib/src

# others
COPY ./.git /lib/.git
COPY ./.config /lib/.config
COPY ./CHANGELOG.md /lib/

WORKDIR /lib

RUN \
    ./build.sh -t Build no-clean

CMD ["./build.sh", "-t", "Tests", "no-clean"]
