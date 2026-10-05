# Booking Platform (T-Rex)

A multi-tenant booking and reservation SaaS platform that any business with bookable resources
(restaurants, clubs, VIP lounges, meeting rooms, spas, courts, event venues...) can use and embed.

Core model: **Tenant → Venue → Section → Bookable Resource → Availability → Booking**.

## Status

**Phase 1 — Foundation: complete.** Backend solution, migrations, local infrastructure, Quasar shell, tests, Dockerfiles and GitLab CI are in place.
See [`docs/DEVELOPMENT_PHASES.md`](docs/DEVELOPMENT_PHASES.md) for the roadmap and current phase.

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
scripts/                  Developer scripts (dev-only reset scripts under scripts/dev)
Backend/
  BookingPlatform.sln
  BookingPlatform.Api/ .Application/ .Domain/ .Infrastructure/ .Worker/ .Migrations/
  Tests/BookingPlatform.UnitTests/ Tests/BookingPlatform.IntegrationTests/
Frontend/
  BookingPlatform.Web/    Quasar app (src/, e2e/)
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

# 2. Start PostgreSQL 18 and Redis 8
docker compose up -d
docker compose ps            # both services should report (healthy)

# 3. Copy connection strings into .NET user-secrets (shared by Api, Worker and Migrations)
./scripts/dev-secrets.ps1    # macOS/Linux: ./scripts/dev-secrets.sh

# 4. Apply database migrations
dotnet run --project Backend/BookingPlatform.Migrations --launch-profile migrate -- migrate

# 5. Run the Api (http://localhost:5080, Swagger at /swagger) and the Worker
dotnet run --project Backend/BookingPlatform.Api --launch-profile http
dotnet run --project Backend/BookingPlatform.Worker

# 6. Run the web app (http://localhost:9000; proxies /api, /health and /hubs to the Api)
cd Frontend/BookingPlatform.Web
npm ci
npm run dev
```

### Common commands

| Task | Command |
|---|---|
| Build backend | `dotnet build Backend/BookingPlatform.sln` |
| Unit tests | `dotnet test --project Backend/Tests/BookingPlatform.UnitTests` |
| Integration tests (Docker required) | `dotnet test --project Backend/Tests/BookingPlatform.IntegrationTests` |
| Frontend lint / tests / build | `npm run lint`, `npm test`, `npm run build` (in `Frontend/BookingPlatform.Web`) |
| Container images | `docker build -f docker/<api\|worker\|migrator\|web>.Dockerfile -t bookingplatform/<name> .` |
| Liveness / readiness | `GET /health`, `GET /health/ready` |

## Documentation

| Document | Purpose |
|---|---|
| [AGENTS.md](AGENTS.md) | Permanent engineering rules |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | System architecture, modules, frontend architecture |
| [docs/DOMAIN_MODEL.md](docs/DOMAIN_MODEL.md) | Entities, relationships, status machine, invariants |
| [docs/DATABASE.md](docs/DATABASE.md) | Schema, migrations, constraints, double-booking protection, indexes |
| [docs/API.md](docs/API.md) | Endpoints, error model, pagination, idempotency |
| [docs/REALTIME.md](docs/REALTIME.md) | SignalR hub, groups, events, reconnect/resync |
| [docs/SECURITY.md](docs/SECURITY.md) | Authentication, authorization, tenant isolation, threat notes |
| [docs/TESTING.md](docs/TESTING.md) | Test strategy, mandatory suites |
| [docs/DEVELOPMENT_PHASES.md](docs/DEVELOPMENT_PHASES.md) | Phase plan, acceptance criteria, deferred items |
| [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) | Local and production deployment |
| [docs/DECISIONS.md](docs/DECISIONS.md) | Architecture decision log |
