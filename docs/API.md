# API

Status: **Phases 1–8 implemented** (health, auth, tenancy, staff, venues, resources, availability, bookings, holds, blocks, board, customers, audit, SignalR, public booking); later sections are the planned surface. Each endpoint is marked with the phase that implements it. This document and Swagger must match the real implementation once built.

## 1. Conventions

- Base paths: internal `/api/v1/...`, public `/api/v1/public/{tenantSlug}/{venueSlug}/...`, hub `/hubs/venue`.
- JSON, camelCase. Instants as ISO-8601 UTC (`2026-10-05T17:00:00Z`); venue-local values as `date` (`2026-10-05`) and `time` (`19:30`) with the venue `timeZone` in responses where relevant.
- Auth: `Authorization: Bearer <access token>`; refresh token in httpOnly cookie (`/api/v1/auth` path only).
- Tenant context comes from the access token (`tid` claim) — **no endpoint accepts a tenantId in the body or query for tenant scoping**.
- Correlation: clients may send `X-Correlation-Id`; responses always include it and `traceId` appears in errors.
- State transitions are explicit command endpoints (`POST /bookings/{id}/cancel`), never a status field update.
- Optimistic concurrency on mutable bookings: `version` in body (or `If-Match`), mismatch → 409 `CONCURRENCY_CONFLICT`.

### Pagination

Request: `?page=1&pageSize=25` (`pageSize` max 100, default 25), optional `sort`.
Response:

```json
{ "items": [], "page": 1, "pageSize": 25, "totalCount": 132 }
```

High-volume append-only lists (audit logs) may use keyset pagination (`?before=<cursor>`), documented per endpoint.

### Idempotency

`POST /bookings` (staff and public) and `POST .../holds` require `Idempotency-Key` (1–100 chars, client-generated UUID recommended).
Replay returns the original status/body with header `Idempotent-Replayed: true`. Same key, different payload → 409 `IDEMPOTENCY_CONFLICT`. Missing header → 400 `IDEMPOTENCY_KEY_REQUIRED`.

## 2. Error model (RFC 7807)

```json
{
  "type": "https://errors.bookingplatform.dev/resource-not-available",
  "title": "Resource not available",
  "status": 409,
  "code": "RESOURCE_NOT_AVAILABLE",
  "detail": "One or more selected resources are no longer available for the requested time.",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
  "conflictingResourceIds": ["0192..."]
}
```

Validation errors add `"errors": { "partySize": ["Party size must be greater than 0."] }`.
Public endpoints never include internal ids such as `conflictingResourceIds`.

| Code | HTTP |
|---|---|
| VALIDATION_ERROR | 400 |
| IDEMPOTENCY_KEY_REQUIRED | 400 |
| INVALID_BOOKING_TIME | 422 |
| PARTY_SIZE_NOT_SUPPORTED | 422 |
| OUTSIDE_OPERATING_HOURS | 422 |
| OUTSIDE_BOOKING_WINDOW | 422 |
| CONTACT_BOOKING_LIMIT | 422 |
| CANCELLATION_NOT_ALLOWED | 422 |
| UNAUTHORIZED | 401 |
| INVALID_CREDENTIALS | 401 |
| ACCOUNT_LOCKED | 423 |
| FORBIDDEN | 403 |
| TENANT_NOT_FOUND / VENUE_NOT_FOUND / RESOURCE_NOT_FOUND / BOOKING_NOT_FOUND / CUSTOMER_NOT_FOUND / HOLD_NOT_FOUND | 404 |
| RESOURCE_NOT_AVAILABLE | 409 |
| BOOKING_ALREADY_CANCELLED | 409 |
| INVALID_STATUS_TRANSITION | 409 |
| CONCURRENCY_CONFLICT | 409 |
| IDEMPOTENCY_CONFLICT | 409 |
| HOLD_EXPIRED | 410 |
| RATE_LIMITED | 429 |
| INTERNAL_ERROR | 500 (generic message, no exception details) |

Cross-tenant access returns **404** (not 403) so existence of other tenants' records is not revealed.

## 3. Internal (staff) API

### Health & docs (Phase 1)

| Method | Path | Notes |
|---|---|---|
| GET | `/health` | Liveness |
| GET | `/health/ready` | PostgreSQL + Redis |
| GET | `/swagger` | Development/Test only by default |

