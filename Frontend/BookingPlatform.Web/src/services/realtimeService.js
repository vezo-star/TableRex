import { HttpTransportType, HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { VENUE_HUB_PATH } from '@/constants/realtimeEvents.js'

/** Automatic reconnect schedule; after it is exhausted the realtime store keeps retrying with its own backoff. */
export const RECONNECT_DELAYS_MS = Object.freeze([0, 2_000, 5_000, 10_000, 30_000])

/**
 * Builds (does not start) the venue hub connection. WebSockets only and no negotiation, so no sticky sessions are
 * needed; the token travels as `access_token`, which the Api accepts only on `/hubs/*` (REALTIME.md §2, §5).
 */
export function createVenueConnection({
  accessTokenFactory,
  url = VENUE_HUB_PATH,
  builder = () => new HubConnectionBuilder(),
} = {}) {
  return builder()
    .withUrl(url, {
      accessTokenFactory,
      skipNegotiation: true,
      transport: HttpTransportType.WebSockets,
    })
    .withAutomaticReconnect([...RECONNECT_DELAYS_MS])
    .configureLogging(LogLevel.Warning)
    .build()
}
