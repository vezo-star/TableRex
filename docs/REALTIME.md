# Real-time

Status: **Phase 0 — design; implemented in Phase 7.**

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
