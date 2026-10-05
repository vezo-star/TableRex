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
- Consequences: all projects target `net10.0`; `global.json` requires SDK 10.0.400 or later within 10.0 (`rollForward: latestFeature`, no prereleases), so CI images with a newer 10.0 feature band work; `Guid.CreateVersion7` available in C#. Library choices unchanged.

**D-014 — AwesomeAssertions for test assertions.** Accepted.
- Reason: FluentAssertions v8+ requires a paid commercial licence (Xceed, Jan 2025). AwesomeAssertions is the Apache-2.0 community fork of FluentAssertions 7 with a near-identical API, so the spec's intent (fluent assertions) is preserved.
- Consequences: FluentAssertions v8+ is prohibited (`AGENTS.md`).

**D-018 — Venue-scoped staff access.** Accepted.
- `tenant_user_venues` (created in V006 with venues). TENANT_OWNER/TENANT_ADMIN implicitly access all tenant venues; MANAGER, HOST, STAFF, VIEW_ONLY access only assigned venues. Enforced by `IVenueAccessGuard` and SignalR `JoinVenue`; inaccessible venues return 404 and are omitted from lists. Covered by the tenant isolation test suite.

## Phase 1 implementation decisions (2026-10-05)

**D-028 — Custom health checks.** Accepted. PostgreSQL (`SELECT 1` via the shared `NpgsqlDataSource`) and Redis (`PING` on the shared multiplexer) checks are ~30 lines each on top of the built-in ASP.NET Core health checks, so the `AspNetCore.HealthChecks.*` packages are not added. Failures report a one-line description without exception details or configuration values, and are not logged with stack traces per probe.

**D-029 — Microsoft.Testing.Platform test runner.** Accepted. The .NET 10 SDK no longer runs xUnit v3 through VSTest by default; `global.json` opts into Microsoft.Testing.Platform, removing `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio`. JUnit output is native (`--report-xunit-junit`), so no logger package is needed.

**D-030 — Quasar app-vite v3.** Accepted. The Phase 1 plan named app-vite v2; v3 (3.10.x) is the current stable release supported by `create-quasar`. Consequences: imports use the `@/` alias; client env vars are exposed via `build.env.clientPrefix` (`VITE_`) as `import.meta.env.VITE_*`.

**D-031 — PostgreSQL GSS encryption disabled by default.** Accepted. Npgsql's default `GssEncryptionMode=Prefer` probes for `libgssapi_krb5`, which the .NET runtime images lack, logging an error on each process start. The Infrastructure data source and the migrator set `Disable` unless the connection string specifies a mode. The platform uses password (SCRAM) authentication; Kerberos is out of scope.

**D-032 — Structured console logging switch.** Accepted. `LogFormat` (`json` | `text`) selects compact JSON (default, containers) or a readable text template (Development). Serilog is registered with `preserveStaticLogger: true`, so no global static logger exists; request logging uses the DI logger.

## Phase 2 implementation decisions (2026-10-05)

**D-033 — BCL date/time types in persistence; NodaTime in Domain only (amends D-003).** Accepted.
- Context: D-003 planned `Npgsql.NodaTime` so NodaTime types flow through Dapper. Dapper does not use Npgsql's type-mapping plugin for parameters or materialisation, so each NodaTime type would still need a custom Dapper handler, and the plugin changes global data-source behaviour.
- Decision: rows use `DateTime` (UTC, `timestamptz`), `DateOnly` (`date`) and `TimeOnly` (`time`) with small Dapper handlers (`DapperConfiguration`). Domain code (`TimeZones`, later operating-hours/availability calculators) converts to NodaTime at the boundary. `Npgsql.NodaTime` is not added.
- Consequences: one fewer package; SQL parameters are plain BCL types; conversions are explicit in the services that do venue-local maths.

**D-034 — Cross-tab refresh serialisation with the Web Locks API.** Accepted. Refresh tokens rotate on every use, so two tabs refreshing at once would present the same token twice and trip reuse detection (revoking the session). The SPA takes a `navigator.locks` lock (`bp-auth-refresh`) around refresh and re-reads state after acquiring it; logout and tenant switches are broadcast on `BroadcastChannel('bp-auth')`. Falls back to in-tab single-flight where Web Locks is unavailable.

