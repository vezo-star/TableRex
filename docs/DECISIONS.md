# Decision Log

Format: ID — title — status (Accepted / Proposed / **Open**) — context, decision, consequences.
Changing an Accepted decision requires the stop-and-explain process in `AGENTS.md` §13.

## Accepted (from the specification)

**D-001 — Modular monolith.** Accepted. One Api + one Worker process, shared Domain/Application/Infrastructure, single PostgreSQL. Keeps booking writes in one ACID transaction. Modules organised by feature folders for future extraction.

**D-003 — Dapper + Npgsql, no EF.** Accepted. Explicit SQL, parameterized, repositories in Infrastructure; `Npgsql.NodaTime` for time types.

**D-005 — Unified allocation table with partial exclusion constraint.** Accepted (spec §8, §17). Bookings, holds and blocks all claim time through `booking_allocations`; `EXCLUDE USING gist (resource_id WITH =, period WITH &&) WHERE (is_active)`. Only a single table lets the database prevent booking/hold/block overlaps. Inactive rows keep history without blocking. See `DATABASE.md` §4.

## Engineering defaults (approved with the Phase 0 plan)

**D-004 — Migrations with DbUp and plain SQL.** Proposed. DbUp journals applied scripts and runs ordered `.sql` files from `database/migrations`; tiny dependency, no ORM. Alternatives considered: FluentMigrator (C# DSL hides SQL; we want exclusion constraints in plain SQL), Evolve (less actively maintained), Flyway (Java tooling). A separate `BookingPlatform.Migrations` console project runs migrations explicitly in CI/deploy and powers integration tests.

**D-006 — Half-open ranges and turnover buffer.** Proposed. `[start, end)` so back-to-back bookings are valid; allocation end = booking end + venue turnover buffer.

**D-007 — Holds live in PostgreSQL, not Redis.** Proposed. Holds are `HOLD` allocations protected by the same constraint; expired holds are released lazily inside allocating transactions and by a sweeper. Redis would add a second source of truth without improving correctness.

**D-008 — NodaTime for time-zone logic.** Proposed. Spec §6–7 require correct DST, overnight hours and venue-local interpretation; `TimeZoneInfo` makes gap/overlap handling error-prone. NodaTime is a pure library (allowed in Domain). Persistence still uses `timestamptz` in UTC.

**D-009 — PostgreSQL 18 with `uuidv7()`.** Proposed. Time-ordered UUID primary keys with native generation. Fallback if a hosting provider lacks PG 18: PostgreSQL 17 with application-generated UUIDv7.

**D-010 — Tenant-scoped access tokens.** Proposed. Token carries `tid` only after server-side membership verification (login or switch-tenant); permissions resolved server-side per request with ≤60 s cache. Platform admins have no implicit tenant data access. Rejected alternative: client-supplied `X-Tenant-Id` header on every request (easier to misuse).

**D-011 — Refresh tokens in httpOnly cookie, access token in memory.** Proposed. Opaque, hashed, rotated, reuse detection. Requires SPA and API on the same site (dev proxy locally). See `SECURITY.md` §1.

**D-012 — Password hashing via standalone `PasswordHasher<T>`.** Proposed. PBKDF2-HMAC-SHA512 at ≥210k iterations (OWASP guidance), no Identity stores/EF. Argon2id would need a third-party library; can be revisited with automatic rehash-on-login.

**D-013 — No MediatR / AutoMapper.** Proposed. Both moved to commercial licensing in 2025 and add indirection; plain application services and explicit mapping are simpler and testable.

**D-015 — Outbox with per-destination rows.** Proposed. One outbox row per destination (`REALTIME`, `NOTIFICATION`) so each retries independently. Realtime rows are dispatched by the Api (owns the hub; Redis backplane fans out), notification rows by the Worker. `SKIP LOCKED` claiming, `LISTEN/NOTIFY` wake-up with polling fallback, exponential backoff, dead-letter.

**D-016 — Demo data outside migrations.** Proposed. Migrations (V027) seed only reference data (roles, permissions). Demo tenant/venues/resources and the dev admin come from `Migrations seed-dev`, which refuses to run in Production and takes the admin password from `DEV_ADMIN_PASSWORD` (never committed).

**D-017 — Booking reference `BK-{yyyyMMdd}-{n}`.** Proposed. Venue-local business date + global sequence (zero-padded to 6). Not a secret; public access uses a separate random token.

**D-019 — Frontend tooling.** Proposed. Quasar CLI with Vite (`@quasar/app-vite`), JavaScript, ESLint + Prettier, Vitest + Vue Test Utils, Playwright for E2E. SVG for floor plans (no canvas library). `Intl.DateTimeFormat` for venue-time formatting; add Luxon only if arithmetic is needed client-side.

**D-020 — GitLab CI.** Proposed (inferred from the team's GitLab workflow). `.gitlab-ci.yml` with Docker-enabled runner for Testcontainers.

**D-021 — Repository layout.** Proposed. Spec §46 lists both `src/`/`tests/` and `Backend/`/`Frontend/`; to avoid duplicate nesting we use `Backend/` (with `Backend/Tests/`) and `Frontend/` only, plus `database/`, `docker/`, `scripts/`, `docs/`. Adds one project not in spec §47: `BookingPlatform.Migrations` (justified in D-004).

**D-022 — Constraints colocated with tables.** Proposed. Correctness constraints/indexes are created in the same migration as their table; V026 holds secondary/search indexes. Logical order of spec §16 preserved; FKs to later tables added by later migrations.

**D-023 — Optimistic concurrency for booking edits.** Proposed. `version` integer incremented on every booking update; stale updates → 409 `CONCURRENCY_CONFLICT`. Also lets realtime clients discard stale events.

**D-025 — Auto-assignment with bounded retries.** Proposed. Public/AUTO bookings try the best-fit candidate; on `23P01` roll back to a savepoint and try the next (max 5) before returning 409. Database remains the arbiter.

**D-026 — Explicit FluentValidation in the application layer.** Proposed. Validators invoked by application services (or an endpoint filter) rather than the deprecated `FluentValidation.AspNetCore` auto-validation.

**D-027 — OpenAPI via Swashbuckle.** Proposed. Swagger UI enabled in Development/Test; JWT scheme, ProblemDetails and `Idempotency-Key` documented.

**D-024 — PostgreSQL Row-Level Security.** Deferred to Phase 12 evaluation as defense in depth; primary isolation is tenant-scoped SQL + composite FKs + tests.

## Resolved with the user (2026-10-05, Phase 0 approval)

**D-002 — .NET 10 (LTS).** Accepted — approved change from the spec's ASP.NET Core 8.
- Reason: .NET 8 LTS reaches end of support on 10 Nov 2026, about five weeks after project start; .NET 10 LTS is supported to Nov 2028. SDK 10.0.400 is installed; no .NET 8 SDK was present.
- Consequences: all projects target `net10.0`; `global.json` pins SDK 10.0.x (roll forward within feature band); `Guid.CreateVersion7` available in C#. Library choices unchanged.

**D-014 — AwesomeAssertions for test assertions.** Accepted.
- Reason: FluentAssertions v8+ requires a paid commercial licence (Xceed, Jan 2025). AwesomeAssertions is the Apache-2.0 community fork of FluentAssertions 7 with a near-identical API, so the spec's intent (fluent assertions) is preserved.
- Consequences: FluentAssertions v8+ is prohibited (`AGENTS.md`).

**D-018 — Venue-scoped staff access.** Accepted.
- `tenant_user_venues` (created in V006 with venues). TENANT_OWNER/TENANT_ADMIN implicitly access all tenant venues; MANAGER, HOST, STAFF, VIEW_ONLY access only assigned venues. Enforced by `IVenueAccessGuard` and SignalR `JoinVenue`; inaccessible venues return 404 and are omitted from lists. Covered by the tenant isolation test suite.

## Risk register

| # | Risk | Impact | Mitigation |
|---|---|---|---|
| R1 | Exclusion constraint misconfigured (e.g. forgets `is_active` partial, wrong bounds) | Double bookings or false conflicts | Migration tests assert constraint definition; concurrency + adjacency tests |
| R2 | Expired holds blocking bookings | Lost bookings | Lazy release in transaction + sweeper + tests |
| R3 | Deadlocks with multi-resource combinations under load | 500s / latency | Ordered inserts, single retry, map to 409, load test |
| R4 | Tenant leak via a missed `tenant_id` filter | Critical data breach | Composite FKs, mandatory isolation test suite, code review checklist, RLS evaluation |
| R5 | DST/overnight bugs in availability | Wrong slots, bookings at wrong times | NodaTime, explicit business-date model, DST unit tests |
| R6 | .NET 8 end of support (D-002) | Unsupported runtime in production | Resolved: targeting .NET 10 LTS |
| R7 | Package licensing changes (FluentAssertions, MediatR, AutoMapper precedent) | Legal/cost exposure | Avoid those libs; review licenses when adding packages |
| R8 | Hosting provider lacks PG 18 / `btree_gist` | Deployment blocked | Fallback PG 17 + app UUIDv7; verify provider in Phase 12 planning |
| R9 | Idempotency key reuse across callers | Wrong response returned | Scope keys by operation + caller; request hash check |
| R10 | Public booking abuse (bots holding inventory) | Inventory starvation | Rate limits, short hold TTL, per-contact caps, optional bot challenge |
| R11 | Realtime missed events | Stale boards | Resync on reconnect/visibility, version checks, outbox at-least-once |
| R12 | Testcontainers needs Docker in CI and locally | Tests can't run | GitLab runner with Docker; Docker Desktop must be running locally |
| R13 | Scope creep (payments/SaaS) before engine is proven | Unstable core | Phase gates; payments only after Phase 5–9 stable |
