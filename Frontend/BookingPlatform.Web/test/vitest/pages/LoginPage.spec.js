import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { BlankComponent, createTestRouter, mountWithQuasar } from '../helpers/mountWithQuasar.js'
import { ApiError } from '@/utils/problemDetails.js'

vi.mock('@/services/authService.js', () => ({
  authService: { login: vi.fn(), me: vi.fn(), refresh: vi.fn(), logout: vi.fn() },
}))

const { authService } = await import('@/services/authService.js')
const { default: LoginPage } = await import('@/pages/auth/LoginPage.vue')

function router() {
  return createTestRouter([
    { path: '/login', name: 'login', component: BlankComponent },
    { path: '/forgot-password', name: 'forgot-password', component: BlankComponent },
    { path: '/', name: 'dashboard', component: BlankComponent },
    { path: '/staff', name: 'staff', component: BlankComponent },
    { path: '/select-tenant', name: 'select-tenant', component: BlankComponent },
    {
      path: '/settings',
      name: 'settings',
      component: BlankComponent,
      meta: { requiresTenant: true, permission: 'settings.manage' },
    },
  ])
}

// QInput forwards data-test to the native <input>.
function field(wrapper, name) {
  const el = wrapper.find(`[data-test="${name}"]`)
  return el.element.tagName === 'INPUT' ? el : el.find('input')
}

async function fillAndSubmit(wrapper) {
  await field(wrapper, 'login-email').setValue('ada@example.test')
  await field(wrapper, 'login-password').setValue('correct horse battery')
  await wrapper.find('[data-test="login-form"]').trigger('submit')
  await flushPromises()
}

describe('LoginPage', () => {
  beforeEach(() => vi.clearAllMocks())

  it('signs in and follows a safe redirect', async () => {
    authService.login.mockResolvedValue({
      accessToken: 'a',
      expiresAt: new Date(Date.now() + 900_000).toISOString(),
      userId: 'u',
      tenantId: 't',
      memberships: [{ tenantId: 't', tenantName: 'T', tenantSlug: 't', roles: ['HOST'] }],
    })
    authService.me.mockResolvedValue({ permissions: [], roles: ['HOST'] })
    const r = router()
    const wrapper = await mountWithQuasar(LoginPage, {
      router: r,
      pinia: createPinia(),
      initialPath: '/login?redirect=/staff',
    })

    await fillAndSubmit(wrapper)

    expect(authService.login).toHaveBeenCalledWith('ada@example.test', 'correct horse battery')
    expect(r.currentRoute.value.fullPath).toBe('/staff')
  })

  it('ignores an external redirect target', async () => {
    authService.login.mockResolvedValue({
      accessToken: 'a',
      userId: 'u',
      tenantId: 't',
      memberships: [{ tenantId: 't', roles: [] }],
    })
    authService.me.mockResolvedValue({ permissions: [], roles: [] })
    const r = router()
    const wrapper = await mountWithQuasar(LoginPage, {
      router: r,
      pinia: createPinia(),
      initialPath: '/login?redirect=https://evil.example',
    })

    await fillAndSubmit(wrapper)

    expect(r.currentRoute.value.name).toBe('dashboard')
  })

  it('goes home instead of following a redirect the user may not open', async () => {
    authService.login.mockResolvedValue({
      accessToken: 'a',
      userId: 'u',
      tenantId: 't',
      memberships: [{ tenantId: 't', roles: ['HOST'] }],
    })
    authService.me.mockResolvedValue({ permissions: ['booking.view'], roles: ['HOST'] })
    const r = router()
    const wrapper = await mountWithQuasar(LoginPage, {
      router: r,
      pinia: createPinia(),
      initialPath: '/login?redirect=/settings',
    })

    await fillAndSubmit(wrapper)

    expect(r.currentRoute.value.name).toBe('dashboard')
  })

  it('keeps the redirect through tenant selection', async () => {
    authService.login.mockResolvedValue({
      accessToken: 'a',
      userId: 'u',
      tenantId: null,
      requiresTenantSelection: true,
      memberships: [
        { tenantId: 't1', roles: ['HOST'] },
        { tenantId: 't2', roles: ['HOST'] },
      ],
    })
    authService.me.mockResolvedValue({ permissions: [], roles: [] })
    const r = router()
    const wrapper = await mountWithQuasar(LoginPage, {
      router: r,
      pinia: createPinia(),
      initialPath: '/login?redirect=/staff',
    })

    await fillAndSubmit(wrapper)

    expect(r.currentRoute.value.name).toBe('select-tenant')
    expect(r.currentRoute.value.query.redirect).toBe('/staff')
  })

  it('shows a generic message for invalid credentials and clears the password', async () => {
    authService.login.mockRejectedValue(
      new ApiError({
        status: 401,
        code: 'INVALID_CREDENTIALS',
        message: 'The email or password is incorrect.',
      }),
    )
    const wrapper = await mountWithQuasar(LoginPage, {
      router: router(),
      pinia: createPinia(),
      initialPath: '/login',
    })

    await fillAndSubmit(wrapper)

    expect(wrapper.find('[data-test="login-error"]').text()).toContain('incorrect')
    expect(field(wrapper, 'login-password').element.value).toBe('')
  })

  it('explains account lockout', async () => {
    authService.login.mockRejectedValue(
      new ApiError({ status: 423, code: 'ACCOUNT_LOCKED', message: 'Locked' }),
    )
    const wrapper = await mountWithQuasar(LoginPage, {
      router: router(),
      pinia: createPinia(),
      initialPath: '/login',
    })

    await fillAndSubmit(wrapper)

    expect(wrapper.find('[data-test="login-error"]').text()).toContain('temporarily locked')
  })
})
