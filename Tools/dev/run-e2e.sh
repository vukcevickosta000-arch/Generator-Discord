#!/usr/bin/env bash
# End-to-end test against the real stack: starts a fresh backend (temporary SQLite database) and one game server
# (with --dev-concede-anytime so the test can finish a match quickly), runs Server/tools/Bloodfall.E2E, then stops
# everything. Exit code = test result.
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
LOGS="$ROOT/Server/artifacts/logs"
mkdir -p "$LOGS"
"$ROOT/Tools/dev/stop-local.sh" > /dev/null 2>&1 || true
dotnet build "$ROOT/Server/src/Bloodfall.Backend" -nologo -v q > /dev/null || exit 2
dotnet build "$ROOT/Server/src/Bloodfall.GameServer" -nologo -v q > /dev/null || exit 2
dotnet build "$ROOT/Server/tools/Bloodfall.E2E" -nologo -v q > /dev/null || exit 2
DB_DIR="$(mktemp -d)"
(cd "$ROOT/Server/src/Bloodfall.Backend" && ASPNETCORE_ENVIRONMENT=Development Database__ConnectionString="Data Source=$DB_DIR/e2e.db" ./bin/Debug/net8.0/Bloodfall.Backend > "$LOGS/e2e-backend.log" 2>&1 &)
for i in $(seq 1 60); do curl -sf http://localhost:5080/healthz > /dev/null && break; sleep 0.5; done
(cd "$ROOT/Server/src/Bloodfall.GameServer" && ./bin/Debug/net8.0/Bloodfall.GameServer --port 27015 --region dev-local --id e2e-27015 --dev-concede-anytime > "$LOGS/e2e-gameserver.log" 2>&1 &)
sleep 3
(cd "$ROOT/Server/tools/Bloodfall.E2E" && timeout 300 ./bin/Debug/net8.0/Bloodfall.E2E)
RESULT=$?
"$ROOT/Tools/dev/stop-local.sh" > /dev/null 2>&1 || true
rm -rf "$DB_DIR"
echo "E2E exit code: $RESULT (logs in $LOGS)"
exit $RESULT
