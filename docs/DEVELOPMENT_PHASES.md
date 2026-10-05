# Development Phases

Workflow: Plan → Review → Approval → Implement → Verify → Document → Report → **STOP**.
The next phase starts only after the explicit instruction **"PROCEED TO NEXT PHASE"**.

> 2026-10-05 17:04 — the user instructed: continue with all remaining phases without stopping, verify that the app works, run security checks and fix what is broken; review the finished product afterwards. Phases 2+ therefore run back to back; each still goes through Implement → Verify → Document and gets its own commits and completion notes below.

## Status

| Phase | Name | Status |
|---|---|---|
| 0 | Discovery & Architecture | **Approved** (2026-10-05) — D-002 .NET 10, D-014 AwesomeAssertions, D-018 venue-scoped access |
| 1 | Foundation | **Complete** (2026-10-05) |
| 2 | Authentication & Multi-tenancy | **Complete** (2026-10-05) |
| 3 | Venues & Resources | **Complete** (2026-10-05) |
| 4 | Availability Engine | **Complete** (2026-10-05) |
| 5 | Core Booking Engine | **Complete** (2026-10-05) |
| 6 | Internal Staff Experience | **Complete** (2026-10-05) |
| 7 | Real-time | **Complete** (2026-10-05) |
| 8 | Public Booking | **Complete** (2026-10-05) |
| 9 | Outbox & Notifications | **Complete** (2026-10-05) |
| 10 | Payments (abstractions) | **Complete** (2026-10-05) |
| 11 | SaaS Features | **Complete** (2026-10-05) |
| 12 | Production Hardening | Not started |

Every phase's Definition of Done: compiles, all tests pass, migrations apply from empty DB, functionality verified (manual smoke test where applicable), security checks for the phase, tenant isolation tested where relevant, docs updated, no critical TODOs, phase completion report delivered.

---

## Phase 0 — Discovery & Architecture

Deliverables: `AGENTS.md`, `README.md`, all `docs/*.md`, phase plan, risks, open decisions.

Acceptance: architecture, technology decisions, repository structure, workflow, risks and phase plan documented; open decisions answered.

## Phase 1 — Foundation

Build:
- `git` repository, `.gitignore`, `.editorconfig`, `.gitattributes`, `global.json`, `Directory.Build.props` (nullable, warnings-as-errors for Domain/Application, analyzers), `Directory.Packages.props` (central package versions).
- `Backend/BookingPlatform.sln` with Api, Application, Domain, Infrastructure, Worker, Migrations, UnitTests, IntegrationTests.
- Api: Serilog (JSON console, correlation id, trace id), global exception → ProblemDetails, Swagger, `/health`, `/health/ready` (PostgreSQL + Redis), CORS config, security headers, configuration per environment, options validation at startup.
- Worker: host skeleton with graceful shutdown and Serilog.
- Migrations: DbUp runner (`migrate`), `V001__extensions.sql`.
- `docker-compose.yml` (postgres:18, redis:8, healthchecks, named volumes), `.env.example`.
- Frontend: Quasar (Vite) JS app with Pinia, Router, Axios `http.js`, layouts (Auth, Dashboard, Public) as shells, Vitest configured, ESLint + Prettier.
- Tests: unit test project runs; integration test boots Testcontainers PostgreSQL + Redis, applies migrations, hits `/health/ready`.
- `.gitlab-ci.yml`: build + test stages.
- Dockerfiles (api, worker, migrator, web) — build verified.

Acceptance: backend builds; frontend builds; Compose starts PostgreSQL and Redis; Api connects (ready check green); Swagger loads; tests run green; a clean clone can reproduce the environment by following README.

Completion notes (2026-10-05):
- Verified: Release build with 0 warnings; 37 unit tests, 18 integration tests (Testcontainers PostgreSQL 18.6 + Redis 8), 29 Vitest tests; Compose healthy; V001 applied and re-run is a no-op; `/health` 200, `/health/ready` 200 → 503 with Redis or PostgreSQL stopped → 200 after restart; Swagger UI/JSON 200 in Development, 404 in Production; ProblemDetails with `code`, `traceId`, `correlationId`; Worker graceful shutdown on SIGTERM (exit 0); all four images build and smoke-run as non-root; dev proxy and status page verified in a browser; `.gitlab-ci.yml` schema-validated with `gitlab-ci-local`.
- Not verifiable locally: GitLab server-side CI Lint and a real pipeline run (no GitLab remote configured).
- Known limitation: the first readiness probe after start takes 1–2.5 s on the PostgreSQL check (first physical connection and authentication); steady state is a few milliseconds.

