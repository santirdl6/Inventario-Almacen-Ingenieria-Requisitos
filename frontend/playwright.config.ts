import { defineConfig, devices } from '@playwright/test';
import { resolve } from 'node:path';

export default defineConfig({
  testDir: resolve(__dirname, 'e2e'),
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: !!process.env['CI'],
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: 'list',
  outputDir: 'test-results',
  use: {
    ...(process.env['HU08_E2E_BROWSER_CHANNEL'] === 'chrome' ? { channel: 'chrome' } : {}),
    baseURL: process.env['HU08_E2E_BASE_URL'] ?? 'http://localhost:8082',
    trace: 'retain-on-failure'
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }]
});
