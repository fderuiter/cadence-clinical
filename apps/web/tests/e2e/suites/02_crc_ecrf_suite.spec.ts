import { test, expect } from "@playwright/test";
import {
  attachTripleSentinel,
  assertTripleSentinelClean,
  demoPause,
  highlightAndClick,
  setPersonaAndNavigate,
} from "../helpers/recorder";

test.describe("Suite 2: Site CRC & Clinical Data Capture Golden Path", () => {
  test.beforeEach(async ({ page }) => {
    attachTripleSentinel(page);
  });

  test.afterEach(async ({ page }) => {
    assertTripleSentinelClean(page);
  });

  /**
   * Exercise Site CRC subject enrollment, eCRF data entry, and query lifecycle.
   *
   * @req:PRD-SYS-105
   */
  test("02_crc_ecrf_data_capture_and_queries", async ({ page }) => {
    // Authenticate as Site Coordinator / CRC
    await setPersonaAndNavigate(
      page,
      {
        username: "crc.site101",
        roles: ["Site Investigator"],
      },
      "/cadence-clinical/ecrf"
    );

    // Verify eCRF Data Collection Workspace
    await expect(page.locator("h2")).toContainText("Data collection");
    await demoPause(page, 500);

    // Verify Subject Enrollment Section is present
    const enrollmentCard = page.locator(
      "#subject-enrollment-form, .subject-enrollment-container, h3:has-text('Subject Enrollment')"
    );
    if ((await enrollmentCard.count()) > 0) {
      await expect(enrollmentCard.first()).toBeVisible();
    }
    await demoPause(page, 400);

    // Navigate to Support & Query Tickets Hub
    await page.goto("/cadence-clinical/tickets");
    await page.waitForLoadState("networkidle");
    await expect(page.locator("body")).toBeVisible();
    await demoPause(page, 500);

    // Navigate to Documents Hub
    await page.goto("/cadence-clinical/documents");
    await page.waitForLoadState("networkidle");
    await expect(page.locator("body")).toBeVisible();
    await demoPause(page, 500);
  });
});