**D-035 — Only owners manage the owner role.** Accepted. `staff.manage` (OWNER and ADMIN) allows role changes, but granting or removing `TENANT_OWNER` requires the caller to be an owner (403 otherwise), so an admin cannot escalate to owner or remove an owner. The last active owner cannot be demoted or deactivated (422 `LAST_OWNER`); the check locks the tenant row (`SELECT … FOR UPDATE`) to serialise concurrent changes.

**D-036 — Per-request authorization from the database, short in-process cache.** Accepted (implements D-010). Permissions are never in the JWT. `TenantContextMiddleware` loads user flags and the membership's roles/permissions via `IMembershipRepository`, cached in `IMemoryCache` for 60 s (10 s for negative results) and invalidated locally on role/status changes. Cross-instance staleness is bounded by the TTL (documented in `SECURITY.md` §12). A Redis-backed invalidation channel can be added later without API changes.

**D-037 — Application-level rate limiter on Redis.** Accepted. ASP.NET Core's built-in rate limiter is per-process; limits must hold across instances, so `IRateLimiter` uses a Redis fixed window (Lua `INCR` + `PEXPIRE`) and falls back to an in-memory window when Redis is unavailable (availability over strictness for auth endpoints). No new package.

## Phase 3 implementation decisions (2026-10-05)

**D-038 — Delete-or-deactivate via savepoint.** Accepted. Sections and resources referenced by other rows (resources, later bookings) must keep their history. Deletes run inside a savepoint (`DbSession.TryDeleteAsync`); a foreign-key violation (SQLSTATE 23503, or 23001 for `ON DELETE RESTRICT`) rolls back to the savepoint and the row is deactivated instead, in the same transaction. The API returns `{ deleted, deactivated }` so the UI can tell the user what happened. No pre-check query, so there is no check-then-act race.

**D-039 — Times of day as `HH:mm`.** Accepted. `TimeOnly` is serialised as `"HH:mm"` (seconds only when non-zero) and accepted as `HH:mm` or `HH:mm:ss`, matching `<input type="time">`. Operating hours must be whole minutes.

**D-040 — Special hours are per-period rows that replace the weekly schedule for that date.** Accepted. A date either has one closed row or one or more custom periods; mixing is rejected (409) and custom periods may not overlap (400). The venue row is locked while validating so concurrent edits cannot create a mix. A business date is the local date a period opens on (D-012); `OperatingSchedule` resolves DST gaps forward and overlaps to the earlier offset (NodaTime lenient resolver).

**D-041 — Selected venue is client state.** Accepted. The header venue switcher stores the selection per tenant in `localStorage` (`bp-venue:{tenantId}`); the server never trusts it — every venue-scoped request passes the venue id and is checked by `IVenueAccessGuard`.

**D-042 — Availability slot grid.** Accepted. Candidate starts are aligned to each operating period's opening time and step by `slot_interval_minutes` in real (UTC) time, so a DST gap shortens the grid instead of producing duplicate or non-existent local times. The guest duration `[t, t + duration)` must fit the period; turnover is added only for the conflict check (it may run past closing). Staff searches omit slots that start before now; public searches (Phase 8) will use now + lead time.

**D-043 — Combination members may be optional.** Accepted. `resource_combination_members.is_required` (default true). A combination is offered when every required member is free; optional members are included in the allocation only when they are free too. A combination whose required member is excluded by a section or type filter is not offered.

**D-044 — Preferred time narrows, it does not rank.** Accepted. `GET /availability?time=HH:mm` returns slots within ±90 minutes of the preferred start, in time order; the client decides how to present "closest" alternatives. Options inside a slot are always ranked: single resources, then smallest sufficient capacity, then sort order, then name.

**D-045 — Expired holds are ignored at read time.** Accepted. Availability treats `HOLD` allocations with `expires_at <= now` as free even if the sweeper has not yet deactivated them; the allocating transaction (Phase 5) deactivates them before inserting so the exclusion constraint never sees a stale hold as a conflict.

