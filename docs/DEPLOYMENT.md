# Deployment

Status: **Phase 0 — outline.** Local development is implemented in Phase 1; production deployment and runbooks are finalised in Phase 12.

## 1. Local development (Phase 1)

| Component | How it runs |
|---|---|
| PostgreSQL 18 | Docker Compose service `postgres`, port 5432, named volume |
| Redis 8 | Docker Compose service `redis`, port 6379 |
| Migrations | `dotnet run --project Backend/BookingPlatform.Migrations -- migrate` (and `seed-dev`) |
| Api | `dotnet run --project Backend/BookingPlatform.Api` (Development) |
| Worker | `dotnet run --project Backend/BookingPlatform.Worker` |
| Web | `npm run dev` in `Frontend/BookingPlatform.Web` (Quasar dev server proxies `/api` and `/hubs` to the Api → same origin, so the refresh cookie works) |

Environment variables are documented in `.env.example` (placeholders only). Copy to `.env` (git-ignored).

Planned variables:

| Variable | Purpose |
|---|---|
| `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB` | Compose PostgreSQL |
| `ConnectionStrings__Postgres` | Api/Worker/Migrations |
| `ConnectionStrings__Redis` | Api/Worker |
| `Jwt__Issuer`, `Jwt__Audience`, `Jwt__SigningKey` | Token issuance (key ≥ 32 random bytes) |
| `Cors__AllowedOrigins__0` | Allowed SPA origins |
| `DEV_ADMIN_EMAIL`, `DEV_ADMIN_PASSWORD` | Dev seeder only |
| `VITE_API_BASE_URL` | Frontend build-time API base |

## 2. Production topology (target)

```
            ┌──────────── TLS reverse proxy / load balancer (WebSockets enabled) ────────────┐
            │                                                                               │
   app.example.com (static SPA via nginx container)                  api.example.com (Api × N)
                                                                              │
                                     Worker × 1..N  ───────────────┬──────────┤
                                                                   │          │
                                                    Managed PostgreSQL 18   Managed Redis
                                                    (PITR backups, btree_gist enabled)
```

- Container images: `api`, `worker`, `migrator`, `web` (multi-stage Dockerfiles in `docker/`). Non-root users, read-only filesystem where possible.
- Deployment order: run `migrator` job (expand-only migrations) → roll out `api` and `worker` → (later) contract migrations in a subsequent release.
- SPA and API on the same registrable domain so the `SameSite=Strict` refresh cookie works.
- Health: load balancer uses `/health/ready` for readiness and `/health` for liveness.
- Scaling: Api is stateless (Redis backplane for SignalR); Worker instances can scale because outbox claims use `SKIP LOCKED`.
- Configuration via environment variables from the platform secret store; no secrets in images.
- Logs: JSON to stdout → platform log aggregation. Metrics/tracing via OpenTelemetry (Phase 12).
- Hosting provider: not yet chosen (decision for Phase 12; must support PostgreSQL 18 with `btree_gist`, Redis, containers, WebSockets).

## 3. Backups and recovery (Phase 12)

- PostgreSQL point-in-time recovery, daily snapshot retention per business policy, quarterly restore drills.
- Redis holds no authoritative data; loss is tolerated (rate-limit counters and backplane reset).

## 4. Runbooks (Phase 12)

To be written: deploy/rollback, migration failure, outbox dead-letter handling, Redis outage, PostgreSQL failover, secret rotation (JWT key rollover with dual-key validation), incident response for suspected tenant data exposure.
