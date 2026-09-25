# Server Deployment

## 1. Components

| Component | Process | Ports | State |
|---|---|---|---|
| Backend | `Bloodfall.Backend` (ASP.NET Core 8) | HTTP 5080 (put TLS in front) | PostgreSQL + in-memory social/lobby/queue state |
| Game server | `Bloodfall.GameServer` (one match per process) | UDP 27015+ (one per process) | none (stateless between matches) |
| Database | PostgreSQL 15+ | 5432 (private) | accounts, stats, clans, news |

## 2. Local development

```bash
Tools/dev/run-local.sh [gameServers=2] [region=dev-local]   # builds + starts backend and game servers
Tools/dev/stop-local.sh
Tools/dev/run-local.ps1                                       # Windows equivalent
```

- Logs go to `Server/artifacts/logs/`.
- The development database is SQLite at `Server/src/Bloodfall.Backend/data/bloodfall.db`. Delete it to reset.
- Development keys live in `appsettings.Development.json`. They are for local use only.

## 3. Production configuration

**The backend refuses to start outside Development if any key is empty.** Provide the keys through environment
variables or a secret store, never in files committed to git:

```bash
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://0.0.0.0:5080
Bloodfall__Environment=Production
Bloodfall__JwtSigningKey=<64+ random bytes, base64>
Bloodfall__GameServerKey=<random secret shared with game servers>
Bloodfall__TicketSigningKey=<random secret shared with game servers>
Bloodfall__SeedDevelopmentContent=false
Bloodfall__AllowCrossRegionFallback=false
Database__Provider=Postgres
Database__ConnectionString=Host=db;Database=bloodfall;Username=bloodfall;Password=<secret>
```

Each game server:

```bash
Bloodfall.GameServer --port 27015 --region eu --id eu-01-27015 \
  --public-address <public IP or DNS> --backend https://api.example.com \
  --server-key $BLOODFALL_GAMESERVER_KEY --ticket-key $BLOODFALL_TICKET_KEY \
  --client-version 0.1.0
```

- `--dev-concede-anytime` must **never** be set in production.
- The **database user for the backend** should own only its schema. Administration uses a separate account.
- The Unity client never receives database or server credentials. It knows only the public backend URL
  (`client_config.json` / `-bloodfall-backend`).

## 4. Topology and scaling

```
           Internet ──TLS──▶ Reverse proxy (HTTPS + WSS) ──▶ Backend ×1 ──▶ PostgreSQL
                     ──UDP──▶ Game server fleet per region (N processes per host, one port each)
```

- **Backend.** Currently a single instance, because presence, parties, lobbies, queues and the server directory are
  in memory. To scale out, move that state to Redis (pub/sub for realtime fan-out, hashes for lobbies and queues) and
  keep the WebSocket hub stateless behind sticky sessions.
- **Game servers.** Run one process per match slot. A 16-core host can run many matches: the simulation costs about
  0.2 ms per tick for a 10-bot match.
  - Servers self-register and heartbeat, so adding capacity means starting processes.
  - Behind NAT, set `--public-address`.
- **Orchestration (recommended):** Kubernetes + Agones, or Nomad, for game server fleets.
  - The directory's register/heartbeat protocol maps onto the Agones SDK `Ready`, `Allocate` and `Shutdown` states.
- **Regions.** Deploy one fleet per region with `--region <id>`. Clients measure latency with unconnected UDP pings to
  each region's `PingHost`.

## 5. Operations

- **Health:**
  - `GET /healthz` for liveness.
  - `GET /api/content/status` for real service checks: database query, directory counts, realtime connections.
- **Client version gate.** `GET /api/content/client-version` returns:
  - `Bloodfall__LatestClientVersion` and `Bloodfall__MinimumClientVersion`
  - the content hash of the **backend's own copy of the game data**, so deploy the same data with every component
  - `Bloodfall__Maintenance` and `Bloodfall__MaintenanceMessage`

  Clients older than the minimum get a clear update message. Clients with different data are warned before online
  play. During maintenance the client stops at the connection screen and shows the message.
- **Backups.** Nightly PostgreSQL base backup + WAL archiving. Match history and ratings are the critical data.
- **Logs.** Structured console logs, collected by the platform (Loki, CloudWatch, etc.).
  - Security events are also written to `audit_log`.
- **Firewalls:**
  - Only 443 (proxy) and the UDP game range are public.
  - The backend port and the database are private.

## 6. Release checklist

1. `dotnet test Server/tests/Bloodfall.Tests` and `Tools/dev/run-e2e.sh` pass.
2. The game data content hash is recorded, and the client and server builds carry the same data.
3. Publish the backend (`dotnet publish -c Release`) and the game server (`dotnet publish -c Release -r linux-x64
   --self-contained`).
4. Build the Windows client (Unity: **Bloodfall ▸ Build ▸ Windows Client (Release)**) and bump `clientVersion` in
   `Resources/Config/client_config.json`.
5. Set `Bloodfall__LatestClientVersion` / `Bloodfall__MinimumClientVersion` for the release (the content hash is
   computed automatically from the deployed game data).
6. Roll out game servers region by region, then the backend, then publish the client.
