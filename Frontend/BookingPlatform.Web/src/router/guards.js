import { RouteNames } from '@/constants/navigation.js'

/**
 * Pure navigation decision so it can be unit tested. Route meta:
 * - public: reachable by anyone (status page, public booking)
 * - guestOnly: login screens; signed-in users are sent home
 * - requiresAuth: signed-in user (no tenant needed)
 * - requiresTenant: signed-in user with a selected tenant
 * - permission / anyPermission: tenant permission(s) required
 * - platformAdmin: platform administrators only
 * The server enforces all of this again; guards only shape the UI.
 */
export function resolveNavigation(to, auth) {
  const meta = to.meta ?? {}

  if (meta.public) {
    return true
  }

  if (meta.guestOnly) {
    return auth.isAuthenticated ? homeFor(auth) : true
  }

  const needsAuth =
    meta.requiresAuth ||
    meta.requiresTenant ||
    meta.permission ||
    meta.anyPermission ||
    meta.platformAdmin
  if (!needsAuth) {
    return true
  }

  if (!auth.isAuthenticated) {
    return {
      name: RouteNames.LOGIN,
      query: to.fullPath && to.fullPath !== '/' ? { redirect: to.fullPath } : {},
    }
  }

  if (meta.platformAdmin) {
    return auth.isPlatformAdmin ? true : { name: RouteNames.FORBIDDEN }
  }

  const needsTenant = meta.requiresTenant || meta.permission || meta.anyPermission
  if (needsTenant && !auth.hasTenant) {
    if (auth.requiresTenantSelection) {
      return { name: RouteNames.SELECT_TENANT, query: { redirect: to.fullPath } }
    }
    return { name: RouteNames.NO_ACCESS }
  }

  if (meta.permission && !auth.hasPermission(meta.permission)) {
    return { name: RouteNames.FORBIDDEN }
  }

  if (meta.anyPermission && !auth.hasAnyPermission(meta.anyPermission)) {
    return { name: RouteNames.FORBIDDEN }
  }

  return true
}

export function homeFor(auth) {
  if (auth.requiresTenantSelection) {
    return { name: RouteNames.SELECT_TENANT }
  }
  if (!auth.hasTenant && auth.isPlatformAdmin) {
    return { name: RouteNames.PLATFORM_TENANTS }
  }
  return { name: RouteNames.DASHBOARD }
}

/** Only same-app relative paths are accepted as post-login redirects (prevents open redirects). */
export function safeRedirect(value) {
  return typeof value === 'string' &&
    value.startsWith('/') &&
    !value.startsWith('//') &&
    !value.includes('\\')
    ? value
    : null
}

/**
 * Post-login redirect that the current user may actually open. A stale redirect (for example
 * one left behind by a previous user's session) falls back to the user's home instead of a
 * forbidden page.
 */
export function permittedRedirect(router, auth, value) {
  const path = safeRedirect(value)
  if (!path) {
    return null
  }
  const target = router.resolve(path)
  if (!target.matched.length || target.name === RouteNames.NOT_FOUND) {
    return null
  }
  return resolveNavigation(target, auth) === true ? path : null
}
