# Database

Status: **Phase 0 — proposed design.** SQL below is illustrative; the migrations are the source of truth once written, and this document is updated to match them.

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
- Implemented: **V001__extensions** (Phase 1).
- Dev data: `-- seed-dev` (C# seeder; hashes the dev admin password from `DEV_ADMIN_PASSWORD`; refuses when `DOTNET_ENVIRONMENT=Production`).

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

Row-Level Security is a candidate defense-in-depth layer, evaluated in Phase 12 (D-024).

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
- **Combinations** — all member allocations are inserted in the same transaction; one conflict aborts the whole transaction, so a combination is never partially allocated. Inserts are ordered by `resource_id` to reduce deadlock likelihood; `40P01` is retried once, then returned as 409.

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
| 40P01 deadlock_detected | Lock cycle | Retry once, then 409 `RESOURCE_NOT_AVAILABLE` |
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
idempotency_keys(id, tenant_id, scope, key, request_hash, status, response_status, response_body jsonb,
                 resource_id, created_at, expires_at)
UNIQUE (tenant_id, scope, key)
```

- `scope` = operation + caller (e.g. `booking.create:user:{userId}` or `booking.create:public:{venueId}`), so keys from different callers never collide.
- `request_hash` = SHA-256 of method + route + canonical JSON body.
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
