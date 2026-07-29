// @ts-check
const { defineConfig, devices } = require("@playwright/test");
const path = require("path");

const mockApiPort = Number(process.env.E2E_MOCK_API_PORT || 18080);
const webPort = Number(process.env.E2E_WEB_PORT || 13000);
const liveBase = process.env.E2E_BASE_URL || "";
const runLive = Boolean(liveBase && process.env.E2E_EMAIL && process.env.E2E_PASSWORD);

const frontendDir = path.join(__dirname, "..", "frontend");
const mockApi = path.join(__dirname, "mock-api", "server.mjs");

/** @type {import('@playwright/test').PlaywrightTestConfig} */
module.exports = defineConfig({
  testDir: path.join(__dirname, "tests"),
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  workers: 1,
  reporter: process.env.CI ? [["list"], ["html", { open: "never", outputFolder: "playwright-report" }]] : "list",
  timeout: 60_000,
  expect: { timeout: 15_000 },
  use: {
    trace: "on-first-retry",
    screenshot: "only-on-failure",
    video: "retain-on-failure",
  },
  projects: [
    {
      name: "mocked",
      testMatch: /.*\.mocked\.spec\.js/,
      use: {
        ...devices["Desktop Chrome"],
        baseURL: `http://127.0.0.1:${webPort}`,
      },
    },
    ...(runLive
      ? [
          {
            name: "live",
            testMatch: /.*\.live\.spec\.js/,
            use: {
              ...devices["Desktop Chrome"],
              baseURL: liveBase.replace(/\/$/, ""),
            },
          },
        ]
      : []),
  ],
  webServer: runLive
    ? undefined
    : {
        command: `node "${path.join(__dirname, "mock-api", "runner.mjs")}"`,
        url: `http://127.0.0.1:${webPort}`,
        reuseExistingServer: !process.env.CI,
        timeout: 60_000,
        env: {
          ...process.env,
          E2E_MOCK_API_PORT: String(mockApiPort),
          E2E_WEB_PORT: String(webPort),
        },
      },
});
