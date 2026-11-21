import { test, expect } from "@playwright/test";
import {
  attachTripleSentinel,
  assertTripleSentinelClean,
  demoPause,
  highlightAndClick,
  setPersonaAndNavigate,
} from "../helpers/recorder";

test.describe("Suite 3: CRA Clinical Monitor Golden Path", () => {
  test.beforeEach(async ({ page }) => {
    attachTripleSentinel(page);
  });

  test.afterEach(async ({ page }) => {
    assertTripleSentinelClean(page);
  });

  /**
   * Exercise CRA Clinical Monitor workflows across CTMS, SSU greenlight, and eTMF.
   *
   * @req:PRD-SYS-105
   */
  test("03_cra_ctms_and_ssu_monitoring", async ({ page }) => {
    // Authenticate as CRA Clinical Monitor
    await setPersonaAndNavigate(
      page,
      {
        username: "cra.monitor",
        roles: ["CRA", "Monitor", "Sponsor Admin"],
      },
      "/cadence-clinical/ctms"
    );

    // Verify CTMS Site Readiness Header & KPIs
    await expect(page.locator("h2")).toContainText("Site readiness");
    await expect(
      page.locator("#kpi-total-subjects, .stat-card").first()
    ).toBeVisible();
    await demoPause(page, 500);

    // Navigate to Study Startup (SSU) Greenlight Console
    await page.goto("/cadence-clinical/ssu");
    await page.waitForLoadState("networkidle");
    await expect(page.locator("body")).toBeVisible();
    await demoPause(page, 500);

    // Navigate to eTMF & Regulatory Binder Manager
    await page.goto("/cadence-clinical/etmf");
    await page.waitForLoadState("networkidle");
    await expect(page.locator("body")).toBeVisible();
    await demoPause(page, 500);
  });
});
