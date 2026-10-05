import { describe, expect, it } from 'vitest'
import { resolveNavigation, safeRedirect, homeFor } from '@/router/guards.js'
import { RouteNames } from '@/constants/navigation.js'

function authState(overrides = {}) {
  const permissions = new Set(overrides.permissions ?? [])
  return {
    isAuthenticated: true,
    hasTenant: true,
    requiresTenantSelection: false,
    isPlatformAdmin: false,
    hasPermission: (p) => permissions.has(p),
    hasAnyPermission: (ps) => ps.some((p) => permissions.has(p)),
    ...overrides,
  }
}

const route = (meta, fullPath = '/x') => ({ meta, fullPath })

describe('resolveNavigation', () => {
  it('lets anyone reach public routes', () => {
    expect(resolveNavigation(route({ public: true }), authState({ isAuthenticated: false }))).toBe(
      true,
    )
  })

  it('sends anonymous users to login with a redirect back', () => {
    const result = resolveNavigation(
      route({ requiresTenant: true }, '/staff?q=a'),
      authState({ isAuthenticated: false }),
    )

    expect(result).toEqual({ name: RouteNames.LOGIN, query: { redirect: '/staff?q=a' } })
  })

  it('sends signed-in users away from login', () => {
    expect(resolveNavigation(route({ guestOnly: true }), authState())).toEqual({
      name: RouteNames.DASHBOARD,
    })
  })

  it('requires tenant selection before tenant pages', () => {
    const result = resolveNavigation(
      route({ requiresTenant: true }, '/staff'),
      authState({ hasTenant: false, requiresTenantSelection: true }),
    )

    expect(result).toEqual({ name: RouteNames.SELECT_TENANT, query: { redirect: '/staff' } })
  })

  it('shows no-access for users without any membership', () => {
    const result = resolveNavigation(
      route({ requiresTenant: true }),
      authState({ hasTenant: false }),
    )

    expect(result).toEqual({ name: RouteNames.NO_ACCESS })
  })

  it('enforces permissions', () => {
    const meta = { requiresTenant: true, permission: 'staff.view' }

    expect(resolveNavigation(route(meta), authState())).toEqual({ name: RouteNames.FORBIDDEN })
    expect(resolveNavigation(route(meta), authState({ permissions: ['staff.view'] }))).toBe(true)
  })

  it('restricts platform pages to platform admins', () => {
    expect(resolveNavigation(route({ platformAdmin: true }), authState())).toEqual({
      name: RouteNames.FORBIDDEN,
    })
    expect(
      resolveNavigation(route({ platformAdmin: true }), authState({ isPlatformAdmin: true })),
    ).toBe(true)
  })

  it('routes platform admins without a tenant to platform administration', () => {
    expect(homeFor(authState({ hasTenant: false, isPlatformAdmin: true }))).toEqual({
      name: RouteNames.PLATFORM_TENANTS,
    })
  })
})

describe('safeRedirect', () => {
  it.each(['/staff', '/a/b?c=d'])('accepts in-app path %s', (value) => {
    expect(safeRedirect(value)).toBe(value)
  })

  it.each([
    'https://evil.example',
    '//evil.example',
    '/\\evil.example',
    'javascript:alert(1)',
    null,
    42,
  ])('rejects %s', (value) => {
    expect(safeRedirect(value)).toBeNull()
  })
})
