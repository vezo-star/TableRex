import { defineBoot } from '#q-app'
import { authBridge } from '@/services/http.js'
import { useAuthStore } from '@/stores/auth.js'
import { RouteNames } from '@/constants/navigation.js'

export default defineBoot(({ router, store }) => {
  const auth = useAuthStore(store)

  const toLogin = () => {
    const current = router.currentRoute.value
    if (current.meta?.public || current.name === RouteNames.LOGIN) {
      return
    }
    router.replace({ name: RouteNames.LOGIN, query: { redirect: current.fullPath } })
  }

  authBridge.getAccessToken = () => auth.accessToken
  authBridge.refreshAccessToken = () => auth.refresh()
  authBridge.onUnauthorized = () => {
    auth.clear()
    toLogin()
  }
  auth.onSignedOut = toLogin
})
