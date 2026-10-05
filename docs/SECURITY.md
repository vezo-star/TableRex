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
| payment.view | ✓ | ✓ | ✓ | ✓ | | |
| payment.refund | ✓ | ✓ | ✓ | | | |

OWNER vs ADMIN difference: ownership transfer and tenant deletion/suspension requests (not permissions in this list; owner-only operations).

## 4. Input handling

- FluentValidation on every command/query DTO (lengths, ranges, formats, enum membership).
- SQL exclusively parameterized via Dapper; dynamic `ORDER BY` only from allow-listed column maps.
- Request body size limits; JSON depth limits; reject unknown enum values.
- Output: API returns JSON only; the SPA renders with Vue's default escaping (no `v-html` with user data).

## 5. Transport, headers, CORS

- HTTPS only outside development; HSTS.
- Headers: `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `X-Frame-Options: DENY` for the API and staff app, a strict `Content-Security-Policy` for the SPA (served by nginx). The public booking pages are served by the same SPA under the same headers, so they cannot be framed; venues link or button to `/book/{tenantSlug}/{venueSlug}` instead of embedding an iframe (D-072). A per-tenant `frame-ancestors` allow-list can be added later without API changes.
- CORS: explicit origins per environment from configuration, credentials allowed only for those origins. No wildcard with credentials.

## 6. Rate limiting & abuse (Phase 2 for auth, Phase 8 for public)

- Login: per IP and per account.
- Public endpoints (implemented in Phase 8), Redis fixed-window counters per client IP:

  | Policy | Endpoints | Default |
  |---|---|---|
  | `public-read` | venue details, availability | 120 per 60 s |
  | `public-booking` | create booking | 10 per 600 s |
  | `public-manage` | view booking by token | 60 per 60 s |
  | `public-cancel` | cancel booking by token | 10 per 600 s |
  | `public-payment` | start a deposit checkout by token | 10 per 600 s |
  | `payment-webhook` | provider webhooks | 600 per 60 s |
  | `payment-simulated` | Simulated checkout completion (non-Production only) | 30 per 60 s |

  Each can be tuned per environment with `RateLimiting__Policies__{policy}__PermitLimit` and `RateLimiting__Policies__{policy}__WindowSeconds`. Exceeding a limit returns 429 `RATE_LIMITED` with `Retry-After`.
- Per contact: at most 5 public booking attempts per email per hour (counter keyed by SHA-256 of the normalised email, so the address is not stored in Redis), and at most `PublicBooking__MaxActiveBookingsPerContact` (default 3) upcoming active bookings per email per venue (422 `CONTACT_BOOKING_LIMIT`).
- Idempotency is required on public booking creation.
- Redis-backed counters so limits hold across instances; fallback to in-memory per instance if Redis is unavailable.
- Staff endpoints: no aggressive limits (authenticated, per-user ceilings only to stop runaway clients).

### Public booking (Phase 8)

- **Manage token.** `base64url(HMAC-SHA256(PublicBooking:TokenKey, "booking:" + bookingId))` — 256 bits, unguessable without the key. Only `SHA-256(token)` is stored (`bookings.public_token_hash`, unique), so a database leak does not expose working links. The idempotency store keeps the confirmation with `manageToken = null`; a replay re-derives the token from the booking id. The key is dedicated (not the JWT signing key) so the Worker can build email links (Phase 9) without being able to mint access tokens. Rotating the key invalidates every outstanding manage link.
- **Token transport.** The API only accepts the token in the `X-Booking-Token` header, never in a path or query string, so it does not reach access logs, proxies or analytics. The SPA link is `/booking/manage#<token>`; URL fragments are never sent to servers or in `Referer`. Request logging records method, path and status only, never headers.
- **Bot defence.** A hidden `website` honeypot field must be empty, plus strict per-IP and per-contact limits. No CAPTCHA (no third-party script or tracking on guest pages); revisit if abuse is observed (D-070).
- **Data minimisation.** Public responses contain no internal ids, resource or table names, staff, notes or customer records. The manage view shows only the guest's first name and their own special requests. An existing customer with the same email is reused but never updated from the public form, so a third party cannot overwrite a customer's details by booking with their email.
- **Uniform 404.** Unknown slugs, inactive or suspended tenants, inactive venues and venues with online booking off all return the same 404 `VENUE_NOT_FOUND`; unknown and malformed tokens return the same 404 `BOOKING_NOT_FOUND`.
- **Audit.** Public creations and cancellations are audited with `user_id = NULL`, the caller's IP and user agent; status history uses source `PUBLIC`.

### Password reset (Phase 9)

