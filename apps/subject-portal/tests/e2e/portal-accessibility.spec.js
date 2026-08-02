import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";

// Dedicated end-to-end accessibility testing suite verifying full WCAG 2.1 AA compliance
test.describe("Subject Portal Headless Accessibility Testing Suite", () => {
  test.beforeEach(async ({ page }) => {
    // Intercept API calls to supply live mock data when running in authenticated mode
    await page.route("**/assignments/subject/*", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify([
          {
            id: "assign_01",
            subject_id: "subject_mocked_001",
            instrument_id: "inst_daily_diary",
            instrument_name: "Daily Health & Vital Diary",
            due_at: new Date(Date.now() + 4 * 3600 * 1000).toISOString(),
            end_date: new Date(Date.now() + 24 * 3600 * 1000).toISOString(),
            status: "PENDING",
          },
          {
            id: "assign_02",
            subject_id: "subject_mocked_001",
            instrument_id: "inst_weekly_symptoms",
            instrument_name: "Weekly Symptoms & eCOA Checklist",
            due_at: new Date(Date.now() - 2 * 3600 * 1000).toISOString(),
            end_date: new Date(Date.now() + 12 * 3600 * 1000).toISOString(),
            status: "OVERDUE",
          },
          {
            id: "assign_03",
            subject_id: "subject_mocked_001",
            instrument_id: "inst_weekly_symptoms",
            instrument_name: "Weekly Symptoms & eCOA Checklist",
            due_at: new Date(Date.now() - 48 * 3600 * 1000).toISOString(),
            end_date: new Date(Date.now() - 24 * 3600 * 1000).toISOString(),
            status: "COMPLETED",
            submitted_at: new Date(Date.now() - 47 * 3600 * 1000).toISOString(),
          },
        ]),
      });
    });

    await page.route("**/instruments", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify([
          {
            id: "inst_daily_diary",
            name: "Daily Health & Vital Diary",
            description:
              "Please record your systolic/diastolic blood pressure, pulse, and current symptoms.",
            items: {
              vssbp: {
                label: "Systolic Blood Pressure (mmHg)",
                type: "numeric",
                required: true,
                min: 50,
                max: 250,
              },
              vsdpb: {
                label: "Diastolic Blood Pressure (mmHg)",
                type: "numeric",
                required: true,
                min: 30,
                max: 150,
              },
              vshr: {
                label: "Pulse Rate (bpm)",
                type: "numeric",
                required: true,
                min: 30,
                max: 200,
              },
              has_symptoms: {
                label: "Are you experiencing any new physical symptoms today?",
                type: "choice_single",
                options: ["Yes", "No"],
              },
            },
          },
        ]),
      });
    });

    await page.route("**/compliance", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          subject_id: "subject_mocked_001",
          compliance_rate: 33.3,
          completed_count: 1,
          pending_count: 1,
          overdue_count: 1,
          assignments: [
            {
              assignment_id: "assign_01",
              instrument_id: "inst_daily_diary",
              instrument_name: "Daily Health & Vital Diary",
              status: "PENDING",
              due_at: new Date(Date.now() + 4 * 3600 * 1000).toISOString(),
              end_date: new Date(Date.now() + 24 * 3600 * 1000).toISOString(),
              submitted_at: null,
            },
            {
              assignment_id: "assign_02",
              instrument_id: "inst_weekly_symptoms",
              instrument_name: "Weekly Symptoms & eCOA Checklist",
              status: "OVERDUE",
              due_at: new Date(Date.now() - 2 * 3600 * 1000).toISOString(),
              end_date: new Date(Date.now() + 12 * 3600 * 1000).toISOString(),
              submitted_at: null,
            },
            {
              assignment_id: "assign_03",
              instrument_id: "inst_weekly_symptoms",
              instrument_name: "Weekly Symptoms & eCOA Checklist",
              status: "COMPLETED",
              due_at: new Date(Date.now() - 48 * 3600 * 1000).toISOString(),
              end_date: new Date(Date.now() - 24 * 3600 * 1000).toISOString(),
              submitted_at: new Date(
                Date.now() - 47 * 3600 * 1000
              ).toISOString(),
            },
          ],
        }),
      });
    });

    await page.route("**/notifications", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify([
          {
            id: "notif_01",
            subject_id: "subject_mocked_001",
            assignment_id: "assign_01",
            message: "Reminder: Daily Health & Vital Diary is due shortly.",
            due_at: new Date(Date.now() + 4 * 3600 * 1000).toISOString(),
            channel: "IN_APP",
            is_read: false,
          },
          {
            id: "notif_02",
            subject_id: "subject_mocked_001",
            assignment_id: "assign_02",
            message:
              "ALERT: Weekly Symptoms & eCOA Checklist is OVERDUE! Please complete immediately.",
            due_at: new Date(Date.now() - 2 * 3600 * 1000).toISOString(),
            channel: "SMS",
            is_read: false,
          },
        ]),
      });
    });

    // Inject pre-configured mock credentials into storage to bypass login server
    await page.addInitScript(() => {
      sessionStorage.setItem("mock_user_id", "subject_mocked_001");
      sessionStorage.setItem("mock_token", "mock_jwt_token_xyz_123");
    });
  });

  test("should successfully bypass third-party login and pass WCAG 2.1 AA audits on landing page", async ({
    page,
  }) => {
    await page.goto("");

    // Verify name is rendered based on injected storage mock credentials
    await expect(page.locator("#session-subject-id")).toContainText(
      "subject_mocked_001"
    );

    // Ensure landing page is loaded
    await expect(page.locator(".portal-header h1")).toContainText("My Cadence");

    // Run Axe automated accessibility audit on the whole page
    const auditResults = await new AxeBuilder({ page })
      .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"])
      .analyze();

    expect(auditResults.violations).toEqual([]);
  });

  test("should pass accessibility audits in questionnaire form view", async ({
    page,
  }) => {
    await page.goto("");

    // Click "Start" / "Start Survey" on the Daily Health & Vital Diary task
    const startSurveyBtn = page.locator("#task-card-assign_01 .btn-start-task");
    await expect(startSurveyBtn).toBeVisible();
    await startSurveyBtn.click();

    // Verify questionnaire view is active
    await expect(page.locator("#questionnaire-title")).toContainText(
      "Daily Health & Vital Diary"
    );

    // Run Axe automated accessibility audit on the questionnaire view
    const auditResults = await new AxeBuilder({ page })
      .include("#view-questionnaire")
      .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"])
      .analyze();

    expect(auditResults.violations).toEqual([]);
  });

  test("should pass accessibility audits when showing validation error messages", async ({
    page,
  }) => {
    await page.goto("");

    // Open questionnaire
    const startSurveyBtn = page.locator("#task-card-assign_01 .btn-start-task");
    await expect(startSurveyBtn).toBeVisible();
    await startSurveyBtn.click();

    // Click submit with empty fields to trigger validation errors
    await page.click("#btn-submit-questionnaire");

    // Verify error classes/messages are visible
    const errorContainer = page.locator("#field-container-vssbp");
    await expect(errorContainer).toHaveClass(/has-error/);
    await expect(errorContainer.locator(".validation-error-msg")).toBeVisible();

    // Run Axe automated accessibility audit on the validation errors and active input elements
    const auditResults = await new AxeBuilder({ page })
      .include("#view-questionnaire")
      .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"])
      .analyze();

    expect(auditResults.violations).toEqual([]);
  });

  test("should pass accessibility audits on compliance score and history view", async ({
    page,
  }) => {
    await page.goto("");

    // Switch to My Compliance view
    await page.click("#tab-btn-compliance button");

    // Verify compliance view is active
    await expect(page.locator("#view-compliance")).toBeVisible();

    // Run Axe automated accessibility audit on compliance panel
    const auditResults = await new AxeBuilder({ page })
      .include("#view-compliance")
      .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"])
      .analyze();

    expect(auditResults.violations).toEqual([]);
  });

  test("should pass accessibility audits on inbox notifications view", async ({
    page,
  }) => {
    await page.goto("");

    // Switch to My Inbox view
    await page.click("#tab-btn-inbox button");

    // Verify inbox view is active
    await expect(page.locator("#view-inbox")).toBeVisible();

    // Run Axe automated accessibility audit on inbox panel
    const auditResults = await new AxeBuilder({ page })
      .include("#view-inbox")
      .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"])
      .analyze();

    expect(auditResults.violations).toEqual([]);
  });
});
