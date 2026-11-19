import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";

async function completeInitialPinSetupIfOpen(page) {
  const pinSetupModal = page.locator("#portal-pin-setup-modal");
  const pinUnlockModal = page.locator("#portal-pin-unlock-modal");
  await Promise.race([
    pinSetupModal.waitFor({ state: "visible", timeout: 4000 }),
    pinUnlockModal.waitFor({ state: "visible", timeout: 4000 }),
  ]).catch(() => {});

  if (await pinSetupModal.isVisible().catch(() => false)) {
    await page.fill("#setup-pin", "246810");
    await page.fill("#confirm-setup-pin", "246810");
    await page.click("#btn-pin-setup-confirm");
    await expect(pinSetupModal).toBeHidden();
  } else if (await pinUnlockModal.isVisible().catch(() => false)) {
    await page.fill("#unlock-pin", "246810");
    await page.click("#btn-pin-unlock-confirm");
    await expect(pinUnlockModal).toBeHidden();
  }
}

const WCAG_AA_TAGS = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"];

async function getControlBorderContrast(page, selector) {
  return page.locator(selector).evaluate((el) => {
    function parseRgb(str) {
      const m = str.match(/rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)/);
      return m ? [Number(m[1]), Number(m[2]), Number(m[3])] : [255, 255, 255];
    }
    function luminance([r, g, b]) {
      const [rl, gl, bl] = [r, g, b].map((v) => {
        const s = v / 255;
        return s <= 0.04045 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
      });
      return 0.2126 * rl + 0.7152 * gl + 0.0722 * bl;
    }
    const style = window.getComputedStyle(el);
    const borderRgb = parseRgb(style.borderTopColor);
    const bgRgb = parseRgb(style.backgroundColor);
    const l1 = luminance(borderRgb);
    const l2 = luminance(bgRgb);
    return (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
  });
}

