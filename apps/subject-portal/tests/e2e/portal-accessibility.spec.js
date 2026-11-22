import { test, expect } from "@playwright/test";
import {
  formatAccessibilityViolations,
  getControlBorderContrast,
  runWcag22Audit,
} from "ui/playwright";

async function completeInitialPinSetupIfOpen(page) {
  const skipLink = page.locator("#app .skip-link");
  const attached = await skipLink
    .waitFor({ state: "attached", timeout: 5000 })
    .then(() => true)
    .catch(() => false);
  if (!attached) {
    await page.reload();
    await skipLink.waitFor({ state: "attached", timeout: 10000 });
  }
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

async function expectPortalAccessible(page, contextLabel, options = {}) {
  const { violations, borderViolations } = await runWcag22Audit(page, options);
  const summary = formatAccessibilityViolations(violations, borderViolations);
  expect(
    violations,
    `WCAG 2.2 AA violations on ${contextLabel}:\n${summary}`
  ).toEqual([]);
  expect(
    borderViolations,
    `SC 1.4.11 control border contrast violations on ${contextLabel}:\n${summary}`
  ).toEqual([]);
}

// Dedicated end-to-end accessibility testing suite verifying full WCAG 2.2 AA compliance across all views
test.describe("Subject Portal Headless Accessibility Testing Suite", () => {
  test.use({ serviceWorkers: "block" });

  test.beforeEach(async ({ page }) => {
    await page.route("**/reconsent/pending/**", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify([]),
      });
    });

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

    await page.route("**/payments/participant/me/wallet", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          wallet: {
            id: "wallet_mock_001",
            studyId: "study_001",
            siteId: "site_001",
            currentBalance: 50,
            lifetimeStipends: 50,
            lifetimeReimbursements: 0,
            lifetimeDisbursements: 0,
            w9Status: "NOT_REQUIRED",
            currency: "USD",
          },
          recentTransactions: [],
          expenseClaims: [],
        }),
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

    // Run WCAG 2.2 AA + SC 1.4.11 automated accessibility audit on the whole page
    await expectPortalAccessible(page, "Portal landing page (#view-tasks)");
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

    // Run WCAG 2.2 AA + SC 1.4.11 automated accessibility audit on the questionnaire view
    await expectPortalAccessible(page, "Questionnaire view", {
      include: "#view-questionnaire",
    });
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

    // Run WCAG 2.2 AA + SC 1.4.11 automated accessibility audit on validation errors and active inputs
    await expectPortalAccessible(page, "Questionnaire validation errors", {
      include: "#view-questionnaire",
    });
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

    // Run WCAG 2.2 AA + SC 1.4.11 automated accessibility audit on compliance panel
    await expectPortalAccessible(page, "Compliance view", {
      include: "#view-compliance",
    });
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

    // Run WCAG 2.2 AA + SC 1.4.11 automated accessibility audit on inbox panel
    await expectPortalAccessible(page, "Inbox view", {
      include: "#view-inbox",
    });
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

    // 3. Unavailable workflow guard outside demo mode (/lay-summaries redirects to #view-unavailable)
    await expect(page.locator("#tab-btn-lay-summaries")).toHaveCount(0);
    await page.goto("lay-summaries");
    await completeInitialPinSetupIfOpen(page);
    await expect(page.locator("#view-unavailable")).toBeVisible();
    await expectPortalAccessible(page, "Unavailable workflow guard", {
      include: "#view-unavailable",
    });
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

    await expectPortalAccessible(page, "PIN unlock modal", {
      include: "#portal-pin-unlock-modal",
    });

    // Sign out from PinUnlockModal transitions cleanly to unauthenticated login prompt
    await page.evaluate(() => sessionStorage.clear());
    await page.click("#btn-pin-unlock-signout");
    await expect(page.locator("#tasks-signin-prompt")).toBeVisible();
    await expect(page.locator("#btn-login")).toBeVisible();

    await expectPortalAccessible(page, "Unauthenticated login prompt");
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
    await expectPortalAccessible(page, "ReconsentModal", {
      include: "#portal-reconsent-modal",
    });

    // Dismiss modal to reveal persistent ReconsentBanner (#reconsent-urgent-banner + #btn-open-reconsent-modal)
    await page.click("#btn-reconsent-cancel");
    await expect(reconsentModal).toBeHidden();
    await expect(page.locator("#reconsent-urgent-banner")).toBeVisible();
    await expect(page.locator("#btn-open-reconsent-modal")).toBeVisible();

    await expectPortalAccessible(page, "ReconsentBanner");

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
    await expectPortalAccessible(page, "Portal header queued sync badge", {
      include: ".portal-header",
    });

    // 3. Open SyncDrawer, audit queued state, then trigger server 422 quarantine and audit quarantined badge + recovery card
    await page.click("#btn-global-sync-drawer");
    await expect(page.locator("#global-sync-drawer")).toHaveClass(/active/);
    await expectPortalAccessible(page, "SyncDrawer queued state", {
      include: "#global-sync-drawer",
    });

    await page.click("#btn-drawer-sync-now");
    await expect(syncBadge).toHaveClass(/sync-badge-quarantined/);

    await expectPortalAccessible(page, "Portal header quarantined sync badge", {
      include: ".portal-header",
    });

    await expectPortalAccessible(page, "SyncDrawer quarantined state", {
      include: "#global-sync-drawer",
    });
  });

  test("should pass WCAG 2.2 AA contrast and SC 1.4.11 >= 3:1 control border audits on demo-mode WalletView, ConsentView, and LaySummariesView", async ({
    page,
  }) => {
    await page.goto("http://127.0.0.1:5175/subject-portal/");
    await completeInitialPinSetupIfOpen(page);

    // 1. WalletView: gradient stipend card, receipt upload zone, and expense form controls
    await page.click("#tab-btn-wallet button");
    await expect(page.locator("#view-wallet")).toBeVisible();
    await expectPortalAccessible(page, "WalletView (light)", {
      include: "#view-wallet",
    });

    const walletSelect = page.locator("#view-wallet select").first();
    if ((await walletSelect.count()) > 0) {
      expect(
        await getControlBorderContrast(page, "#view-wallet select")
      ).toBeGreaterThanOrEqual(3);
    }

    // 2. ConsentView: language selector, comprehension radio grid, and liveness verification controls
    await page.click("#tab-btn-consent button");
    await expect(page.locator("#view-consent")).toBeVisible();
    await expectPortalAccessible(page, "ConsentView (light)", {
      include: "#view-consent",
    });

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
    await expectPortalAccessible(page, "LaySummariesView (light)", {
      include: "#view-lay-summaries",
    });
  });

  test("should exclude closed SyncDrawer from Tab order, trap focus when open, and restore focus on Escape", async ({
    page,
  }) => {
    await page.goto("");
    await completeInitialPinSetupIfOpen(page);

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

    await expectPortalAccessible(page, "SyncDrawer focus trap", {
      include: ".global-drawer",
    });

    await page.keyboard.press("Escape");
    await expect(syncDrawer).not.toHaveClass(/active/);
    await expect(syncTrigger).toBeFocused();
  });

  test("should trap focus in 21 CFR Part 11 SignatureModal, wipe password on Escape, and restore focus", async ({
    page,
  }) => {
    await page.goto("");
    await completeInitialPinSetupIfOpen(page);

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

    await expectPortalAccessible(page, "SignatureModal focus trap", {
      include: "#portal-sign-modal",
    });

    await page.keyboard.press("Escape");
    await expect(sigModal).toBeHidden();
    await expect(sigPassword).toHaveValue("");
    await expect(submitBtn).toBeFocused();
  });

  test("should support single-source ARIA state, arrow-key navigation, and WCAG 2.1 AA across available portal tabs", async ({
    page,
  }) => {
    await page.route("**/econsent/templates/**", async (route) => {
      const url = route.request().url();
      if (url.includes("approved-content")) {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            template_id: "template-icf",
            template_name: "Main Study Informed Consent Form",
            version_index: 1,
            language_code: "en",
            clauses: [
              {
                clause_id: "clause-1",
                title: "Study Purpose",
                text: "This study evaluates participant symptom tracking.",
                version_index: 1,
              },
            ],
          }),
        });
      } else {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            template_id: "template-icf",
            version_index: 1,
            questions: [],
          }),
        });
      }
    });

    await page.route(
      "**/api/v1/payments/participant/me/wallet",
      async (route) => {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            wallet: {
              id: "wallet-001",
              study_id: "study_001",
              site_id: "site_001",
              current_balance: 50,
              lifetime_stipends: 100,
              lifetime_reimbursements: 25,
              lifetime_disbursements: 75,
              w9_status: "NOT_REQUIRED",
              currency: "USD",
            },
            recent_transactions: [],
            expense_claims: [],
          }),
        });
      }
    );

    await page.goto("");
    await completeInitialPinSetupIfOpen(page);

    const expectedTabs = [
      { id: "btn-tab-btn-tasks", panel: "view-tasks" },
      { id: "btn-tab-btn-compliance", panel: "view-compliance" },
      { id: "btn-tab-btn-inbox", panel: "view-inbox" },
      { id: "btn-tab-btn-consent", panel: "view-consent" },
      { id: "btn-tab-btn-wallet", panel: "view-wallet" },
    ];

    const tabLocator = page.locator('.nav-tabs [role="tab"]');
    await expect(tabLocator).toHaveCount(expectedTabs.length);

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

      await expectPortalAccessible(page, `Portal tab panel #${current.panel}`, {
        include: `#${current.panel}`,
      });

      await page.keyboard.press("ArrowRight");
    }

    // Wraparound from last visible tab back to 1st tab
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
    await completeInitialPinSetupIfOpen(page);

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
        animSeconds: parseFloat(btnStyle.animationDuration) || 0,
        drawerTransitionSec: parseFloat(drawerStyle.transitionDuration) || 0,
      };
    });

    expect(motionStyles.btnOpacity).toBe("1");
    expect(motionStyles.badgeOpacity).toBe("1");
    expect(motionStyles.animationIterationCount).toBe("1");
    expect(motionStyles.animSeconds).toBeLessThanOrEqual(0.001);
    expect(motionStyles.drawerTransitionSec).toBeLessThanOrEqual(0.001);
  });

  test("enforces zero WCAG 2.2 AA and SC 1.4.11 control border violations across all 7 portal views in light and dark themes", async ({
    page,
  }) => {
    await page.goto("http://127.0.0.1:5175/subject-portal/");
    await completeInitialPinSetupIfOpen(page);

    for (const theme of ["light", "dark"]) {
      await page.evaluate((activeTheme) => {
        document.documentElement.setAttribute("data-theme", activeTheme);
      }, theme);

      // 1. tasks (#view-tasks)
      await page.click("#tab-btn-tasks button");
      await expect(page.locator("#view-tasks")).toBeVisible();
      await expectPortalAccessible(page, `Portal tasks view (${theme})`);

      // 2. questionnaire (#view-questionnaire)
      await page.locator("#task-card-assign_01 .btn-start-task").click();
      await expect(page.locator("#view-questionnaire")).toBeVisible();
      await expectPortalAccessible(
        page,
        `Portal questionnaire view (${theme})`
      );
      await page.click("#btn-back-to-tasks");
      await expect(page.locator("#view-tasks")).toBeVisible();

      // 3. compliance (#view-compliance)
      await page.click("#tab-btn-compliance button");
      await expect(page.locator("#view-compliance")).toBeVisible();
      await expectPortalAccessible(page, `Portal compliance view (${theme})`);

      // 4. inbox (#view-inbox)
      await page.click("#tab-btn-inbox button");
      await expect(page.locator("#view-inbox")).toBeVisible();
      await expectPortalAccessible(page, `Portal inbox view (${theme})`);

      // 5. consent (#view-consent)
      await page.click("#tab-btn-consent button");
      await expect(page.locator("#view-consent")).toBeVisible();
      await expectPortalAccessible(page, `Portal consent view (${theme})`);

      // 6. wallet (#view-wallet)
      await page.click("#tab-btn-wallet button");
      await expect(page.locator("#view-wallet")).toBeVisible();
      await expectPortalAccessible(page, `Portal wallet view (${theme})`);

      // 7. lay-summaries (#view-lay-summaries)
      await page.click("#tab-btn-lay-summaries button");
      await expect(page.locator("#view-lay-summaries")).toBeVisible();
      await expectPortalAccessible(
        page,
        `Portal lay-summaries view (${theme})`
      );
    }
  });
});
