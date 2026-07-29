// @ts-check
const { test, expect } = require("@playwright/test");

const email = process.env.E2E_EMAIL || "";
const password = process.env.E2E_PASSWORD || "";

test.describe("Live stack proposals", () => {
  test("authenticated user can open roles and load proposals API with session", async ({ page }) => {
    test.skip(!email || !password, "Set E2E_EMAIL and E2E_PASSWORD for live tests");

    await page.goto("/login");
    await page.locator("#email").fill(email);
    await page.locator("#password").fill(password);
    await page.getByRole("button", { name: /Sign in/i }).click();
    await expect(page).not.toHaveURL(/\/login/, { timeout: 30_000 });

    const proposalsResponse = page.waitForResponse(
      (res) => res.url().includes("/api/v1/ai/proposals") && res.request().method() === "GET",
    );
    await page.goto("/roles");
    const res = await proposalsResponse;
    expect(res.status()).toBeLessThan(500);
    await expect(page.getByRole("heading", { name: "Life roles" })).toBeVisible({ timeout: 20_000 });
  });
});