- **No account enumeration.** `POST /auth/password/forgot` returns 202 for every well-formed address; unknown, disabled and throttled addresses get the same answer. Limits: 5 per 15 min per IP (`password-forgot`), 3 per hour per account (silent; Redis key is the SHA-256 of the normalised email), 10 resets per 15 min per IP (`password-reset`).
- **Token handling.** The Api never sees a raw token: it stores a request, and the Worker generates 256 random bits at send time and stores only `SHA-256(token)` (unique, 32 bytes). The link is `/reset-password#<token>` — a fragment, so it never reaches servers, logs or `Referer` — and the page removes it from the address bar on load. Lookup is by hash under `FOR UPDATE`, so two concurrent resets with one token cannot both succeed.
- **Lifetime.** Single use, 30 minutes. A newer request, a successful reset and a password change all void outstanding links. Expired requests are purged after 7 days.
- **Effect.** A reset sets the password (policy enforced), clears lockout, revokes every refresh token (`PASSWORD_RESET`) and issues no session — the user signs in again. Requests and resets are audited (`auth.password.reset_requested`, `auth.password.reset`) with IP and user agent.

### Email notifications (Phase 9)

- Sent only from committed outbox rows by the Worker; a failed send never affects the booking. Payloads hold ids only; the Worker loads current data and skips anonymised customers, customers without email and bookings whose state no longer matches.
- Templates HTML-encode every user-supplied value (names, special requests, venue and tenant names). Links come only from `Notifications:PublicBaseUrl` (validated absolute origin) and server-generated tokens — never from request headers.
- `notification_deliveries` stores ids, template, status and provider message id, never recipient addresses or bodies. The development `Log` provider logs a masked recipient (`t***@example.test`) and subject only, because bodies contain manage and reset links.
- SMTP credentials (`Notifications__Email__SmtpUsername` / `SmtpPassword`) are secrets; use TLS (`SmtpEnableSsl=true`, port 587) for any non-local relay.

### Payments (Phase 10)

- **No card data, ever.** Guests pay on the provider's hosted page; the platform stores amounts, statuses, provider names and opaque references only. PCI scope stays with the provider (SAQ-A style integration).
- **Webhook authentication.** Every provider implements `VerifyWebhook`. An unauthenticated delivery is rejected with 401 `INVALID_WEBHOOK_SIGNATURE` and logged as a warning without the body. The Simulated provider shows the expected shape: `HMAC-SHA256(secret, "{timestamp}.{body}")`, compared in constant time, with a 300 s replay window. Bodies are limited to 64 KB and rate limited per IP.
- **Idempotent and auditable.** Each event is claimed once by `(provider, provider_event_id)` inside the transaction that applies it. Only the body's SHA-256 is stored, together with the outcome (`payment_webhook_events`). State changes are audited (`payment.*`) with no amounts beyond the payment's own.
- **Never trust the event's numbers.** Events are matched by our payment id (the merchant reference). A wrong amount or currency, or money for a cancelled booking, flags the payment and never confirms (D-083).
- **Manage token stays with the guest.** The provider receives the payment id, amount, a description and a return URL without the token. The SPA parks the token in `sessionStorage` for that tab only and the return page puts it back in the fragment (D-085). The Simulated checkout page only follows same-origin return URLs, and its completion endpoint requires an HMAC checkout token bound to the payment.
- **Refunds** need `payment.refund`, an `Idempotency-Key`, and a refundable amount that already counts pending refunds. An unknown provider outcome stays pending and is flagged (D-086).
- **Simulated is not deployable.** It is not registered in Production, so configuring it there fails startup (D-084). Production leaves `Payments__Provider` empty until a real adapter is added; provider keys will be secrets like any other.

## 7. Secrets & configuration

- No secrets in source control. `.env.example` / `appsettings*.json` contain placeholders only.
- Development: `dotnet user-secrets` or `.env` (git-ignored). Production: platform secret store injected as env vars.
- Secrets: `Jwt__SigningKey`, `PublicBooking__TokenKey`, `ConnectionStrings__Postgres`, `ConnectionStrings__Redis`, `DEV_ADMIN_PASSWORD` (dev only), `Notifications__Email__SmtpPassword` (when the relay needs auth), `Payments__Simulated__WebhookSecret` (optional, non-Production only; random per process when unset), and the keys of a real payment provider once one is added.
- Startup fails fast if required secrets are missing or too weak (signing key and public booking token key < 32 bytes or still a `change-me` placeholder).

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

## 11. Future: integrations

- Payment webhooks: implemented in Phase 10 (see §6 Payments). A real provider adds its own signature scheme behind `IPaymentProvider.VerifyWebhook`, plus IP allow-listing where the provider publishes ranges.
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
