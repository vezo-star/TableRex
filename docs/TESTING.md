# Testing

Status: **Phase 1 — harness implemented.** Each phase adds the tests listed for it; a phase is not complete while any test fails.

## 0. Running the suites

| Suite | Command | Needs |
|---|---|---|
| Backend unit | `dotnet test --project Backend/Tests/BookingPlatform.UnitTests` | — |
| Backend integration | `dotnet test --project Backend/Tests/BookingPlatform.IntegrationTests` | Docker running |
| Only non-Docker integration tests | add `--filter-not-trait "Category=Docker"` | — |
| Frontend | `npm test` in `Frontend/BookingPlatform.Web` | — |

- Test projects use **xUnit v3 on Microsoft.Testing.Platform** (`global.json` → `"test": { "runner": "Microsoft.Testing.Platform" }`). The VSTest packages (`Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`) are not used; on the .NET 10 SDK `dotnet test` requires `--project`.
- JUnit for CI is produced natively: `--results-directory <dir> --report-xunit-junit --report-xunit-junit-filename <name>.xml`; Vitest uses its built-in `junit` reporter.
- Integration fixture (`InfrastructureFixture`): one PostgreSQL 18 and one Redis 8 container per test run (xUnit collection fixture "Infrastructure"); migrations are applied once to `booking_template`, and tests get a fresh database via `CREATE DATABASE x TEMPLATE booking_template` (or an empty one for migration-from-empty tests).
- Api tests use `WebApplicationFactory<Program>` in the `Test` environment with in-memory configuration; a test-only controller (`__tests/errors`) is added as an application part to exercise exception → ProblemDetails mapping without shipping test endpoints.
- jsdom lacks `ScreenOrientation` and `ResizeObserver`; `test/vitest/setup.js` provides minimal stubs so Quasar components mount.

## 1. Tooling

| Level | Tools |
|---|---|
| Backend unit | xUnit, AwesomeAssertions (D-014), NSubstitute only where a fake is genuinely needed |
| Backend integration | xUnit + `WebApplicationFactory<Program>` + **Testcontainers** (PostgreSQL 18, Redis) + real migrations |
| Concurrency | Integration harness with many parallel HTTP requests and direct parallel transactions |
| SignalR | `Microsoft.AspNetCore.SignalR.Client` against the test server |
| Frontend unit | Vitest + Vue Test Utils (stores, composables, services, key components) |
| E2E | Playwright against the full stack (Compose infra + Api + Worker + built SPA) |

Requirements: Docker running locally and in CI (GitLab runner with Docker/DinD for Testcontainers).

## 2. Test layout

```
Backend/Tests/
  BookingPlatform.UnitTests/
    Domain/ (BookingStatusMachineTests, OperatingScheduleTests, AvailabilityCalculatorTests, ...)
    Application/ (validators, service orchestration with fakes where useful)
  BookingPlatform.IntegrationTests/
    Infrastructure/ (PostgresFixture: one container per test run, fresh database per test class via template DB)
    Migrations/ Auth/ TenantIsolation/ Venues/ Resources/ Availability/ Bookings/ Concurrency/ Idempotency/ Outbox/ Realtime/
Frontend/BookingPlatform.Web/
  test/vitest/ ...
  e2e/ (Playwright specs)
```

Database isolation: migrations are applied once to a template database; each test class clones it (`CREATE DATABASE x TEMPLATE booking_template`) for speed and independence.

## 3. Unit tests (Domain/Application)

- **Status transitions**: every valid transition in the matrix succeeds; every other (from, to) pair is rejected with `INVALID_STATUS_TRANSITION` (generated exhaustively from the enum).
- **Operating hours**: normal day; multiple periods per day; overnight (Fri 18:00 → Sat 02:00) incl. booking at 01:00 belonging to Friday's business date; closed days; special hours override (closed and custom); DST spring-forward gap and fall-back overlap (using a DST zone such as `Europe/London`) and a non-DST zone (`Africa/Johannesburg`).
- **Availability calculator**: free resources; existing bookings; active vs expired holds; caller's own hold ignored; blocks; cancelled allocations ignored; capacity min/max; sections and resource type filters; combinations (all required members free / one busy); ranking; turnover buffer; lead time; slot generation at period edges.
- **Validation**: party size, duration bounds, date ranges, email/phone formats, idempotency key format.
- **Booking reference** formatting; idempotency request hashing (canonical JSON).

## 4. Integration tests

