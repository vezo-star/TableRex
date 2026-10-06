# Booking Platform (T-Rex)

A multi-tenant booking and reservation SaaS platform that any business with bookable resources
(restaurants, clubs, VIP lounges, meeting rooms, spas, courts, event venues...) can use and embed.

Core model: **Tenant → Venue → Section → Bookable Resource → Availability → Booking**.

## Status

**All phases (0–12) complete.** The MVP includes:

- multi-tenant authentication with venue-scoped roles;
- venues, resources, combinations and floor plans;
- an availability engine and a booking engine protected by a PostgreSQL exclusion constraint;
- a staff board with real-time updates;
- public booking and guest self-service;
- outbox-driven email notifications;
- payment abstractions with deposits;
- SaaS plans, branding and reports;
- production hardening: least-privilege database role, deny-by-default authorization, load and accessibility verification, runbooks.

See [`docs/DEVELOPMENT_PHASES.md`](docs/DEVELOPMENT_PHASES.md) for each phase's completion notes, the final architectural test and the deferred items. Going live still needs a hosting choice, an SMTP relay and, for deposits, a real payment provider (DEPLOYMENT.md §2).

## Stack

- Backend: ASP.NET Core Web API on .NET 10 (C#), Dapper + Npgsql, SignalR, Serilog, FluentValidation, Swagger
- Database: PostgreSQL 18 (authoritative), migrations via DbUp (plain SQL)
- Cache / scale-out: Redis
- Frontend: Vue 3 + Quasar + Pinia + Vue Router + Axios (JavaScript)
- Tests: xUnit, Testcontainers, Vitest, Playwright
- Local infrastructure: Docker Compose

## Repository layout

```
AGENTS.md                 Permanent engineering rules (read first)
README.md
docker-compose.yml        PostgreSQL + Redis for local development      (Phase 1)
.env.example              Environment variable template (placeholders)  (Phase 1)
docs/                     Architecture, domain, database, API, security, testing, phases, decisions
database/
  migrations/             V###__*.sql, applied in order by DbUp
docker/                   Dockerfiles (api, worker, web, migrator)
deploy/                   Production-like stack: compose.yml, edge proxy config, .env.example
tools/load/               Load and concurrency test (no dependencies)
scripts/                  Developer scripts (dev-only reset scripts under scripts/dev)
Backend/
  BookingPlatform.sln
  BookingPlatform.Api/ .Application/ .Domain/ .Infrastructure/ .Worker/ .Migrations/
  Tests/BookingPlatform.UnitTests/ Tests/BookingPlatform.IntegrationTests/
Frontend/
  BookingPlatform.Web/    Quasar app (src/, test/vitest/, test/e2e/)
```

## Getting started

### Prerequisites

- .NET 10 SDK (10.0.400 or a later 10.0 feature band; see `global.json`)
- Node.js 24 (or 22.22+) and npm
- Docker Desktop (running) — needed for Compose and for the integration tests (Testcontainers)

### Quickstart

```powershell
# 1. Local secrets: copy the template and replace every CHANGE_ME value (.env is git-ignored)
Copy-Item .env.example .env

# 2. Start PostgreSQL 18, Redis 8 and Mailpit (development email inbox: http://localhost:8025)
docker compose up -d
docker compose ps            # postgres and redis should report (healthy)

# 3. Copy connection strings into .NET user-secrets (shared by Api, Worker and Migrations)
./scripts/dev-secrets.ps1    # macOS/Linux: ./scripts/dev-secrets.sh

# 4. Apply database migrations and create demo data (Development only; idempotent)
dotnet run --project Backend/BookingPlatform.Migrations --launch-profile migrate
dotnet run --project Backend/BookingPlatform.Migrations --launch-profile seed-dev

# 5. Run the Api (http://localhost:5080, Swagger at /swagger) and the Worker
#    (the Worker sends booking and password reset emails to Mailpit in Development)
dotnet run --project Backend/BookingPlatform.Api --launch-profile http
dotnet run --project Backend/BookingPlatform.Worker

# 6. Run the web app (http://localhost:9000; proxies /api, /health and /hubs to the Api)
cd Frontend/BookingPlatform.Web
npm ci
npm run dev
```

### Demo accounts (after `seed-dev`)

All demo accounts use the password from `DEV_ADMIN_PASSWORD` in your `.env`. Never reuse it outside local development.

| Account | Access |
|---|---|
| `DEV_ADMIN_EMAIL` (default `admin@demo.local`) | Platform admin; owner of **Demo Hospitality** and **Demo Coworking** (chooses a workspace after sign-in) |
| `manager@`, `host@`, `staff@`, `viewer@` + the admin's email domain | Demo Hospitality with the Manager, Host, Staff and View-only roles |

### Common commands

| Task | Command |
|---|---|
| Build backend | `dotnet build Backend/BookingPlatform.sln` |
| Unit tests | `dotnet test --project Backend/Tests/BookingPlatform.UnitTests` |
| Integration tests (Docker required) | `dotnet test --project Backend/Tests/BookingPlatform.IntegrationTests` |
| Frontend lint / tests / build | `npm run lint`, `npm test`, `npm run build` (in `Frontend/BookingPlatform.Web`) |
| Container images | `docker build -f docker/<api\|worker\|migrator\|web>.Dockerfile -t bookingplatform/<name> .` |
| Production-like stack | `docker compose -f deploy/compose.yml --env-file deploy/.env up -d --build`, then `… run --rm migrator bootstrap-admin` once (DEPLOYMENT.md §2) |
| End-to-end tests | `npm run test:e2e` in `Frontend/BookingPlatform.Web` with `E2E_BASE_URL`, `E2E_ADMIN_EMAIL`, `E2E_ADMIN_PASSWORD` |
| Load / concurrency test | `node tools/load/load-test.mjs` with `LOAD_BASE_URL`, `LOAD_EMAIL`, `LOAD_PASSWORD` (never against a production tenant) |
| Liveness / readiness | `GET /health`, `GET /health/ready` |

## Documentation

| Document | Purpose |
|---|---|
| [AGENTS.md](AGENTS.md) | Permanent engineering rules |
| [docs/USER_GUIDE.md](docs/USER_GUIDE.md) | Staff training guide: roles, daily workflows, guest experience, setup, FAQ |
| [docs/presentation/index.html](docs/presentation/index.html) | Product presentation (open in a browser; PDF copy alongside) |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | System architecture, modules, frontend architecture |
| [docs/DOMAIN_MODEL.md](docs/DOMAIN_MODEL.md) | Entities, relationships, status machine, invariants |
| [docs/DATABASE.md](docs/DATABASE.md) | Schema, migrations, constraints, double-booking protection, indexes |
| [docs/API.md](docs/API.md) | Endpoints, error model, pagination, idempotency |
| [docs/REALTIME.md](docs/REALTIME.md) | SignalR hub, groups, events, reconnect/resync |
| [docs/SECURITY.md](docs/SECURITY.md) | Authentication, authorization, tenant isolation, threat notes |
| [docs/TESTING.md](docs/TESTING.md) | Test strategy, mandatory suites |
| [docs/DEVELOPMENT_PHASES.md](docs/DEVELOPMENT_PHASES.md) | Phase plan, acceptance criteria, deferred items |
| [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) | Local and production deployment, backups, release verification |
| [docs/RUNBOOKS.md](docs/RUNBOOKS.md) | Deploy/rollback, migration failure, outbox, outages, secret rotation, incidents |
| [docs/DECISIONS.md](docs/DECISIONS.md) | Architecture decision log |
