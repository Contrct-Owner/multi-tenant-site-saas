import { defineConfig, devices } from '@playwright/test';

// The stack (Postgres, migrate + api roles with the local auth provider, the
// console dev server) is booted by tools/e2e-stack.sh, locally and in CI;
// this config only drives the browser. Sign-in is by hint: an automated run
// never types credentials (PREMISE_AUTH=local in the AppHost is the same idea).
export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL: process.env.E2E_CONSOLE ?? 'http://localhost:5173',
    trace: 'retain-on-failure',
    ...devices['Desktop Chrome'],
  },
  projects: [{ name: 'chromium' }],
});