## Phase 2 — Authentication & Multi-tenancy

Build: migrations V002–V005, V023 (audit), V027 (roles/permissions reference data); users, refresh tokens, tenants, memberships, roles, permissions; login/refresh/logout/me/switch-tenant/change-password; JWT + refresh rotation with reuse detection; password hashing; lockout; `ITenantContext` + middleware; permission policies; `IVenueAccessGuard` skeleton; staff/membership endpoints; platform tenant endpoints; dev seeder (Demo Hospitality tenant + dev admin from `DEV_ADMIN_PASSWORD`); login rate limiting; frontend login page, `auth` + `tenant` stores, route guards, tenant switcher.

Acceptance: login/refresh/logout/me work; tenant resolution works; permission checks work; tenant isolation tests (users, memberships, staff endpoints) pass; secrets only from configuration.

Completion notes (2026-10-05):
- Verified: Release build with 0 warnings; 62 unit tests, 46 integration tests (Testcontainers), 70 Vitest tests; ESLint + Prettier clean; production SPA build. Integration suites cover login/cookie attributes, generic credential errors, progressive lockout, refresh rotation and reuse detection (family revoked + audited), hash-only token storage, logout (revocation + audit), Origin check, multi-tenant selection and switching, change password, disabled users, rate limiting, cross-tenant 404, forged `tid` → 403, the full role → permission matrix against real endpoints, last-owner protection, admin cannot grant owner, immediate effect of role changes and deactivation, and platform-admin-only tenant administration.
- Migrations V002–V005, V023, V027 applied to the Compose database; `seed-dev` is idempotent.
- Browser smoke test against the running Api + dev server: login → workspace selection → dashboard; staff list; tenant settings; tenant switcher (Manager role hides Settings, direct `/settings` → forbidden page); session survives full page reloads via the refresh cookie; logout revokes the session; wrong password shows a generic error; a single-tenant Host skips workspace selection and is denied `/staff`.
- Fixed during verification: a stale `?redirect=` from a previous session could send the next user straight to a forbidden page — login now only follows redirects the signed-in user may open, and the redirect survives workspace selection. Logout was not audited — added. The per-account login rate-limit key now hashes the email.
- Not built (moved): `IVenueAccessGuard` lands in Phase 3 with venues; `/me.accessibleVenueIds` is `[]` until then. A separate `tenant` store was not needed — the tenant lives in the `auth` store with the session.
- Known limitations: see `SECURITY.md` §12 (cross-instance cache staleness ≤60 s, lockout reveals account existence, no access-token deny-list).

## Phase 3 — Venues & Resources

Build: migrations V006–V013; venue assignments for staff (`tenant_user_venues`, D-018) wired into `IVenueAccessGuard`; venues (incl. booking rules, time zone validation against IANA via NodaTime), sections, resource types, resources, floor plans + positions, weekly and special operating hours; `OperatingSchedule` domain logic (overnight, DST); schedule endpoint; audit for all mutations; frontend venue switcher, ResourcesPage, FloorPlanPage (SVG editor: drag, resize, rotate), SettingsPage (hours, special hours); dev seed of Demo Restaurant and Demo Club with sections and resources.

Acceptance: admin can create venue, sections, resources; resources appear on the floor plan; operating hours and overnight hours correct (unit tests incl. DST); tenant isolation tests extended to venues/resources; tests pass.

