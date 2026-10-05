import { ref } from 'vue'
import { normaliseError } from '@/utils/problemDetails.js'

/**
 * Wraps an async API call with `loading`, a user-facing `error` message and per-field `fieldErrors`
 * (from ProblemDetails `errors`). `run` resolves the result, or undefined when the call failed.
 */
export function useApiAction(action) {
  const loading = ref(false)
  const error = ref(null)
  const fieldErrors = ref({})

  async function run(...args) {
    loading.value = true
    error.value = null
    fieldErrors.value = {}
    try {
      return await action(...args)
    } catch (e) {
      const apiError = normaliseError(e)
      error.value = apiError
      fieldErrors.value = apiError.errors ?? {}
      return undefined
    } finally {
      loading.value = false
    }
  }

  function fieldError(name) {
    const messages = fieldErrors.value[name]
    return messages?.length ? messages[0] : null
  }

  function reset() {
    error.value = null
    fieldErrors.value = {}
  }

  return { loading, error, fieldErrors, fieldError, run, reset }
}
