FROM dcreg.service.consul/dev/development-dotnet-core-sdk-common:7.0

# build scripts
COPY ./build.sh /lib/
COPY ./build /lib/build
COPY ./paket.dependencies /lib/
COPY ./paket.references /lib/
COPY ./paket.lock /lib/

# sources
COPY ./KafkaApplication.fsproj /lib/
COPY ./src /lib/src

# test
COPY ./tests /lib/tests

# others
COPY ./.git /lib/.git
COPY ./.config /lib/.config
COPY ./CHANGELOG.md /lib/

WORKDIR /lib

RUN \
    ./build.sh -t Build no-clean

CMD ["./build.sh", "-t", "Tests", "no-clean"]
