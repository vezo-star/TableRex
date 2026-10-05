import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { BlankComponent, createTestRouter, mountWithQuasar } from '../helpers/mountWithQuasar.js'
import { ApiError } from '@/utils/problemDetails.js'

vi.mock('@/services/authService.js', () => ({
  authService: { forgotPassword: vi.fn(), resetPassword: vi.fn() },
}))

const { authService } = await import('@/services/authService.js')
const { default: ForgotPasswordPage } = await import('@/pages/auth/ForgotPasswordPage.vue')
const { default: ResetPasswordPage } = await import('@/pages/auth/ResetPasswordPage.vue')

function router() {
  return createTestRouter([
    { path: '/login', name: 'login', component: BlankComponent },
    { path: '/forgot-password', name: 'forgot-password', component: BlankComponent },
    { path: '/reset-password', name: 'reset-password', component: BlankComponent },
  ])
}

function field(wrapper, name) {
  const el = wrapper.find(`[data-test="${name}"]`)
  return el.element.tagName === 'INPUT' ? el : el.find('input')
}

const TOKEN = 'AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_abcde'

describe('ForgotPasswordPage', () => {
  beforeEach(() => vi.clearAllMocks())

  it('shows the same confirmation whether or not the account exists', async () => {
    authService.forgotPassword.mockResolvedValue(undefined)
    const wrapper = await mountWithQuasar(ForgotPasswordPage, {
      router: router(),
      pinia: createPinia(),
      initialPath: '/forgot-password',
    })

    await field(wrapper, 'forgot-email').setValue('ada@example.test')
    await wrapper.find('[data-test="forgot-form"]').trigger('submit')
    await flushPromises()

    expect(authService.forgotPassword).toHaveBeenCalledWith('ada@example.test')
    const sent = wrapper.find('[data-test="forgot-sent"]')
    expect(sent.text()).toContain('If an account exists for ada@example.test')
    expect(wrapper.find('[data-test="forgot-form"]').exists()).toBe(false)
  })

  it('explains rate limiting', async () => {
    authService.forgotPassword.mockRejectedValue(
      new ApiError({ status: 429, code: 'RATE_LIMITED', message: 'Too many requests' }),
    )
    const wrapper = await mountWithQuasar(ForgotPasswordPage, {
      router: router(),
      pinia: createPinia(),
      initialPath: '/forgot-password',
    })

    await field(wrapper, 'forgot-email').setValue('ada@example.test')
    await wrapper.find('[data-test="forgot-form"]').trigger('submit')
    await flushPromises()

    expect(wrapper.find('[data-test="forgot-error"]').text()).toContain('wait a few minutes')
  })
})

describe('ResetPasswordPage', () => {
  beforeEach(() => vi.clearAllMocks())

  async function mountReset(hash = `#${TOKEN}`) {
    const r = router()
    const wrapper = await mountWithQuasar(ResetPasswordPage, {
      router: r,
      pinia: createPinia(),
      initialPath: `/reset-password${hash}`,
    })
    await flushPromises()
    return { wrapper, r }
  }

  async function submit(wrapper, password = 'Brand-New-Passphrase-42', confirm = password) {
    await field(wrapper, 'reset-new').setValue(password)
    await field(wrapper, 'reset-confirm').setValue(confirm)
    await wrapper.find('[data-test="reset-form"]').trigger('submit')
    await flushPromises()
  }

  it('takes the token from the fragment, removes it from the URL and resets the password', async () => {
    authService.resetPassword.mockResolvedValue(undefined)
    const { wrapper, r } = await mountReset()

    expect(r.currentRoute.value.hash).toBe('')
    await submit(wrapper)

    expect(authService.resetPassword).toHaveBeenCalledWith(TOKEN, 'Brand-New-Passphrase-42')
    expect(wrapper.find('[data-test="reset-done"]').exists()).toBe(true)
  })

  it('does not submit when the confirmation differs', async () => {
    const { wrapper } = await mountReset()
    await submit(wrapper, 'Brand-New-Passphrase-42', 'Something-Else-42')
    expect(authService.resetPassword).not.toHaveBeenCalled()
  })

  it('offers a new link when the token is missing or rejected', async () => {
    const missing = await mountReset('')
    expect(missing.wrapper.find('[data-test="reset-invalid"]').exists()).toBe(true)

    authService.resetPassword.mockRejectedValue(
      new ApiError({ status: 400, code: 'INVALID_RESET_TOKEN', message: 'Invalid' }),
    )
    const { wrapper } = await mountReset()
    await submit(wrapper)
    expect(wrapper.find('[data-test="reset-invalid"]').exists()).toBe(true)
    expect(wrapper.find('[data-test="reset-request-new"]').exists()).toBe(true)
  })

  it('shows password policy errors next to the field', async () => {
    authService.resetPassword.mockRejectedValue(
      new ApiError({
        status: 400,
        code: 'VALIDATION_ERROR',
        message: 'One or more validation errors occurred.',
        errors: { newPassword: ['Password must be at least 12 characters.'] },
      }),
    )
    const { wrapper } = await mountReset()
    await submit(wrapper, 'short')
    expect(wrapper.text()).toContain('Password must be at least 12 characters.')
    expect(wrapper.find('[data-test="reset-form"]').exists()).toBe(true)
  })
})
