// @ts-check
const { test, expect } = require("@playwright/test");

async function login(page) {
  await page.goto("/login");
  await page.locator("#email").fill("e2e@speroflow.test");
  await page.locator("#password").fill("CorrectHorseBattery1!");
  await page.getByRole("button", { name: /Sign in/i }).click();
  await expect(page).toHaveURL(/\/calendar/, { timeout: 20_000 });
}

test.describe("AI proposals approval (mocked API)", () => {
  test("pending proposal appears on /roles and approve sends CSRF + concurrency token", async ({ page }) => {
    const approveCalls = [];

    await page.route("**/api/v1/ai/proposals/**/approve", async (route) => {
      approveCalls.push({
        method: route.request().method(),
        csrf: route.request().headers()["x-csrf-token"] || "",
        body: route.request().postDataJSON(),
        url: route.request().url(),
      });
      await route.continue();
    });

    await login(page);
    await page.goto("/roles");

    await expect(page.getByRole("heading", { name: "Life roles" })).toBeVisible({ timeout: 15_000 });
    await expect(page.getByText("Schedule a 30-minute focus block")).toBeVisible();

    await page.getByRole("button", { name: "Approve" }).first().click();

    await expect(page.getByText(/approved change is now in your workspace/i)).toBeVisible({
      timeout: 15_000,
    });

    expect(approveCalls.length).toBe(1);
    expect(approveCalls[0].method).toBe("POST");
    expect(approveCalls[0].csrf).toMatch(/^csrf-/);
    expect(approveCalls[0].body).toEqual({ concurrencyToken: "proposal-token-1" });
    expect(approveCalls[0].url).toContain("/ai/proposals/11111111-1111-1111-1111-111111111111/approve");

    // Proposal should disappear from the pending list after approval.
    await expect(page.getByText("Schedule a 30-minute focus block")).toHaveCount(0);
  });
});
