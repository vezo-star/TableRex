# Runbooks

Operational procedures for the reference deployment (`deploy/compose.yml`, DEPLOYMENT.md §2). On other platforms the
steps are the same; only the commands that start containers differ. Shorthands used below:

```bash
dc="docker compose -f deploy/compose.yml --env-file deploy/.env"
psql_owner() { $dc exec -T postgres sh -c 'psql -X -U "$POSTGRES_USER" -d "$POSTGRES_DB"'; }   # reads SQL from stdin
```

Ground rules: never run `DROP DATABASE`, `TRUNCATE` or destructive SQL against production without a fresh backup and a
second person; never paste secrets, tokens or guest data into tickets or chat; record what you did with timestamps.

---

## 1. Deploy and roll back

**Deploy**

1. Confirm the release pipeline is green and `package:images` pushed the SHA you are deploying.
2. Take a snapshot (or confirm PITR is healthy) — migrations are expand-only, but a snapshot makes any surprise cheap.
3. Set `IMAGE_TAG=<sha>` in `deploy/.env` and run `$dc up -d`. Compose runs the migrator first; Api and Worker start only if it exits 0.
4. Run `$dc exec proxy nginx -s reload` so the edge resolves the new Api container at once (otherwise it can serve 502 for up to ~50 s; DEPLOYMENT.md §2.6).
5. Verify: `curl -fsS https://<host>/health/ready` is `Healthy`; `$dc logs --since 5m api worker | grep -E '"@l":"(Error|Fatal)"'` is empty; sign in and open the booking board.

**Roll back**

1. Set `IMAGE_TAG` to the previous SHA, `$dc up -d`, then `$dc exec proxy nginx -s reload`. The old migrator ignores journal entries it does not know; expand-only migrations keep the old code working.
2. If the new release wrote data the old code cannot read (should not happen under expand-only), stop and escalate instead of editing data by hand.
3. Never "un-apply" a migration by deleting journal rows or dropping objects. Fix forward with a new migration.

## 2. Migration failure

Symptoms: the `migrator` container exits non-zero; Api and Worker keep running the previous version (Compose does not replace them).

1. Read the error: `$dc logs migrator | tail -n 50`. The log names the failing script; each script runs in its own transaction, so the failed one left nothing behind and earlier ones are committed.
2. Common causes: a lock held by long-running queries (retry off-peak), data violating a new constraint (write a data fix as a new migration, reviewed), or the owner role lacking `CREATEROLE` for app-role provisioning (grant it or provision the role manually with the same statements as `AppRoleProvisioner`).
3. Fix in code, release a new image, deploy again. Do not hand-edit `schema_versions`.
4. If the previous version must keep running for a while, leave it: the database is at the last successfully applied script, which that version already tolerates.

## 3. Outbox: stuck messages and dead letters

The outbox carries realtime hints (Api) and notifications (Worker). Delivery is retried with backoff and then marked `DEAD`.

```sql
-- health overview
SELECT destination, status, count(*), min(created_at) AS oldest
FROM outbox_messages WHERE status <> 'PROCESSED' GROUP BY 1, 2 ORDER BY 1, 2;

-- dead letters with their last error (no payload PII: payloads hold ids only, D-052)
SELECT id, tenant_id, destination, type, attempt_count, created_at, last_error
FROM outbox_messages WHERE status = 'DEAD' ORDER BY created_at DESC LIMIT 50;
```

- `PROCESSING` rows with `locked_until` in the past are reclaimed automatically by the next dispatcher pass.
- `PENDING`/`FAILED` NOTIFICATION rows piling up: check the Worker is running and its logs (`$dc logs --since 15m worker`). SMTP outages show as retries; permanent recipient rejections go straight to `DEAD`.
- **Requeue** after fixing the cause (for example, SMTP credentials). Notifications are de-duplicated per message, so a message that was actually sent is not sent twice:

```sql
UPDATE outbox_messages
SET status = 'PENDING', attempt_count = 0, available_at = now(), last_error = NULL
WHERE status = 'DEAD' AND destination = 'NOTIFICATION' AND id = '<id>';   -- or a reviewed set of ids
```

- Do not requeue old `REALTIME` rows: they are hints and are skipped when older than 5 minutes anyway (D-062).
- Payments needing staff action: `SELECT id, tenant_id, booking_id, status, failure_reason FROM payments WHERE requires_attention;`

## 4. Redis outage

Effect (D-097): the Api keeps serving; readiness reports `Degraded` (HTTP 200). Rate limits fall back to per-instance memory (a warning is logged once), and realtime hints are dropped, so open boards can miss updates.

