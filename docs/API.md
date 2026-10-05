# API

Status: **Phases 1–2 implemented** (health, auth, tenancy, staff); later sections are the planned surface. Each endpoint is marked with the phase that implements it. This document and Swagger must match the real implementation once built.

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

### Availability, holds, blocks (Phase 4 implemented; holds/blocks Phase 5)

| Method | Path | Permission |
|---|---|---|
| GET | `/api/v1/availability?venueId&date&partySize&durationMinutes&time&sectionId&resourceTypeId` | booking.view |
| POST | `/api/v1/holds` | booking.create (Idempotency-Key) — Phase 5 |
| DELETE | `/api/v1/holds/{id}` | booking.create — Phase 5 |
| GET / POST | `/api/v1/resource-blocks?venueId&from&to` | resource.view / resource.update — Phase 5 |
| POST | `/api/v1/resource-blocks/{id}/cancel` | resource.update — Phase 5 |

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

### Bookings (Phase 5)

| Method | Path | Permission |
|---|---|---|
| GET | `/api/v1/bookings?venueId&date&from&to&status&source&customerId&resourceId&sectionId&q&page&pageSize` | booking.view |
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

`allocation.mode`: `AUTO` (best fit), `RESOURCES` (explicit ids), `COMBINATION` (`combinationId`). `customer` may instead contain new-customer details. Responses: 201 with booking; 409 `RESOURCE_NOT_AVAILABLE`; 422 rule violations.

### Customers (Phase 6)

| Method | Path | Permission |
|---|---|---|
| GET | `/api/v1/customers?q&page&pageSize` | customer.view (search name/phone/email) |
| POST | `/api/v1/customers` | customer.manage |
| GET / PUT | `/api/v1/customers/{id}` | customer.view / customer.manage |
| GET | `/api/v1/customers/{id}/bookings` | customer.view + booking.view |
| POST | `/api/v1/customers/{id}/anonymize` | customer.manage (POPIA erasure) |

### Audit & reports

| Method | Path | Permission | Phase |
|---|---|---|---|
| GET | `/api/v1/audit-logs?entityType&entityId&from&to&before` | settings.manage | 6 |
| GET | `/api/v1/reports/...` | reports.view | 11 |

## 4. Public API (Phase 8)

Separate controllers and DTOs; never return internal notes, staff data, customer records, or internal ids beyond what's needed.

| Method | Path | Notes |
|---|---|---|
| GET | `/api/v1/public/{tenantSlug}/{venueSlug}` | Name, description, time zone, party size limits, booking window, sections offered online |
| GET | `/api/v1/public/{tenantSlug}/{venueSlug}/availability?date&partySize[&time]` | Available times only (optionally section names) — no resource ids |
| POST | `/api/v1/public/{tenantSlug}/{venueSlug}/holds` | Optional short hold during checkout (Idempotency-Key, rate-limited) |
| POST | `/api/v1/public/{tenantSlug}/{venueSlug}/bookings` | Idempotency-Key; server auto-assigns resources via the same `BookingService` with source `WEBSITE`/`WIDGET` |
| GET | `/api/v1/public/bookings/{publicToken}` | Confirmation view using an unguessable token |
| POST | `/api/v1/public/bookings/{publicToken}/cancel` | Customer cancellation within venue policy (Phase 8/9) |

Public booking request: date, time, party size, first/last name, email, phone, special requests, consent flags. Response: reference, venue-local date/time, party size, status, public token link.

Abuse controls: rate limits per IP and per venue, payload size limits, optional bot challenge (decision deferred to Phase 8), email/phone format validation, max active future bookings per contact (configurable).

## 5. SignalR

See `REALTIME.md`. Hub at `/hubs/venue`; methods `JoinVenue(venueId)`, `LeaveVenue(venueId)`.
