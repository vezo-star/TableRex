# Deployment

Status: **Final (Phase 12).** Local development (§1), container images (§1a), the production reference deployment (§2), backups (§3) and verification (§4). Operational procedures are in [RUNBOOKS.md](RUNBOOKS.md).

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

## 2. Production deployment

### 2.1 Topology

```
 browser ── TLS ──► edge proxy (one origin, WebSockets on, no query strings in logs)
                     ├── /api, /health ─────────► api × N   (stateless; SignalR via Redis backplane)
                     ├── /hubs (WebSocket) ─────► api × N
                     └── everything else ───────► web       (static SPA, CSP + security headers)

 migrator (one-shot per release) ──► PostgreSQL 18 (owner role; PITR backups)
 api, worker ──────────────────────► PostgreSQL 18 (least-privilege app role, D-095)
 api, worker ──────────────────────► Redis 8 (rate-limit counters, backplane; no persistence needed)
 worker × 1..N ────────────────────► SMTP relay (emails), outbox claims with SKIP LOCKED
```

- **One origin** for the SPA and the Api (D-094): the refresh cookie is `SameSite=Strict` and scoped to `/api/v1/auth`, the CSP allows `'self'` only, and the SPA calls `/api` relatively. Separate hosts would need CORS and break the cookie.
- **Hosting:** provider not chosen (D-098). Requirements: containers, WebSockets through the load balancer, PostgreSQL 18 with `btree_gist` and `citext`, Redis 7+ with AUTH, TLS termination, a secret store. `deploy/compose.yml` is the reference deployment and runs as-is on a single VM.
- **Scaling:** the Api is stateless (no sticky sessions: SignalR is WebSockets-only, D-065); Worker instances can be added because outbox and sweep claims use `SKIP LOCKED`. Run one migrator at a time.

### 2.2 Reference stack (`deploy/compose.yml`)

| Service | Image | Notes |
|---|---|---|
| `postgres` | `postgres:18-alpine` | Volume `postgres-data`. Replace with a managed service in production. |
| `redis` | `redis:8-alpine` | Password required, persistence off (holds nothing authoritative). |
| `migrator` | `${IMAGE_REGISTRY}/migrator:${IMAGE_TAG}` | Runs `migrate` on every `up`, connects as the owner, then provisions `APP_DB_USER` (D-095). Api and Worker start only after it exits 0. |
| `api` | `${IMAGE_REGISTRY}/api:${IMAGE_TAG}` | `read_only`, tmpfs `/tmp`, Production, trusts forwarded headers only from the stack network `10.89.0.0/24`. |
| `worker` | `${IMAGE_REGISTRY}/worker:${IMAGE_TAG}` | `read_only`, 35 s stop grace for in-flight sends. |
| `web` | `${IMAGE_REGISTRY}/web:${IMAGE_TAG}` | nginx serving the SPA on 8080. |
| `proxy` | `nginx:1.31-alpine` + `deploy/proxy.conf` | Edge on `${HTTP_BIND}:${HTTP_PORT}` (default `127.0.0.1:8088`). Overwrites `X-Forwarded-For`, re-resolves upstreams every 5 s, logs `$uri` only. |

First deployment:

```bash
cp deploy/.env.example deploy/.env           # git-ignored; replace every CHANGE_ME value
docker compose -f deploy/compose.yml --env-file deploy/.env up -d --build      # or IMAGE_REGISTRY/IMAGE_TAG for CI images
# create the first platform admin (set PLATFORM_ADMIN_EMAIL / PLATFORM_ADMIN_PASSWORD in deploy/.env first)
docker compose -f deploy/compose.yml --env-file deploy/.env run --rm migrator bootstrap-admin
# then remove PLATFORM_ADMIN_PASSWORD from deploy/.env
curl -fsS http://127.0.0.1:8088/health/ready
```

The platform admin then signs in, creates tenants with their first owner (Platform → Tenants), and owners set up venues, opening hours, resource types and tables. `bootstrap-admin` is idempotent and never changes an existing account's password (D-093).

### 2.3 Production variables (`deploy/.env`)