Completion notes (2026-10-05):
- Verified: Release build with 0 warnings; 88 unit tests (26 new: `OperatingSchedule` overnight, special closed/custom days, Europe/London spring-forward gap and fall-back overlap, `FindContaining` after midnight, weekly overlap incl. Sunday→Monday wrap; venue/resource/floor-plan validators), 55 integration tests (9 new: venue CRUD + defaults + audit, `SLUG_TAKEN`, cross-tenant 404 for venues/resources/floor plans and cross-tenant resource creation, venue scoping for an assigned Host incl. `/me.accessibleVenueIds`, Host 403 on manage endpoints, capacity/section/type rules and `CODE_TAKEN`, delete-vs-deactivate, floor-plan bounds and foreign-venue rejection, overnight schedule in UTC, special-day conflicts); 111 Vitest tests; ESLint + Prettier clean.
- Migrations V006–V013 applied to the Compose database; the migration test re-applies all scripts from empty. `seed-dev` adds Demo Restaurant (16 resources, lunch + dinner Tue–Sun), Demo Club (7 resources, Wed–Sat 20:00–04:00), Demo Workspace in Demo Coworking (9 rooms/desks, Mon–Fri), each with a floor plan; demo staff are assigned to Demo Restaurant (manager also to Demo Club). Re-running is a no-op.
- Browser smoke test (throwaway admin account): venue list, venue settings with weekly hours (overnight "+1 day") and the 14-day schedule preview, resources per venue with the header venue switcher (selection remembered per tenant), server validation shown on the capacity field, floor plan drag → save → reload keeps the new position, staff venue assignments.
- Fixed during verification: `ON DELETE RESTRICT` raises SQLSTATE 23001, not 23503, so delete-or-deactivate now treats both as "still referenced"; nested validation keys were only partly camel-cased (`rules.MaxPartySize`) — every path segment is now camel-cased.
- Not built (moved): resource combinations are Phase 4 (they belong with availability). The planned "SettingsPage (hours)" lives under Venues → venue settings because hours are per venue.

## Phase 4 — Availability Engine

Build: migrations V014–V020 (customers, bookings, booking_resources, booking_allocations with exclusion constraint, holds, combinations, blocks); combinations CRUD; `AvailabilityCalculator` + `AvailabilityService`; `GET /availability`; AvailabilityPage (staff) showing slots/options. Test data for bookings/holds/blocks inserted via test fixtures (the write API is Phase 5).

Acceptance: availability correctly handles bookings, holds (active/expired), blocks, operating hours, overnight periods, special hours, capacity, sections, combinations — each covered by unit and integration tests; migration tests prove the exclusion constraint rejects overlaps and accepts inactive/adjacent rows.

Completion notes (2026-10-05):
- Verified: Release build with 0 warnings; 104 unit tests (15 new `AvailabilityCalculator` tests: slot grid, half-open adjacency, turnover, capacity range, required/optional combination members, filtered-out members, ranking, overnight business date, previous-period overnight claim, special closed/custom days, earliest start, Europe/London DST gap, invalid queries); 65 integration tests (4 new database tests: overlapping/contained ranges rejected with 23P01, adjacent/inactive/other-resource rows accepted, reactivating an overlapping row rejected, owner-type check, empty range, cross-tenant composite FK, hold token length, two concurrent transactions → second waits then fails with 23P01; 5 API tests: bookings + active holds + blocks occupy, expired holds and cancelled bookings do not, turnover + preferred-time window, combinations CRUD + duplicate name + cross-venue member + availability for large parties + deactivate + delete + audit, section/type filters, closed day/inactive venue/validation/cross-tenant/unassigned Host; 1 seeder test: demo data seeded once and re-run is a no-op); 120 Vitest tests; ESLint + Prettier clean.
- Migrations V014–V020 applied to the Compose database and from empty in the migration test.
- Browser smoke test (throwaway admin): availability for Demo Restaurant shows the Monday closure, Tuesday lunch 12:00–13:00 and dinner 18:00–21:00 slots with smallest tables ranked first; a combination "Tables 6 + 7" created through the Resources page; with a temporary maintenance block on Table 6 (19:00–21:00) a party of 10 is offered the combination only at lunch and 21:00 (the block was cancelled afterwards).
- Decisions: D-042 slot grid, D-043 optional combination members, D-044 preferred-time window, D-045 expired holds ignored at read time.

## Phase 5 — Core Booking Engine

Build: migrations V021, V022, V024, V025, V026; `BookingService` (create, update, move, cancel, confirm, check-in, seat, complete, no-show), holds (create/release/convert), blocks (create/cancel), idempotency, unit of work, status machine enforcement, history/events/audit/outbox writes, 23P01 → 409 mapping, auto-assignment with savepoints, optimistic concurrency, booking reference sequence; expired-hold sweeper in Worker.

Acceptance: **all concurrency tests pass** (100-parallel single resource, combinations, overlap, adjacency, auto-assign, move vs create); database prevents overlapping allocations; idempotency tests pass; no partial bookings; no race-condition workaround replaces database protection.

