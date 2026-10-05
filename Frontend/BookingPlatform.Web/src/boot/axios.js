import { defineBoot } from '#q-app'
import { http } from '@/services/http.js'

export default defineBoot(({ app }) => {
  app.config.globalProperties.$http = http
})
