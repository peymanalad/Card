#!/bin/bash
set -e

echo "Creating SigNoz databases..."

clickhouse-client --query "CREATE DATABASE IF NOT EXISTS signoz_traces;"
clickhouse-client --query "CREATE DATABASE IF NOT EXISTS signoz_logs;"
clickhouse-client --query "CREATE DATABASE IF NOT EXISTS signoz_metrics;"
clickhouse-client --query "CREATE DATABASE IF NOT EXISTS signoz_metadata;"

echo "Databases created!"