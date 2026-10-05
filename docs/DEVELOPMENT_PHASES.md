# Development Phases

Workflow: Plan → Review → Approval → Implement → Verify → Document → Report → **STOP**.
The next phase starts only after the explicit instruction **"PROCEED TO NEXT PHASE"**.

## Status

| Phase | Name | Status |
|---|---|---|
| 0 | Discovery & Architecture | **Approved** (2026-10-05) — D-002 .NET 10, D-014 AwesomeAssertions, D-018 venue-scoped access |
| 1 | Foundation | Not started |
| 2 | Authentication & Multi-tenancy | Not started |
| 3 | Venues & Resources | Not started |
| 4 | Availability Engine | Not started |
| 5 | Core Booking Engine | Not started |
| 6 | Internal Staff Experience | Not started |
| 7 | Real-time | Not started |
| 8 | Public Booking | Not started |
| 9 | Outbox & Notifications | Not started |
| 10 | Payments (abstractions) | Not started |
| 11 | SaaS Features | Not started |
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

## Phase 2 — Authentication & Multi-tenancy

Build: migrations V002–V005, V023 (audit), V027 (roles/permissions reference data); users, refresh tokens, tenants, memberships, roles, permissions; login/refresh/logout/me/switch-tenant/change-password; JWT + refresh rotation with reuse detection; password hashing; lockout; `ITenantContext` + middleware; permission policies; `IVenueAccessGuard` skeleton; staff/membership endpoints; platform tenant endpoints; dev seeder (Demo Hospitality tenant + dev admin from `DEV_ADMIN_PASSWORD`); login rate limiting; frontend login page, `auth` + `tenant` stores, route guards, tenant switcher.

Acceptance: login/refresh/logout/me work; tenant resolution works; permission checks work; tenant isolation tests (users, memberships, staff endpoints) pass; secrets only from configuration.

## Phase 3 — Venues & Resources

Build: migrations V006–V013; venue assignments for staff (`tenant_user_venues`, D-018) wired into `IVenueAccessGuard`; venues (incl. booking rules, time zone validation against IANA via NodaTime), sections, resource types, resources, floor plans + positions, weekly and special operating hours; `OperatingSchedule` domain logic (overnight, DST); schedule endpoint; audit for all mutations; frontend venue switcher, ResourcesPage, FloorPlanPage (SVG editor: drag, resize, rotate), SettingsPage (hours, special hours); dev seed of Demo Restaurant and Demo Club with sections and resources.

Acceptance: admin can create venue, sections, resources; resources appear on the floor plan; operating hours and overnight hours correct (unit tests incl. DST); tenant isolation tests extended to venues/resources; tests pass.

## Phase 4 — Availability Engine

Build: migrations V014–V020 (customers, bookings, booking_resources, booking_allocations with exclusion constraint, holds, combinations, blocks); combinations CRUD; `AvailabilityCalculator` + `AvailabilityService`; `GET /availability`; AvailabilityPage (staff) showing slots/options. Test data for bookings/holds/blocks inserted via test fixtures (the write API is Phase 5).

Acceptance: availability correctly handles bookings, holds (active/expired), blocks, operating hours, overnight periods, special hours, capacity, sections, combinations — each covered by unit and integration tests; migration tests prove the exclusion constraint rejects overlaps and accepts inactive/adjacent rows.

## Phase 5 — Core Booking Engine

Build: migrations V021, V022, V024, V025, V026; `BookingService` (create, update, move, cancel, confirm, check-in, seat, complete, no-show), holds (create/release/convert), blocks (create/cancel), idempotency, unit of work, status machine enforcement, history/events/audit/outbox writes, 23P01 → 409 mapping, auto-assignment with savepoints, optimistic concurrency, booking reference sequence; expired-hold sweeper in Worker.

Acceptance: **all concurrency tests pass** (100-parallel single resource, combinations, overlap, adjacency, auto-assign, move vs create); database prevents overlapping allocations; idempotency tests pass; no partial bookings; no race-condition workaround replaces database protection.

## Phase 6 — Internal Staff Experience

Build: DashboardPage (today's summary), BookingBoardPage with Timeline / Floor plan / List views, booking drawer (create/edit with guided steps), status actions (confirm, check-in, seat, complete, no-show, cancel with confirmation), move (drag on timeline/floor plan → move endpoint), holds, search and filters, CustomersPage + CustomerDetailsPage, ResourcesPage polish, StaffPage, audit log viewer; customers API; Playwright E2E for staff journeys.

Acceptance: a staff member can operate a venue entirely through the dashboard; E2E staff journeys pass; loading/empty/error states present; responsive on tablet.

## Phase 7 — Real-time

Build: `VenueHub`, join authorization, realtime outbox dispatcher in Api, Redis backplane, event constants, frontend `realtimeService` + `realtime` store with reconnect/resync, connection status indicator.

Acceptance: two browser sessions on the same venue stay synchronised; tenant isolation intact (tests); reconnect triggers resync (tests).

## Phase 8 — Public Booking

Build: public controllers/DTOs, public availability (times only), public holds (optional), public booking via `BookingService`, public confirmation by token, customer cancellation per policy, Redis-backed rate limiting, abuse controls, PublicBookingPage + BookingConfirmationPage (mobile-first), embeddable widget approach decision.

Acceptance: a customer can book without staff access; public booking uses the same engine; double-booking protection proven for public flow (A/B race test); public responses contain no internal data (contract tests).

## Phase 9 — Outbox & Notifications

Build: notification outbox dispatcher in Worker, retries/backoff/dead-letter, `INotificationSender` + `IEmailProvider` abstractions with a development provider (writes to log/mailpit container), confirmation and cancellation templates, reminder scheduling architecture, password reset/email verification flows.

Acceptance: notifications triggered only from committed events; failure never rolls back bookings; retry/dead-letter tested.

## Phase 10 — Payments (abstractions only)

Build: provider-independent `IPaymentProvider`, payment intents, deposits, payment status, refunds, webhook ingestion (authenticated, idempotent, audited), `HELD` booking expiry on unpaid deposit. No concrete provider unless instructed (PayFast, Peach Payments, Ozow later).

## Phase 11 — SaaS Features

Plans, tenant/venue/staff/resource limits, branding, custom booking pages, tenant settings, usage metrics, reporting read models, analytics. Kept outside the core booking domain.

## Phase 12 — Production Hardening

Full review against the checklist in the master specification §67 Phase 12 and the 20 final architectural questions (§100); load/concurrency testing; RLS evaluation (D-024); backups/PITR; runbooks; `DEPLOYMENT.md` finalised.

---

## Deferred items log

| Item | Deferred to | Reason |
|---|---|---|
| Reinstating CANCELLED/NO_SHOW bookings | Phase 6 review | Requires re-allocation semantics; not in initial status model |
| Bookings overrunning closing time / out-of-hours staff bookings | Phase 6 review | Needs explicit product rule |
| Recurring resource blocks | Post-MVP | Not required initially |
| Per-slot pacing / covers limits | Post-MVP | Booking rule extension; engine designed to accept more rules |
| Tenant custom roles | Phase 11 | System roles suffice initially |
| Availability caching | Only if measured need | Correctness first; avoid premature optimisation |
| External API keys for server-to-server integrations | Post-Phase 8 | Public API first |
| Row-Level Security | Phase 12 evaluation | Defense in depth on top of tested tenant scoping |
