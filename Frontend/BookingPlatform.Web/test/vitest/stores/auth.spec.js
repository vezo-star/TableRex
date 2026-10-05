import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'

vi.mock('@/services/authService.js', () => ({
  authService: {
    login: vi.fn(),
    refresh: vi.fn(),
    logout: vi.fn(),
    me: vi.fn(),
    switchTenant: vi.fn(),
    changePassword: vi.fn(),
  },
}))

const { authService } = await import('@/services/authService.js')
const { useAuthStore } = await import('@/stores/auth.js')

const session = (overrides = {}) => ({
  accessToken: 'access-1',
  expiresAt: new Date(Date.now() + 15 * 60_000).toISOString(),
  userId: 'u1',
  tenantId: 't1',
  requiresTenantSelection: false,
  memberships: [{ tenantId: 't1', tenantName: 'Alpha', tenantSlug: 'alpha', roles: ['MANAGER'] }],
  ...overrides,
})

const me = {
  id: 'u1',
  displayName: 'Ada',
  permissions: ['booking.view', 'staff.view'],
  roles: ['MANAGER'],
}

describe('auth store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('login applies the session and loads permissions', async () => {
    authService.login.mockResolvedValue(session())
    authService.me.mockResolvedValue(me)
    const auth = useAuthStore()

    await auth.login('ada@example.test', 'pw')

    expect(auth.isAuthenticated).toBe(true)
    expect(auth.tenantId).toBe('t1')
    expect(auth.currentTenant.tenantName).toBe('Alpha')
    expect(auth.hasPermission('staff.view')).toBe(true)
    expect(auth.hasPermission('staff.manage')).toBe(false)
  })

  it('requires tenant selection when several memberships and no tenant', async () => {
    authService.login.mockResolvedValue(
      session({
        tenantId: null,
        memberships: [
          { tenantId: 'a', roles: [] },
          { tenantId: 'b', roles: [] },
        ],
      }),
    )
    authService.me.mockResolvedValue({ ...me, permissions: [] })
    const auth = useAuthStore()

    await auth.login('ada@example.test', 'pw')

    expect(auth.requiresTenantSelection).toBe(true)
    expect(auth.hasTenant).toBe(false)
  })

  it('concurrent refresh calls share one request', async () => {
    authService.refresh.mockImplementation(async () => {
      await new Promise((resolve) => setTimeout(resolve, 5))
      return session({ accessToken: 'access-2' })
    })
    const auth = useAuthStore()

    const [a, b] = await Promise.all([auth.refresh(), auth.refresh()])

    expect(a).toBe('access-2')
    expect(b).toBe('access-2')
    expect(authService.refresh).toHaveBeenCalledTimes(1)
  })

  it('failed refresh clears the session and resolves null', async () => {
    authService.login.mockResolvedValue(session())
    authService.me.mockResolvedValue(me)
    authService.refresh.mockRejectedValue(new Error('401'))
    const auth = useAuthStore()
    await auth.login('a@b.c', 'pw')

    const token = await auth.refresh()

    expect(token).toBeNull()
    expect(auth.isAuthenticated).toBe(false)
    expect(auth.me).toBeNull()
  })

  it('initialize performs a silent refresh once', async () => {
    authService.refresh.mockResolvedValue(session())
    authService.me.mockResolvedValue(me)
    const auth = useAuthStore()

    await Promise.all([auth.initialize(), auth.initialize()])
    await auth.initialize()

    expect(authService.refresh).toHaveBeenCalledTimes(1)
    expect(auth.initialized).toBe(true)
    expect(auth.isAuthenticated).toBe(true)
  })

  it('logout clears local state even if the server call fails', async () => {
    authService.login.mockResolvedValue(session())
    authService.me.mockResolvedValue(me)
    authService.logout.mockRejectedValue(new Error('offline'))
    const auth = useAuthStore()
    await auth.login('a@b.c', 'pw')

    await auth.logout()

    expect(auth.isAuthenticated).toBe(false)
    expect(auth.accessToken).toBeNull()
  })
})
