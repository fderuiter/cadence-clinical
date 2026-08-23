import { test, expect } from "@playwright/test";
import {
  attachTripleSentinel,
  assertTripleSentinelClean,
  demoPause,
  highlightAndClick,
  setPersonaAndNavigate,
} from "../helpers/recorder";

test.describe("Suite 1: Sponsor Protocol Designer Golden Path", () => {
  test.beforeEach(async ({ page }) => {
    attachTripleSentinel(page);
  });

  test.afterEach(async ({ page }) => {
    assertTripleSentinelClean(page);
  });

  /**
   * Exercise full Sponsor Protocol Designer workflows across SoA matrix,
   * eCRF visual canvas, digitization DAG, eConsent, and amendment diffs.
   *
   * @req:PRD-SYS-105
   */
  test("01_protocol_designer_and_soa_matrix", async ({ page }) => {
    // Authenticate as Sponsor Protocol Designer
    await setPersonaAndNavigate(
      page,
      {
        username: "designer.lead",
        roles: ["Sponsor Designer", "Data Manager", "Sponsor Admin"],
      },
      "/cadence-clinical/mdr"
    );

    // Verify MDR Header & SoA tab
    await expect(page.locator("h2")).toContainText("MDR / Protocol Visualizer");
    await demoPause(page, 500);

    // Switch to Visual Form Designer Tab
    await highlightAndClick(page, "button.tab-btn-canvas");
    await demoPause(page, 500);

    // Switch to NCI Concept Registry Tab
    await highlightAndClick(page, "button.tab-btn-mdr");
    await demoPause(page, 500);

    // Switch back to SoA Matrix Tab
    await highlightAndClick(page, "button.tab-btn-soa");
    await demoPause(page, 500);

    // Navigate to Protocol Digitization DAG
    await page.goto("/cadence-clinical/digitization");
    await page.waitForLoadState("networkidle");
    await expect(page.locator("body")).toBeVisible();
    await demoPause(page, 600);

    // Navigate to eConsent Authoring & Readability
    await page.goto("/cadence-clinical/econsent-authoring");
    await page.waitForLoadState("networkidle");
    await expect(page.locator("body")).toBeVisible();
    await demoPause(page, 600);

    // Navigate to ICF Builder
    await page.goto("/cadence-clinical/icf-builder");
    await page.waitForLoadState("networkidle");
    await expect(page.locator("body")).toBeVisible();
    await demoPause(page, 600);

    // Navigate to Amendment Diff
    await page.goto("/cadence-clinical/amendment-diff");
    await page.waitForLoadState("networkidle");
    await expect(page.locator("body")).toBeVisible();
    await demoPause(page, 600);
  });
});