| Variable | Required | Purpose |
|---|---|---|
| `PUBLIC_BASE_URL` | yes | The https origin users open, e.g. `https://book.example.com`. Used for CORS, email links and payment return URLs. |
| `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD` | yes | Database and owner role (migrations only). |
| `APP_DB_USER`, `APP_DB_PASSWORD` | yes | Least-privilege role for the Api and Worker; 16–128 printable ASCII characters without spaces, `;` or quotes (it is embedded in a connection string). Changing it and redeploying rotates it. |
| `API_DB_MAX_POOL_SIZE`, `WORKER_DB_MAX_POOL_SIZE` | no | Npgsql pool cap per instance (defaults 50 and 10). The sum over all Api and Worker instances must stay below PostgreSQL `max_connections` (default 100) minus ~10 for the migrator, backups and superuser slots. Above it, bursts fail with `53300` (returned as 503) instead of queueing for a pooled connection. |
| `REDIS_PASSWORD` | yes | Redis AUTH. |
| `JWT_SIGNING_KEY` | yes | ≥ 32 random bytes, base64. |
| `PUBLIC_BOOKING_TOKEN_KEY` | yes | ≥ 32 random bytes, base64, different from the JWT key (signs guest manage links, D-066). |
| `MAIL_PROVIDER` | **set to `Smtp`** | Default `Log` delivers nothing (R19). |
| `MAIL_FROM_ADDRESS`, `SMTP_HOST`, `SMTP_PORT`, `SMTP_ENABLE_SSL`, `SMTP_USERNAME`, `SMTP_PASSWORD` | with `Smtp` | Relay settings (TLS on by default). |
| `PAYMENTS_PROVIDER` | no | Leave empty until a real provider adapter exists (D-087). |
| `PLATFORM_ADMIN_EMAIL`, `PLATFORM_ADMIN_PASSWORD`, `PLATFORM_ADMIN_NAME` | first run | Read by `bootstrap-admin` only. |
| `IMAGE_REGISTRY`, `IMAGE_TAG` | no | Defaults `bookingplatform` / `local` (local builds). CI images: `$CI_REGISTRY_IMAGE` and a commit SHA or tag. |
| `HTTP_BIND`, `HTTP_PORT` | no | Where the edge listens (default `127.0.0.1:8088`). |

All other Api/Worker settings in §1 keep their defaults and can be added to the service `environment` blocks when needed. Secrets belong in the host's secret store or a root-only `deploy/.env`; they are never baked into images, and the Api refuses to start with missing, short or placeholder keys.

### 2.4 TLS, proxies and headers

- Terminate TLS in front of `proxy` (or in it) and set `PUBLIC_BASE_URL` to the https origin. The refresh cookie is always `Secure`; browsers accept it on `http://localhost` only.
- The Api sends HSTS (`max-age=31536000`) when the request arrived over https, as reported by a trusted proxy's `X-Forwarded-Proto`. Any extra proxy layer must be added to `ReverseProxy__TrustedNetworks__N`, and the outermost one must overwrite, not append, `X-Forwarded-For`.
- **Never log query strings** at any proxy, load balancer or CDN: SignalR sends the access token as `?access_token=` (D-065, D-094). Guest manage and password-reset tokens travel in URL fragments and headers, which are not sent or logged.
- WebSockets must be allowed on `/hubs/*` with a read timeout above 15 s (the hub pings every 15 s; the reference proxy uses 100 s).
- `client_max_body_size 1m` is enough for every request the platform accepts.

### 2.5 Database roles

| Role | Used by | Privileges |
|---|---|---|
| Owner (`POSTGRES_USER`) | migrator only | Owns the schema; runs migrations, `bootstrap-admin` and role provisioning. On managed PostgreSQL this is the admin user (needs `CREATEROLE`). |
| App (`APP_DB_USER`) | api, worker | `LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE`; `SELECT, INSERT, UPDATE, DELETE` on all tables, sequence usage, read-only migration journal, no DDL. Re-granted after every migration run. |

### 2.6 Releases and rollback

1. CI builds and pushes `api`, `worker`, `migrator`, `web` tagged with the commit SHA (and the git tag) after all tests pass (`package:images`).
2. Optionally run the e2e suite against staging (`e2e:staging`, manual).
3. Set `IMAGE_TAG` to the new SHA and run `docker compose … up -d`: the migrator applies new migrations and re-grants the app role, then Api and Worker are recreated. Then run `docker compose … exec proxy nginx -s reload` (zero-downtime). The proxy re-resolves upstreams every 5 s, but if a lookup lands while the old Api container is gone, Docker DNS answers SERVFAIL and nginx keeps the stale address for about 50 s; the reload resolves immediately (measured: ready 4.6 s after recreating the Api).
4. Migrations are expand-only within a release (add columns/tables, never drop or rename what the previous version reads), so the previous images keep working against the new schema. Contracting changes ship one release later.
5. Rollback = set `IMAGE_TAG` back and `up -d`. The migrator of the old version sees newer journal entries it does not know and leaves them alone; nothing is ever rolled back automatically, and no script drops data. See RUNBOOKS.md §1–2.

### 2.7 Health, logs and monitoring

