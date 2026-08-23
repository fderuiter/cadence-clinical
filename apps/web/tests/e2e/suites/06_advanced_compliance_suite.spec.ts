import { test, expect } from "@playwright/test";
import {
  attachTripleSentinel,
  assertTripleSentinelClean,
  demoPause,
  highlightAndClick,
  setPersonaAndNavigate,
} from "../helpers/recorder";

test.describe("Suite 6: Advanced Clinical, Devices & GxP Compliance Golden Path", () => {
  test.beforeEach(async ({ page }) => {
    attachTripleSentinel(page);
  });

  test.afterEach(async ({ page }) => {
    assertTripleSentinelClean(page);
  });

  /**
   * Exercise advanced clinical modules: DICOM imaging, device tracking, DCT sensors, training, and 21 CFR Part 11 audit trails.
   *
   * @req:PRD-SYS-105
   */
  test("06_advanced_imaging_devices_and_audit", async ({ page }) => {
    // Authenticate as GxP Auditor / Super Admin
    await setPersonaAndNavigate(
      page,
      {
        username: "auditor.lead",
        roles: ["Auditor", "TMF Auditor", "Sponsor Admin", "Super Admin"],
      },
      "/cadence-clinical/imaging"
    );

    // Verify Medical Imaging Console
    await expect(page.locator("h2")).toContainText("Medical Imaging");
    await demoPause(page, 500);

    // Navigate to Device Investigation Console
    await page.goto("/cadence-clinical/devices");
    await page.waitForLoadState("networkidle");
    await expect(page.locator("body")).toBeVisible();
    await demoPause(page, 500);

    // Navigate to Sensor Telemetry Console
    await page.goto("/cadence-clinical/sensors");
    await page.waitForLoadState("networkidle");
    await expect(page.locator("body")).toBeVisible();
    await demoPause(page, 500);

    // Navigate to Decentralized Trials / Telehealth
    await page.goto("/cadence-clinical/dct");
    await page.waitForLoadState("networkidle");
    await expect(page.locator("body")).toBeVisible();
    await demoPause(page, 500);

    // Navigate to Participant Financial Wallet
    await page.goto("/cadence-clinical/participant-wallet");
    await page.waitForLoadState("networkidle");
    await expect(page.locator("body")).toBeVisible();
    await demoPause(page, 500);

    // Navigate to Study Training Console
    await page.goto("/cadence-clinical/training");
    await page.waitForLoadState("networkidle");
    await expect(page.locator("body")).toBeVisible();
    await demoPause(page, 500);

    // Navigate to Notifications Hub
    await page.goto("/cadence-clinical/notifications");
    await page.waitForLoadState("networkidle");
    await expect(page.locator("body")).toBeVisible();
    await demoPause(page, 500);

    // Navigate to Knowledge Base & SOPs
    await page.goto("/cadence-clinical/knowledge");
    await page.waitForLoadState("networkidle");
    await expect(page.locator("body")).toBeVisible();
    await demoPause(page, 500);

    // Navigate to 21 CFR Part 11 Audit Trail Viewer
    await page.goto("/cadence-clinical/audit");
    await page.waitForLoadState("networkidle");
    await expect(page.locator("body")).toBeVisible();
    await demoPause(page, 500);
  });
});
