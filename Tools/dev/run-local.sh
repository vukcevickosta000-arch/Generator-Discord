#!/usr/bin/env bash
# Starts the full Bloodfall development stack on this machine:
#   backend (all services) on http://localhost:5080  (SQLite database in Server/src/Bloodfall.Backend/data)
#   N dedicated game servers on UDP 27015..       (region dev-local by default)
# Usage: Tools/dev/run-local.sh [gameServerCount=2] [region=dev-local]
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
COUNT="${1:-2}"
REGION="${2:-dev-local}"
LOGS="$ROOT/Server/artifacts/logs"
mkdir -p "$LOGS"
echo "Building..."
dotnet build "$ROOT/Server/src/Bloodfall.Backend" -c Debug -nologo -v q
dotnet build "$ROOT/Server/src/Bloodfall.GameServer" -c Debug -nologo -v q
echo "Starting backend -> $LOGS/backend.log"
(cd "$ROOT/Server/src/Bloodfall.Backend" && ASPNETCORE_ENVIRONMENT=Development ./bin/Debug/net8.0/Bloodfall.Backend > "$LOGS/backend.log" 2>&1 &)
for i in $(seq 1 60); do curl -sf http://localhost:5080/healthz > /dev/null && break; sleep 0.5; done
for i in $(seq 0 $((COUNT-1))); do
  PORT=$((27015+i))
  echo "Starting game server on UDP $PORT -> $LOGS/gameserver-$PORT.log"
  (cd "$ROOT/Server/src/Bloodfall.GameServer" && ./bin/Debug/net8.0/Bloodfall.GameServer --port $PORT --region "$REGION" --id "local-$PORT" > "$LOGS/gameserver-$PORT.log" 2>&1 &)
done
echo "Bloodfall services running. Status: curl http://localhost:5080/api/content/status"
echo "Stop with: Tools/dev/stop-local.sh"