### Auth (Phase 2 — implemented)

| Method | Path | Permission | Notes |
|---|---|---|---|
| POST | `/api/v1/auth/login` | anonymous; 20/min per IP + 10 per 5 min per account | `{ email, password }`. Returns access token + memberships; sets the refresh cookie. Token is tenant-scoped if the user has exactly one active membership, otherwise `requiresTenantSelection: true` and the client calls switch-tenant. Wrong email and wrong password both return 401 `INVALID_CREDENTIALS`; a locked account returns 423 `ACCOUNT_LOCKED` with `retryAfterSeconds`. |
| POST | `/api/v1/auth/refresh` | refresh cookie; 60/min per IP; Origin checked | Rotates the refresh token; reuse of a rotated token revokes the whole family (401). |
| POST | `/api/v1/auth/logout` | refresh cookie (anonymous allowed); Origin checked | Revokes the current refresh-token family and clears the cookie. Always 204. |
| GET | `/api/v1/auth/me` | authenticated | User, current tenant, roles, permissions, memberships, `accessibleVenueIds`, `hasAllVenueAccess`. `accessibleVenueIds` is `[]` until Phase 3 adds venues. |
| POST | `/api/v1/auth/switch-tenant` | authenticated; Origin checked | `{ tenantId }` → server verifies an active membership in an active tenant → new access + refresh token (old family revoked). Not a member (or tenant inactive) → 404 `TENANT_NOT_FOUND`, so tenant ids cannot be probed. |
| POST | `/api/v1/auth/change-password` | authenticated; Origin checked; 10 per 15 min per user | `{ currentPassword, newPassword }`. Revokes every refresh token for the user and issues a new session. |

Auth responses carry `Cache-Control: no-store`. Access token: JWT HS256, 15 min, claims `sub`, `tid` (optional), `jti`, `iss`, `aud` — permissions are **not** in the token; they are resolved per request. Refresh cookie: `bp_refresh`, HttpOnly, Secure, SameSite=Strict, Path=`/api/v1/auth`, 14-day sliding / 30-day absolute.

Auth response:

```json
{
  "accessToken": "…", "expiresAt": "2026-10-05T15:45:00Z", "userId": "…", "tenantId": null,
  "requiresTenantSelection": true,
  "memberships": [{ "tenantId": "…", "tenantName": "Demo Hospitality", "tenantSlug": "demo-hospitality", "roles": ["TENANT_OWNER"] }]
}
```

Password reset / email verification: Phase 9 (needs notifications).

### Tenancy & staff (Phase 2 — implemented)

| Method | Path | Permission |
|---|---|---|
| GET | `/api/v1/tenants/current` | any member (tenant selected) |
| PUT | `/api/v1/tenants/current` | settings.manage (`{ name, defaultTimeZone }`; slug is immutable; audited) |
| GET | `/api/v1/staff?status&q&page&pageSize` | staff.view (`q` matches name or email) |
| POST | `/api/v1/staff` | staff.manage — `{ email, displayName, password?, roles[] }`; creates the account (password required) or adds a membership for an existing account. 201 `{ staff, accountCreated }`; 409 if already a member. |
| GET | `/api/v1/staff/{tenantUserId}` | staff.view |
| PUT | `/api/v1/staff/{tenantUserId}/roles` | staff.manage — audited; 422 `LAST_OWNER` when removing the last owner; only owners may grant or remove `TENANT_OWNER` (403) |
| POST | `/api/v1/staff/{tenantUserId}/deactivate` | staff.manage — revokes that user's sessions for the tenant; cannot deactivate yourself or the last owner (422) |
| POST | `/api/v1/staff/{tenantUserId}/activate` | staff.manage |
| GET | `/api/v1/roles` | staff.view — system roles with their permissions |
| GET / POST | `/api/v1/platform/tenants` | platform admin only — list tenants; create a tenant with its first owner (`{ name, slug, defaultTimeZone, owner: { email, displayName, password? } }`) |

Permission failures: 401 `UNAUTHORIZED` (no/invalid token), 403 `TENANT_SELECTION_REQUIRED` (tenant endpoint without `tid`), 403 `FORBIDDEN` (missing permission, or a `tid` the user is not an active member of).

### Venues (Phase 3 — implemented)

