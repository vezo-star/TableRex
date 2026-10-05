# AGENTS.md — Permanent Engineering Rules

Every agent (human or AI) working in this repository MUST read this file before making changes and MUST obey it.
If a rule here conflicts with a request, stop and raise the conflict instead of silently deviating.

Detailed rationale lives in `docs/` (start with `docs/ARCHITECTURE.md` and `docs/DECISIONS.md`).

---

## 1. Product

A multi-tenant booking and reservation SaaS platform. The core abstraction is:

**Tenant → Venue → Section → Bookable Resource → Availability → Booking**

The platform is generic (restaurants, clubs, lounges, meeting rooms, spas, courts, event venues...).
Never introduce restaurant-only assumptions into the core booking engine.

## 2. Approved stack

| Layer | Technology |
|---|---|
| Backend | ASP.NET Core Web API, C#, REST, SignalR, JWT + refresh tokens, Serilog, FluentValidation, Swagger/OpenAPI |
| .NET version | **.NET 10 (LTS)** — `net10.0`, approved change from the original .NET 8 spec (`docs/DECISIONS.md` D-002) |
| Data access | Npgsql + Dapper, parameterized SQL, repository/query abstractions |
| Database | PostgreSQL (authoritative source of truth) |
| Migrations | DbUp running plain, ordered SQL scripts from `database/migrations` |
| Time | NodaTime in the domain for time-zone/local-time logic |
| Cache/coordination | Redis (never authoritative) |
| Real-time | SignalR with Redis backplane |
| Frontend | Vue 3, Quasar (Vite CLI), Pinia, Vue Router, Axios, SignalR JS client, **JavaScript** |
| Testing | xUnit, AwesomeAssertions (not FluentAssertions v8+, D-014), Testcontainers, WebApplicationFactory, Vitest, Playwright |
| Local infra | Docker Compose (PostgreSQL, Redis) |

### Prohibited without explicit written approval

- Entity Framework / EF Core
- SQL Server
- MongoDB (or any document DB) as a primary store
- Microservices, message brokers (RabbitMQ, Kafka, ...)
- TypeScript in the frontend
- MediatR, AutoMapper (licensing + unnecessary indirection — use plain services and explicit mapping)
- FluentAssertions v8+ (commercial licence) — use AwesomeAssertions
- Any new framework/library that duplicates an approved one

## 3. Architecture

- **Modular monolith.** One deployable API + one Worker process, sharing Domain/Application/Infrastructure.
- Projects (under `Backend/`):
  - `BookingPlatform.Domain` — entities, value objects, domain rules (status machine, operating hours, availability calculator). No I/O, no infrastructure references.
  - `BookingPlatform.Application` — use-case services, commands/queries, validators, interfaces (repositories, clock, unit of work). Owns transaction boundaries.
  - `BookingPlatform.Infrastructure` — Dapper repositories, SQL, Redis, auth token services, outbox, SignalR publisher implementations.
  - `BookingPlatform.Api` — controllers (thin), auth setup, middleware, ProblemDetails mapping, SignalR hub, health checks.
  - `BookingPlatform.Worker` — background services (outbox notifications, hold expiry, cleanup).
  - `BookingPlatform.Migrations` — DbUp runner and dev seeding CLI.
- Dependency direction: `Api/Worker → Application → Domain`; `Infrastructure → Application → Domain`. Domain depends on nothing but NodaTime.
- Controllers: parse request → call one application service method → map result. No SQL, no business rules.

## 4. Non-negotiable rules

1. **PostgreSQL is authoritative** for bookings, allocations, holds, customers, users, tenants, venues and configuration.
2. **Never trust a TenantId from the client.** Tenant context comes from the authenticated, server-verified token claim (issued only after membership verification) or, for public endpoints, from the tenant/venue slug resolved server-side.
3. **Every tenant-owned query is tenant-scoped** (`WHERE tenant_id = @TenantId`), with `TenantId` taken from `ITenantContext`, never from request bodies.
4. **The frontend is not authoritative.** Server and database enforce every rule.
5. **Redis is not authoritative.** "Redis says available" never means "booking guaranteed".
6. **The database enforces critical invariants.** Overlapping active allocations for the same resource are prevented by a PostgreSQL exclusion constraint (`btree_gist`, `tstzrange`). Never weaken or remove it; never replace it with application-level locking.
7. **SignalR only notifies.** It is never a source of state. Clients resync from the API after reconnect.

## 5. Database rules

- PostgreSQL 18, UUID primary keys (`uuidv7()` default), `snake_case` names, `TIMESTAMPTZ` for all instants (stored UTC).
- Every table has explicit PKs, FKs, check constraints and unique constraints where meaningful. Tenant consistency is enforced with composite FKs on `(tenant_id, id)` where a child references a tenant-owned parent.
- JSONB only for genuinely flexible structured data (floor-plan visual metadata, outbox payloads, audit diffs, idempotency responses).
- No blanket `is_deleted`. Use `is_active`/status/archive for historically important entities. **Bookings are never deleted.**
- Indexes are explicit and justified (documented in `docs/DATABASE.md`).

### Migration rules

- Plain SQL files in `database/migrations`, named `V###__description.sql`, applied in order by DbUp; journal table `schema_versions`.
- **A migration is immutable once merged to the main branch.** Fix forward with a new migration.
- The full schema must build from an empty PostgreSQL database; this is tested in CI.
- Migrations never contain demo data. Reference data (roles, permissions) only. Dev demo data is seeded by the Migrations CLI with `--seed-dev`, which refuses to run in Production.
- No `DROP DATABASE`, no destructive reset in normal startup. Dev reset scripts live in `scripts/dev/` and refuse non-local connection strings.

## 6. Concurrency & transaction rules