Completion notes (2026-10-05):
- Verified: Release build with 0 warnings; 220 backend tests green (140 unit — 36 new: status machine transitions, timing resolution incl. overnight/turnover/duration limits/closed days, reference format, hold tokens, phone normalisation; 80 integration — 15 new). `BookingTests` (9): create side effects (history, event, audit without PII, both outbox rows), idempotent replay byte-identical + `IDEMPOTENCY_CONFLICT` + missing/too-long key + 10 concurrent same-key requests → one booking, explicit conflicts with `conflictingResourceIds`, AUTO + combination + party-size errors, the full status machine incl. versions/`If-Match`/no-show-before-start/complete truncation, update + move (conflict keeps the original slot), holds (convert, expired hold ignored, release, replay without token), blocks, tenant isolation + venue scoping + VIEW_ONLY permissions. `ConcurrencyTests` (5): 100 requests → exactly 1 booking; 50 combination + 50 member requests → 1 success, no partial combination; overlapping windows → 1, adjacent windows → all; 10 AUTO requests for 3 tables → exactly 3 on distinct tables; move vs create × 5 rounds. `HousekeepingTests` (1): sweep + purges. Booking invariants (no overlapping active allocations, no active allocation on a cancelled/no-show booking, every active booking allocated) asserted after each.
- Migrations V021, V022, V024–V026 applied to the Compose database. API smoke test (throwaway admin) against Demo Restaurant: AUTO booking → 201 `BK-…` on Table 1; same key → 201 `Idempotent-Replayed: true` with an identical body; history; cancel → version 2; cancel again → 409 `BOOKING_ALREADY_CANCELLED`; unauthenticated → 401; outbox holds `BookingCreated`, `BookingConfirmation`, `BookingCancelled`, `BookingCancellation`.
- Fixed during verification: `jsonb` reordered keys in stored responses (now `json`); stored responses escaped `+` differently from MVC (serializer now mirrors MVC's relaxed encoder); concurrent AUTO requests deadlocked on each other's uncommitted rows under the exclusion constraint and only 1 of 3 tables was booked — fixed with ordered advisory locks per candidate resource (D-047) plus savepoint retry on `40P01`.
- Decisions: D-046 – D-053.
- Not built (moved): the booking UI is Phase 6; outbox dispatchers are Phases 7 and 9 (rows accumulate as PENDING until then).

## Phase 6 — Internal Staff Experience

Build: DashboardPage (today's summary), BookingBoardPage with Timeline / Floor plan / List views, booking drawer (create/edit with guided steps), status actions (confirm, check-in, seat, complete, no-show, cancel with confirmation), move (drag on timeline/floor plan → move endpoint), holds, search and filters, CustomersPage + CustomerDetailsPage, ResourcesPage polish, StaffPage, audit log viewer; customers API; Playwright E2E for staff journeys.

Acceptance: a staff member can operate a venue entirely through the dashboard; E2E staff journeys pass; loading/empty/error states present; responsive on tablet.

Completion notes (2026-10-05):
- Backend: `GET /bookings/board`, customers CRUD + search + bookings + anonymise, audit log with keyset paging (API.md). Release build 0 warnings; 225 backend tests green (5 new `StaffExperienceTests`: customer CRUD/search/duplicate email/bookings, anonymise incl. booking notes, permissions + tenant isolation, board, audit paging + permissions).
- Frontend: dashboard (today's numbers, upcoming arrivals, status breakdown), bookings board with Timeline (sections, closed bands, now marker, drag-to-move with confirmation, double-click to create, auto-scroll), Floor (occupancy colours at a chosen time, table details, create here) and List views; booking drawer (status actions by permission, cancel with reason, edit, move form, history); guided create dialog (availability → table → hold → customer search/new/walk-in → details, idempotent retry); block dialog (venue-zone conversion incl. DST); customers list/details/edit/anonymise; audit log viewer. 152 Vitest tests green (32 new), ESLint and Prettier clean, `quasar build` succeeds.
- Browser walkthrough (throwaway admin, Demo Restaurant): closed Monday shown as closed; Tuesday availability → Table 3 hold ("Held until") → new customer → booking `BK-20261006-000002`; dragged to Table 5 19:30 with confirmation; seated from the drawer; history shows created → moved → seated; list and floor views agree; duplicate customer email offers the existing record; audit log lists the actions.
- Fixed during verification: the board did not load when the venue was already selected on navigation (watcher order); New booking/Block defaulted to today instead of the board date; timeline section labels scrolled away and booking blocks were not keyboard/screen-reader accessible.
- Decisions: D-054 – D-060. Playwright staff journeys are scheduled with the public journeys in Phase 12 (D-059). Realtime refresh replaces polling in Phase 7.

## Phase 7 — Real-time

Build: `VenueHub`, join authorization, realtime outbox dispatcher in Api, Redis backplane, event constants, frontend `realtimeService` + `realtime` store with reconnect/resync, connection status indicator.

Acceptance: two browser sessions on the same venue stay synchronised; tenant isolation intact (tests); reconnect triggers resync (tests).

Completion notes (2026-10-05):
- Backend: `VenueHub` (WebSockets only, join re-checks membership + `booking.view` + venue access, uniform `FORBIDDEN`), `HubErrorFilter`, Redis backplane (`bp-{env}` channel prefix), shared `OutboxStore`/`OutboxDispatcher` (SKIP LOCKED claims, lease, backoff, dead-letter, `LISTEN outbox` + poll fallback) and the Api's `RealtimeOutboxDispatcher`; resource and floor-plan services now emit `ResourceUpdated`/`FloorPlanUpdated`. Release build 0 warnings; 235 backend tests green (new: 4 `RealtimeTests`, 2 `OutboxDispatcherTests` covering success/retry/dead-letter/permanent failure/lease reclaim/stale skip, retry-policy unit tests).
- Frontend: `@microsoft/signalr` client, `realtime` store (reconnect, manual retry, re-join, resync, dedup, hidden-tab resync), `useRealtimeRefresh`, header indicator; board, drawer and dashboard refresh on events and poll only while disconnected. 166 Vitest tests green, ESLint/Prettier clean, `quasar build` succeeds; `npm audit` 0 vulnerabilities.
- Browser check with two sessions on Demo Restaurant: API-created booking appeared on the board ~1.5 s later; a cancel in one tab updated the other; Api stopped → "Reconnecting…"; Api restarted → both tabs re-joined, resynced and received the next booking live. Dev database: stale pre-Phase-7 REALTIME rows skipped as designed, live rows dispatched within 0.5 s, NOTIFICATION rows left for Phase 9. No `access_token` in request logs.
- Fixed during verification: SignalR logged every refused join as an error (now a filter + log override); Pinia proxied the connection object and broke retry identity checks (`markRaw`).
- Decisions: D-061 – D-065.

## Phase 8 — Public Booking

Build: public controllers/DTOs, public availability (times only), public holds (optional), public booking via `BookingService`, public confirmation by token, customer cancellation per policy, Redis-backed rate limiting, abuse controls, PublicBookingPage + BookingConfirmationPage (mobile-first), embeddable widget approach decision.

Acceptance: a customer can book without staff access; public booking uses the same engine; double-booking protection proven for public flow (A/B race test); public responses contain no internal data (contract tests).

Completion notes (2026-10-05):
- Backend: V028 (`public_booking_enabled`, `public_cancellation_cutoff_minutes`); `PublicBookingController` (venue, availability, create, view and cancel by `X-Booking-Token`); `PublicBookingService` on top of the shared `BookingService.CreateCoreAsync`/`TransitionCoreAsync` with online-only allocation; HMAC manage tokens under the new required secret `PublicBooking__TokenKey` (only the hash stored, re-derived on replay); honeypot, slot-grid, lead-time, window and party-size rules; per-email attempt limit and active-booking cap; four configurable per-IP rate-limit policies. Release build 0 warnings; 241 backend tests green (new: 5 `PublicBookingTests` incl. 2-guest, 20-guest and staff-vs-guest races, 1 `PublicBookingRateLimitTests`).
- Frontend: `/book/{tenantSlug}/{venueSlug}` (mobile-first: date, guests, time grid, guest details, honeypot, stable idempotency key, taken-slot recovery, confirmation with private manage link) and `/booking/manage#token` (details, cancel with confirmation, contact-venue state) in `PublicLayout`; venue settings gain "Accept online bookings", "Online cancel cutoff" and the shareable public link. 184 Vitest tests green (18 new), ESLint/Prettier clean, `quasar build` succeeds.
- Browser check at 360 px: Monday shown as closed; Tuesday 19:00 for 2 booked as a throwaway guest → `BK-20261006-000005` confirmed on Table 1 (online table) with source `PUBLIC`; manage link opened, cancel deadline 17:00 shown, cancelled with confirmation (history `PUBLIC`, "Cancelled by the guest online."); turning online booking off in venue settings made the public endpoint 404 and the banner switch; turned back on.
- Not built: public holds (D-068) and an iframe widget (D-072).
- Decisions: D-066 – D-074.

## Phase 9 — Outbox & Notifications

Build: notification outbox dispatcher in Worker, retries/backoff/dead-letter, `INotificationSender` + `IEmailProvider` abstractions with a development provider (writes to log/mailpit container), confirmation and cancellation templates, reminder scheduling architecture, password reset/email verification flows.

Acceptance: notifications triggered only from committed events; failure never rolls back bookings; retry/dead-letter tested.

Completion notes (2026-10-05):
- Backend: V029 (`notification_deliveries`, `password_reset_requests`); `NotificationOutboxDispatcher` in the Worker on the shared outbox core (8 attempts, 2 min lease, no age cut-off); Application `NotificationService` + `EmailTemplates` (BookingConfirmed, BookingRequested for PENDING, BookingCancelled, PasswordReset; plain text + HTML, every user value HTML-encoded); `IEmailProvider` with `SmtpEmailProvider` (SMTP relay or pickup directory, System.Net.Mail — no new package) and `LogEmailProvider` (masked recipient + subject only). Confirming a PENDING/HELD booking now queues a confirmation. Password reset: `POST /api/v1/auth/password/forgot` (always 202) and `/password/reset`; the Worker generates the token when it sends, only its SHA-256 is stored; reset revokes every refresh token and earlier links; changing the password also voids outstanding links. Housekeeping purges expired reset requests (7 days) and delivery records (180 days). Mailpit added to `docker-compose.yml`. Release build 0 warnings; 262 backend tests green (new: 4 `NotificationTests` with the Worker's real DI graph and a capturing provider, 17 template/options/masking unit tests).
- Frontend: "Forgot password?" on the login page, `/forgot-password` (same answer whether or not the account exists) and `/reset-password#token` (token read from the fragment, removed from the address bar, inline policy errors, "request a new link" state). 190 Vitest tests green (6 new), ESLint/Prettier clean, `quasar build` succeeds.
- Real run (dev database, Worker in Development against Mailpit): the backlog of NOTIFICATION rows from Phases 5–8 drained — 4 cancellations sent, 5 stale confirmations skipped with a reason; a new public booking's confirmation arrived in Mailpit in about 2 s with the venue-local date and time, encoded special requests and a manage link that opened the booking at 360 px; the guest cancel produced the cancellation email. Password reset in the browser for a throwaway admin: request → email → link → policy error shown → new password set → old password 401, new 200, link reuse `INVALID_RESET_TOKEN`, 14 refresh tokens revoked, both audit rows written. No tokens, passwords or guest addresses in Api/Worker logs.
- Found and fixed: the Worker could not resolve `IAuditWriter` (no `ICurrentUser`/`IRequestMetadata` outside HTTP) — background implementations are now registered with `TryAdd`; `localhost` SMTP cost ~2 s per message on Windows (IPv6 first, Mailpit bound to 127.0.0.1) — dev config uses `127.0.0.1`.
- Not built: reminders (architecture in D-079), email verification (D-080), SMS.
- Decisions: D-075 – D-081.

## Phase 10 — Payments (abstractions only)

Build: provider-independent `IPaymentProvider`, payment intents, deposits, payment status, refunds, webhook ingestion (authenticated, idempotent, audited), `HELD` booking expiry on unpaid deposit. No concrete provider unless instructed (PayFast, Peach Payments, Ozow later).

Completion notes (2026-10-05):
- Backend:
  - Schema and permissions: V030 (`venue_payment_settings`, `payments`, `payment_refunds`, `payment_webhook_events`; `payment.view` and `payment.refund`).
  - Application: `IPaymentProvider`/`IPaymentProviderRegistry` and `PaymentService` (settings, deposit quote, checkout, webhooks, refunds in three phases). `BookingTransitions` was extracted from `BookingService` so the Api, webhooks and the Worker share one transition path. `DepositExpiryService` runs in the Worker's housekeeping tick.
  - Simulated provider for Development/Test only: HMAC-signed webhooks, a random secret unless configured, and a checkout page in the SPA.
  - Public deposit flow: `HELD` plus a `CREATED` payment in the booking transaction, `POST /public/booking/payment`, and 503 `PAYMENT_UNAVAILABLE` when payments are off.
  - Endpoints: staff payments list and refund, payment settings, and webhooks.
  - Verification: Release build has 0 warnings and all 282 backend tests pass. New: 3 `PaymentTests`, 2 resilience tests, payment unit tests, and the role matrix count.
- Frontend:
  - New screens: a "Deposits" tab in venue settings; a deposit notice and "Continue to deposit" on the public page; `DepositPayCard` on the confirmation and manage pages; `/booking/payment-return` (restores the parked token); `/pay/simulated`; a "Payments" tab in the booking drawer with a refund dialog (idempotency key, partial amounts, unknown-outcome warning).
  - Verification: all 210 Vitest tests pass (20 new); ESLint, Prettier and `quasar build` are clean.
- Browser check (dev stack, throwaway admin and guests):
  - Demo Restaurant set to R50 per guest with a 5-minute window, audited.
  - At 360 px: Tuesday 19:00 for 2 showed the deposit notice and became `BK-20261006-000008` (`HELD`, R100). Pay went to the Simulated checkout; the return URL carried no token.
  - A declined card came back to the manage page as "Payment failed" and still payable. Paying again confirmed the booking (history "Deposit paid.", two webhook events `APPLIED`, audit trail complete), and the confirmation email arrived in Mailpit.
  - Staff refunded R40 of the R100 from the drawer (refund `SUCCEEDED`, `refunded_amount_minor` 4000, audit `payment.refund_requested`/`payment.refunded`).
  - An unpaid second booking (`BK-20261006-000009`) was cancelled by the Worker 30 s after its 5-minute window ("Deposit not paid in time.", allocation released `EXPIRED`, payment `EXPIRED`), and the cancellation email arrived.
- Found and fixed:
  - The Api hung at startup: `Validate<IEnumerable<IPaymentProvider>>` resolved providers while the options factory was being built, and providers depend on `IOptions<PaymentOptions>`. It is now a lazy `IValidateOptions` (`PaymentProviderAvailableValidator`), and the provider reads the base URL per call.
  - `parseSimulatedCheckout` accepted a link without an amount.
  - Expected 503s were logged as unhandled errors with stack traces.
  - API.md claimed staff could create `HELD` bookings.
- Not built: a real provider adapter (Phase 10 instruction), automatic reconciliation of `requires_attention` payments (needs a real provider API), and payment purposes other than deposits.
- Decisions: D-082 – D-087.

## Phase 11 — SaaS Features

Plans, tenant/venue/staff/resource limits, branding, custom booking pages, tenant settings, usage metrics, reporting read models, analytics. Kept outside the core booking domain.

Completion notes (2026-10-05):
- Backend:
  - Schema: V031 (`plans` seeded with STARTER / PROFESSIONAL / ENTERPRISE, `tenant_subscriptions` with per-tenant overrides, `tenant_branding`). Existing tenants were backfilled to PROFESSIONAL.
  - Application: a `Subscriptions` module (`SubscriptionService`, `PlanLimits`) and a `Reports` module (`ReportService`). The booking engine has no dependency on either (D-088).
  - Limits are checked under the tenant row lock (`FOR NO KEY UPDATE`) when venues are created, staff memberships are created or reactivated, and resources are created or reactivated → 422 `PLAN_LIMIT_REACHED` (D-089).
  - Endpoints: plans list, current subscription and usage, branding (422 `PLAN_FEATURE_UNAVAILABLE` without the feature), platform plan and overrides, reports summary and CSV, and the public tenant locations page `GET /public/{tenantSlug}`. Platform tenant creation accepts `planCode`, and the slug `booking` is reserved.
  - Verification: Release build has 0 warnings and all 314 backend tests pass. New: 10 `SaasTests`, including 6 concurrent venue creates at a limit of 1 giving exactly 1 success; 22 unit tests for CSV escaping, logo URL safety, overrides and the range validator.
- Frontend:
  - New screens: a Reports page (KPIs, daily, hour-of-day, status and source breakdowns, deposits, CSV download, 7/30/90-day presets, 366-day guard); "Plan & usage" and "Booking page branding" cards in Settings; a plan column and plan/override dialog for platform admins, plus a plan choice when creating a tenant; the public locations page `/book/{tenantSlug}`; and a branded header and booking terms on the public booking page.
  - The brand colour is applied only when white text on it is readable (contrast ≥ 3:1).
  - Verification: all 242 Vitest tests pass (32 new); ESLint, Prettier and `quasar build` are clean.
- Browser check (dev stack, throwaway admin):
  - Reports for Demo Restaurant (20 Sep – 20 Oct) matched SQL exactly: 8 bookings, 5 cancelled (62.5 %), 3 active with 6 guests, hours 18:00 → 2 and 19:00 → 4, R100 deposit collected and R40 refunded. The CSV had the right headers, `no-store`, 8 rows and no contact details.
  - Settings showed Professional with usage 2/5 venues, 6/25 staff and 16/100 resources. Saving branding was audited as `tenant_branding`.
  - At 360 px, `/book/demo-hospitality` showed the logo, intro, green accent and both venues. The venue page showed the branded header, the link back to the tenant, green time slots and the booking terms in the guest form, with no horizontal overflow.
  - Plan limits: the throwaway account was made platform admin temporarily (revoked afterwards). It set a venue override of 2 in the plan dialog (audited as `tenant_subscription`); "Add venue" then showed "Your Professional plan allows 2 venues. Upgrade the plan to add another venue." Clearing the override stored NULL again.
- Found and fixed:
  - The development Api does not run migrations; V031 had to be applied with the migrator CLI.
  - The public booking page linked to a route that the page tests' router lacked.
  - The public locations test assumed an order among venues the test helper had given identical names; the helper now names venues after their slug.
- Not built: billing, invoices and trials (D-088); materialised reporting read models, because on-demand SQL is enough at this scale (D-090); tenant custom roles (system roles still suffice).
- Decisions: D-088 – D-091.

## Phase 12 — Production Hardening

Full review against the checklist in the master specification §67 Phase 12 and the 20 final architectural questions (§100); load/concurrency testing; RLS evaluation (D-024); backups/PITR; runbooks; `DEPLOYMENT.md` finalised.

---

## Deferred items log

| Item | Deferred to | Reason |
|---|---|---|
| Reinstating CANCELLED/NO_SHOW bookings | Phase 6 review | Requires re-allocation semantics; not in initial status model |
| Bookings overrunning closing time / out-of-hours staff bookings | Phase 6 review | Needs explicit product rule |
| Restricting check-in/seat to the booking's business date | Product decision | D-058; staff may seat early today |
| Recurring resource blocks | Post-MVP | Not required initially |
| Per-slot pacing / covers limits | Post-MVP | Booking rule extension; engine designed to accept more rules |
| Tenant custom roles | Post-MVP | System roles still suffice; not requested in the Phase 11 spec |
| SaaS billing (prices, invoices, trials) | When a billing provider is chosen | D-088: plans are capabilities only; a billing integration writes `tenant_subscriptions` |
| Reporting read models / materialised views | Only if measured need | D-090: on-demand SQL over indexed bookings |
| Availability caching | Only if measured need | Correctness first; avoid premature optimisation |
| External API keys for server-to-server integrations | Post-Phase 8 | Public API first |
| Row-Level Security | Phase 12 evaluation | Defense in depth on top of tested tenant scoping |
| ~~`http.js` auth header and single-flight refresh interceptors~~ | Done in Phase 2 | |
| ~~`Migrations seed-dev` command~~ | Done in Phase 2 | |
| ~~`IVenueAccessGuard` and `/me.accessibleVenueIds`~~ | Done in Phase 3 | |
| ~~Resource combinations CRUD~~ | Done in Phase 4 | |
| ~~Password reset~~ | Done in Phase 9 | |
| Email verification | When self-service sign-up exists | D-080: staff accounts are created by admins |
| Booking reminders (scheduled emails) | Post-MVP | D-079: architecture documented; needs a scheduler table |
| SMS notifications | Post-MVP | Needs a provider decision; `IEmailProvider` pattern applies |
| ~~SignalR JS client (`realtimeService`)~~ | Done in Phase 7 | |
| CI `package` stage (build and push images) | Phase 12 | Needs a registry and hosting decision; images are build-verified locally |
