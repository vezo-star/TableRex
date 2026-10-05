import { describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import routes from '@/router/routes.js'
import { RouteNames } from '@/constants/navigation.js'
import { Permissions } from '@/constants/permissions.js'

const createAppRouter = () => createRouter({ history: createMemoryHistory(), routes })

describe('routes', () => {
  it('resolves / to the dashboard inside the dashboard layout and requires a tenant', async () => {
    const router = createAppRouter()

    await router.push('/')

    const route = router.currentRoute.value
    expect(route.name).toBe(RouteNames.DASHBOARD)
    expect(route.matched).toHaveLength(2)
    expect(route.meta.requiresTenant).toBe(true)
  })

  it.each([
    ['/login', RouteNames.LOGIN, 'guestOnly'],
    ['/select-tenant', RouteNames.SELECT_TENANT, 'requiresAuth'],
    ['/system-status', RouteNames.SYSTEM_STATUS, 'public'],
    ['/account', RouteNames.ACCOUNT, 'requiresAuth'],
    ['/platform/tenants', RouteNames.PLATFORM_TENANTS, 'platformAdmin'],
  ])('resolves %s with the expected access meta', async (path, name, metaKey) => {
    const router = createAppRouter()

    await router.push(path)

    expect(router.currentRoute.value.name).toBe(name)
    expect(router.currentRoute.value.meta[metaKey]).toBe(true)
  })

  it('protects staff and settings pages with permissions', async () => {
    const router = createAppRouter()

    expect(router.resolve('/staff').meta.permission).toBe(Permissions.STAFF_VIEW)
    expect(router.resolve('/settings').meta.permission).toBe(Permissions.SETTINGS_MANAGE)
  })

  it.each([
    ['/venues', RouteNames.VENUES, Permissions.VENUE_VIEW],
    ['/venues/abc', RouteNames.VENUE_SETTINGS, Permissions.VENUE_VIEW],
    ['/resources', RouteNames.RESOURCES, Permissions.RESOURCE_VIEW],
    ['/floor-plans', RouteNames.FLOOR_PLANS, Permissions.FLOORPLAN_VIEW],
  ])('protects %s with a view permission', (path, name, permission) => {
    const route = createAppRouter().resolve(path)

    expect(route.name).toBe(name)
    expect(route.meta.permission).toBe(permission)
    expect(route.meta.requiresTenant).toBe(true)
  })

  it.each(['/does-not-exist', '/deeply/nested/unknown'])(
    'resolves %s to not-found',
    async (path) => {
      const router = createAppRouter()

      await router.push(path)

      expect(router.currentRoute.value.name).toBe(RouteNames.NOT_FOUND)
    },
  )

  it('loads every route component', async () => {
    const loaders = routes
      .flatMap((r) => [r, ...(r.children ?? [])])
      .map((r) => r.component)
      .filter((c) => typeof c === 'function')

    const modules = await Promise.all(loaders.map((load) => load()))

    for (const module of modules) {
      expect(module.default).toBeTruthy()
    }
  })
})