**D-046 — Idempotency is scoped per caller and stores exact bytes.** Accepted. Scopes are `booking.create:user:{userId}` and `hold.create:user:{userId}` (public scopes per venue in Phase 8). The key row is the first write of the business transaction, so a failed operation leaves no key behind. `response_body` is `json`, not `jsonb`, and is serialised with MVC's settings, so a replay is byte-identical. Hold tokens are never stored in plain text, so a replayed hold returns `token: null`; the client must keep the token from the first response.

**D-047 — Advisory locks queue allocation contention; the exclusion constraint still decides.** Accepted. Measured: with only savepoints, 10 concurrent AUTO requests for 3 tables yielded 1 booking — the inserters deadlocked on each other's uncommitted rows until retries ran out. Each allocating transaction now takes `pg_advisory_xact_lock` on all candidate resources in id order, once, before inserting. Same-resource writers serialise for a few milliseconds; different resources are unaffected. Hash collisions only add serialisation. Correctness never depends on the lock.

**D-048 — AUTO allocation tries ranked options inside savepoints.** Accepted. Up to 20 options from `AvailabilityCalculator.OptionsAt` (same ranking as availability, D-042) are tried in order, each insert in a savepoint. No option can ever seat the party → 422 `PARTY_SIZE_NOT_SUPPORTED`; all fitting options taken → 409 `RESOURCE_NOT_AVAILABLE`. Explicit `RESOURCES`/`COMBINATION` requests yield exactly one candidate and return `conflictingResourceIds` on 409.

**D-049 — Staff create reuses a customer by email.** Accepted. `customer.customerId` selects an existing customer; otherwise new details are matched tenant-wide by email (case-insensitive) and the existing record is reused instead of creating a duplicate; with no email (or no match) a new customer is created. `WALK_IN` bookings may omit the customer.

**D-050 — Staff bookings stay inside one operating period and cannot end in the past.** Accepted. A start/end that does not fit one period of the business date (including its overnight period) → 422 `OUTSIDE_OPERATING_HOURS`; ending before now → 422 `INVALID_BOOKING_TIME`. Public limits (lead time, advance days, max party) are not applied to staff (DOMAIN_MODEL §5). Out-of-hours staff bookings remain a deferred product question.

**D-051 — Complete truncates, no-show waits for the start.** Accepted. `complete` keeps the allocation active but truncates it to `max(now + turnover, start + 1 min)` so the table frees up early without breaking the range check. `no-show` before the start time → 409 `INVALID_STATUS_TRANSITION`. Cancel and no-show deactivate allocations.

**D-052 — Outbox payloads carry identifiers only.** Accepted. REALTIME messages carry `{ eventId, type, venueId, entityId, version, businessDate, occurredAt }`; NOTIFICATION messages carry the booking id and notification type, and the Phase 9 dispatcher loads current data at send time. No PII, notes or tokens in the outbox. `BookingConfirmation` is queued only when the booking has a customer and is CONFIRMED or PENDING.

**D-053 — Housekeeping runs in the Worker on fixed intervals.** Accepted. The hold sweep (every 30 s, batches of 500, `FOR UPDATE SKIP LOCKED`) deactivates expired hold allocations, marks the holds EXPIRED and queues `HoldExpired`. An hourly purge deletes expired idempotency keys, refresh tokens 30 days past their family expiry, and PROCESSED outbox rows older than 30 days. DEAD outbox rows are kept for review.

## Phase 6 implementation decisions (2026-10-05)

**D-054 — Customers are tenant-wide.** Accepted. A customer record is shared by all venues of a tenant (one guest, one profile). `bookingCount`/`lastBookingAt` therefore cover every venue, while `GET /customers/{id}/bookings` only lists bookings at venues the caller can access (D-018). Email is unique among active customers per tenant (case-insensitive) → 409 `EMAIL_TAKEN` with the existing `customerId` so the UI can link to it.

