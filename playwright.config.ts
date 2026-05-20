import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests/playwright',
  timeout: 180_000,
  fullyParallel: false,
  workers: 1,
  use: {
    baseURL: 'http://localhost:5000',
    headless: true
  }
});