1. Confirm: `curl -s https://<host>/health/ready` shows `"redis","status":"Degraded"`; `$dc ps redis`.
2. Restart or fail over Redis (`$dc up -d redis` on the reference stack). No data needs restoring.
3. After recovery, readiness returns to `Healthy` without restarting the Api. Ask staff with the board open to reload it once.

## 5. PostgreSQL outage, failover or restore

**Outage / failover** (managed service): readiness returns 503 and the load balancer stops sending traffic; the Api answers 503 problem details meanwhile. When the provider fails over, Npgsql reconnects automatically; no restart is needed unless the host name changed (then update the connection strings and redeploy).

**Restore** (data corruption or accidental deletion):

1. Stop writes: `$dc stop api worker` (guests see the 502/503 page; tell venues by phone/email).
2. Restore to a **new** server or database using PITR to just before the incident (or the latest `pg_dump`, DEPLOYMENT.md §3). Never restore over the live database.
3. Point `POSTGRES_*` at the restored server, run `$dc run --rm migrator migrate` (re-applies grants and provisions the app role), then `$dc up -d`.
4. Bookings created between the restore point and the outage are lost. Export them from the old server first if it is readable (`SELECT reference, business_date, … FROM bookings WHERE created_at > '<restore point>'`) and give venues the list so they can re-enter or contact guests.
5. Write up the incident; schedule the next restore drill sooner.

## 6. Secret rotation

Change the value in the secret store / `deploy/.env`, then `$dc up -d` (recreates only the services whose environment changed). Rotate immediately if a secret may have leaked; otherwise yearly.

| Secret | Effect of rotation | Notes |
|---|---|---|
| `JWT_SIGNING_KEY` | Outstanding access tokens (≤ 15 min) become invalid; the SPA silently refreshes with its cookie, so users stay signed in. | Single key, no dual-key window needed. |
| Refresh sessions (suspected theft) | — | `UPDATE refresh_tokens SET revoked_at = now(), revoked_reason = 'ADMIN_REVOKED' WHERE revoked_at IS NULL [AND user_id = '<id>'];` signs those users out on their next refresh. |
| `PUBLIC_BOOKING_TOKEN_KEY` | Every guest manage link (emails, saved links) stops working. Guests must contact the venue to change bookings. | Same value in Api and Worker. Rotate only for a real leak. |
| `APP_DB_PASSWORD` | The migrator updates the role's password on `up`; Api and Worker reconnect with the new one. | A brief window of failed connections while containers are recreated. |
| `POSTGRES_PASSWORD` (owner) | Used by the migrator only. | Change it in PostgreSQL first (`ALTER ROLE … PASSWORD`), then in `deploy/.env`. |
| `REDIS_PASSWORD` | Redis and clients restart together; a few seconds of `Degraded`. | |
| SMTP credentials | Worker retries until the new value is deployed; nothing is lost. | |
| Platform admin password | Use "Forgot password" or change it from the account page. | `bootstrap-admin` never changes an existing password. |

## 7. Compromised account

- **Staff member:** a tenant owner or admin deactivates them on the Staff page (revokes their sessions for that tenant immediately).
- **Any user, platform-wide:** `UPDATE users SET status = 'DISABLED', updated_at = now() WHERE email = lower('<email>')::citext;` then revoke their refresh tokens (§6). User and membership status are checked on every request through a 60-second cache, so access ends within 60 seconds on every Api instance (immediately on a single instance after a restart: `$dc restart api`).
- **Locked out by failed logins:** lockout clears itself (exponential, max 60 min); a password reset also clears it.

## 8. Suspected tenant data exposure

Treat as a security incident from the first report.

1. **Contain.** If a code path is suspected, disable it (deploy the previous release, or suspend the affected tenant: `UPDATE tenants SET status = 'SUSPENDED', updated_at = now() WHERE slug = '<slug>';` — staff access stops within 60 seconds (membership cache) and its public pages stop taking bookings). Revoke sessions of involved accounts (§6–7).
2. **Preserve evidence.** Export the relevant logs (they hold correlation ids, user ids and paths, never tokens or full emails) and `audit_logs` rows for the tenants involved before retention removes them. Do not modify the data under investigation.
3. **Scope.** Use `audit_logs` (who did what, per tenant) and request logs filtered by `UserId`/`TenantId`/`CorrelationId` to establish which records were read or changed, by whom, and when. Reproduce the suspected path in staging with two tenants (the isolation tests in `Backend/Tests/BookingPlatform.IntegrationTests` show the pattern).
4. **Fix** with a regression test in the isolation suite, release, and verify in production.
5. **Notify.** Personal data breaches may require notifying the regulator and affected people within strict deadlines (South Africa POPIA s22: as soon as reasonably possible; GDPR: 72 hours). Involve the responsible person/legal early.
6. **Review** whether D-096 (Row-Level Security) should now be enabled.