- **Migrations**: empty database → all migrations apply → schema checks (exclusion constraint exists, extensions present) → re-run is a no-op → app starts → `/health/ready` is healthy.
- **Auth**: login, wrong password, lockout, refresh rotation, refresh reuse detection revokes family, logout, disabled user, `me`, switch-tenant with and without membership.
- **Tenant isolation (mandatory)** — Tenant A and Tenant B with full data; authenticated as Tenant A:
  - read Tenant B venue → 404
  - read Tenant B booking → 404
  - list bookings never contains Tenant B rows (even when filtering by Tenant B venue id)
  - modify Tenant B resource → 404
  - cancel Tenant B booking → 404 and booking unchanged in DB
  - read Tenant B customer → 404
  - create booking using Tenant B resource id / customer id → rejected; nothing written
  - join Tenant B SignalR venue group → fails; no events received
  - forged token with Tenant B `tid` but no membership → 403
  - venue scoping: a HOST assigned only to Venue 1 cannot read/modify Venue 2 of the same tenant (404) and doesn't see it in venue lists; an ADMIN sees both
  - DB-level: direct insert of cross-tenant allocation violates composite FK
- **Permissions**: each role vs representative endpoints (allowed/forbidden).
- **Booking engine**: create (single, combination, auto-assign), validation failures write nothing, move (success, conflict leaves original allocation intact), cancel releases allocation (resource re-bookable), no-show, complete-early truncation, hold → booking conversion, expired hold doesn't block, block prevents booking, all side tables written (history, events, audit, outbox) in the same transaction.
- **Idempotency (mandatory)**: same key + same body twice → one booking, identical response, `Idempotent-Replayed`; same key + different body → 409 `IDEMPOTENCY_CONFLICT`; two concurrent requests with the same key → one booking.
- **Outbox**: message written with booking; rollback writes none; dispatcher marks processed; failure increments attempts with backoff; dead-letter after max; crashed lease reclaimed; notification failure never affects the booking.

## 5. Concurrency tests (mandatory, Phase 5)

1. **100 simultaneous requests, one resource**: same venue/resource/date/time/duration, distinct idempotency keys, released together via a barrier. Expect exactly 1 × 201 and 99 × 409 `RESOURCE_NOT_AVAILABLE`.
2. **Combinations**: 50 requests for combination {5, 6} + 50 requests for table 6 alone at the same time → exactly one success overall, no partial allocations.
3. **Overlapping (not identical) windows** on the same resource (19:00–21:00 vs 20:00–22:00) → one success.
4. **Adjacent windows** (19:00–21:00 and 21:00–23:00) → both succeed.
5. **Auto-assignment**: N concurrent requests for a venue with K suitable tables → exactly min(N, K) successes, each on a distinct table.
6. **Concurrent move vs create** targeting the same slot → one wins, the moved booking never ends without an allocation.

After each test, invariant queries assert:

```sql
-- no overlapping active allocations
SELECT count(*) FROM booking_allocations a JOIN booking_allocations b
  ON a.resource_id = b.resource_id AND a.id < b.id AND a.is_active AND b.is_active AND a.period && b.period;   -- = 0
-- no orphan active booking allocations
SELECT count(*) FROM booking_allocations al JOIN bookings bk ON bk.id = al.booking_id
  WHERE al.is_active AND bk.status IN ('CANCELLED','NO_SHOW');                                                -- = 0
-- every booking has resources and allocations; booking count = success count
```

The Npgsql pool size in tests is raised so the database — not the pool — is the contention point. Constraints are never weakened to make these pass.

## 6. SignalR tests (Phase 7)

Connection with valid token; join own venue; receive BookingCreated/Updated/Cancelled/Moved after commit; no event for rolled-back operations; cross-tenant join rejected; reconnect + rejoin + resync (store-level unit test and Playwright two-session test).

## 7. Frontend tests

- Vitest: `http.js` refresh single-flight and replay, ProblemDetails normalisation, `auth`/`tenant`/`venue` stores, `realtime` store reconnect → resync, `usePermissions`, venue time formatting, booking form validation and idempotency key reuse on retry.
- Playwright E2E (critical journeys): staff login → create booking → see it on the board; move and cancel; two sessions stay in sync (Phase 7); public booking flow on a mobile viewport (Phase 8); double-submit on public booking creates one booking.

## 8. CI (GitLab, from Phase 1)

Stages: `build` (dotnet build, npm ci + build) → `test` (unit, integration with Testcontainers, Vitest) → `e2e` (from Phase 6) → later `package` (container images).
Pipeline fails on any test failure or build warning-as-error in Domain/Application.
