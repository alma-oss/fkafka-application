#!/usr/bin/env bash

set -e

export RPK_BROKERS="127.0.0.1:19092"
export RPK_ADMIN_HOSTS="127.0.0.1:19644"
rpk cluster info
rpk cluster health

# rpk topic create development-local-experimental-v1

# Service identification
DOMAIN=consents
CONTEXT=fkafkaCompressor
PURPOSE=example
VERSION=local
export ENVIRONMENT=dev1-services
export INSTANCE="$DOMAIN-$CONTEXT-$PURPOSE-$VERSION"

export KAFKA_BROKER="$RPK_BROKERS"
export INPUT_STREAM="development-compressor-local-v1v10"

#export GROUP_ID="$INSTANCE-2" # -2 zustal na 105
ATTEMPT=17
export GROUP_ID="$INSTANCE-$ATTEMPT" # -2 by mel zustal na 100
export BATCH_SIZE=10

export BATCH_FILE="batch-v1v10-$ATTEMPT.txt"

# Logging common
export LOG_TO="console"
export VERBOSITY=v

# Tracing specific
export TRACING_THRIFT_HOST="tracing-thrift.service.$ENVIRONMENT.consul:80"
export TRACING_SERVICE_NAME="$DOMAIN-$CONTEXT"
export TRACING_TAGS="svc_domain=$DOMAIN,svc_context=$CONTEXT,svc_purpose=$PURPOSE,svc_version=$VERSION"

# Tracing common
export TRACING_LOG_TO="$LOG_TO"
export TRACING_LOG_META="$LOGGER_TAGS"
export TRACING_LOG_LEVEL="$VERBOSITY"

dotnet run
