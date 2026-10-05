# Security

Status: **Authentication, tenant context and authorization implemented (Phase 2).** Security is a first-class feature; each phase's acceptance criteria include its security checks. §12 records the concrete Phase 2 parameters and known limitations.

## 1. Authentication

| Item | Design |
|---|---|
| Passwords | `PasswordHasher<T>` from `Microsoft.Extensions.Identity.Core` (PBKDF2-HMAC-SHA512, iteration count raised to ≥210,000). Used standalone — no ASP.NET Identity stores, no EF. Rehash on login when parameters change. |
| Password policy | Min 12 chars (max 128), no forced composition rules, block top common passwords list. |
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

## 12. Phase 2 implementation notes

| Control | Implemented value |
|---|---|
| Password hashing | `PasswordHasher<object>` IdentityV3, 210,000 iterations; rehash on login when the stored hash uses weaker parameters. |
| Password policy | 12–128 chars, ≥4 distinct characters, not in the bundled common-password list, must not contain the email local part (when ≥4 chars). |
| Lockout | 5 consecutive failures → lock for 1 min × 2^(n−5), capped at 60 min. Locked accounts get 423 `ACCOUNT_LOCKED` + `retryAfterSeconds`. Failed attempts are committed even though the request fails. |
| Unknown user | A dummy hash verification runs so response timing does not reveal whether the account exists. |
| Rate limits | Login 20/min per IP and 10 per 5 min per account (key = SHA-256 of the normalised email, so no PII is stored in Redis); refresh 60/min per IP; change-password 10 per 15 min per user. Redis fixed-window counters (`INCR` + `PEXPIRE` Lua script) with an in-memory per-instance fallback when Redis is down. Key prefix configurable via `RateLimiting__KeyPrefix`. |
| JWT validation | HS256 only, issuer + audience + lifetime validated, clock skew 30 s, `MapInboundClaims=false`. The signing key must be ≥32 bytes and not a placeholder — startup fails otherwise. `access_token` query string is accepted only on `/hubs/*` (SignalR). |
| Refresh tokens | 256-bit random, base64url, stored only as SHA-256 (`bytea`, 32 bytes). Rotated on every refresh; presenting a rotated token revokes the whole family (`REUSE_DETECTED`) and is audited. Expired families are purged in batches. |
| Origin check | `refresh`, `logout`, `switch-tenant` and `change-password` reject requests whose `Origin` is neither in `Cors:AllowedOrigins` nor the API's own host. Requests without `Origin` (non-browser clients) are allowed — the cookie is SameSite=Strict, so browsers cannot send it cross-site anyway. |
| Tenant context | `TenantContextMiddleware` runs after authentication: user flags (active / platform admin) and the membership for `tid` are cached in memory for 60 s (negative results 10 s) and invalidated on role / status changes **on the instance that made the change**. Inactive user → 401; `tid` without an active membership in an active tenant → 403. |
| Role changes | Applied on the next request (permissions are never in the token). Deactivating a member revokes their refresh tokens for that tenant. Only owners may grant or remove `TENANT_OWNER`; the last active owner cannot be demoted or deactivated (serialised with `SELECT … FOR UPDATE` on the tenant row). Users cannot deactivate themselves. |
| Audit | Login success/failure, lockout, logout, tenant switch, refresh reuse, password change, tenant settings, staff creation, role changes and (de)activation write `audit_logs` (append-only trigger). Sensitive keys (`password`, `token`, `secret`, `hash`, …) are recursively redacted to `[REDACTED]`. |
| Forwarded headers | `X-Forwarded-For/Proto` are honoured only from `ReverseProxy__TrustedNetworks` (CIDR list) so clients cannot spoof their IP for rate limiting. |
| Request limits | Kestrel body limit 1 MB (413 `PAYLOAD_TOO_LARGE`), JSON max depth 32, user agent stored truncated to 512 chars. |

Known limitations (accepted, revisit in Phase 12):

- **Cross-instance cache staleness.** With several API instances, a role change or deactivation is visible immediately on the instance that handled it but may take up to 60 s on others (the membership cache TTL). Refresh tokens are revoked immediately in PostgreSQL, so a deactivated user cannot obtain new access tokens; an existing access token may keep working on another instance for at most 60 s. A Redis pub/sub invalidation can remove this window if needed.
- **Account enumeration via lockout.** 423 `ACCOUNT_LOCKED` is only returned for existing accounts, so an attacker who triggers 5 failures can learn that an email exists. Accepted because the UX benefit (telling a real user why they cannot sign in) outweighs it; the per-account and per-IP rate limits bound the probing rate.
- **Access token lifetime.** A stolen access token stays valid until it expires (≤15 min); there is no access-token deny-list. Membership/user-status checks on every request limit the impact.
