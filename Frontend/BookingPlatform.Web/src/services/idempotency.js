/**
 * Keeps one Idempotency-Key per logical submission: retrying the same payload (after a timeout or network error)
 * reuses the key so the server replays instead of creating a duplicate; a changed payload gets a new key.
 */
export function createIdempotencyKeeper(generate = () => crypto.randomUUID()) {
  let key = null
  let fingerprint = null

  return {
    keyFor(payload) {
      const next = JSON.stringify(payload)
      if (key === null || next !== fingerprint) {
        key = generate()
        fingerprint = next
      }
      return key
    },
    /** Call after a definitive outcome (success, or an error the user must fix) so the next submit is new. */
    reset() {
      key = null
      fingerprint = null
    },
  }
}

/** True when the failure leaves the outcome unknown, so the same key must be reused on retry. */
export function isRetryableFailure(error) {
  return error?.status === 0 || error?.status >= 500
}
