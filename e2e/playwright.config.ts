import { defineConfig, devices } from "@playwright/test";

const baseURL = process.env.E2E_BASE_URL ?? "http://localhost:3000";

// The tests book and cancel, and sign in with the public demo password: they run against the local stack only.
if (!/^https?:\/\/(localhost|127\.0\.0\.1)(:\d+)?$/.test(baseURL)) {
  throw new Error(`Refusing to run the E2E tests against ${baseURL}: they only run against the local stack.`);
}

export default defineConfig({
  testDir: "./tests",
  // One worker, in file order: the flows share one database and one client address (rate limits), and a few stable flows
  // are worth more than a fast, flaky suite.
  fullyParallel: false,
  workers: 1,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 1 : 0,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: process.env.CI ? [["github"], ["list"], ["html", { open: "never" }]] : [["list"]],
  use: {
    baseURL,
    locale: "pt-PT",
    timezoneId: "Europe/Lisbon",
    actionTimeout: 15_000,
    navigationTimeout: 30_000,
    trace: "on-first-retry",
    video: "on-first-retry",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
});
