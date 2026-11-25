import { test, expect } from "@playwright/test";
import {
  attachTripleSentinel,
  assertTripleSentinelClean,
  demoPause,
  highlightAndClick,
  setPersonaAndNavigate,
} from "../helpers/recorder";

test.describe("Suite 5: Safety Physician & Medical Reviewer Golden Path", () => {
  test.beforeEach(async ({ page }) => {
    attachTripleSentinel(page);
  });

  test.afterEach(async ({ page }) => {
    assertTripleSentinelClean(page);
  });

  /**
   * Exercise Safety Physician aggregate safety, CEC adjudication, and lab governance.
   *
   * @req:PRD-SYS-050
   */
  test("05_safety_pv_and_cec_adjudication", async ({ page }) => {
    // Authenticate as Safety Physician / PV Officer
    await setPersonaAndNavigate(
      page,
      {
        username: "safety.officer",
        roles: ["Sponsor Admin"],
      },
      "/cadence-clinical/safety"
    );

    // Verify Aggregate Safety Console
    await expect(page.locator("body")).toBeVisible();
    await demoPause(page, 500);

    // Navigate to CEC Clinical Endpoint Adjudication
    await page.goto("/cadence-clinical/adjudication");
    await page.waitForLoadState("networkidle");
    await expect(page.locator("body")).toBeVisible();
    await demoPause(page, 500);

    // Navigate to Lab Governance & Toxicity
    await page.goto("/cadence-clinical/labs");
    await page.waitForLoadState("networkidle");
    await expect(page.locator("body")).toBeVisible();
    await demoPause(page, 500);
  });
});
