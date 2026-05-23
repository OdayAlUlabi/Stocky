import { defineConfig, devices } from '@playwright/test';

/**
 * E2E test configuration.
 * Tests target the locally running Vite dev server (http://localhost:5173).
 * The API must also be running at http://localhost:5170 for full-stack tests.
 *
 * For smoke tests that don't require the API, only the frontend needs to be up.
 */
export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: 'html',
  use: {
    baseURL: 'http://localhost:5173',
    trace: 'on-first-retry',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});
