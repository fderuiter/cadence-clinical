import { test, expect } from "@playwright/test";
import { CrfCanvasPage } from "./pages/CrfCanvasPage";

test.describe("CRF Authoring and Interactive Builder Workspace", () => {
  test.use({ storageState: "playwright/.auth/user.json" });

  test("should successfully configure epochs, encounters, and applicability timing", async ({ page }) => {
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
});
