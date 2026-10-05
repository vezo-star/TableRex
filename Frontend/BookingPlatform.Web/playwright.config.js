import { defineConfig, devices } from '@playwright/test'

// End-to-end journeys against a running deployment (deploy/compose.yml or a staging environment).
// The suite provisions its own tenant through the platform-admin API: never point it at production.
//
//   E2E_BASE_URL=http://localhost:8088 E2E_ADMIN_EMAIL=... E2E_ADMIN_PASSWORD=... npm run test:e2e
export default defineConfig({
  testDir: 'test/e2e',
  globalSetup: './test/e2e/global-setup.js',
  globalTeardown: './test/e2e/global-teardown.js',
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  timeout: 45_000,
  expect: { timeout: 10_000 },
  reporter: process.env.CI
    ? [['list'], ['junit', { outputFile: 'test-results/e2e-junit.xml' }]]
    : [['list']],
  use: {
    baseURL: (process.env.E2E_BASE_URL ?? 'http://localhost:8088').replace(/\/$/, ''),
    testIdAttribute: 'data-test',
    actionTimeout: 15_000,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    { name: 'desktop', use: { ...devices['Desktop Chrome'] }, testIgnore: /public\.spec\.js/ },
    { name: 'mobile', use: { ...devices['Pixel 7'] }, testMatch: /public\.spec\.js/ },
  ],
})
