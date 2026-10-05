import { h } from 'vue'
import { flushPromises } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mountWithQuasar } from '../helpers/mountWithQuasar.js'
import { ApiError } from '@/utils/problemDetails.js'
import { ErrorCodes } from '@/constants/errorCodes.js'

vi.mock('@/services/systemService.js', () => ({ getReadiness: vi.fn() }))

const { getReadiness } = await import('@/services/systemService.js')
const { default: SystemStatusPage } = await import('@/pages/SystemStatusPage.vue')
const { QLayout, QPageContainer } = await import('quasar')

// QPage must be rendered inside QLayout > QPageContainer.
const Host = {
  render: () => h(QLayout, null, () => h(QPageContainer, null, () => h(SystemStatusPage))),
}

describe('SystemStatusPage', () => {
  beforeEach(() => {
    getReadiness.mockReset()
  })

  it('renders each dependency check from the readiness report', async () => {
    getReadiness.mockResolvedValue({
      status: 'Unhealthy',
      checks: [
        { name: 'postgres', status: 'Healthy', durationMs: 1.52 },
        { name: 'redis', status: 'Unhealthy', durationMs: 1000.4 },
      ],
    })

    const wrapper = await mountWithQuasar(Host)
    await flushPromises()

    expect(wrapper.find('[data-test="overall-status"]').text()).toContain('Unhealthy')
    const checks = wrapper.findAll('[data-test="check"]')
    expect(checks).toHaveLength(2)
    expect(checks[0].text()).toContain('postgres')
    expect(checks[0].text()).toContain('Healthy')
    expect(checks[1].text()).toContain('redis')
    expect(checks[1].text()).toContain('Unhealthy')
  })

  it('shows the error state with the trace reference and retries on demand', async () => {
    getReadiness
      .mockRejectedValueOnce(
        new ApiError({
          status: 0,
          code: ErrorCodes.NETWORK_ERROR,
          message: 'The server could not be reached.',
          traceId: 'trace-123',
        }),
      )
      .mockResolvedValueOnce({ status: 'Healthy', checks: [] })

    const wrapper = await mountWithQuasar(Host)
    await flushPromises()

    expect(wrapper.find('[data-test="error-state"]').text()).toContain(
      'The server could not be reached.',
    )
    expect(wrapper.find('[data-test="error-trace-id"]').text()).toContain('trace-123')

    await wrapper.find('[data-test="error-retry"]').trigger('click')
    await flushPromises()

    expect(getReadiness).toHaveBeenCalledTimes(2)
    expect(wrapper.find('[data-test="error-state"]').exists()).toBe(false)
    expect(wrapper.find('[data-test="empty-state"]').exists()).toBe(true)
  })
})
