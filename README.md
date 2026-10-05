# Booking Platform (T-Rex)

A multi-tenant booking and reservation SaaS platform that any business with bookable resources
(restaurants, clubs, VIP lounges, meeting rooms, spas, courts, event venues...) can use and embed.

Core model: **Tenant → Venue → Section → Bookable Resource → Availability → Booking**.

## Status

**Phase 0 — Discovery & Architecture: approved.** Documentation only; no application code yet. Phase 1 starts on explicit instruction.
See [`docs/DEVELOPMENT_PHASES.md`](docs/DEVELOPMENT_PHASES.md) for the roadmap and current phase.

## Stack

- Backend: ASP.NET Core Web API on .NET 10 (C#), Dapper + Npgsql, SignalR, Serilog, FluentValidation, Swagger
- Database: PostgreSQL 18 (authoritative), migrations via DbUp (plain SQL)
- Cache / scale-out: Redis
- Frontend: Vue 3 + Quasar + Pinia + Vue Router + Axios (JavaScript)
- Tests: xUnit, Testcontainers, Vitest, Playwright
- Local infrastructure: Docker Compose

## Repository layout (target)

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

Available from Phase 1. Prerequisites will be:

- .NET 10 SDK
- Node.js 20+ and npm
- Docker Desktop (running)

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