Venue scoping (D-018): OWNER/ADMIN see every venue of the tenant; other roles only venues assigned via `PUT /api/v1/staff/{id}/venues`. A venue outside the caller's scope (or another tenant's) is **404 `VENUE_NOT_FOUND`**, never 403. Resources and floor plans inherit this (404 `RESOURCE_NOT_FOUND` / `FLOOR_PLAN_NOT_FOUND`). Times of day are `"HH:mm"` strings; dates are `"yyyy-MM-dd"`; day of week is ISO (1 = Monday … 7 = Sunday).

| Method | Path | Permission |
|---|---|---|
| GET | `/api/v1/venues` | venue.view (only accessible venues) |
| POST | `/api/v1/venues` | venue.manage — `{ name, slug, timeZone, description?, address…, countryCode?, phone?, email?, rules? }`; rules default when omitted; 409 `SLUG_TAKEN` |
| GET / PUT | `/api/v1/venues/{venueId}` | venue.view / venue.manage (PUT also sets `status` ACTIVE/INACTIVE and `rules`; slug is immutable) |
| GET / POST | `/api/v1/venues/{venueId}/sections` | venue.view / venue.manage (409 `CONFLICT` on duplicate name) |
| PUT / DELETE | `/api/v1/venues/{venueId}/sections/{sectionId}` | venue.manage — DELETE returns `{ deleted, deactivated }`: referenced sections are deactivated |
| GET / PUT | `/api/v1/venues/{venueId}/operating-hours` | venue.view / venue.manage — PUT `{ periods: [{ dayOfWeek, openTime, closeTime }] }` replaces the week; close ≤ open = overnight; overlaps (incl. overnight into the next day and Sunday→Monday) → 400 |
| GET / POST | `/api/v1/venues/{venueId}/special-hours?from&to` | venue.view / venue.manage — `{ date, name?, isClosed, openTime?, closeTime? }`; one row per period; mixing closed and open rows on one date → 409 `CONFLICT`; overlapping periods → 400 |
| PUT / DELETE | `/api/v1/venues/{venueId}/special-hours/{id}` | venue.manage |
| GET | `/api/v1/venues/{venueId}/schedule?from&to` | venue.view — effective periods per business date (max 62 days) with UTC `startsAt`/`endsAt` and `overnight` |

`rules` = `{ slotIntervalMinutes (5/10/15/20/30/60), defaultDurationMinutes, minDurationMinutes, maxDurationMinutes, turnoverBufferMinutes, minPartySize, maxPartySize, minLeadTimeMinutes, maxAdvanceDays, holdTtlSeconds, autoConfirmPublicBookings }`.

`PUT /api/v1/staff/{tenantUserId}/venues` (staff.manage) — `{ venueIds: [] }` replaces the member's venue assignments; venues must belong to the tenant (400 otherwise); audited as `venues.changed`. `/api/v1/auth/me` returns `accessibleVenueIds` and `hasAllVenueAccess`.

### Resources & floor plans (Phase 3 — implemented)

| Method | Path | Permission |
|---|---|---|
| GET / POST | `/api/v1/resource-types` | resource.view / resource.create |
| PUT | `/api/v1/resource-types/{id}` | resource.update |
| GET | `/api/v1/resources?venueId&sectionId&resourceTypeId&isActive&page&pageSize` | resource.view |
| POST | `/api/v1/resources` | resource.create |
| GET / PUT | `/api/v1/resources/{id}` | resource.view / resource.update |
| DELETE | `/api/v1/resources/{id}` | resource.delete (deactivates if it has booking history) |
| GET / POST | `/api/v1/resource-combinations?venueId` | resource.view / resource.create (Phase 4 — implemented) |
| GET / PUT / DELETE | `/api/v1/resource-combinations/{id}` | resource.view / resource.update / resource.delete (Phase 4 — implemented) |
| GET | `/api/v1/floor-plans?venueId` | floorplan.view |
| POST | `/api/v1/floor-plans` | floorplan.manage |
| GET / PUT / DELETE | `/api/v1/floor-plans/{id}` | floorplan.view / floorplan.manage |
| PUT | `/api/v1/floor-plans/{id}/resources` | floorplan.manage — `{ positions: [{ resourceId, x, y, width, height, rotation, shape: RECT\|ROUND }] }` replaces all positions; each resource once, must belong to the plan's venue and fit inside the plan (400) |

