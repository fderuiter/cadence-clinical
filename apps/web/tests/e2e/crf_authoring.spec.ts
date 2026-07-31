import { test, expect } from "@playwright/test";
import { CrfCanvasPage } from "./pages/CrfCanvasPage";

test.describe("CRF Authoring and Interactive Builder Workspace", () => {
  test.use({ storageState: "playwright/.auth/user.json" });

  test("should successfully configure epochs, encounters, and applicability timing", async ({
    page,
  }) => {
    const canvas = new CrfCanvasPage(page);
    await canvas.goto();

    // Open Interactive Builder
    await canvas.openInteractiveBuilder();

    // 1. Add Epoch
    await canvas.addEpoch("EP-TEST-E2E", "E2E Test Epoch");

    // 2. Add Visit associated with the new epoch
    await canvas.addVisit("V-TEST-E2E", "E2E Visit", "EP-TEST-E2E");

    // 3. Add Activity / Procedure
    await canvas.addProcedure("ACT-TEST-E2E", "E2E Procedure");

    // 4. Configure applicability and timing
    await canvas.configureApplicability(
      "ACT-TEST-E2E",
      "V-TEST-E2E",
      "Continuous observation within 5 mins"
    );

    // 5. Verify the Matrix contains the newly added items and timing details
    await canvas.verifyMatrixContains("E2E Test Epoch");
    await canvas.verifyMatrixContains("E2E Visit");
    await canvas.verifyMatrixContains("E2E Procedure");
    await canvas.verifyMatrixContains("Continuous observation within 5 mins");
  });

  test("should verify Viewport-Aware Grid Inspector responsiveness, warnings and compilation bypass", async ({
    page,
  }) => {
    // Navigate to MDR
    await page.goto("mdr");

    // Click eCRF Canvas tab
    await page.click("#btn-tab-canvas");

    // Check that we are on the canvas view and desktop viewport is active by default
    const desktopBtn = page.locator(".btn-viewport-desktop");
    await expect(desktopBtn).toHaveClass(/bg-indigo-600/);

    // Click on the first field widget on the canvas ("Subject Initials") to select it
    const fieldWidget = page.locator(".canvas-field-widget").first();
    await fieldWidget.click();

    // Verify properties inspector displays details for the selected field
    const labelInput = page.locator("#inspect-field-label");
    await expect(labelInput).toHaveValue("Subject Initials");

    // By default, subject initials gridSpan is 6, which is > 150px.
    // Check that there are zero layout warnings
    const warningCount = page.locator(".warning-count");
    await expect(warningCount).toContainText("0");

    // Change grid span of selected field to 1
    await page.selectOption("#inspect-field-span", "1");

    // Changing gridSpan to 1 on desktop (width 1200) sets width to (1200/12)*1 = 100px < 150px.
    // Verify that viewport warnings counter increases to 1
    await expect(warningCount).toContainText("1");

    // Verify warning list item contains the warning details
    const warningItem = page.locator(".warning-item").first();
    await expect(warningItem).toContainText("Subject Initials");
    await expect(warningItem).toContainText("column width is 100px (< 150px)");

    // Try to compile without bypass
    await page.click(".btn-compile");

    // Expect compilation blocked banner
    const blockBanner = page.locator(".compilation-error");
    await expect(blockBanner).toBeVisible();
    await expect(blockBanner).toContainText("Compilation blocked");

    // Click on "Dismiss layout warnings" checkbox
    await page.check("#dismiss-warnings-checkbox");

    // Click compile again
    await page.click(".btn-compile");

    // Expect successful compilation banner
    const successBanner = page.locator(".compilation-success");
    await expect(successBanner).toBeVisible();
    await expect(successBanner).toContainText("Compilation successful");

    // Now switch viewport to Tablet
    await page.click(".btn-viewport-tablet");
    const tabletBtn = page.locator(".btn-viewport-tablet");
    await expect(tabletBtn).toHaveClass(/bg-indigo-600/);

    // Reset grid span back to 6
    await page.selectOption("#inspect-field-span", "6");
    // Under Tablet, width is 768. 768/12 * 6 = 384px (> 150px), so warnings should be 0
    await expect(warningCount).toContainText("0");

    // Switch viewport to Mobile
    await page.click(".btn-viewport-mobile");
    const mobileBtn = page.locator(".btn-viewport-mobile");
    await expect(mobileBtn).toHaveClass(/bg-indigo-600/);

    // On mobile (width 480), grid span 3 has width (480/12)*3 = 120px < 150px.
    // Let's set grid span of "Subject Initials" to 3
    await page.selectOption("#inspect-field-span", "3");
    // Under Mobile, 120px < 150px, which should trigger layout warnings
    await expect(warningCount).toContainText("1");
  });
});