- One business operation = one database transaction, opened and committed by the Application service via a unit of work. Repositories never open their own transactions.
- Booking create/move/cancel/status changes, holds and blocks write allocations inside that transaction.
- Allocation conflicts (SQLSTATE `23P01` exclusion violation) map to **HTTP 409 `RESOURCE_NOT_AVAILABLE`**. They are business conflicts — never blindly retried as transient errors.
- Multi-resource allocations are inserted in ascending `resource_id` order to minimise deadlocks. A deadlock (`40P01`) may be retried once, then surfaces as 409.
- Booking creation requires an `Idempotency-Key` header (staff and public). Same key + same payload → original result; same key + different payload → 409 `IDEMPOTENCY_CONFLICT`.
- Booking edits use optimistic concurrency (`version` column, `If-Match`/`version` in request) → 409 `CONCURRENCY_CONFLICT` on mismatch.
- Moves are atomic: deactivate old allocations and insert new ones in the same transaction.
- Side effects (SignalR, email, SMS) go through the **outbox**, written in the same transaction as the mutation.

## 7. Time rules

- Store instants as `TIMESTAMPTZ` (UTC). Never treat local time as UTC; never use server local time for business logic.
- Every venue has an IANA time zone (e.g. `Africa/Johannesburg`). Customer-facing times are venue-local.
- Operating periods may cross midnight (`18:00 → 02:00`). Never assume `open < close`.
- Use an injected clock (`NodaTime.IClock`) — never `DateTime.Now`/`UtcNow` directly in Domain/Application.
- DST: nonexistent local times are skipped; ambiguous local times resolve to the earlier instant. Covered by tests.

## 8. Security rules

- Authorization is server-side and permission-based (`[HasPermission("booking.create")]`), not scattered role checks.
- Tenant membership (and venue access) is checked for every protected tenant operation.
- Venue access is scoped (D-018): TENANT_OWNER/TENANT_ADMIN access all tenant venues; other roles only venues assigned in `tenant_user_venues`. Every venue id in a request is re-checked via `IVenueAccessGuard`.
- Passwords hashed (PBKDF2-HMAC-SHA512 via `PasswordHasher`, ≥210k iterations). Refresh tokens are random, stored hashed, rotated, with reuse detection.
- Never log passwords, tokens, secrets, full payment data. Never commit secrets — use env vars / user-secrets / secret stores. `.env.example` contains placeholders only.
- Public endpoints expose only customer-necessary data. No internal notes, no internal IDs unless required, no customer records.
- Errors use RFC 7807 ProblemDetails with stable `code` and `traceId`. Never leak stack traces or exception messages outside Development.

## 9. API conventions

- Versioned routes: `/api/v1/...` (internal) and `/api/v1/public/{tenantSlug}/{venueSlug}/...` (public).
- REST resources + explicit command endpoints for state transitions (`POST /bookings/{id}/cancel`), never a generic "set status".
- Lists are paginated (`page`, `pageSize` ≤ 100) and return `{ items, page, pageSize, totalCount }`.
- Request DTOs validated with FluentValidation in the application layer before business logic. Validation never replaces DB constraints.
- Every async method accepts and honours a `CancellationToken`.
- Swagger/OpenAPI must reflect the real implementation, including error and 409 responses.

## 10. Frontend conventions

- JavaScript only. Vue 3 Composition API with `<script setup>`.
- Folder layout under `Frontend/BookingPlatform.Web/src`: `boot/ components/ composables/ constants/ layouts/ pages/ router/ services/ stores/ utils/`.
- All HTTP goes through the single Axios instance in `src/services/http.js` (auth header, refresh single-flight, correlation id, error normalisation). Components never call Axios directly; they use services/stores.
- Focused Pinia stores (`auth`, `tenant`, `venue`, `resource`, `booking`, `availability`, `customer`, `floorPlan`, `realtime`). No god store.
- Route guards are UX only; the API is the security boundary.
- Every data view has loading, empty and error states. Destructive actions need confirmation.
- Times are displayed in the **venue** time zone, not the browser's.

## 11. Testing requirements

- Every phase ships tests. A phase is not complete if any test fails.
- Mandatory suites (as their phase arrives): status transitions, operating hours incl. overnight and DST, availability, combinations, validation, migrations-from-empty, tenant isolation, auth, idempotency, outbox, **100-parallel-request concurrency test**, SignalR group isolation and resync.
- Integration tests run against real PostgreSQL/Redis via Testcontainers. Never mock the database to test concurrency or constraints.
- Never weaken a constraint or test to make a test pass.

## 12. Code quality

- Small focused classes, clear names, DI, async/await, explicit errors. No static mutable state, no magic strings (use constants for permissions, error codes, event names).
- No TODO comments as a substitute for implementation. Deferred work is recorded in `docs/DEVELOPMENT_PHASES.md` with a reason.
- No mocked core functionality presented as complete.
- Comments only for constraints the code cannot express.

## 13. Phase workflow

Plan → Review → Approval → Implement → Verify → Document → Report → **STOP**.

- Work only on the currently approved phase (see `docs/DEVELOPMENT_PHASES.md`).
- At the end of a phase: build, run all tests, verify migrations, update docs, produce the phase completion report, then stop and wait for **"PROCEED TO NEXT PHASE"**. Silence is not approval.
- Architecture changes require stopping and presenting: current decision, problem, alternative, pros, cons, migration impact.
- Never claim something works without having run it. Never fabricate build/test output.

## 14. Version control

- Conventional commits (`feat`, `fix`, `chore`, `docs`, `refactor`, `test`) with scope, e.g. `feat(bookings): add transactional booking engine`. Reference GitLab issues as `#<n>`.
- Small, focused commits/MRs per phase. Do not rewrite unrelated code.
- Never delete or overwrite existing work without explicit approval.
