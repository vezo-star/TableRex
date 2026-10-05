import { describe, expect, it, vi } from 'vitest'
import { flushPromises } from '@vue/test-utils'
import { mountWithQuasar } from '../helpers/mountWithQuasar.js'

vi.mock('@/services/venueService.js', () => ({
  venueService: { createResource: vi.fn(), updateResource: vi.fn() },
}))

const { default: ResourceDialog } = await import('@/components/resources/ResourceDialog.vue')

async function open(types) {
  const wrapper = await mountWithQuasar(ResourceDialog, {
    props: { modelValue: false, venueId: 'v1', types },
  })
  await wrapper.setProps({ modelValue: true })
  await flushPromises()
  return wrapper
}

describe('ResourceDialog', () => {
  it('guides a new tenant to create a resource type before adding resources', async () => {
    const wrapper = await open([])

    const banner = document.body.querySelector('[data-test="no-types-banner"]')
    expect(banner?.textContent).toContain('Create one first')
    const save = [...document.body.querySelectorAll('button')].find((b) =>
      b.textContent.includes('Save'),
    )
    expect(save.disabled).toBe(true)

    document.body.querySelector('[data-test="create-type-link"]').click()
    expect(wrapper.emitted('manage-types')).toHaveLength(1)
    wrapper.unmount()
  })

  it('preselects the first active type when types exist', async () => {
    const wrapper = await open([
      { id: 'old', name: 'Old', isActive: false },
      { id: 'table', name: 'Table', isActive: true },
    ])

    expect(document.body.querySelector('[data-test="no-types-banner"]')).toBeNull()
    expect(wrapper.vm.form.resourceTypeId).toBe('table')
    wrapper.unmount()
  })
})
