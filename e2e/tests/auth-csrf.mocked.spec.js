// @ts-check
const { test, expect } = require("@playwright/test");

test.describe("Login + CSRF (mocked API)", () => {
  test("login page renders sign-in form", async ({ page }) => {
    await page.goto("/login");
    await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();
    await expect(page.locator("#email")).toBeVisible();
    await expect(page.locator("#password")).toBeVisible();
    await expect(page.getByRole("button", { name: /Sign in/i })).toBeVisible();
  });

  test("unsafe login POST sends X-CSRF-TOKEN after fetching csrf", async ({ page }) => {
    const csrfCalls = [];
    const loginCalls = [];

    await page.route("**/api/v1/auth/csrf", async (route) => {
      csrfCalls.push(route.request().method());
      // Let the mock API handle it (webServer proxy) — or fulfill if direct.
      await route.continue();
    });

    await page.route("**/api/v1/auth/login", async (route) => {
      loginCalls.push({
        method: route.request().method(),
        csrf: route.request().headers()["x-csrf-token"] || "",
        body: route.request().postDataJSON(),
      });
      await route.continue();
    });

    await page.goto("/login");
    await page.locator("#email").fill("e2e@speroflow.test");
    await page.locator("#password").fill("CorrectHorseBattery1!");
    await page.getByRole("button", { name: /Sign in/i }).click();

    await expect(page).toHaveURL(/\/calendar/, { timeout: 20_000 });

    expect(csrfCalls.length).toBeGreaterThanOrEqual(1);
    expect(loginCalls.length).toBe(1);
    expect(loginCalls[0].method).toBe("POST");
    expect(loginCalls[0].csrf).toMatch(/^csrf-/);
    expect(loginCalls[0].body).toMatchObject({
      email: "e2e@speroflow.test",
      password: "CorrectHorseBattery1!",
    });
  });

  test("login without CSRF header is rejected by API contract", async ({ request, baseURL }) => {
    // Direct API call omitting X-CSRF-TOKEN must fail (middleware + mock).
    const res = await request.post(`${baseURL}/api/v1/auth/login`, {
      data: { email: "e2e@speroflow.test", password: "x", rememberMe: false },
      headers: { "Content-Type": "application/json" },
    });
    // Without csrf cookie/header the mock returns 400; real API also 400.
    expect([400, 401]).toContain(res.status());
  });

  test("wrong password shows error and stays on login", async ({ page }) => {
    await page.goto("/login");
    await page.locator("#email").fill("e2e@speroflow.test");
    await page.locator("#password").fill("wrong-password");
    await page.getByRole("button", { name: /Sign in/i }).click();
    await expect(page.getByText(/Invalid email or password/i)).toBeVisible({ timeout: 10_000 });
    await expect(page).toHaveURL(/\/login/);
  });
});
