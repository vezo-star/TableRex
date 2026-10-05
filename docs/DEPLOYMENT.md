# Deployment

Status: **Phase 1 — local development and container images implemented.** Production deployment and runbooks are finalised in Phase 12.

## 1. Local development

| Component | How it runs |
|---|---|
| PostgreSQL 18 | Compose service `postgres` (`postgres:18-alpine`), bound to `127.0.0.1:${POSTGRES_PORT:-5432}`, volume `postgres-data` at `/var/lib/postgresql` |
| Redis 8 | Compose service `redis` (`redis:8-alpine`, password required), bound to `127.0.0.1:${REDIS_PORT:-6379}` |
| Mailpit | Compose service `mailpit` (`axllent/mailpit:v1.27`): catches the Worker's development emails; SMTP `127.0.0.1:1025`, inbox UI `http://localhost:8025` |
| Migrations | `dotnet run --project Backend/BookingPlatform.Migrations --launch-profile migrate -- migrate` |
| Api | `dotnet run --project Backend/BookingPlatform.Api --launch-profile http` → `http://localhost:5080` |
| Worker | `dotnet run --project Backend/BookingPlatform.Worker` |
| Web | `npm run dev` in `Frontend/BookingPlatform.Web` → `http://localhost:9000` (proxies `/api`, `/health` and `/hubs` to the Api → same origin, so the refresh cookie works) |

`.env.example` documents every variable with placeholders. Copy it to `.env` (git-ignored); Compose reads it directly and `scripts/dev-secrets.ps1` / `scripts/dev-secrets.sh` copy the .NET values into user-secrets (one `UserSecretsId` shared by Api, Worker and Migrations). Both scripts refuse values that still contain `CHANGE_ME`.

