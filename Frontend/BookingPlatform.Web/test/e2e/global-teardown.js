import { rmSync } from 'node:fs'
import { statePath } from './support.js'

// The run's owner credentials live only for the duration of the run.
export default async function globalTeardown() {
  rmSync(statePath, { force: true })
}
