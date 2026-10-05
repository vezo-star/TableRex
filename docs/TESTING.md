# Testing

Status: **Phases 1–10 implemented** (282 backend tests, 210 Vitest tests at the end of Phase 10). Each phase adds the tests listed for it; a phase is not complete while any test fails.

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
- **Notifications (Phase 9, `Notifications/NotificationTests.cs`)**: the Worker's DI graph (`AddInfrastructure` + `AddPublicBookingTokens` + `AddNotifications`, built with `ValidateOnBuild`) against the test database, with a capturing `IEmailProvider`. Covers: public booking → one confirmation with venue-local time, HTML-encoded special requests, Reply-To venue email and a manage link that opens the booking; re-queued message not sent twice; guest cancel → cancellation without a manage link; PENDING → "request received", staff confirm → "confirmed"; staff booking without a manage link; skips for no email, anonymised customer and booking cancelled before sending; transport failure → FAILED with backoff → DEAD after `MaxAttempts`, booking untouched, no delivery record; permanent failure, unknown type and malformed payload dead-lettered at once; password reset end to end (unknown email queues nothing, token only hashed, policy error keeps the link usable, wrong token 400, reset revokes refresh tokens, old password fails, reuse 400, superseded request skipped, expired link 400, per-account throttle).
- **Templates and options (unit, `Notifications/NotificationTemplateTests.cs`)**: local-time rendering, HTML encoding, template choice by type/status, `NotificationOptions.IsValid` (origin shape, provider settings, sender) and recipient masking.

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

The Npgsql pool size in tests is raised (`MaxPoolSize=150`, container `max_connections=400`) so the database — not the pool — is the contention point. Constraints are never weakened to make these pass. Implemented in `Bookings/ConcurrencyTests.cs` (scenarios 1–6; the move-vs-create race runs 5 rounds) with `BookingInvariants` checked after each; `BookingTests` cover the booking-engine and idempotency lists in §4, and `HousekeepingTests` the hold sweep and purges.

## 6. SignalR tests (Phase 7)

Connection with valid token; join own venue; receive BookingCreated/Updated/Cancelled/Moved after commit; no event for rolled-back operations; cross-tenant join rejected; reconnect + rejoin + resync (store-level unit test and Playwright two-session test).

Implemented in Phase 7:
- `Realtime/RealtimeTests.cs` (SignalR client over the TestServer WebSocket): `BookingCreated` after commit with an ids-only payload (no customer data); the losing request of a conflict emits nothing; Updated/Moved/Cancelled delivered and nothing after `LeaveVenue`; NOTIFICATION rows untouched by the Api dispatcher; cross-tenant `JoinVenue` → `FORBIDDEN` and no events; a Host can join only an assigned venue and receives `ResourceUpdated`; an invalid token is refused.
- `Outbox/OutboxDispatcherTests.cs`: success, retry with backoff, dead-letter after max attempts, permanent failure dead-letters at once, expired lease reclaimed, stale rows skipped. `OutboxRetryPolicyTests` (unit): exponential growth, jitter bounds, cap.
- Vitest: `stores/realtime.spec.js` (no connection without a session, connect + join + resync, token refresh near expiry, dedup + other-venue filter, rejoin + resync after reconnect, retry backoff until stopped, venue switch + denied join, hidden-tab resync), `services/realtimeService.spec.js`, `composables/useRealtimeRefresh.spec.js`, plus board/drawer/layout updates (refetch rules, remote-change banner, indicator).
- Two-session browser check done manually (Phase 7); automated as Playwright in Phase 12.

## 6a. Public booking tests (Phase 8)

`PublicBooking/PublicBookingTests.cs` (the `public-booking` per-IP limit is raised to 1000 in this class via `RateLimiting:Policies`):
- **Venue and times without leaks**: response fields are whitelisted (no ids, resources, notes); an offline-only table is not offered; party size / date window validation; unknown, malformed, inactive-venue, online-booking-off and suspended-tenant all give 404 `VENUE_NOT_FOUND`.
- **Book → replay → manage → cancel**: 201 with a manage token; the database row has source `PUBLIC`, no creator, the token's SHA-256 and the online table (T2) when the first table is offline-only; the idempotency row never contains the token; replay returns the same booking and token with `Idempotent-Replayed`; changed body → 409 `IDEMPOTENCY_CONFLICT`; missing key → 400; GET/cancel by `X-Booking-Token` (missing or wrong token → 404); cancel is idempotent; history `PUBLIC` with the guest reason, outbox rows, audit with `user_id NULL`.
- **Venue rules**: off-grid time 400, outside hours 422, party size 400, honeypot 400, invalid email 400, beyond the booking window 400, inside the lead time 422 `OUTSIDE_BOOKING_WINDOW`, fourth active booking for one email 422 `CONTACT_BOOKING_LIMIT` (one customer row, reused case-insensitively), `PENDING` when auto-confirm is off.
- **Cancellation policy**: inside the cutoff → 422 `CANCELLATION_NOT_ALLOWED`; with cutoff 0 a checked-in booking still cannot be cancelled online.
- **A/B race**: 2 guests for the last online table → one 201 and one 409; 20 guests for one slot → exactly one; a staff booking and a guest booking racing for the same table → exactly one; `BookingInvariants` (no overlapping active allocations, no orphans) after each round.