| Variable | Used by | Purpose |
|---|---|---|
| `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_PORT` | Compose | Local PostgreSQL |
| `REDIS_PASSWORD`, `REDIS_PORT` | Compose | Local Redis |
| `ConnectionStrings__Postgres` | Api, Worker, Migrations | Required; startup fails without it |
| `ConnectionStrings__Redis` | Api, Worker | Required; `abortConnect=false` is forced so the Api serves without Redis |
| `Cors__AllowedOrigins__N` | Api | Explicit SPA origins (absolute http/https, no path); never a wildcard |
| `Database__AutoMigrate` | Api | Development only; `false` by default |
| `LogFormat` | Api, Worker | `json` (default, compact JSON) or `text` (Development) |
| `VITE_API_BASE_URL` | Web build | API base path baked into the SPA (default `/api`); never put secrets in `VITE_*` |
| `Jwt__SigningKey` | Api | **Required secret.** ≥32 random bytes (base64); startup fails if missing, short or a placeholder. Rotating it signs everyone out of their access tokens (refresh cookies keep working). |
| `Jwt__Issuer`, `Jwt__Audience`, `Jwt__AccessTokenMinutes` | Api | Defaults `booking-platform`, `booking-platform-web`, `15` (1–60) |
| `Auth__RefreshTokenDays`, `Auth__RefreshTokenAbsoluteDays`, `Auth__LockoutThreshold`, `Auth__LockoutBaseMinutes`, `Auth__LockoutMaxMinutes` | Api | Session and lockout tuning (defaults 14, 30, 5, 1, 60) |
| `ReverseProxy__TrustedNetworks__N` | Api | CIDRs of the reverse proxies whose `X-Forwarded-For/Proto` are trusted (e.g. `10.0.0.0/8`). Empty = only loopback. Required behind a proxy, otherwise rate limits key on the proxy IP. |
| `RateLimiting__KeyPrefix` | Api | Redis key prefix for rate-limit counters (default `rl`); set per environment when sharing one Redis |
| `RateLimiting__Policies__{policy}__PermitLimit`, `RateLimiting__Policies__{policy}__WindowSeconds` | Api | Optional per-IP overrides for `public-read` (120/60 s), `public-booking` (10/600 s), `public-manage` (60/60 s), `public-cancel` (10/600 s) and the auth policies |
| `PublicBooking__TokenKey` | Api, Worker | **Required secret** (same value in both; the Worker builds manage links in emails). ≥32 random bytes (base64), different from `Jwt__SigningKey`; startup fails if missing, short or a placeholder. Signs guest manage links; rotating it invalidates every outstanding link. |
| `PublicBooking__MaxActiveBookingsPerContact` | Api | Upcoming active public bookings allowed per email per venue (default 3, 1–100) |
| `Notifications__PublicBaseUrl` | Worker | **Required.** Origin of the web app used in email links, e.g. `https://app.example.com` (absolute http/https, no path, no trailing slash). Development: `http://localhost:9000`. |
| `Notifications__Email__Provider` | Worker | `Log` (default; delivers nothing, logs masked recipient + subject), `Smtp` (relay) or `Pickup` (writes `.eml` files) |
| `Notifications__Email__FromAddress`, `Notifications__Email__FromName` | Worker | **Required** sender address (validated) and display name (default `Booking Platform`) |
| `Notifications__Email__SmtpHost`, `SmtpPort`, `SmtpEnableSsl` | Worker | Relay for `Smtp` (defaults port 587, TLS on). Development uses Mailpit at `127.0.0.1:1025` without TLS — use the IP, not `localhost`, on Windows (IPv6 fallback adds ~2 s per message). |
| `Notifications__Email__SmtpUsername`, `Notifications__Email__SmtpPassword` | Worker | Relay credentials (**secret**) when required |
| `Notifications__Email__PickupDirectory` | Worker | Target directory for `Pickup` |
| `Notifications__BatchSize`, `Notifications__PollSeconds`, `Notifications__MaxAttempts` | Worker | Dispatcher tuning (defaults 20, 5, 8). Wake-up is instant via `LISTEN outbox`; polling is the fallback. |
| `Payments__Provider` | Api | Active payment provider name; **empty disables online payments** (deposit venues then refuse online bookings with 503, D-087). Only registered providers are accepted, and startup fails otherwise. `Simulated` exists in Development/Test only. **Production: leave empty until a real provider adapter is added.** |
| `Payments__PublicBaseUrl` | Api | Required when a provider is set: origin of the web app for provider return URLs, e.g. `https://app.example.com` (absolute http/https, no path, no trailing slash) |
| `Payments__Simulated__WebhookSecret` | Api | Optional (non-Production): ≥ 32 bytes. When unset, a random key per process is used, which is fine for a single dev instance |
| `MAILPIT_SMTP_PORT`, `MAILPIT_UI_PORT` | Compose | Local Mailpit ports (defaults 1025 / 8025; UI at `http://localhost:8025`) |
| `DEV_ADMIN_EMAIL`, `DEV_ADMIN_PASSWORD` | Migrations `seed-dev` | Development only; password ≥12 chars. `seed-dev` refuses to run when `DOTNET_ENVIRONMENT=Production`. |

PostgreSQL connections default to `GSS Encryption Mode=Disable` unless the connection string sets it: the platform uses password authentication, and the .NET runtime images do not ship the Kerberos library that Npgsql otherwise probes for.

## 1a. Container images

All Dockerfiles live in `docker/` and use the repository root as build context.

| Image | Dockerfile | Base | Runs as | Notes |
|---|---|---|---|---|
| `api` | `docker/api.Dockerfile` | `mcr.microsoft.com/dotnet/aspnet:10.0` | `$APP_UID` (1654) | Listens on 8080; Swagger disabled outside Development/Test |
| `worker` | `docker/worker.Dockerfile` | `mcr.microsoft.com/dotnet/runtime:10.0` | `$APP_UID` | Graceful SIGTERM shutdown (30 s timeout) |
| `migrator` | `docker/migrator.Dockerfile` | `mcr.microsoft.com/dotnet/runtime:10.0` | `$APP_UID` | Default command `migrate`; exit 0 success, 1 failure, 2 usage |
| `web` | `docker/web.Dockerfile` | `node:24-alpine` → `nginx:alpine` | `nginx` (101) | Listens on 8080; SPA fallback, CSP and security headers; `VITE_API_BASE_URL` build arg |

The `web` container serves only the SPA: `/api`, `/hubs` and `/health` return 404 there and must be routed to the Api by the reverse proxy in front of both.

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