Resource rules: `code` unique per venue (409 `CODE_TAKEN`; type codes unique per tenant, `^[A-Z][A-Z0-9_]{0,39}$`); `1 ≤ minCapacity ≤ capacity ≤ 1000`; `sectionId` must belong to the same venue; `attributes` is a string map (≤ 20 keys). DELETE returns `{ deleted, deactivated }`.

Combination body: `{ venueId, name, minCapacity, capacity, members: [{ resourceId, isRequired }], isBookableOnline, sortOrder }` (PUT omits `venueId` and adds `isActive`). Rules: 2–20 distinct members, at least one required, every member a resource of the same venue (400 `members`); name unique per venue, case-insensitive (409 `CONFLICT`); `1 ≤ minCapacity ≤ capacity ≤ 1000`. Optional members are allocated only when free (D-043). DELETE deactivates instead when bookings reference the combination.

### Availability, holds, blocks (Phase 4 / Phase 5 — implemented)

| Method | Path | Permission |
|---|---|---|
| GET | `/api/v1/availability?venueId&date&partySize&durationMinutes&time&sectionId&resourceTypeId` | booking.view |
| POST | `/api/v1/holds` | booking.create (Idempotency-Key) |
| DELETE | `/api/v1/holds/{id}` | booking.create — releases an active hold; 204 (also when it already expired/converted/was released) |
| GET | `/api/v1/resource-blocks?venueId&from&to&includeCancelled` | resource.view — blocks overlapping `[from, to)` (UTC instants) |
| POST | `/api/v1/resource-blocks` | resource.update |
| GET | `/api/v1/resource-blocks/{id}` | resource.view |
| POST | `/api/v1/resource-blocks/{id}/cancel` | resource.update |

Hold request: `{ venueId, date, time, durationMinutes?, partySize, allocation? }` (same allocation modes as bookings). Response 201 `{ id, venueId, status, partySize, startsAt, endsAt, expiresAt, resourceIds, token }`. The TTL is the venue's `holdTtlSeconds`. **`token` is returned only by the first response** — only its SHA-256 is stored, so an idempotent replay returns `token: null`. Pass the token as `holdToken` when creating the booking; the hold's resources are converted atomically (`allocation` must then be omitted or `AUTO`). An expired hold → 410 `HOLD_EXPIRED`; unknown token → 404 `HOLD_NOT_FOUND`.

Block request: `{ venueId, resourceIds (1–50, same venue), startsAt, endsAt (UTC, ≤ 31 days), reason, notes? }`; `reason` ∈ MAINTENANCE, PRIVATE_EVENT, CLEANING, MANAGEMENT, UNAVAILABLE, STAFF_USE. Overlap with a booking, hold or block → 409 `RESOURCE_NOT_AVAILABLE` with `conflictingResourceIds`. Cancelling releases the resources immediately.

Availability response (staff):

```json
{
  "venueId": "…", "date": "2026-10-09", "timeZone": "Africa/Johannesburg", "partySize": 4, "durationMinutes": 120,
  "turnoverMinutes": 15, "isClosed": false,
  "slots": [
    { "time": "19:30", "startsAt": "2026-10-09T17:30:00Z", "endsAt": "2026-10-09T19:30:00Z", "available": true,
      "options": [
        { "kind": "RESOURCE", "id": "…", "name": "Table 4", "capacity": 4, "resourceIds": ["…"] },
        { "kind": "COMBINATION", "id": "…", "name": "Table 5 + 6", "capacity": 8, "resourceIds": ["…","…"] }
      ] }
  ]
}
```

- `date` is the venue-local business date (an overnight period belongs to the date it opens on); `time` is the venue-local start (`HH:mm`).
- `durationMinutes` defaults to the venue default and must lie within the venue's min/max (400 `durationMinutes`); `partySize` 1–1000. Public limits (max party, lead time, advance days) do not apply to staff (DOMAIN_MODEL §5).
- `time` (optional, `HH:mm`) narrows the result to slots within ±90 minutes (D-044). `sectionId` (must belong to the venue) and `resourceTypeId` filter the candidate resources; a combination is offered only if its required members pass the filters.
- Closed dates and inactive venues return `isClosed: true` with no slots. Slots that start before now are omitted. Options are ranked: single resources, smallest sufficient capacity, sort order (D-042).
- Inaccessible venues return 404 `VENUE_NOT_FOUND` (D-018).

