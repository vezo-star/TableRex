# Database

Status: **Phases 1–12 implemented (V001–V031; Phase 12 added no schema changes, only the app role, D-095).** SQL below is illustrative; the migrations are the source of truth once written, and this document is updated to match them.

## 1. Platform

- PostgreSQL **18** (native `uuidv7()`; time-ordered UUIDs give better B-tree locality than random v4).
- Extensions (V001): `btree_gist` (scalar equality inside GiST exclusion constraints), `pg_trgm` (customer name search), `citext` (case-insensitive emails).
- Naming: `snake_case` tables (plural) and columns; constraint prefixes `pk_`, `fk_`, `uq_`, `ck_`, `ex_`, `ix_`.
- All instants: `timestamptz`, written as UTC. Local wall-clock concepts use `date` / `time` plus the venue's IANA `time_zone`.
- Data access: Npgsql + Dapper with explicit SQL; Npgsql.NodaTime plugin maps `Instant`/`LocalDate`/`LocalTime`.

## 2. Migrations

- Tool: **DbUp** (`dbup-postgresql`), scripts embedded from `database/migrations/` into `BookingPlatform.Migrations`.
- File naming: `V001__extensions.sql`, `V002__tenants.sql`, ... applied in lexical order; journal table `public.schema_versions`. Each script runs in its own transaction.
- **Immutable once merged** to main. Corrections are new migrations.
- Execution: `dotnet run --project Backend/BookingPlatform.Migrations --launch-profile migrate -- migrate` locally, or the `migrator` image (`docker run --rm -e ConnectionStrings__Postgres=... bookingplatform/migrator`). Exit codes: 0 success (including "nothing to apply"), 1 failure or missing connection string, 2 unknown command. The Api may auto-migrate only in Development when `Database__AutoMigrate=true`. Production runs the migrator as an explicit deployment step/job.
- Script names are validated (`V###__lower_snake.sql`, unique version numbers) by a unit test, so a misnamed file fails CI rather than being silently skipped.
- Implemented: **V001__extensions** (Phase 1); **V002–V005, V023, V027** (Phase 2); **V006–V013** (Phase 3); **V014–V020** (Phase 4); **V021, V022, V024–V026** (Phase 5); **V028** (Phase 8); **V029** (Phase 9); **V030** (Phase 10); **V031** (Phase 11).
- Dev data: `--launch-profile seed-dev` (C# seeder, idempotent; runs migrations first; creates Demo Hospitality and Demo Coworking, the dev admin as platform admin + owner of both, and manager/host/staff/viewer users in Demo Hospitality; all passwords come from `DEV_ADMIN_PASSWORD`; refuses when `DOTNET_ENVIRONMENT=Production`). Phase 3 adds Demo Restaurant and Demo Club (Demo Hospitality) and Demo Workspace (Demo Coworking) with sections, resource types, resources, weekly hours and a floor plan each; a venue's contents are only seeded when the venue itself is created, so edits made in the app survive re-runs. Phase 4 adds a "Tables 4 + 5" combination to a newly created Demo Restaurant.
- Out-of-order numbers: DbUp applies every script missing from the journal, sorted by name, so a database that already has V027 still receives V006–V022 later. Consequently a lower-numbered migration added later must not depend on data or objects from a higher-numbered one that was applied earlier, and vice versa.

### Phase 2 schema notes

- `tenants.slug` is unique, lower-case `[a-z0-9-]`, ≤63 chars, immutable via the API; `status` ACTIVE / SUSPENDED / ARCHIVED; `default_time_zone` IANA id (validated in the application with NodaTime).
- `users.email` is `citext` and unique (queries compare `@email::citext`); `status` ACTIVE / LOCKED / DISABLED; lockout columns `failed_login_count`, `lockout_until`.
- `refresh_tokens.token_hash` is `bytea` with `CHECK (octet_length = 32)` and unique; `family_id` groups a rotation chain; `replaced_by_id` self-FK is `ON DELETE SET NULL` so batch purges never hit the self-reference; `revoked_at` and `revoked_reason` are both null or both set.
- `tenant_users` is unique on `(tenant_id, user_id)` and exposes `(tenant_id, id)` for composite FKs; `tenant_user_roles` references it via `(tenant_id, tenant_user_id)` so a role assignment can never point at another tenant's membership.
- `roles.tenant_id` is null for system roles (partial unique indexes for system vs tenant role codes); `permissions.code` is the PK with format `area.action`.
- `audit_logs` is append-only: a trigger raises on `UPDATE`/`DELETE`. Retention purges (Phase 12) must run as a privileged maintenance job that disables the trigger explicitly.

### Phase 3 schema notes

- `venues`: unique `(tenant_id, slug)`; booking rules are columns with CHECKs mirroring the validators (slot interval in 5/10/15/20/30/60, min ≤ default ≤ max duration ≤ 1440, party size 1–1000, hold TTL 60–3600 s). `time_zone` is an IANA id validated by the application.
- `tenant_user_venues(tenant_id, tenant_user_id, venue_id)` — venue assignments for D-018; both FKs are composite with `tenant_id` and `ON DELETE CASCADE`.
- Every child table exposes `(tenant_id, venue_id, id)` and references its parent with the full composite key, so a resource can only sit in a section of the **same venue**, and a floor-plan position can only reference a resource of the plan's venue — enforced by the database, not just the service.
- `bookable_resources`: unique `(venue_id, code)`; `0 < min_capacity ≤ capacity ≤ 1000`; `attributes jsonb` must be an object. Section/type/venue FKs are `ON DELETE RESTRICT` (delete-or-deactivate, D-038).
- `floor_plan_resources`: PK `(floor_plan_id, resource_id)`; numeric(8,2) geometry, rotation `[0, 360)`, shape RECT/ROUND; cascades when the plan or resource is deleted.
- `operating_hours`: ISO `day_of_week` 1–7, `open_time <> close_time` (close ≤ open = overnight). `special_operating_hours`: one row per period; `is_closed` ⇔ both times null.

### Phase 4 schema notes

- `customers`: tenant-wide; `email citext`, `phone_normalized` (`^\+?[0-9]{4,20}$`), `preferences jsonb` object, `anonymized_at` for erasure; trigram GIN index on first + last name.
- `bookings`: `reference` unique and `^BK-[0-9]{8}-[0-9]{6,}$` from `booking_reference_seq`; status and source CHECKs; `HELD ⇒ expires_at`; `CANCELLED ⇔ cancelled_at`; `public_token_hash` 32 bytes, unique; `version` for optimistic concurrency; exposes `(tenant_id, venue_id, id)`.
- `booking_resources`: PK `(booking_id, resource_id)`; composite FKs to the booking and resource of the **same venue**; `combination_id` FK added in V019.
- `booking_allocations`: exactly as §4.1 — generated `period tstzrange '[)'`, owner CHECK (one of booking/hold/block matching `allocation_type`; HOLD needs `expires_at`), `is_active ⇔ released_at IS NULL`, `release_reason` enum, and `ex_booking_allocations_no_overlap EXCLUDE USING gist (resource_id WITH =, period WITH &&) WHERE (is_active)`. GiST `(venue_id, period) WHERE is_active` serves availability reads. The hold and block FKs are added by V018/V020.
- `resource_holds`: `token_hash` 32 bytes, unique; owner USER/PUBLIC; status ACTIVE/CONVERTED/RELEASED/EXPIRED with `CONVERTED ⇔ converted_booking_id`.
- `resource_combinations` + `resource_combination_members(is_required)`: unique `(venue_id, name)`; members reference resources of the same venue; members cascade with the combination; combinations referenced by bookings are deactivated, not deleted.
- `resource_blocks`: reason enum (MAINTENANCE, PRIVATE_EVENT, CLEANING, MANAGEMENT, UNAVAILABLE, STAFF_USE); `CANCELLED ⇔ cancelled_at`.
- Verified by `AllocationConstraintTests`: overlapping and contained ranges → 23P01; adjacent, inactive and other-resource rows accepted; reactivating an overlapping row → 23P01; a second concurrent transaction waits for the first and then fails with 23P01.

### Phase 5 schema notes

- `booking_status_history`: `from_status` (null for the initial status), `to_status`, `source` STAFF/PUBLIC/SYSTEM, `reason` ≤500, `changed_by_user_id`; composite FK to `bookings (tenant_id, venue_id, id)`; index `(booking_id, changed_at)`.
- `booking_events`: `event_type` CREATED/UPDATED/MOVED/STATUS_CHANGED/CANCELLED/NOTE_ADDED, `data jsonb` object (changed fields only — never customer PII or internal notes), `actor_user_id`; index `(booking_id, occurred_at)`.
- `outbox_messages`: `tenant_id` nullable (platform messages); destination REALTIME/NOTIFICATION; status PENDING/PROCESSING/PROCESSED/FAILED/DEAD with `PROCESSED ⇔ processed_at`; partial pending index `(destination, available_at) WHERE status IN ('PENDING','FAILED')`, plus `(locked_until)` and `(processed_at)` for lease recovery and purges.
- `idempotency_keys`: `request_hash` 32 bytes; status IN_PROGRESS/COMPLETED (COMPLETED requires `response_status`); **`response_body json`** (not `jsonb`, which reorders keys) so a replay is byte-identical; index on `expires_at`.
- V026: trigram GIN on `bookings.reference`, `bookings (tenant_id, venue_id, business_date, status)`, `booking_resources (tenant_id, resource_id, booking_id)`, active-blocks and active-holds-by-owner partial indexes, `bookings (tenant_id, customer_id, starts_at DESC)`.
- Verified by `BookingTests`, `ConcurrencyTests` and `HousekeepingTests` (TESTING.md §4–5).

### Phase 8 schema notes

- V028: `venues.public_booking_enabled boolean NOT NULL DEFAULT true` and `venues.public_cancellation_cutoff_minutes integer NOT NULL DEFAULT 120` with `CHECK (BETWEEN 0 AND 10080)` (up to 7 days). Existing venues stay bookable online.
- No new indexes: slug lookups use `uq_tenants_slug` then `uq_venues_tenant_slug`; manage links use `uq_bookings_public_token_hash` (V015).
- Public bookings set `bookings.source = 'PUBLIC'`, `created_by_user_id = NULL`, and `public_token_hash = SHA-256(manage token)`; their history rows use source `PUBLIC`.

### Phase 9 schema notes

- V029 `notification_deliveries`: one row per outbox message and channel settled by the notification dispatcher — `status` `SENT` or `SKIPPED` (with `detail` ≤ 500 chars), `template`, `provider`, `provider_message_id`, optional `tenant_id` (FK CASCADE), `booking_id` (no FK: bookings are never deleted) and `user_id` (FK SET NULL). `uq_notification_deliveries_message (outbox_message_id, channel)` makes recording idempotent (`ON CONFLICT DO NOTHING`) and is the "already handled" check; the partial index `(booking_id, template) WHERE booking_id IS NOT NULL` serves "this booking already got this template". No recipient addresses or bodies are stored. Purged after 180 days.
- V029 `password_reset_requests`: `user_id` (FK CASCADE), `token_hash bytea NULL` (unique, exactly 32 bytes when set — written by the Worker at send time), `expires_at` (CHECK `> created_at`), `used_at`, `invalidated_at`, `requested_ip`. Indexes on `(user_id, created_at DESC)` and `expires_at`. Purged 7 days after expiry.
- Password reset outbox rows have `tenant_id = NULL` (users are global) and payload `{ eventId, type: "PasswordReset", requestId, occurredAt }`.

### Phase 10 schema notes

- V030 `venue_payment_settings`: PK `(tenant_id, venue_id)`, FK to `venues (tenant_id, id)` CASCADE. `deposit_policy` is `NONE` / `PER_BOOKING` / `PER_GUEST`. `deposit_amount_minor` is NULL exactly when the policy is `NONE`, otherwise 100–10 000 000. `currency` matches `^[A-Z]{3}$` and `payment_window_minutes` is 5–1440. A venue without a row has no deposit. Kept out of `venues` so payment settings never mix with booking rules (D-082).
- V030 `payments`:
  - Columns: `purpose` (`DEPOSIT`), `provider` (a name only), the opaque `provider_reference`, `amount_minor > 0`, `currency`, `status` (`CREATED`/`PENDING`/`SUCCEEDED`/`FAILED`/`CANCELLED`/`EXPIRED`), `refunded_amount_minor` (0..amount, non-zero only when `SUCCEEDED`), `failure_reason` ≤ 500, `requires_attention`, `expires_at`, `succeeded_at` (set exactly when `SUCCEEDED`), `version`.
  - Integrity: composite FK `(tenant_id, venue_id, booking_id)` to `bookings` RESTRICT, so a payment can never point at another tenant's or venue's booking.
  - `uq_payments_open_deposit (booking_id) WHERE purpose = 'DEPOSIT' AND status IN ('CREATED','PENDING','SUCCEEDED')` allows at most one live deposit per booking.
  - Indexes: by booking, a partial index on flagged payments, and `(provider, provider_reference)`.
- V030 `payment_refunds`: FK `(tenant_id, payment_id)` RESTRICT; `amount_minor > 0`; `status` `PENDING`/`SUCCEEDED`/`FAILED`, with `completed_at` NULL exactly while `PENDING`; `requested_by_user_id` (FK SET NULL); `reason` and `failure_reason` ≤ 500. Pending refunds reserve their amount (D-086).
- V030 `payment_webhook_events`: unique `(provider, provider_event_id)` (the dedupe key, D-083), `event_type`, `outcome` (`APPLIED`/`IGNORED`/`UNMATCHED`) with `detail`, optional `tenant_id` (FK CASCADE) and `payment_id`, `payload_sha256` (exactly 32 bytes; bodies are never stored), `received_at` (indexed for a future purge).
- V030 permissions: `payment.view` (OWNER, ADMIN, MANAGER, HOST) and `payment.refund` (OWNER, ADMIN, MANAGER).
- Deposit expiry uses the existing `ix_bookings_held_expiry (expires_at) WHERE status = 'HELD'` from V015.

### Phase 11 schema notes

- V031 `plans`: reference data keyed by `code` (`^[A-Z][A-Z0-9_]{1,39}$`), with `name`, `description`, nullable `max_venues` / `max_active_staff` / `max_resources_per_venue` (NULL = unlimited, otherwise ≥ 1), `custom_branding`, `is_default` (a unique partial index allows one default) and `sort_order`. Seeded: STARTER (1 venue, 5 staff, 30 resources per venue, no branding, default), PROFESSIONAL (5 / 25 / 100, branding), ENTERPRISE (unlimited, branding). There are no prices: plans are capabilities, not billing (D-088).
- V031 `tenant_subscriptions`: PK `tenant_id` (FK CASCADE), `plan_code` (FK to `plans`), optional overrides for each limit (1–100 000), timestamps. Every tenant that existed when V031 ran was backfilled to PROFESSIONAL. A tenant without a row is treated as being on the default plan.
- V031 `tenant_branding`: PK `tenant_id` (FK CASCADE). `primary_color` is checked against `^#[0-9a-f]{6}$`; `logo_url` must start with `https://` and be ≤ 500 chars; `booking_page_intro` ≤ 1000 and `booking_terms` ≤ 2000 chars. The application validates further (D-091).
- Limit checks lock the tenant row with `FOR NO KEY UPDATE` and count with existing indexes (D-089). Reports aggregate `bookings` by `(tenant_id, venue_id, business_date)` on demand (D-090).

### Planned sequence and phase

Indexes and constraints that guard correctness live **with their table** (so a table never exists without its protection, e.g. the exclusion constraint is in V017). V026 holds secondary/search indexes added once query paths are known.
FKs pointing to tables created later (e.g. `booking_allocations.hold_id → resource_holds`) are added by the later migration via `ALTER TABLE`.

| Migration | Contents | Phase |
|---|---|---|
| V001__extensions | btree_gist, pg_trgm, citext | 1 |
| V002__tenants | tenants | 2 |
| V003__users | users, refresh_tokens | 2 |
| V004__tenant_users | tenant_users | 2 |
| V005__roles_permissions | roles, permissions, role_permissions, tenant_user_roles | 2 |
| V006__venues | venues (incl. booking rule columns), tenant_user_venues (D-018; needs venues to exist) | 3 |
| V007__venue_sections | venue_sections | 3 |
| V008__resource_types | resource_types | 3 |
| V009__bookable_resources | bookable_resources | 3 |
| V010__floor_plans | floor_plans | 3 |
| V011__floor_plan_resources | floor_plan_resources | 3 |
| V012__operating_hours | operating_hours | 3 |
| V013__special_operating_hours | special_operating_hours | 3 |
| V014__customers | customers | 4 |
| V015__bookings | bookings, booking_reference_seq | 4 |
| V016__booking_resources | booking_resources | 4 |
| V017__booking_allocations | booking_allocations + **exclusion constraint** | 4 |
| V018__resource_holds | resource_holds, FK from allocations | 4 |
| V019__resource_combinations | resource_combinations, resource_combination_members | 4 |
| V020__resource_blocks | resource_blocks, FK from allocations | 4 |
| V021__booking_status_history | booking_status_history | 5 |
| V022__booking_events | booking_events | 5 |
| V023__audit_logs | audit_logs | 2–5 (first auditable mutation) |
| V024__outbox | outbox_messages | 5 |
| V025__idempotency | idempotency_keys | 5 |
| V026__indexes_and_constraints | secondary/search indexes | 5–6 |
| V027__seed_reference_data | system roles, permissions, role→permission mapping | 2 |

Note: Phase 4 (availability) needs the claim tables to exist, so V014–V020 are created in Phase 4 even though the booking *write path* arrives in Phase 5. If a migration must be created earlier than its number suggests (e.g. audit logs in Phase 2), the number is still reserved and order preserved; DbUp applies by name order, and no already-applied migration is ever renumbered.

## 3. Tenant integrity at database level

Tenant-owned parents expose `UNIQUE (tenant_id, id)`; children reference them with composite FKs:

```sql
ALTER TABLE bookable_resources ADD CONSTRAINT uq_bookable_resources_tenant_id UNIQUE (tenant_id, id);

ALTER TABLE booking_allocations
  ADD CONSTRAINT fk_booking_allocations_resource
  FOREIGN KEY (tenant_id, resource_id) REFERENCES bookable_resources (tenant_id, id) ON DELETE RESTRICT;
```

This makes it impossible — even with an application bug — to allocate Tenant B's resource to Tenant A's booking. The same pattern applies to venues, customers, bookings, sections, resource types and combinations. Application SQL additionally filters every query by `tenant_id`.

Row-Level Security was evaluated in Phase 12 and is not enabled (D-096): several legitimate paths are cross-tenant by design, and a missing session setting would silently hide data instead of failing. The Api and Worker connect as a least-privilege role (`APP_DB_USER`) that the migrator creates and re-grants on every run: data access on all tables and sequences, read-only on `schema_versions`, no DDL, `NOSUPERUSER NOBYPASSRLS` (D-095). So RLS can be enabled later without changing how the application connects.

## 4. Double-booking protection (the core invariant)

### 4.1 Design: one claim table for bookings, holds and blocks

An exclusion constraint only compares rows within one table. If bookings, holds and blocks were stored in separate tables, the database could not stop a booking overlapping a hold or a block. Therefore all time claims live in `booking_allocations`, discriminated by `allocation_type`:

```sql
CREATE TABLE booking_allocations (
  id              uuid        NOT NULL DEFAULT uuidv7(),
  tenant_id       uuid        NOT NULL,
  venue_id        uuid        NOT NULL,
  resource_id     uuid        NOT NULL,
  allocation_type text        NOT NULL,
  booking_id      uuid        NULL,
  hold_id         uuid        NULL,
  block_id        uuid        NULL,
  starts_at       timestamptz NOT NULL,
  ends_at         timestamptz NOT NULL,
  period          tstzrange   GENERATED ALWAYS AS (tstzrange(starts_at, ends_at, '[)')) STORED,
  expires_at      timestamptz NULL,
  is_active       boolean     NOT NULL DEFAULT true,
  released_at     timestamptz NULL,
  release_reason  text        NULL,
  created_at      timestamptz NOT NULL DEFAULT now(),

  CONSTRAINT pk_booking_allocations PRIMARY KEY (id),
  CONSTRAINT ck_booking_allocations_type CHECK (allocation_type IN ('BOOKING','HOLD','BLOCK')),
  CONSTRAINT ck_booking_allocations_owner CHECK (
       (allocation_type = 'BOOKING' AND booking_id IS NOT NULL AND hold_id IS NULL AND block_id IS NULL)
    OR (allocation_type = 'HOLD'    AND hold_id    IS NOT NULL AND booking_id IS NULL AND block_id IS NULL AND expires_at IS NOT NULL)
    OR (allocation_type = 'BLOCK'   AND block_id   IS NOT NULL AND booking_id IS NULL AND hold_id IS NULL)),
  CONSTRAINT ck_booking_allocations_period CHECK (ends_at > starts_at),
  CONSTRAINT ck_booking_allocations_released CHECK (is_active OR released_at IS NOT NULL),

  CONSTRAINT ex_booking_allocations_no_overlap
    EXCLUDE USING gist (resource_id WITH =, period WITH &&) WHERE (is_active)
);
```

### 4.2 Why this is correct

- **Half-open ranges `[start, end)`** — back-to-back bookings (19:00–21:00 and 21:00–23:00) do not overlap.
- **Partial constraint `WHERE (is_active)`** — cancelled bookings, no-shows, released/converted/expired holds and cancelled blocks are kept for history with `is_active = false` and no longer participate. Historical records never block legitimate new bookings.
- **Turnover buffer** — the allocation's `ends_at` = booking end + venue `turnover_buffer_minutes`. The booking row keeps the customer-facing end time.
- **Concurrency** — when two transactions insert conflicting rows, PostgreSQL makes the second wait for the first to commit or roll back, then raises `23P01 exclusion_violation` (or succeeds if the first rolled back). This holds under READ COMMITTED, across any number of Api instances, without application locks.
- **Combinations** — all member allocations are inserted in one statement (ordered by `resource_id`) inside a savepoint; a conflict rolls back the savepoint, so a combination is never partially allocated.
- **Contention** — conflicting inserters under an exclusion constraint can wait on each other's uncommitted rows and deadlock (measured: 10 concurrent auto-assign requests for 3 tables produced only 1 booking before this was addressed). Before inserting, the allocating transaction therefore takes `pg_advisory_xact_lock(class, hashtext(resource_id))` for **every candidate resource, in id order, once per transaction** (D-047). Claimants for the same resources queue instead of deadlocking; locks are released at commit/rollback. This is an optimisation only — the exclusion constraint remains the arbiter (a hash collision merely serialises more). A residual `40P01` on the insert is retried from the savepoint (up to 5 times); a `40P01`/`40001` elsewhere retries the whole transaction once, then 409.

### 4.3 Expired holds

`now()` cannot appear in a constraint predicate, so an expired hold remains `is_active = true` until released. Correctness is preserved by **lazy release inside every allocating transaction**, immediately before inserting:

```sql
UPDATE booking_allocations
SET    is_active = false, released_at = now(), release_reason = 'HOLD_EXPIRED'
WHERE  tenant_id = @TenantId
  AND  resource_id = ANY(@ResourceIds)
  AND  allocation_type = 'HOLD'
  AND  is_active
  AND  expires_at <= now()
  AND  period && tstzrange(@StartsAt, @EndsAt, '[)');
```

Concurrent transactions releasing the same hold serialise on the row lock; the loser's update matches zero rows and proceeds normally. The Worker sweeper releases the rest periodically (and emits `HoldExpired`). Availability queries ignore holds with `expires_at <= now()`.

### 4.4 State changes on allocations

| Operation | Allocation effect (same transaction as the booking change) |
|---|---|
| Create booking | Insert active `BOOKING` allocations |
| Convert hold | Deactivate `HOLD` allocations (`HOLD_CONVERTED`), insert `BOOKING` allocations |
| Move / resize / change resources | Deactivate old (`MOVED`), insert new; failure rolls back both — the booking never loses its allocation |
| Cancel | Deactivate (`CANCELLED`) |
| No-show | Deactivate (`NO_SHOW`) |
| Complete early | Truncate `ends_at` to completion instant (row stays active) |
| Block / unblock | Insert / deactivate `BLOCK` allocations |

Allocations are never deleted by application code.

### 4.5 Error mapping

| SQLSTATE | Meaning | API |
|---|---|---|
| 23P01 exclusion_violation on `ex_booking_allocations_no_overlap` | Resource taken | 409 `RESOURCE_NOT_AVAILABLE` (+ conflicting resource ids on staff API) |
| 23505 unique_violation on idempotency key | Concurrent duplicate | Resolved by idempotency flow (§6) |
| 40P01 deadlock_detected / 40001 serialization_failure | Lock cycle | Insert: retried from the savepoint; elsewhere: transaction retried once, then 409 `RESOURCE_NOT_AVAILABLE` |
| 23503 / 23514 | FK / check violation | 500 (indicates a validation gap — logged as error) |

Conflicting resource ids are determined by a follow-up overlap query after rollback (constraint error detail is not parsed for business data).

## 5. Key tables (summary)

| Table | Important constraints | Important indexes |
|---|---|---|
| tenants | uq slug; ck status | — |
| users | uq email (citext); ck status | — |
| refresh_tokens | uq token_hash; FK user | (user_id), (family_id), partial (expires_at) for cleanup |
| tenant_users | uq (tenant_id, user_id) | (user_id) |
| tenant_user_venues | pk (tenant_user_id, venue_id); composite FKs keep both in the same tenant | (venue_id) |
| venues | uq (tenant_id, slug); uq (tenant_id, id); ck rule ranges; ck time_zone not empty | (tenant_id) |
| venue_sections | uq (venue_id, name) | (venue_id) |
| resource_types | uq (tenant_id, code) | — |
| bookable_resources | uq (venue_id, code); ck `0 < min_capacity <= capacity`; composite FKs | (tenant_id, venue_id) WHERE is_active, (section_id) |
| resource_combination_members | pk (combination_id, resource_id) | (resource_id) |
| floor_plan_resources | uq (floor_plan_id, resource_id); ck width/height > 0 | — |
| operating_hours | ck day_of_week 1–7; ck open_time <> close_time | (venue_id, day_of_week) |
| special_operating_hours | ck closed ⇔ times null | (venue_id, date) |
| customers | composite FK tenant | (tenant_id, lower(email)), (tenant_id, phone_normalized), GIN trigram on name |
| bookings | uq reference; uq public_token_hash; ck party_size > 0; ck ends_at > starts_at; ck status/source values | (tenant_id, venue_id, business_date), (tenant_id, venue_id, starts_at), (tenant_id, customer_id), (tenant_id, status) partial on active statuses |
| booking_resources | pk (booking_id, resource_id) | (resource_id) |
| booking_allocations | **ex_booking_allocations_no_overlap**, checks above | GiST (resource_id, period) via constraint; GiST (venue_id, period) WHERE is_active; (booking_id); (hold_id); (block_id); partial (expires_at) WHERE allocation_type='HOLD' AND is_active |
| resource_holds | uq token_hash; ck status | partial (expires_at) WHERE status='ACTIVE' |
| resource_blocks | ck ends_at > starts_at | (tenant_id, venue_id, starts_at) |
| booking_status_history | FK booking | (booking_id, changed_at) |
| booking_events | FK booking | (booking_id, occurred_at) |
| audit_logs | — | (tenant_id, created_at DESC), (tenant_id, entity_type, entity_id) |
| outbox_messages | ck status, destination | partial (destination, available_at) WHERE status IN ('PENDING','FAILED') |
| idempotency_keys | uq (tenant_id, scope, key) | (expires_at) |

Cascade policy: `ON DELETE RESTRICT` by default (business entities are deactivated, not deleted). `ON DELETE CASCADE` only for pure child/join rows whose parent may legitimately be deleted (e.g. floor_plan_resources when a floor plan is deleted, role_permissions).

## 6. Idempotency

```
idempotency_keys(id, tenant_id, scope, key, request_hash, status, response_status, response_body json,
                 resource_id, created_at, expires_at)
UNIQUE (tenant_id, scope, key)
```

- `scope` = operation + caller (e.g. `booking.create:user:{userId}` or `booking.create:public:{venueId}`), so keys from different callers never collide.
- `request_hash` = SHA-256 of `"METHOD route\n"` + the request body re-serialised from the bound request with the API's JSON options (so whitespace and property order do not matter).
- An expired row for the same key is deleted before the insert, so keys can be reused after retention.
- Stored responses are written with the same serializer settings as MVC (relaxed encoder), so replays are byte-identical. Hold responses are stored **without the token** (only its hash exists server-side); a replayed hold returns `token: null`.
- Flow (inside the booking transaction, as the first statement): `INSERT ... ON CONFLICT DO NOTHING RETURNING id`.
  - Inserted → proceed; on success store `response_status`/`response_body`, `status = 'COMPLETED'`, commit with the booking.
  - Conflict → a concurrent duplicate blocks on the unique index until the first transaction finishes; then read the row: same hash + completed → return stored response (`Idempotent-Replayed: true` header); different hash → 409 `IDEMPOTENCY_CONFLICT`.
  - If the original transaction failed (e.g. 409 conflict), its key row rolled back with it, so a retry re-executes safely (no side effects were committed).
- Retention: 24 hours (configurable), purged by the Worker.

## 7. Outbox

```
outbox_messages(id, tenant_id, destination, type, payload jsonb, status, attempt_count,
                available_at, locked_until, locked_by, processed_at, last_error, created_at)
```

- Written in the business transaction, one row per destination (`REALTIME`, `NOTIFICATION`) so each is retried independently.
- `pg_notify('outbox', destination)` in the same transaction wakes dispatchers on commit; polling is the fallback.
- Claim: `UPDATE ... SET status='PROCESSING', locked_until = now() + lease ... WHERE id IN (SELECT id ... FOR UPDATE SKIP LOCKED LIMIT n) RETURNING *`.
- Failure: `attempt_count += 1`, `available_at = now() + backoff(attempt)` (exponential with jitter, capped), `last_error` (truncated, no secrets); after max attempts → `DEAD`.
- Stale `PROCESSING` rows past `locked_until` are reclaimable (crash recovery). Delivery is at-least-once; consumers are idempotent (message id as dedupe key).

## 8. Retention (initial policy — to be confirmed by the business; POPIA applies to South African customer data)

| Data | Retention | Mechanism |
|---|---|---|
| Bookings, booking_resources, allocations, status history, events | Indefinite (business records) | Never deleted; customer PII can be anonymised |
| Customers | Until erasure request or tenant-defined period | Anonymise (`anonymized_at`, PII nulled) rather than delete, to preserve booking history |
| Audit logs | ≥ 2 years (configurable) | Archive/partition later if volume requires |
| Idempotency keys | 24 h | Worker purge |
| Outbox (processed) | 30 days; DEAD rows kept until reviewed | Worker purge |
| Refresh tokens | Until expiry + 30 days | Worker purge |

## 9. Safety

- No `DROP DATABASE` or destructive resets in application startup or migrations.
- Dev reset script (`scripts/dev/reset-db.ps1` / `.sh`) only targets the local Compose database and refuses non-localhost hosts.
- Destructive schema changes (dropping columns/tables) follow expand → migrate → contract across releases.
