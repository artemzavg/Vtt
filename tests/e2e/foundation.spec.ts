import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";

test("foundation shell loads and diagnostics stay usable", async ({
  page,
}) => {
  await page.goto("/");

  await expect(
    page.getByRole("heading", {
      name: /один стол для историй/i,
    }),
  ).toBeVisible();

  await page.getByRole("link", { name: "Система" }).click();
  await expect(
    page.getByRole("heading", {
      name: "Состояние системы",
    }),
  ).toBeVisible();

  const accessibility = await new AxeBuilder({ page }).analyze();
  expect(accessibility.violations).toEqual([]);
});