### Bookings (Phase 5 — implemented)

| Method | Path | Permission |
|---|---|---|
| GET | `/api/v1/bookings?venueId&date&from&to&status&source&customerId&resourceId&sectionId&q&page&pageSize` | booking.view — `status` may repeat or be comma-separated; `q` matches reference, customer name, email, phone; only accessible venues |
| POST | `/api/v1/bookings` | booking.create (Idempotency-Key required) |
| GET | `/api/v1/bookings/{id}` | booking.view |
| PUT | `/api/v1/bookings/{id}` | booking.update (party size, notes, requests, customer; re-validates capacity) |
| POST | `/api/v1/bookings/{id}/move` | booking.move (time and/or resources, atomic) |
| POST | `/api/v1/bookings/{id}/confirm` | booking.update |
| POST | `/api/v1/bookings/{id}/check-in` | booking.checkin |
| POST | `/api/v1/bookings/{id}/seat` | booking.seat |
| POST | `/api/v1/bookings/{id}/complete` | booking.seat |
| POST | `/api/v1/bookings/{id}/no-show` | booking.update |
| POST | `/api/v1/bookings/{id}/cancel` | booking.cancel (`{ reason }`) |
| GET | `/api/v1/bookings/{id}/history` | booking.view (status history + events) |

Create request:

```json
{
  "venueId": "…",
  "customer": { "customerId": "…" },
  "date": "2026-10-09", "time": "19:30", "durationMinutes": 120, "partySize": 4,
  "allocation": { "mode": "RESOURCES", "resourceIds": ["…"] },
  "holdToken": null,
  "source": "PHONE",
  "status": "CONFIRMED",
  "specialRequests": "Window seat", "internalNotes": "Regular"
}
```

`allocation.mode`: `AUTO` (best fit, default), `RESOURCES` (explicit ids), `COMBINATION` (`combinationId`). `customer` may instead contain new-customer details `{ firstName, lastName?, email?, phone? }` — an existing customer with the same email is reused (D-049); `WALK_IN` bookings may omit `customer`. `source` ∈ STAFF (default), PHONE, WALK_IN; `status` ∈ CONFIRMED (default; SEATED for `WALK_IN`), PENDING, HELD, SEATED. `durationMinutes` defaults to the venue default.

Responses: 201 with the booking (`Location` header); 400 validation; 404 `VENUE_NOT_FOUND` / `RESOURCE_NOT_FOUND` / `CUSTOMER_NOT_FOUND`; 409 `RESOURCE_NOT_AVAILABLE` (explicit allocations include `conflictingResourceIds`); 422 `OUTSIDE_OPERATING_HOURS` (must fit one operating period), `INVALID_BOOKING_TIME` (ends in the past), `PARTY_SIZE_NOT_SUPPORTED`.

Booking response: `{ id, venueId, reference, status, source, partySize, startsAt, endsAt, businessDate, time, durationMinutes, timeZone, customer, resources: [{ resourceId, name, code, combinationId }], specialRequests, internalNotes, expiresAt, createdByUserId, version, confirmedAt, checkedInAt, seatedAt, completedAt, cancelledAt, noShowAt, cancellationReason, createdAt, updatedAt }`.

Commands and updates accept an optional `version` in the body (or `If-Match: <version>`); a mismatch → 409 `CONCURRENCY_CONFLICT`. Transitions: confirm (PENDING/HELD → CONFIRMED), check-in (CONFIRMED → CHECKED_IN), seat (CONFIRMED/CHECKED_IN → SEATED), complete (SEATED → COMPLETED; frees the resource after turnover), no-show (CONFIRMED, only after the start → NO_SHOW), cancel (PENDING/HELD/CONFIRMED/CHECKED_IN → CANCELLED, `{ reason? }`). Anything else → 409 `INVALID_STATUS_TRANSITION`; cancelling twice → 409 `BOOKING_ALREADY_CANCELLED`.

Move body: `{ date?, time?, durationMinutes?, allocation?, version? }` (at least one field; omitted values keep the current ones, and the current resources/combination are kept when `allocation` is omitted). Old and new allocation change in one transaction — on 409 the booking keeps its original slot.

History: `{ bookingId, statusHistory: [{ fromStatus, toStatus, source, reason, changedByUserId, changedAt }], events: [{ eventType, data, actorUserId, occurredAt }] }`.

