// @ts-check
/**
 * Live stack specs — only run when E2E_BASE_URL, E2E_EMAIL, E2E_PASSWORD are set
 * (see playwright.config.js project "live").
 */
const { test, expect } = require("@playwright/test");

const email = process.env.E2E_EMAIL || "";
const password = process.env.E2E_PASSWORD || "";

test.describe("Live stack login", () => {
  test("signs in with real credentials and lands on calendar", async ({ page }) => {
    test.skip(!email || !password, "Set E2E_EMAIL and E2E_PASSWORD for live tests");

    await page.goto("/login");
    await page.locator("#email").fill(email);
    await page.locator("#password").fill(password);

    const loginRequest = page.waitForRequest(
      (req) => req.url().includes("/api/v1/auth/login") && req.method() === "POST",
    );
    await page.getByRole("button", { name: /Sign in/i }).click();
    const req = await loginRequest;
    expect(req.headers()["x-csrf-token"]).toBeTruthy();

    await expect(page).toHaveURL(/\/(calendar|roles|tasks|coach)/, { timeout: 30_000 });
  });
});
