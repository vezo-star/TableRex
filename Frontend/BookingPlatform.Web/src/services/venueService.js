import { http } from '@/services/http.js'

const id = (value) => encodeURIComponent(value)

export const venueService = {
  async listVenues() {
    const { data } = await http.get('/v1/venues')
    return data
  },

  async getVenue(venueId) {
    const { data } = await http.get(`/v1/venues/${id(venueId)}`)
    return data
  },

  async createVenue(payload) {
    const { data } = await http.post('/v1/venues', payload)
    return data
  },

  async updateVenue(venueId, payload) {
    const { data } = await http.put(`/v1/venues/${id(venueId)}`, payload)
    return data
  },

  async listSections(venueId) {
    const { data } = await http.get(`/v1/venues/${id(venueId)}/sections`)
    return data
  },

  async createSection(venueId, payload) {
    const { data } = await http.post(`/v1/venues/${id(venueId)}/sections`, payload)
    return data
  },

  async updateSection(venueId, sectionId, payload) {
    const { data } = await http.put(`/v1/venues/${id(venueId)}/sections/${id(sectionId)}`, payload)
    return data
  },

  async deleteSection(venueId, sectionId) {
    const { data } = await http.delete(`/v1/venues/${id(venueId)}/sections/${id(sectionId)}`)
    return data
  },

  async getOperatingHours(venueId) {
    const { data } = await http.get(`/v1/venues/${id(venueId)}/operating-hours`)
    return data
  },

  async replaceOperatingHours(venueId, periods) {
    const { data } = await http.put(`/v1/venues/${id(venueId)}/operating-hours`, { periods })
    return data
  },

  async listSpecialHours(venueId, { from, to } = {}) {
    const { data } = await http.get(`/v1/venues/${id(venueId)}/special-hours`, {
      params: { from, to },
    })
    return data
  },

  async createSpecialHours(venueId, payload) {
    const { data } = await http.post(`/v1/venues/${id(venueId)}/special-hours`, payload)
    return data
  },

  async updateSpecialHours(venueId, entryId, payload) {
    const { data } = await http.put(
      `/v1/venues/${id(venueId)}/special-hours/${id(entryId)}`,
      payload,
    )
    return data
  },

  async deleteSpecialHours(venueId, entryId) {
    await http.delete(`/v1/venues/${id(venueId)}/special-hours/${id(entryId)}`)
  },

  async getSchedule(venueId, from, to) {
    const { data } = await http.get(`/v1/venues/${id(venueId)}/schedule`, { params: { from, to } })
    return data
  },

  async listResourceTypes() {
    const { data } = await http.get('/v1/resource-types')
    return data
  },

  async createResourceType(payload) {
    const { data } = await http.post('/v1/resource-types', payload)
    return data
  },

  async updateResourceType(typeId, payload) {
    const { data } = await http.put(`/v1/resource-types/${id(typeId)}`, payload)
    return data
  },

  async listResources(
    { venueId, sectionId, resourceTypeId, isActive, page = 1, pageSize = 100 } = {},
    { signal } = {},
  ) {
    const { data } = await http.get('/v1/resources', {
      params: { venueId, sectionId, resourceTypeId, isActive, page, pageSize },
      signal,
    })
    return data
  },

  /** Every resource of a venue, following pages (the API caps page size at 100). */
  async listAllResources(venueId, { signal } = {}) {
    const items = []
    for (let page = 1; ; page++) {
      const result = await venueService.listResources({ venueId, page, pageSize: 100 }, { signal })
      items.push(...result.items)
      if (items.length >= result.totalCount || result.items.length === 0) {
        return items
      }
    }
  },

  async createResource(payload) {
    const { data } = await http.post('/v1/resources', payload)
    return data
  },

  async updateResource(resourceId, payload) {
    const { data } = await http.put(`/v1/resources/${id(resourceId)}`, payload)
    return data
  },

  async deleteResource(resourceId) {
    const { data } = await http.delete(`/v1/resources/${id(resourceId)}`)
    return data
  },

  async listCombinations(venueId) {
    const { data } = await http.get('/v1/resource-combinations', { params: { venueId } })
    return data
  },

  async createCombination(payload) {
    const { data } = await http.post('/v1/resource-combinations', payload)
    return data
  },

  async updateCombination(combinationId, payload) {
    const { data } = await http.put(`/v1/resource-combinations/${id(combinationId)}`, payload)
    return data
  },

  async deleteCombination(combinationId) {
    const { data } = await http.delete(`/v1/resource-combinations/${id(combinationId)}`)
    return data
  },

  async listFloorPlans(venueId) {
    const { data } = await http.get('/v1/floor-plans', { params: { venueId } })
    return data
  },

  async createFloorPlan(payload) {
    const { data } = await http.post('/v1/floor-plans', payload)
    return data
  },

  async updateFloorPlan(planId, payload) {
    const { data } = await http.put(`/v1/floor-plans/${id(planId)}`, payload)
    return data
  },

  async deleteFloorPlan(planId) {
    await http.delete(`/v1/floor-plans/${id(planId)}`)
  },

  async replaceFloorPlanPositions(planId, positions) {
    const { data } = await http.put(`/v1/floor-plans/${id(planId)}/resources`, { positions })
    return data
  },

  async updateStaffVenues(tenantUserId, venueIds) {
    const { data } = await http.put(`/v1/staff/${id(tenantUserId)}/venues`, { venueIds })
    return data
  },
}
