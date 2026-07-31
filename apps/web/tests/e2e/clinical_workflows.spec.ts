import { test, expect } from "@playwright/test";

test.describe("Clinical Workflows and ePRO Portal Tests", () => {
  test.describe("CRA Monitoring Query Management Workflow", () => {
    test.use({ storageState: "playwright/.auth/user.json" });

    test("should successfully create, respond to, and close a CRA discrepancy query", async ({ page }) => {
      // 1. Navigate relative to baseURL
      await page.goto("ecrf");

      // Verify page is loaded
      await expect(page.locator(".card-title").first()).toContainText("Subject eCRF Data Entry Form");

      // Locate the query flag button next to pulse input
      const queryFlag = page.locator("#query-flag-pulse");
      await expect(queryFlag).toBeVisible();
      await queryFlag.click();

      // Open the query panel
      const queryPanel = page.locator("#query-panel-pulse");
      await expect(queryPanel).toBeVisible();

      // Raise a query
      await page.fill("#query-message-pulse", "Pulse rate of 72 bpm requires clinical verification of resting state.");
      await page.click("button:has-text('Submit Query')");

      // Verify the query flag status class is updated to open
      await expect(queryFlag).toHaveClass(/query-status-open/);

      // Now, respond to the query
      // Open panel if it closed, otherwise reuse open panel
      const responseArea = page.locator("#query-response-pulse");
      if (!(await responseArea.isVisible())) {
        await queryFlag.click();
      }
      await responseArea.fill("Confirmed: subject was in resting state for 15 minutes prior to measurement.");
      await page.click("button:has-text('Submit Response')");

      // Verify query flag status is updated to answered
      await expect(queryFlag).toHaveClass(/query-status-answered/);

      // Verify we can close/resolve the query
      if (!(await page.locator("button:has-text('Close Query')").isVisible())) {
        await queryFlag.click();
      }
      await page.click("button:has-text('Close Query')");

      // Handle Re-authentication step-up PIN signature modal
      const reauthModal = page.locator("#reauth-modal");
      await expect(reauthModal).toBeVisible();
      await page.fill("#reauth-password", "admin_password");
      await page.click("#btn-confirm-reauth");

      // Verify query flag status is updated to closed
      await expect(queryFlag).toHaveClass(/query-status-closed/);
    });
  });

  test.describe("ePRO Participant Companion Portal Workflow", () => {
    test("should complete a daily health diary questionnaire with Part 11 compliant digital signature", async ({ page }) => {
      // Intercept ePRO sync endpoint
      await page.route("**/epro/sync", async (route) => {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            results: [
              {
                status: "CREATED",
                answers: { vssbp: "120", vsdpb: "80", vshr: "72", has_symptoms: "No" },
              },
            ],
          }),
        });
      });

      // Inject test env mock flag before page load to bypass hanging Keycloak setup
      await page.addInitScript(() => {
        (window as any).__MOCK_TEST_ENV__ = true;
      });

      // Navigate to ePRO portal on port 5174
      await page.goto("http://localhost:5174/subject-portal/");

      // Verify portal header is visible
      await expect(page.locator(".portal-header h1")).toContainText("My Cadence");

      // Click "Start Survey" on the Daily Health & Vital Diary task
      const startSurveyBtn = page.locator("#task-card-assign_01 .btn-start-task");
      await expect(startSurveyBtn).toBeVisible();
      await startSurveyBtn.click();

      // Verify questionnaire form view is displayed
      await expect(page.locator("#questionnaire-title")).toContainText("Daily Health & Vital Diary");

      // Populate CDASH clinical inputs
      await page.fill("#vssbp", "120");
      await page.fill("#vsdpb", "80");
      await page.fill("#vshr", "72");
      await page.check("input[name='has_symptoms'][value='No']");

      // Click "Sign and Submit"
      await page.click("#btn-submit-questionnaire");

      // Verify digital signature credential modal is presented
      const signatureModal = page.locator("#portal-sign-modal");
      await expect(signatureModal).toBeVisible();

      // Select standard reason and provide credentials
      await page.selectOption("#sign-reason", "Initial Questionnaire Completion");
      await page.fill("#sign-username", "subject_001");
      await page.fill("#sign-password", "subject_pin_123");

      // Click Sign and Confirm
      await page.click("#btn-modal-sign");

      // Verify we returned to the tasks dashboard and the first assignment is marked as completed
      await expect(page.locator("#view-tasks")).toBeVisible();
      await expect(page.locator("#task-card-assign_01")).not.toBeVisible();

      // Verify that offline sync queue list shows the synchronized entry
      const syncList = page.locator("#sync-queue-list");
      await expect(syncList).toContainText("SYNCED");
    });
  });
});