Every write also records audit (no PII or notes) and outbox rows (REALTIME `BookingCreated`/`BookingUpdated`/`BookingMoved`/`BookingCancelled`/`BookingStatusChanged`, NOTIFICATION confirmation/cancellation) in the same transaction.

### Board (Phase 6 — implemented)

| Method | Path | Permission |
|---|---|---|
| GET | `/api/v1/bookings/board?venueId&date` | booking.view (venue must be accessible) |

One venue-local business date for the staff board: `{ venueId, date, timeZone, isClosed, periods: [{ openTime, closeTime, startsAt, endsAt, overnight }], slotIntervalMinutes, turnoverMinutes, bookings: [Booking], blocks: [Block], truncated }`. `bookings` holds every status (the UI filters cancelled/no-show) up to 1000 rows (`truncated: true` beyond that); `blocks` holds active blocks overlapping the day's window. Missing `date` → 400; inaccessible venue → 404 `VENUE_NOT_FOUND`.

### Customers (Phase 6 — implemented)

| Method | Path | Permission |
|---|---|---|
| GET | `/api/v1/customers?q&page&pageSize` | customer.view — `q` matches name and email (contains) and phone digits (≥ 3 digits); anonymised customers sort last |
| POST | `/api/v1/customers` | customer.manage → 201 |
| GET / PUT | `/api/v1/customers/{id}` | customer.view / customer.manage |
| GET | `/api/v1/customers/{id}/bookings?page&pageSize` | customer.view + booking.view — newest first, only bookings at venues the caller can access |
| POST | `/api/v1/customers/{id}/anonymize` | customer.manage (POPIA erasure, D-055) |

Body (create/update): `{ firstName, lastName?, email?, phone?, notes?, marketingConsent }`. Response: `{ id, firstName, lastName, email, phone, notes, marketingConsent, isAnonymized, anonymizedAt, bookingCount, lastBookingAt, createdAt, updatedAt }` (`bookingCount` counts bookings at all venues of the tenant, D-054). Errors: 409 `EMAIL_TAKEN` (another active customer has the email, case-insensitive; the problem includes `customerId`), 422 `CUSTOMER_ANONYMIZED` (editing an anonymised customer), 404 `CUSTOMER_NOT_FOUND`.

### Audit log (Phase 6 — implemented)

| Method | Path | Permission |
|---|---|---|
| GET | `/api/v1/audit-logs?entityType&entityId&userId&action&from&to&before&limit` | settings.manage |

Newest first, tenant-scoped, keyset-paged: `{ items: [{ id, entityType, entityId, action, userId, userEmail, userDisplayName, oldValues, newValues, ip, userAgent, correlationId, createdAt }], nextCursor }`. Pass `nextCursor` as `before` for the next page (`limit` 1–100, default 50); a malformed cursor → 400.

### Reports (Phase 11)

| Method | Path | Permission |
|---|---|---|
| GET | `/api/v1/reports/...` | reports.view |

## 4. Public API (Phase 8 — implemented)

`PublicBookingController` (anonymous) with its own DTOs. Responses never contain internal ids, resource/table names, staff, notes or customer records. A venue is reachable only when the tenant is `ACTIVE`, the venue is `ACTIVE` and `rules.publicBookingEnabled` is true; anything else (unknown slugs, malformed slugs, suspended tenant, inactive venue, online booking off) is the same 404 `VENUE_NOT_FOUND`.

| Method | Path | Rate-limit policy (per IP) | Notes |
|---|---|---|---|
| GET | `/api/v1/public/{tenantSlug}/{venueSlug}` | `public-read` 120/min | Venue name, description, address, contact, `timeZone`, party size limits, `durationMinutes`, `minLeadTimeMinutes`, `today`, `lastBookableDate`, `cancellationCutoffMinutes`, `autoConfirm` |
| GET | `/api/v1/public/{tenantSlug}/{venueSlug}/availability?date&partySize` | `public-read` 120/min | `{ date, timeZone, partySize, durationMinutes, isClosed, times: [{ time, startsAt }] }` — only bookable times, computed on online-bookable resources and combinations, honouring lead time |
| POST | `/api/v1/public/{tenantSlug}/{venueSlug}/bookings` | `public-booking` 10 per 10 min | `Idempotency-Key` required; 201 with `Cache-Control: no-store`; replay adds `Idempotent-Replayed: true` |
| GET | `/api/v1/public/booking` | `public-manage` 60/min | Header `X-Booking-Token: <manage token>` |
| POST | `/api/v1/public/booking/cancel` | `public-cancel` 10 per 10 min | Header `X-Booking-Token`; cancels within the venue's online cutoff |

