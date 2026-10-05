import { describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import routes from '@/router/routes.js'
import { RouteNames } from '@/constants/navigation.js'

const createAppRouter = () => createRouter({ history: createMemoryHistory(), routes })

describe('routes', () => {
  it('resolves / to the system status page inside the dashboard layout', async () => {
    const router = createAppRouter()

    await router.push('/')

    const route = router.currentRoute.value
    expect(route.name).toBe(RouteNames.SYSTEM_STATUS)
    expect(route.matched).toHaveLength(2)
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