`PublicBookingRateLimitTests` (`public-read` set to 3 per window): the fourth read → 429 `RATE_LIMITED` with `Retry-After`.

Vitest: `services/publicBookingService.spec.js` (anonymous calls, token only in the header, fragment links), `utils/publicBooking.spec.js`, `pages/publicBookingPages.spec.js` (times, closed/empty states, booking and confirmation link, slot taken → times reloaded, idempotency key reused after a network failure, 404 venue, manage page: missing fragment, load + confirm cancel, contact-venue state). Mobile (360 px) browser walkthrough done in Phase 8: book → confirmation → manage link → cancel, and online booking toggled off/on from venue settings; Playwright in Phase 12.

## 6b. Payment tests (Phase 10)

`Payments/PaymentTests.cs` (Simulated provider with a known random webhook secret):
- **Deposit flow**: a deposit venue books as `HELD` with a `CREATED` payment and `canPay`, and nothing is emailed until the deposit is paid. Checkout returns a redirect without the manage token. A forged checkout token gets 404. Completing it confirms the booking (history "Deposit paid.", audit, outbox). A HOST gets 403 on refunds. Then a partial refund, an idempotent replay, the refundable amount and 422 for too much.
- **Webhooks**: missing, wrong and replayed signatures → 401 with nothing written. A wrong amount flags the payment and never confirms. A failure keeps the booking payable. Two different event ids racing for one payment apply once. A redelivery is a no-op, and an unknown payment is recorded as `UNMATCHED`.
- **Expiry**: `DepositExpiryService` releases due `HELD` bookings (allocation reason `EXPIRED`, payment `EXPIRED`, the table bookable again) and leaves unexpired ones. A payment arriving afterwards is recorded but flagged. A guest cancel closes the open payment as `CANCELLED`.

`Payments/PaymentResilienceTests.cs`: an always-failing provider → checkout 503 with the booking still `HELD`; a refund stays `PENDING` and flagged; a second refund and five concurrent ones → 422. With payments off: settings validation, `onlinePaymentsAvailable: false`, a public booking at a deposit venue → 503 with nothing written, and a staff booking is still `CONFIRMED`.

Unit (`Payments/PaymentPrimitivesTests.cs`): deposit amounts per policy, `PaymentOptions` validity, sign/verify round trip, tampering, foreign secret, replay window, malformed payloads, checkout tokens, and `Expire` allowed only from `HELD`.

Vitest: `utils/payments.spec.js` (money parsing without float drift, notices, token parking, fragment parsing, same-origin return URLs) and `components/paymentComponents.spec.js` (pay card, return page, Simulated checkout incl. a cross-origin return URL being ignored, staff payments panel with a partial refund and idempotency key, deposit settings in minor units), plus the deposit cases in `pages/publicBookingPages.spec.js`.

## 7. Frontend tests

- Vitest: `http.js` refresh single-flight and replay, ProblemDetails normalisation, `auth`/`tenant`/`venue` stores, `realtime` store reconnect → resync, `usePermissions`, venue time formatting, booking form validation and idempotency key reuse on retry.
- Playwright E2E (critical journeys): staff login → create booking → see it on the board; move and cancel; two sessions stay in sync (Phase 7); public booking flow on a mobile viewport (Phase 8); double-submit on public booking creates one booking.
- Implemented in Phase 6: `utils/bookings.spec.js` (status actions, timeline geometry, snapping, occupancy, `zonedTimeToUtc` incl. DST gap/overlap), `services/bookingService.spec.js` (idempotency key reuse and retryable failures, status command endpoints with versions, board/holds/customers/audit calls), `components/bookingComponents.spec.js` (create dialog: prefill, hold, stable key, release on back, refresh after a taken table, walk-in rule; drawer: actions by status and permission, concurrency reload, changed-fields move; block dialog: zone conversion past midnight) and `pages/bookingBoardPage.spec.js` (loads the URL date with a selected venue, view switching, view-only users). Playwright lands in Phase 12 (D-059); until then staff journeys are verified by a scripted browser walkthrough.

## 8. CI (GitLab, from Phase 1)

Stages: `build` (dotnet build, npm ci + build) → `test` (unit, integration with Testcontainers, Vitest) → `e2e` (from Phase 6) → later `package` (container images).
Pipeline fails on any test failure or build warning-as-error in Domain/Application.