**D-055 — Anonymisation erases PII but keeps booking facts.** Accepted. `POST /customers/{id}/anonymize` sets the first name to "Anonymised", clears last name, email, phone, notes and preferences, withdraws marketing consent, stamps `anonymized_at`, and clears `special_requests`/`internal_notes` on that customer's bookings (bumping their version). Booking times, party sizes and statuses stay for reporting. Anonymised customers cannot be edited (422 `CUSTOMER_ANONYMIZED`) or attached to new bookings (404). Customer audit rows record changed field names only, never values.

**D-056 — The board is one request per day; polling until realtime.** Accepted. `GET /bookings/board` returns the schedule, every booking of the business date (capped at 1000, `truncated` flag) and overlapping active blocks, so the timeline, floor and list views share one payload. Until Phase 7 the board and dashboard refresh every 60 s while the tab is visible (the board skips refreshes while the drawer or create dialog is open), and after every local change.

**D-057 — Drag-to-move is limited to single-resource bookings and always confirms.** Accepted. Dragging snaps to the venue slot interval, keeps the board's business date, and sends `time` and/or `allocation: RESOURCES [target]` with the booking `version`; the server stays the authority (409 conflicts are shown and the board reloads). Multi-resource and combination bookings move through the drawer's move form. The floor view selects and creates; it does not drag bookings.

**D-058 — Check-in and seat are not time-restricted.** Accepted for now. Staff can check in or seat a confirmed booking at any time (early arrivals, corrections); only no-show waits for the start (D-051). Whether to restrict seating to the business date is a product question tracked in the deferred list.

**D-059 — Staff journeys are verified by component tests and a scripted browser walkthrough in Phase 6; the Playwright suite lands in Phase 12.** Accepted. Phase 6 adds Vitest component tests for the create dialog (hold, stable idempotency key, refresh after a taken table), drawer (actions by permission, concurrency reload, move payload), block dialog (zone conversion) and board page, and a browser walkthrough (create → drag-move → seat → history → customers → audit). Playwright (`@playwright/test`, a dev dependency) is added in Phase 12 to cover staff and public journeys together against the composed stack.

**D-060 — Audit log uses keyset pagination.** Accepted. Newest first by `(created_at, id)`; `nextCursor` is an opaque base64url `ticks_id` token passed back as `before`. Offsets are avoided because the table only grows and is read newest-first.

## Phase 7 implementation decisions (2026-10-05)

**D-061 — One dispatcher core for both outbox destinations.** Accepted. `OutboxStore` (claim / processed / failed) and the abstract `OutboxDispatcher` live in Infrastructure; the Api hosts the REALTIME dispatcher (it owns the hub), the Worker will host the NOTIFICATION dispatcher (Phase 9). Claims use `FOR UPDATE SKIP LOCKED` with a 30 s lease, so several instances never send the same row concurrently and a crashed instance's rows are reclaimed. Retries back off `2^n` s ± 20 % (max 15 min), 5 attempts, then `DEAD`. This required `Microsoft.Extensions.Hosting.Abstractions` in Infrastructure (Microsoft, same version train as the framework) for `BackgroundService`.

**D-062 — Stale realtime events are skipped, not replayed.** Accepted. A REALTIME row older than 300 s when claimed is marked PROCESSED with `last_error = 'Skipped: …'`. Clients always resync from REST after a reconnect, so delivering minutes-old "something changed" hints adds load without information. NOTIFICATION rows are never skipped.

**D-063 — Realtime is a hint; polling is the fallback only.** Accepted. Pages refresh on matching events (debounced 300 ms) and on resync; the 60 s polling from D-056 now runs only while the realtime status is not connected. The REST API remains the only source of state.

**D-064 — The drawer never overwrites an in-progress edit.** Accepted. A newer event for the open booking reloads the drawer in view mode; while the user is editing or a request is in flight it shows a "changed by someone else" banner with Reload / Keep editing. Saving with the stale version still fails with 409 from optimistic concurrency.

**D-065 — WebSockets only, no negotiation.** Accepted. Client uses `skipNegotiation` + WebSockets; the server only enables WebSockets. No sticky sessions are needed behind a load balancer, and the token travels once in the WebSocket upgrade URL (`access_token`, accepted only on `/hubs/*`, never logged). Long polling/SSE can be enabled later together with sticky sessions if a client environment requires it.

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
