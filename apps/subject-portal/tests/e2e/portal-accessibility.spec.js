import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";

// Dedicated end-to-end accessibility testing suite verifying full WCAG 2.1 AA compliance across all views
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

  test("should verify specific WCAG 2.1 AA remediation elements (skip link, main landmark, dropzone, forms, aria-live)", async ({
    page,
  }) => {
    await page.goto("");

    // 1. Skip link & Main Landmark
    const skipLink = page.locator(".skip-link");
    await expect(skipLink).toBeAttached();
    await expect(skipLink).toHaveAttribute("href", "#main-content");

    const mainLandmark = page.locator("main#main-content");
    await expect(mainLandmark).toBeAttached();
    await expect(mainLandmark).toHaveAttribute("tabindex", "-1");

    // 2. Sync Status Live Region
    const syncStatusText = page.locator("#sync-queue-status-text");
    await expect(syncStatusText).toHaveAttribute("role", "status");
    await expect(syncStatusText).toHaveAttribute("aria-live", "polite");

    // 3. Financial Wallet Travel Receipt Dropzone
    await page.click("#tab-btn-wallet button");
    const dropzone = page.locator("#receipt-upload-dropzone");
    await expect(dropzone).toBeVisible();
    await expect(dropzone).toHaveAttribute("tabindex", "0");
    await expect(dropzone).toHaveAttribute("role", "button");
    await expect(dropzone).toHaveAttribute(
      "aria-label",
      "Upload receipt image for travel reimbursement"
    );

    // 4. Form wrappers and numeric input attributes
    await page.click("#tab-btn-tasks button");
    const startSurveyBtn = page.locator("#task-card-assign_01 .btn-start-task");
    await startSurveyBtn.click();

    const formWrapper = page.locator("#questionnaire-form");
    await expect(formWrapper).toBeVisible();

    const numericInput = page.locator("#vssbp");
    await expect(numericInput).toHaveAttribute("type", "number");
    await expect(numericInput).toHaveAttribute("inputmode", "numeric");
  });

  test("should exclude closed SyncDrawer from Tab order, trap focus when open, and restore focus on Escape", async ({
    page,
  }) => {
    await page.goto("");

    const syncTrigger = page.locator("#btn-global-sync-drawer");
    const syncDrawer = page.locator(".global-drawer");
    await expect(syncTrigger).toBeVisible();
    await expect(syncDrawer).toHaveAttribute("aria-hidden", "true");

    // Tab through page controls and verify focus never lands inside the closed SyncDrawer
    await syncTrigger.focus();
    for (let i = 0; i < 12; i++) {
      await page.keyboard.press("Tab");
      const insideClosedDrawer = await page.evaluate(() => {
        const drawer = document.querySelector(".global-drawer");
        return Boolean(drawer && drawer.contains(document.activeElement));
      });
      expect(insideClosedDrawer).toBe(false);
    }

    // Open SyncDrawer and verify focus moves inside, wraps on Tab, passes Axe, and restores focus on Escape
    await syncTrigger.focus();
    await syncTrigger.click();
    await expect(syncDrawer).toHaveClass(/active/);
    await expect(syncDrawer).toHaveAttribute("aria-modal", "true");

    await expect
      .poll(() =>
        page.evaluate(() => {
          const drawer = document.querySelector(".global-drawer");
          return Boolean(drawer && drawer.contains(document.activeElement));
        })
      )
      .toBe(true);

    for (let i = 0; i < 5; i++) {
      await page.keyboard.press("Tab");
      const stillInside = await page.evaluate(() => {
        const drawer = document.querySelector(".global-drawer");
        return Boolean(drawer && drawer.contains(document.activeElement));
      });
      expect(stillInside).toBe(true);
    }

    const drawerAudit = await new AxeBuilder({ page })
      .include(".global-drawer")
      .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"])
      .analyze();
    expect(drawerAudit.violations).toEqual([]);

    await page.keyboard.press("Escape");
    await expect(syncDrawer).not.toHaveClass(/active/);
    await expect(syncTrigger).toBeFocused();
  });

  test("should trap focus in 21 CFR Part 11 SignatureModal, wipe password on Escape, and restore focus", async ({
    page,
  }) => {
    await page.goto("");

    await page.locator("#task-card-assign_01 .btn-start-task").click();
    await page.fill("#vssbp", "120");
    await page.fill("#vsdpb", "80");
    await page.fill("#vshr", "72");

    const submitBtn = page.locator("#btn-submit-questionnaire");
    await submitBtn.focus();
    await submitBtn.click();

    const sigModal = page.locator("#portal-sign-modal");
    const sigPassword = page.locator("#sign-password");
    await expect(sigModal).toBeVisible();

    await expect
      .poll(() =>
        page.evaluate(() => {
          const modal = document.getElementById("portal-sign-modal");
          return Boolean(modal && modal.contains(document.activeElement));
        })
      )
      .toBe(true);

    await sigPassword.fill("SecretSignPin123!");
    await expect(sigPassword).toHaveValue("SecretSignPin123!");

    const modalAudit = await new AxeBuilder({ page })
      .include("#portal-sign-modal")
      .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"])
      .analyze();
    expect(modalAudit.violations).toEqual([]);

    await page.keyboard.press("Escape");
    await expect(sigModal).toBeHidden();
    await expect(sigPassword).toHaveValue("");
    await expect(submitBtn).toBeFocused();
  });

  test("should support single-source ARIA state, arrow-key navigation, and WCAG 2.1 AA across all 6 portal tabs", async ({
    page,
  }) => {
    await page.goto("");

    const expectedTabs = [
      { id: "btn-tab-btn-tasks", panel: "view-tasks" },
      { id: "btn-tab-btn-compliance", panel: "view-compliance" },
      { id: "btn-tab-btn-inbox", panel: "view-inbox" },
      { id: "btn-tab-btn-consent", panel: "view-consent" },
      { id: "btn-tab-btn-wallet", panel: "view-wallet" },
      { id: "btn-tab-btn-lay-summaries", panel: "view-lay-summaries" },
    ];

    const tabLocator = page.locator('.nav-tabs [role="tab"]');
    await expect(tabLocator).toHaveCount(6);

    await page.locator("#btn-tab-btn-tasks").focus();
    for (let idx = 0; idx < expectedTabs.length; idx++) {
      const current = expectedTabs[idx];
      const activeTab = page.locator(`#${current.id}`);
      const activePanel = page.locator(`#${current.panel}`);

      await expect(activeTab).toHaveAttribute("aria-selected", "true");
      await expect(activeTab).toHaveAttribute("tabindex", "0");
      await expect(activeTab).toHaveAttribute("aria-controls", current.panel);
      await expect(activePanel).toHaveAttribute("role", "tabpanel");
      await expect(activePanel).toHaveAttribute("aria-labelledby", current.id);
      await expect(activePanel).toBeVisible();

      const panelAudit = await new AxeBuilder({ page })
        .include(`#${current.panel}`)
        .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"])
        .analyze();
      expect(panelAudit.violations).toEqual([]);

      await page.keyboard.press("ArrowRight");
    }

    // Wraparound from 6th tab back to 1st tab
    await expect(page.locator("#btn-tab-btn-tasks")).toHaveAttribute(
      "aria-selected",
      "true"
    );
    await expect(page.locator("#btn-tab-btn-tasks")).toBeFocused();
  });

  test("should halt decorative pulse animations and collapse drawer transitions under prefers-reduced-motion while keeping status indicators visible", async ({
    page,
  }) => {
    await page.emulateMedia({ reducedMotion: "reduce" });
    await page.goto("");

    // Queue an offline submission so `.has-queued` and `.sync-badge` render
    await page.context().setOffline(true);
    await page.locator("#task-card-assign_01 .btn-start-task").click();
    await page.fill("#vssbp", "118");
    await page.fill("#vsdpb", "76");
    await page.fill("#vshr", "68");
    await page.click("#btn-submit-questionnaire");
    await page.fill("#sign-username", "subject_mocked_001");
    await page.fill("#sign-password", "ValidPin123!");
    await page.click("#btn-modal-sign");
    await page.context().setOffline(false);

    const syncBtn = page.locator("#btn-global-sync-drawer");
    const syncBadge = syncBtn.locator(".sync-badge");
    await expect(syncBtn).toHaveClass(/has-queued/);
    await expect(syncBadge).toBeVisible();

    const motionStyles = await page.evaluate(() => {
      const btn = document.getElementById("btn-global-sync-drawer");
      const badge = btn.querySelector(".sync-badge");
      const drawer = document.querySelector(".global-drawer");
      const btnStyle = window.getComputedStyle(btn);
      const badgeStyle = window.getComputedStyle(badge);
      const drawerStyle = window.getComputedStyle(drawer);
      return {
        btnOpacity: btnStyle.opacity,
        badgeOpacity: badgeStyle.opacity,
        animationIterationCount: btnStyle.animationIterationCount,
        animationDurationSec: parseFloat(btnStyle.animationDuration) || 0,
        drawerTransitionSec: parseFloat(drawerStyle.transitionDuration) || 0,
      };
    });

    expect(motionStyles.btnOpacity).toBe("1");
    expect(motionStyles.badgeOpacity).toBe("1");
    expect(motionStyles.animationIterationCount).toBe("1");
    expect(motionStyles.animationDurationSec).toBeLessThanOrEqual(0.001);
    expect(motionStyles.drawerTransitionSec).toBeLessThanOrEqual(0.001);
  });
});