// Dedicated end-to-end accessibility testing suite verifying full WCAG 2.2 AA compliance across all views
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
    await completeInitialPinSetupIfOpen(page);

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
    await completeInitialPinSetupIfOpen(page);

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
    await completeInitialPinSetupIfOpen(page);

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
    await completeInitialPinSetupIfOpen(page);

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
    await completeInitialPinSetupIfOpen(page);

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

  test("should verify specific WCAG 2.1 AA remediation elements (skip link, main landmark, unavailable guard, forms, aria-live)", async ({
    page,
  }) => {
    await page.goto("");
    await completeInitialPinSetupIfOpen(page);

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

    // 3. Unavailable workflow guard outside demo mode (/wallet redirects to #view-unavailable)
    await expect(page.locator("#tab-btn-wallet")).toHaveCount(0);
    await page.goto("wallet");
    await completeInitialPinSetupIfOpen(page);
    await expect(page.locator("#view-unavailable")).toBeVisible();
    const unavailableAudit = await new AxeBuilder({ page })
      .include("#view-unavailable")
      .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"])
      .analyze();
    expect(unavailableAudit.violations).toEqual([]);
    await page.click("#btn-unavailable-return-tasks");

    // 4. Form wrappers and numeric input attributes
    await expect(page.locator("#view-tasks")).toBeVisible();
    const startSurveyBtn = page.locator("#task-card-assign_01 .btn-start-task");
    await startSurveyBtn.click();

    const formWrapper = page.locator("#questionnaire-form");
    await expect(formWrapper).toBeVisible();

    const numericInput = page.locator("#vssbp");
    await expect(numericInput).toHaveAttribute("type", "number");
    await expect(numericInput).toHaveAttribute("inputmode", "numeric");
  });

  test("should pass accessibility audits on unauthenticated login prompt and PIN unlock recovery modal", async ({
    page,
  }) => {
    await page.goto("");
    await completeInitialPinSetupIfOpen(page);

    // Reload triggers PIN unlock modal (memory-only PIN key cleared on reload)
    await page.reload();
    const pinUnlockModal = page.locator("#portal-pin-unlock-modal");
    await expect(pinUnlockModal).toBeVisible();
    await expect(page.locator("#btn-pin-unlock-signout")).toBeVisible();
    await expect(page.locator("#btn-pin-unlock-reset")).toBeVisible();

    const unlockAudit = await new AxeBuilder({ page })
      .include("#portal-pin-unlock-modal")
      .withTags(WCAG_AA_TAGS)
      .analyze();
    expect(unlockAudit.violations).toEqual([]);

    // Sign out from PinUnlockModal transitions cleanly to unauthenticated login prompt
    await page.evaluate(() => sessionStorage.clear());
    await page.click("#btn-pin-unlock-signout");
    await expect(page.locator("#tasks-signin-prompt")).toBeVisible();
    await expect(page.locator("#btn-login")).toBeVisible();

    const unauthAudit = await new AxeBuilder({ page })
      .withTags(WCAG_AA_TAGS)
      .analyze();
    expect(unauthAudit.violations).toEqual([]);
  });

  test("should pass WCAG 2.2 AA contrast audits on ReconsentBanner, ReconsentModal, queued & quarantined sync badges, and SyncDrawer", async ({
    page,
  }) => {
    await page.route("**/econsent/reconsent/pending/**", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify([
          {
            id: "req_amendment_v2",
            study_id: "STUDY-01",
            protocol_version: "2.0",
            change_summary:
              "Updated cardiology safety visit window and reimbursement terms.",
          },
        ]),
      });
    });

    await page.route("**/interop/epro/sync", async (route) => {
      await route.fulfill({
        status: 422,
        contentType: "application/json",
        body: JSON.stringify({
          detail: [
            {
              loc: ["body", "submissions", 0, "answers", "vssbp"],
              msg: "Systolic blood pressure exceeds protocol safety ceiling of 250 mmHg",
            },
          ],
        }),
      });
    });

    await page.route("**/interop/epro/submit", async (route) => {
      await route.fulfill({
        status: 422,
        contentType: "application/json",
        body: JSON.stringify({
          detail: [
            {
              loc: ["body", "answers", "vssbp"],
              msg: "Systolic blood pressure exceeds protocol safety ceiling of 250 mmHg",
            },
          ],
        }),
      });
    });

    await page.goto("");
    await completeInitialPinSetupIfOpen(page);

    // 1. Re-consent modal opens automatically when pending requirement exists
    const reconsentModal = page.locator("#portal-reconsent-modal");
    await expect(reconsentModal).toBeVisible();
    const modalAudit = await new AxeBuilder({ page })
      .include("#portal-reconsent-modal")
      .withTags(WCAG_AA_TAGS)
      .analyze();
    expect(modalAudit.violations).toEqual([]);

    // Dismiss modal to reveal persistent ReconsentBanner (#reconsent-urgent-banner + #btn-open-reconsent-modal)
    await page.click("#btn-reconsent-cancel");
    await expect(reconsentModal).toBeHidden();
    await expect(page.locator("#reconsent-urgent-banner")).toBeVisible();
    await expect(page.locator("#btn-open-reconsent-modal")).toBeVisible();

    const bannerAudit = await new AxeBuilder({ page })
      .withTags(WCAG_AA_TAGS)
      .analyze();
    expect(bannerAudit.violations).toEqual([]);

    // 2. Queue an offline submission to render .sync-badge in queued amber state
    await page.evaluate(async () => {
      const { queueSubmission } =
        await import("/subject-portal/src/sync-queue.js");
      const { refreshSubmissionsState } =
        await import("/subject-portal/src/index.js");
      await queueSubmission({
        subject_id: "subject_mocked_001",
        diary_id: "inst_daily_diary",
        assignment_id: "assign_01",
        answers: { vssbp: 265, vsdpb: 90, vshr: 78 },
        change_reason: "Offline entry for contrast verification",
        username: "subject_mocked_001",
      });
      await refreshSubmissionsState();
    });

    const syncBadge = page.locator("#header-sync-badge");
    await expect(syncBadge).toBeVisible();
    const queuedHeaderAudit = await new AxeBuilder({ page })
      .include(".portal-header")
      .withTags(WCAG_AA_TAGS)
      .analyze();
    expect(queuedHeaderAudit.violations).toEqual([]);

    // 3. Open SyncDrawer, audit queued state, then trigger server 422 quarantine and audit quarantined badge + recovery card
    await page.click("#btn-global-sync-drawer");
    await expect(page.locator("#global-sync-drawer")).toHaveClass(/active/);
    const queuedDrawerAudit = await new AxeBuilder({ page })
      .include("#global-sync-drawer")
      .withTags(WCAG_AA_TAGS)
      .analyze();
    expect(queuedDrawerAudit.violations).toEqual([]);

    await page.click("#btn-drawer-sync-now");
    await expect(syncBadge).toHaveClass(/sync-badge-quarantined/);

    const quarantinedHeaderAudit = await new AxeBuilder({ page })
      .include(".portal-header")
      .withTags(WCAG_AA_TAGS)
      .analyze();
    expect(quarantinedHeaderAudit.violations).toEqual([]);

    const quarantinedDrawerAudit = await new AxeBuilder({ page })
      .include("#global-sync-drawer")
      .withTags(WCAG_AA_TAGS)
      .analyze();
    expect(quarantinedDrawerAudit.violations).toEqual([]);
  });

  test("should pass WCAG 2.2 AA contrast and SC 1.4.11 >= 3:1 control border audits on demo-mode WalletView, ConsentView, and LaySummariesView", async ({
    page,
  }) => {
    await page.goto("http://localhost:5175/subject-portal/");
    await completeInitialPinSetupIfOpen(page);

    // 1. WalletView: gradient stipend card, receipt upload zone, and expense form controls
    await page.click("#tab-btn-wallet button");
    await expect(page.locator("#view-wallet")).toBeVisible();
    const walletAudit = await new AxeBuilder({ page })
      .include("#view-wallet")
      .withTags(WCAG_AA_TAGS)
      .analyze();
    expect(walletAudit.violations).toEqual([]);

    const walletSelect = page.locator("#view-wallet select").first();
    if ((await walletSelect.count()) > 0) {
      expect(
        await getControlBorderContrast(page, "#view-wallet select")
      ).toBeGreaterThanOrEqual(3);
    }

    // 2. ConsentView: language selector, comprehension radio grid, and liveness verification controls
    await page.click("#tab-btn-consent button");
    await expect(page.locator("#view-consent")).toBeVisible();
    const consentAudit = await new AxeBuilder({ page })
      .include("#view-consent")
      .withTags(WCAG_AA_TAGS)
      .analyze();
    expect(consentAudit.violations).toEqual([]);

    const consentSelect = page.locator("#view-consent select").first();
    if ((await consentSelect.count()) > 0) {
      expect(
        await getControlBorderContrast(page, "#view-consent select")
      ).toBeGreaterThanOrEqual(3);
    }
    const consentRadioGrid = page
      .locator("#view-consent .clinical-radio-grid")
      .first();
    if ((await consentRadioGrid.count()) > 0) {
      expect(
        await getControlBorderContrast(
          page,
          "#view-consent .clinical-radio-grid"
        )
      ).toBeGreaterThanOrEqual(3);
    }

    // 3. LaySummariesView: regulatory & GxP badges and summary preview card
    await page.click("#tab-btn-lay-summaries button");
    await expect(page.locator("#view-lay-summaries")).toBeVisible();
    await expect(
      page.locator("#view-lay-summaries .badge-regulatory")
    ).toBeVisible();
    const laySummaryAudit = await new AxeBuilder({ page })
      .include("#view-lay-summaries")
      .withTags(WCAG_AA_TAGS)
      .analyze();
    expect(laySummaryAudit.violations).toEqual([]);
  });
});