- Liveness `GET /health` (no dependencies). Readiness `GET /health/ready`: 503 when PostgreSQL is unreachable; a Redis outage reports `Degraded` with HTTP 200 (D-097). The body lists check names and statuses only.
- Logs are compact JSON on stdout (Serilog) with `CorrelationId`/`TraceId`; passwords, tokens, keys and full email addresses are never logged. Ship stdout to the platform's log store.
- Alert on: readiness not `Healthy`, any `DEAD` outbox rows, `PENDING`/`FAILED` NOTIFICATION rows older than 15 minutes, payments with `requires_attention`, 5xx rate, and PostgreSQL storage/replication lag. Queries are in RUNBOOKS.md §3.
- OpenTelemetry metrics/tracing are deferred until a collector is chosen (D-098).

### 2.8 Go-live checklist

- [ ] TLS in front of the proxy; `PUBLIC_BASE_URL` is the https origin; HSTS observed in responses.
- [ ] No proxy/LB/CDN logs query strings; WebSockets enabled on `/hubs`.
- [ ] Every secret generated fresh, stored outside git; `APP_DB_USER` differs from the owner.
- [ ] `MAIL_PROVIDER=Smtp` with a working relay (send a password reset to a test address).
- [ ] `PAYMENTS_PROVIDER` empty unless a real adapter is deployed; venues with deposits understand online booking is off.
- [ ] First platform admin created with `bootstrap-admin`; its password removed from the environment.
- [ ] PITR backups enabled and one restore drill done (§3).
- [ ] Monitoring and alerts from §2.7 in place.
- [ ] e2e suite and load test pass against the environment (§4) before real guests are invited.

## 3. Backups and recovery

- **PostgreSQL is the only stateful component.** Use the provider's point-in-time recovery (continuous WAL archiving) with at least 7 days of PITR and daily snapshots retained per business policy (30 days suggested). Redis needs no backup: losing it only resets rate-limit counters and the backplane.
- **Logical backups** (extra copy, or the main mechanism on a single VM): `pg_dump -Fc` daily to storage outside the host, encrypted at rest. It contains personal data, so access is restricted like the database itself.
- **Restore drill (quarterly, and after major schema changes)** — verified in Phase 12 against the reference stack:

```bash
# 1. dump
docker compose -f deploy/compose.yml --env-file deploy/.env exec -T postgres \
  sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc -f /tmp/backup.dump'
docker compose -f deploy/compose.yml --env-file deploy/.env cp postgres:/tmp/backup.dump ./backup.dump
docker compose -f deploy/compose.yml --env-file deploy/.env exec -T postgres rm /tmp/backup.dump

# 2. restore into a throwaway server, never into the live one
docker run -d --name restore-drill -e POSTGRES_PASSWORD=drill-only postgres:18-alpine
docker cp backup.dump restore-drill:/tmp/backup.dump
docker exec restore-drill createdb -U postgres restored
docker exec restore-drill pg_restore -U postgres -d restored --no-owner --no-privileges --exit-on-error /tmp/backup.dump

# 3. compare row counts of bookings, booking_allocations, tenants, users, outbox_messages, audit_logs and
#    schema_versions with the source, and check the exclusion constraint exists:
#    SELECT count(*) FROM pg_constraint WHERE contype = 'x';   -- expect 1
docker rm -f restore-drill && rm backup.dump
```

  Phase 12 result: restore exit 0; bookings 23, allocations 27, tenants 6, users 7, outbox 101, audit 146, journal 31, exclusion constraints 1 — identical in source and restore.
- After restoring into a new server, run the migrator once (it re-provisions the app role and grants, which `--no-owner --no-privileges` skipped), then start Api and Worker. Bookings made after the restore point are lost; see RUNBOOKS.md §5.

## 4. Release verification

| Check | Command | Phase 12 result (reference stack) |
|---|---|---|
| Backend tests | `dotnet test --solution Backend/BookingPlatform.sln -c Release` | all pass |
| Frontend | `npm run lint && npm run format:check && npm test && npm run build` | all pass |
| End-to-end | `E2E_BASE_URL=… E2E_ADMIN_EMAIL=… E2E_ADMIN_PASSWORD=… npm run test:e2e` (in `Frontend/BookingPlatform.Web`) | 6 journeys pass (desktop + mobile) |
| Load / concurrency | `LOAD_BASE_URL=… LOAD_EMAIL=… LOAD_PASSWORD=… node tools/load/load-test.mjs` | ~550–600 req/s, p95 180–400 ms; 1 winner of 100 for one table; 0 overlaps; no 5xx |

The e2e suite creates a new tenant per run; the load test creates and cancels bookings on a date 14 days ahead. Run both against staging or a fresh stack, **never against production tenants**.
