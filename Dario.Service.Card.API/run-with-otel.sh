set -euo pipefail

CONFIGURATION="${1:-Release}"
FRAMEWORK="net9.0"
PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUTPUT_DIR="${PROJECT_DIR}/bin/${CONFIGURATION}/${FRAMEWORK}"

if [ ! -f "${OUTPUT_DIR}/instrument.sh" ]; then
  cat <<'MSG' >&2
OpenTelemetry auto-instrumentation bootstrapper not found.
Build or publish the project so that instrument.sh is generated, for example:
  dotnet publish -c ${CONFIGURATION}
MSG
  exit 1
fi

export OTEL_EXPORTER_OTLP_ENDPOINT="${OTEL_EXPORTER_OTLP_ENDPOINT:-http://otel-collector:4318}"
export OTEL_SERVICE_NAME="${OTEL_SERVICE_NAME:-card-webapi}"

exec "${OUTPUT_DIR}/instrument.sh" dotnet "${OUTPUT_DIR}/Dario.Service.Card.API.dll"