import { describe, expect, it, vi } from 'vitest'
import { HttpTransportType } from '@microsoft/signalr'
import { createVenueConnection, RECONNECT_DELAYS_MS } from '@/services/realtimeService.js'

describe('realtimeService', () => {
  it('builds a WebSocket-only connection without negotiation and with automatic reconnect', () => {
    const built = { id: 'connection' }
    const builder = {
      withUrl: vi.fn(() => builder),
      withAutomaticReconnect: vi.fn(() => builder),
      configureLogging: vi.fn(() => builder),
      build: vi.fn(() => built),
    }
    const accessTokenFactory = () => 'token'

    const connection = createVenueConnection({ accessTokenFactory, builder: () => builder })

    expect(connection).toBe(built)
    expect(builder.withUrl).toHaveBeenCalledWith('/hubs/venue', {
      accessTokenFactory,
      skipNegotiation: true,
      transport: HttpTransportType.WebSockets,
    })
    expect(builder.withAutomaticReconnect).toHaveBeenCalledWith([...RECONNECT_DELAYS_MS])
  })
})
