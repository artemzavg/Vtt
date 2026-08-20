import { expect, test } from "@playwright/test";

test("player signs in without receiving a browser token", async ({ page }) => {
  await page.route("**/api/v1/auth/login", async (route) => {
    await route.fulfill({
      contentType: "application/json",
      body: JSON.stringify({
        expiresAt: "2026-09-15T10:00:00Z",
        user: {
          userId: "018f0000-0000-7000-8000-000000000001",
          displayName: "Пол",
          locale: "ru-RU",
          timeZone: "Europe/Moscow",
          version: 1,
        },
      }),
    });
  });
  await page.route("**/api/v1/me", (route) =>
    route.fulfill({
      json: {
        userId: "018f0000-0000-7000-8000-000000000001",
        displayName: "Пол",
        locale: "ru-RU",
        timeZone: "Europe/Moscow",
        version: 1,
      },
    }),
  );
  await page.route("**/api/v1/me/sessions", (route) => route.fulfill({ json: [] }));

  await page.goto("/auth");
  await page.getByLabel("Email").fill("player@example.test");
  await page.getByLabel("Пароль").fill("Correct-horse-42!");
  await page.getByRole("button", { name: "Продолжить" }).click();

  await expect(page).toHaveURL(/\/account$/);
  await expect(page.getByRole("heading", { name: "Пол" })).toBeVisible();
  expect(await page.evaluate(() => Object.keys(localStorage))).toEqual([]);
  expect(await page.evaluate(() => Object.keys(sessionStorage))).toEqual([]);
});
