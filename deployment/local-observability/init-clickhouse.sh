#!/bin/bash
set -e

echo "Waiting for ClickHouse to be ready..."
until clickhouse-client --host localhost --query "SELECT 1" >/dev/null 2>&1; do
  sleep 2
done

echo "Creating SigNoz databases..."

clickhouse-client --query "CREATE DATABASE IF NOT EXISTS signoz_traces;"
clickhouse-client --query "CREATE DATABASE IF NOT EXISTS signoz_logs;"
clickhouse-client --query "CREATE DATABASE IF NOT EXISTS signoz_metrics;"
clickhouse-client --query "CREATE DATABASE IF NOT EXISTS signoz_metadata;"

echo "Databases created!"