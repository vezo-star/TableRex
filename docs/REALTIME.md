# Real-time

Status: **Implemented (Phase 7).** See §8 for implementation notes.

## 1. Principles

- SignalR notifies clients that **state changed**. It is never the source of state.
- Events originate from committed outbox rows, so a notification is never sent for a rolled-back change, and a committed change is never silently lost.
- After any reconnect, the client resynchronises from the REST API.
- A SignalR or Redis outage degrades live updates only; bookings keep working.

## 2. Hub

- Path: `/hubs/venue`, `[Authorize]`.
- Auth: JWT bearer; for WebSockets the token is passed as `access_token` query string, accepted only for `/hubs/*` paths. Query strings are excluded from request logs.
- Client → server methods:
  - `JoinVenue(venueId)` — server checks: token has `tid`; membership active; venue belongs to `tid`; user has venue access (D-018) and `booking.view`. On success adds connection to group `tenant:{tid}:venue:{venueId}`. On failure throws `HubException("FORBIDDEN")` without revealing whether the venue exists.
  - `LeaveVenue(venueId)`.
- The tenant id in the group name always comes from the server-verified claim, never from the client.
- Tenant switch on the client → new token → new connection.
- Membership revocation: connections are re-validated on reconnect and token expiry; access tokens are short-lived (15 min), and `CloseOnAuthenticationExpiration = true` closes connections whose token expired.

## 3. Events

Server → client method names (constants in `RealtimeEvents`):

`BookingCreated`, `BookingUpdated`, `BookingCancelled`, `BookingMoved`, `BookingStatusChanged`, `ResourceUpdated`, `ResourceBlocked`, `HoldCreated`, `HoldExpired`, `FloorPlanUpdated`.

Payload (minimal, no PII):

```json
{
  "eventId": "0192…",
  "type": "BookingMoved",
  "venueId": "…",
  "entityId": "…",
  "version": 4,
  "businessDate": "2026-10-09",
  "occurredAt": "2026-10-09T15:02:11Z"
}
```

Clients use the payload to decide *what* to refetch (e.g. the affected booking, or the board for that business date) and ignore events whose `version` is older than what they already hold. Customer names, notes and contact details are fetched through the authorised API, never pushed.

## 4. Delivery path

```
Booking transaction ── writes ──► outbox_messages (destination = REALTIME) ── commit + pg_notify
        ▲                                                                       │
        │                                                                       ▼
  REST API (any instance)                         Api instance: RealtimeOutboxDispatcher (SKIP LOCKED claim)
                                                                                │
                                                               IHubContext<VenueHub>.Clients.Group(...)
                                                                                │
                                                                   Redis backplane → all Api instances
                                                                                │
                                                                     connected clients in that group
```

- Dispatcher runs inside the Api process (it owns the hub). With multiple instances, `SKIP LOCKED` ensures each message is claimed once; the Redis backplane fans it out to connections on every instance.
- Ordering is best effort; `version` lets clients discard stale events.
- Delivery is at-least-once; clients deduplicate by `eventId` (small LRU set).

## 5. Scale-out

- `AddSignalR().AddStackExchangeRedis(...)` with a channel prefix per environment.
- Load balancer: WebSockets enabled; client configured `skipNegotiation: true` + WebSockets transport so sticky sessions aren't required. If fallback transports are later needed, enable sticky sessions.
- Group size = staff connected to one venue (small); no fan-out concerns expected.

## 6. Client behaviour (Vue)

`services/realtimeService.js` + `stores/realtime.js`:

1. Build connection with `accessTokenFactory` returning the current in-memory token (refreshing if near expiry), `withAutomaticReconnect([0, 2000, 5000, 10000, 30000])`.
2. `onreconnecting` → state `reconnecting`, show "Live updates paused" indicator.
3. `onreconnected` → re-invoke `JoinVenue` for the current venue (groups don't survive a new connection id), then **resync**: refetch bookings/availability for the visible board date. Only then mark state `connected`.
4. `onclose` → state `disconnected`; start a manual retry loop with backoff; on success follow step 3.
5. Venue change → `LeaveVenue(old)`, `JoinVenue(new)`, fetch new board.
6. Browser tab regains visibility after a long sleep → resync even if the connection reports connected.

## 7. Tests (Phase 7)

- Authorised user joins own venue and receives `BookingCreated` after a booking is committed.
- User from Tenant A cannot join Tenant B's venue (call fails) and receives no Tenant B events.
- User without venue access cannot join that venue.
- Rolled-back booking emits no event.
- Booking created/updated/cancelled/moved events delivered.
- Reconnect: client rejoins and resync endpoint returns the authoritative state (frontend unit test for the store logic; E2E for two browser sessions).

## 8. Implementation notes (Phase 7)

Backend (`BookingPlatform.Api/Realtime`, `BookingPlatform.Infrastructure/Outbox`):

- `VenueHub` — WebSockets transport only, `CloseOnAuthenticationExpiration`, maximum incoming message 4 KB. `JoinVenue` re-checks on every call: membership active, `booking.view`, then `IVenueAccessGuard`. Every refusal is the same `HubException("FORBIDDEN")`.
- `HubErrorFilter` — hub exceptions pass through; anything else is logged once and replaced by `HubException("UNAVAILABLE")` so internals never reach the client. SignalR's own dispatcher logger is raised to `Fatal` so expected refusals don't log as errors.
- Redis backplane — `AddStackExchangeRedis` with channel prefix `bp-{environment}` (`Realtime:ChannelPrefix` overrides), `AbortOnConnectFail=false`. A Redis outage stops cross-instance fan-out only. Events published during the outage are lost, because their outbox rows are already marked processed. Clients stay connected to their own instance and do not switch to polling, so open boards catch up on the next event or a reload (D-097, RUNBOOKS.md §4).
- `OutboxStore` + abstract `OutboxDispatcher` (shared with the Phase 9 Worker) — rows are claimed with `FOR UPDATE SKIP LOCKED` (PENDING/FAILED that are due, or PROCESSING whose 30 s lease expired), `attempt_count` incremented on claim. Wake-up via `LISTEN outbox` with a 2 s poll fallback; the listener reconnects with backoff up to 30 s. Retry backoff `2^n` s ± 20 %, capped at 15 min; after 5 attempts the row is `DEAD`. `OutboxPermanentException` / `JsonException` dead-letter immediately.
- `RealtimeOutboxDispatcher` processes `destination = 'REALTIME'` only (NOTIFICATION rows are left for the Worker). Rows older than `Realtime:MaxAgeSeconds` (300) when claimed are marked PROCESSED with `last_error = 'Skipped: …'` — after downtime, clients resync from REST anyway, so replaying stale events would only cause refetch storms. Unknown event types or rows without a tenant are dead-lettered.
- Event producers: `BookingService` (created/updated/moved/cancelled/status changed), `HoldService` (created/expired), `BlockService` (`ResourceBlocked`), `ResourceService` (`ResourceUpdated` on create/update/delete) and `FloorPlanService` (`FloorPlanUpdated` on create/update/delete/positions).
- Settings (`Realtime` section): `DispatcherEnabled` (true), `ChannelPrefix`, `BatchSize` (100), `PollSeconds` (2), `MaxAgeSeconds` (300).

Frontend:

- `services/realtimeService.js` — connection factory (`skipNegotiation`, WebSockets, automatic reconnect `[0, 2s, 5s, 10s, 30s]`).
- `stores/realtime.js` — status (`disconnected` / `connecting` / `connected` / `reconnecting`), current/joined venue, manual retry `[2s, 5s, 10s, 30s, 60s]` after the automatic schedule gives up, re-join + resync broadcast on every (re)connect, `eventId` dedup (last 500), events for other venues ignored, resync when a tab returns after ≥ 60 s hidden. The connection object is kept out of Pinia's reactivity (`markRaw`).
- `composables/useRealtimeRefresh.js` — pages subscribe with a filter; matching events (and resyncs) are batched with a 300 ms debounce into one refresh. Returns `live` so pages fall back to 60 s polling only while disconnected.
- `components/common/RealtimeIndicator.vue` in the dashboard header — "Live", "Reconnecting…" or "Live updates paused" (click to retry now).
- Board: resource/floor-plan events reload resources; booking/hold/block events reload the board when they concern the shown date or a booking already on it. Drawer: in view mode it reloads silently; while editing it shows a "changed by someone else" banner instead of overwriting the form. Dashboard: refreshes on today's booking events.

Verified manually (2026-10-05): two browser sessions on Demo Restaurant — a booking created through the API appears on the board within ~1.5 s; a cancel in one tab updates the other; stopping the Api shows "Reconnecting…", and after restart both tabs re-join, resync and receive new events live. Tokens never appear in request logs.
