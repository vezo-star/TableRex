# Security

Status: **Phase 0 — design.** Security is a first-class feature; each phase's acceptance criteria include its security checks.

## 1. Authentication

| Item | Design |
|---|---|
| Passwords | `PasswordHasher<T>` from `Microsoft.Extensions.Identity.Core` (PBKDF2-HMAC-SHA512, iteration count raised to ≥210,000). Used standalone — no ASP.NET Identity stores, no EF. Rehash on login when parameters change. |
| Password policy | Min 12 chars, no forced composition rules, block top common passwords list. |
| Access token | JWT, HMAC-SHA256 (key ≥256 bits from secret store), 15 min lifetime. Claims: `sub` (user id), `tid` (tenant id, only after membership verification), `jti`, `iat`, `exp`, `iss`, `aud`. **No permissions in the token** — resolved server-side so changes apply immediately. Clock skew 30 s. |
| Refresh token | 256-bit random, stored as SHA-256 hash in `refresh_tokens`, 14-day lifetime (sliding by rotation, absolute max 30 days). Rotated on every use; reuse of a rotated token revokes the whole family (theft detection). Bound to user + tenant. |
| Token transport (SPA) | Access token in memory only. Refresh token in cookie: `HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth`. |
| CSRF | Only the refresh/logout endpoints use the cookie. Mitigations: SameSite=Strict, path-scoped cookie, Origin header check against allow-list, POST only. All other endpoints use bearer tokens (not ambient credentials) and are not CSRF-exposed. |
| Brute force | Per-account failed-login counter with progressive lockout (`lockout_until`), per-IP rate limit on `/auth/login`, uniform error `INVALID_CREDENTIALS` and constant-time-ish verification (hash a dummy when the user doesn't exist). |
| Account status | `ACTIVE`, `LOCKED`, `DISABLED`; disabled users cannot log in or refresh; existing refresh families revoked. |
| Logout | Revokes the refresh family and clears the cookie; access token expires naturally (short TTL). |

## 2. Tenant context and isolation

1. **Login**: the server lists the user's active memberships. If exactly one, the token gets that `tid`; otherwise the client chooses and calls `switch-tenant`.
2. **Switch tenant**: client sends a desired tenant id; the server **verifies active membership** before issuing a token with that `tid`. A client-selected id is never trusted without this check.
3. **Every request**: `TenantContextMiddleware` reads `tid` from the validated token and confirms the membership is still active (cached ≤60 s). Missing/invalid → 403.
4. **Every query**: repositories require `TenantId` from `ITenantContext` and include `tenant_id = @TenantId`. Request DTOs do not contain tenant ids.
5. **Database**: composite FKs `(tenant_id, id)` prevent cross-tenant references even if application code is wrong (`DATABASE.md` §3).
6. **Not found vs forbidden**: records from another tenant return 404 to avoid existence disclosure.
7. **SignalR**: group names built from the verified `tid`; join requires membership + venue access (`REALTIME.md`).
8. **Public API**: tenant and venue resolved from slugs server-side; only public-safe DTOs.
9. **Platform admins**: manage tenants only; no implicit access to tenant bookings/customers. Any future support-access feature must be explicit, time-boxed and audited.
10. **Defense in depth (Phase 12 evaluation)**: PostgreSQL Row-Level Security keyed on `SET LOCAL app.tenant_id` per transaction (D-024).

Mandatory tests: see `TESTING.md` §4.

## 3. Authorization

- Permission-based. Controllers use `[HasPermission(Permissions.BookingCreate)]`; a custom `IAuthorizationPolicyProvider` builds policies on demand, and `PermissionAuthorizationHandler` checks the caller's permission set for the current tenant.
- Venue access: application services call `IVenueAccessGuard.EnsureAccessAsync(venueId)`, which checks the venue belongs to the current tenant **and** the caller may access it (D-018): TENANT_OWNER/TENANT_ADMIN access every venue; other roles only venues in `tenant_user_venues`. Inaccessible venues return 404. Venue lists return only accessible venues. Venue ids in requests are always re-checked.
- Role/permission data is seeded reference data (V027); tenant custom roles are deferred.
- Last `TENANT_OWNER` cannot be removed or demoted.

### Initial role → permission matrix

| Permission | OWNER | ADMIN | MANAGER | HOST | STAFF | VIEW_ONLY |
|---|---|---|---|---|---|---|
| booking.view | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| booking.create | ✓ | ✓ | ✓ | ✓ | | |
| booking.update | ✓ | ✓ | ✓ | ✓ | | |
| booking.cancel | ✓ | ✓ | ✓ | ✓ | | |
| booking.move | ✓ | ✓ | ✓ | ✓ | | |
| booking.checkin | ✓ | ✓ | ✓ | ✓ | ✓ | |
| booking.seat | ✓ | ✓ | ✓ | ✓ | ✓ | |
| resource.view | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| resource.create / update / delete | ✓ | ✓ | ✓ | | | |
| venue.view | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| venue.manage | ✓ | ✓ | | | | |
| floorplan.view | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| floorplan.manage | ✓ | ✓ | ✓ | | | |
| customer.view | ✓ | ✓ | ✓ | ✓ | ✓ | |
| customer.manage | ✓ | ✓ | ✓ | ✓ | | |
| staff.view | ✓ | ✓ | ✓ | | | |
| staff.manage | ✓ | ✓ | | | | |
| reports.view | ✓ | ✓ | ✓ | | | |
| settings.manage | ✓ | ✓ | | | | |

OWNER vs ADMIN difference: ownership transfer and tenant deletion/suspension requests (not permissions in this list; owner-only operations).

## 4. Input handling

- FluentValidation on every command/query DTO (lengths, ranges, formats, enum membership).
- SQL exclusively parameterized via Dapper; dynamic `ORDER BY` only from allow-listed column maps.
- Request body size limits; JSON depth limits; reject unknown enum values.
- Output: API returns JSON only; the SPA renders with Vue's default escaping (no `v-html` with user data).

## 5. Transport, headers, CORS

- HTTPS only outside development; HSTS.
- Headers: `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `X-Frame-Options: DENY` for the API and staff app, a strict `Content-Security-Policy` for the SPA (served by nginx). The public booking widget's framing policy is decided in Phase 8 (`frame-ancestors` allow-list per tenant).
- CORS: explicit origins per environment from configuration, credentials allowed only for those origins. No wildcard with credentials.

## 6. Rate limiting & abuse (Phase 2 for auth, Phase 8 for public)

- Login: per IP and per account.
- Public availability: per IP + per venue, generous.
- Public booking/holds: per IP + per contact, strict; idempotency required.
- Redis-backed counters so limits hold across instances; fallback to in-memory per instance if Redis is unavailable.
- Staff endpoints: no aggressive limits (authenticated, per-user ceilings only to stop runaway clients).

## 7. Secrets & configuration

- No secrets in source control. `.env.example` / `appsettings*.json` contain placeholders only.
- Development: `dotnet user-secrets` or `.env` (git-ignored). Production: platform secret store injected as env vars.
- Secrets: `Jwt__SigningKey`, `ConnectionStrings__Postgres`, `ConnectionStrings__Redis`, `DEV_ADMIN_PASSWORD` (dev only), notification/payment provider keys (later).
- Startup fails fast if required secrets are missing or too weak (e.g. signing key < 32 bytes).

## 8. Logging & sensitive data

- Never log: passwords, tokens (JWT/refresh), cookies, `Authorization` headers, `access_token` query strings, payment data, full request bodies of auth/public booking endpoints.
- Customer PII in logs: ids only.
- Audit logs redact sensitive fields (`password_hash`, token hashes) from old/new values.
- Production error responses: generic message + traceId; details only in logs.

## 9. Audit

Audited actions include: login success/failure (security log), booking create/move/cancel/status changes, resource create/update/deactivate, blocks, floor plan changes, operating hours changes, staff/role changes, tenant settings changes, customer anonymisation. Each row captures tenant, user, entity, action, old/new values (redacted), IP, user agent, correlation id, timestamp.

## 10. Privacy (POPIA)

- Collect minimal customer data (name, email and/or phone, notes, optional preferences, marketing consent).
- Customer data is never exposed on public endpoints.
- Erasure via anonymisation (bookings remain for business records without PII).
- Data retention per `DATABASE.md` §8.

## 11. Future: payments and integrations

- Payment webhooks (Phase 10): signature verification per provider, idempotent processing keyed by provider event id, audit trail, replay window checks.
- External API clients: per-tenant API keys (hashed, scoped, revocable, rate-limited) — later phase.
