import { test, expect } from "@playwright/test";
import {
  attachTripleSentinel,
  assertTripleSentinelClean,
  demoPause,
  highlightAndClick,
  setPersonaAndNavigate,
} from "../helpers/recorder";

test.describe("Suite 4: Clinical Data Manager Golden Path", () => {
  test.beforeEach(async ({ page }) => {
    attachTripleSentinel(page);
  });

  test.afterEach(async ({ page }) => {
    assertTripleSentinelClean(page);
  });

  /**
   * Exercise Clinical Data Manager rules designer, medical coding, data lock, and lakehouse.
   *
   * @req:PRD-SYS-050
   */
  test("04_data_manager_rules_coding_and_lakehouse", async ({ page }) => {
    // Authenticate as Clinical Data Manager
    await setPersonaAndNavigate(
      page,
      {
        username: "dm.lead",
        roles: ["Data Manager", "Sponsor Designer", "Sponsor Admin"],
      },
      "/cadence-clinical/rules"
    );

    // Verify Interactive Rules Designer
    await expect(page.locator("h2")).toContainText(
      "Interactive Rules Designer"
    );
    await demoPause(page, 500);

    // Navigate to Medical Coding
    await page.goto("/cadence-clinical/coding");
    await page.waitForLoadState("networkidle");
    await expect(page.locator("body")).toBeVisible();
    await demoPause(page, 500);

    // Navigate to Data Lock View
    await page.goto("/cadence-clinical/data-lock");
    await page.waitForLoadState("networkidle");
    await expect(page.locator("body")).toBeVisible();
    await demoPause(page, 500);

    // Navigate to CDR Lakehouse
    await page.goto("/cadence-clinical/lakehouse");
    await page.waitForLoadState("networkidle");
    await expect(page.locator("body")).toBeVisible();
    await demoPause(page, 500);

    // Navigate to Quality Cockpit
    await page.goto("/cadence-clinical/quality");
    await page.waitForLoadState("networkidle");
    await expect(page.locator("body")).toBeVisible();
    await demoPause(page, 500);

    // Navigate to Export Wizard
    await page.goto("/cadence-clinical/exports");
    await page.waitForLoadState("networkidle");
    await expect(page.locator("body")).toBeVisible();
    await demoPause(page, 500);
  });
});
