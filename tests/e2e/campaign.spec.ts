import { expect, test } from "@playwright/test";

test("owner creates a campaign draft from the dashboard", async ({ page }) => {
  const campaign = {
    campaignId: "018f0000-0000-7000-8000-000000000010",
    ownerId: "018f0000-0000-7000-8000-000000000001",
    name: "Арракис",
    description: "",
    locale: "ru-RU",
    timeZone: "Europe/Moscow",
    rulesetVersionId: "dnd5e-srd@1.0.0",
    status: "Draft",
    automationLevel: "Assisted",
    dicePolicy: "ServerAuthoritative",
    version: 1,
    policyRevision: 1,
    role: "Owner",
    effectiveCapabilities: ["campaign.read", "campaign.activate", "members.manage"],
  };
  await page.route("**/api/v1/campaigns", async (route) => {
    if (route.request().method() === "POST") await route.fulfill({ json: campaign });
    else await route.fulfill({ json: [] });
  });

  await page.goto("/campaigns");
  await page.getByLabel("Название").fill("Арракис");
  await page.getByRole("button", { name: "Создать черновик" }).click();

  await expect(page.getByText("Арракис")).toBeVisible();
  await expect(page.getByText("Черновик кампании создан.")).toBeVisible();
  expect(await page.evaluate(() => Object.keys(localStorage))).toEqual([]);
});