Request body for `POST .../bookings`:

```json
{
  "date": "2026-10-09", "time": "19:00", "partySize": 2,
  "firstName": "Ada", "lastName": "Lovelace", "email": "ada@example.com", "phone": "+27 21 555 0101",
  "specialRequests": "Window seat", "marketingConsent": false, "website": null
}
```

- `website` is a honeypot: it must be empty (bots that fill every field get 400 `VALIDATION_ERROR`).
- Validation (400): first name 1–100, last name ≤100, valid email ≤254, phone ≤40 with 4–20 digits, special requests ≤1000, party size within the venue's min/max, `date` between `today` and `lastBookableDate` (venue-local), `time` on the venue's slot grid.
- Business rules (422): `OUTSIDE_OPERATING_HOURS`, `OUTSIDE_BOOKING_WINDOW` (inside the minimum lead time), `CONTACT_BOOKING_LIMIT` (the email already has `PublicBooking:MaxActiveBookingsPerContact` (default 3) upcoming active bookings at this venue), `PARTY_SIZE_NOT_SUPPORTED`. Taken slot: 409 `RESOURCE_NOT_AVAILABLE`.
- Per-contact limit: at most 5 booking attempts per email per hour (429 `RATE_LIMITED` with `Retry-After`).
- Resources are assigned by the same engine as staff bookings (`AUTO` over online-bookable resources and combinations; the exclusion constraint decides races). Source `PUBLIC`; status `CONFIRMED` when `autoConfirmPublicBookings`, else `PENDING`. An existing customer with the same email is reused unchanged; marketing consent is stored only for new customers.

201 response:

```json
{
  "reference": "BK-20261009-000042", "status": "CONFIRMED", "venueName": "Demo Restaurant",
  "date": "2026-10-09", "time": "19:00", "durationMinutes": 120, "partySize": 2,
  "timeZone": "Africa/Johannesburg", "startsAt": "2026-10-09T17:00:00Z",
  "manageToken": "<43-char base64url>"
}
```

`GET /public/booking` and `POST /public/booking/cancel` return `{ reference, status, tenantSlug, venueSlug, venueName, venuePhone, venueEmail, date, time, durationMinutes, partySize, timeZone, startsAt, guestFirstName, specialRequests, canCancel, cancelUntil, cancelledAt }`. A missing, malformed or unknown token is 404 `BOOKING_NOT_FOUND`. Cancellation is allowed for `PENDING`/`HELD`/`CONFIRMED` bookings until `startsAt − publicCancellationCutoffMinutes`; otherwise 422 `CANCELLATION_NOT_ALLOWED`. Cancelling an already cancelled booking returns it unchanged.

The manage token is never put in an API URL. The SPA's manage link is `/booking/manage#<token>` — the fragment is not sent to servers or in `Referer`. See `SECURITY.md` §Public booking.

Public holds (`POST .../holds`) are not exposed — see D-068.

## 5. SignalR

Implemented in Phase 7 — see `REALTIME.md`.

- Hub: `/hubs/venue`, WebSockets only (`skipNegotiation: true` on the client). Authenticate with the access token as the `access_token` query string (only accepted on `/hubs/*`).
- Client → server: `JoinVenue(venueId)`, `LeaveVenue(venueId)`. Refusals throw `HubException` with message `FORBIDDEN` (no membership, no `booking.view`, no access to the venue, or the venue doesn't exist in the tenant); unexpected failures surface as `UNAVAILABLE`.
- Server → client: one method per event type (`BookingCreated`, `BookingUpdated`, `BookingCancelled`, `BookingMoved`, `BookingStatusChanged`, `ResourceUpdated`, `ResourceBlocked`, `HoldCreated`, `HoldExpired`, `FloorPlanUpdated`) with the ids-only payload `{ eventId, type, venueId, entityId, version, businessDate, occurredAt }` (`version`/`businessDate` may be null). Delivery is at-least-once; deduplicate by `eventId` and refetch through REST.
