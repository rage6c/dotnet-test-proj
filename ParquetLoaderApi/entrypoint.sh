#!/usr/bin/env sh
set -eu

LOG_FILE="${LOG_FILE:-/logs/dotnet-test-proj.log}"
LOG_DIR="$(dirname "$LOG_FILE")"

mkdir -p "$LOG_DIR" /base-parquet-path
touch "$LOG_FILE"

exec dotnet ParquetLoaderApi.dll >> "$LOG_FILE" 2>&1
