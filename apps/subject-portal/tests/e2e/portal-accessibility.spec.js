import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";

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
    // and capture every browser Content-Security-Policy violation event.
    await page.addInitScript(() => {
      window.__cadenceCspViolations = [];
      document.addEventListener("securitypolicyviolation", (event) => {
        window.__cadenceCspViolations.push({
          effectiveDirective: event.effectiveDirective,
          violatedDirective: event.violatedDirective,
          blockedURI: event.blockedURI,
          sourceFile: event.sourceFile,
          sample: event.sample,
        });
      });
      sessionStorage.setItem("mock_user_id", "subject_mocked_001");
      sessionStorage.setItem("mock_token", "mock_jwt_token_xyz_123");
    });
  });

  test.afterEach(async ({ page }) => {
    const violations = await page
      .evaluate(() => window.__cadenceCspViolations || [])
      .catch(() => []);
    expect(
      violations,
      `Unexpected browser Content-Security-Policy violations: ${JSON.stringify(violations, null, 2)}`
    ).toEqual([]);
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

    // 3. Unavailable workflow guard outside demo mode (/lay-summaries redirects to #view-unavailable)
    await expect(page.locator("#tab-btn-lay-summaries")).toHaveCount(0);
    await page.goto("lay-summaries");
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
    await page.goto("http://127.0.0.1:5175/subject-portal/");
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

      const panelAudit = await new AxeBuilder({ page })
        .include(`#${current.panel}`)
        .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"])
        .analyze();
      expect(panelAudit.violations).toEqual([]);

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

  test("should enforce hoisted CSP meta, delegated slider and 10 query panels in demo mode, and OIDC connect-src in non-demo mode", async ({
    page,
  }) => {
    const captureScreenshots = process.env.CAPTURE_CSP_SCREENSHOTS === "1";

    async function inspectHeadCsp(targetPage) {
      return targetPage.evaluate(() => {
        const headHtml = document.head.innerHTML;
        const cspMeta = document.head.querySelector(
          'meta[http-equiv="Content-Security-Policy"]'
        );
        const cspIndex = headHtml.search(
          /<meta\b[^>]*http-equiv\s*=\s*["']?Content-Security-Policy/i
        );
        const firstScriptIndex = headHtml.search(/<script\b/i);
        const firstLinkIndex = headHtml.search(/<link\b/i);
        return {
          content: cspMeta?.getAttribute("content") || "",
          cspIndex,
          firstScriptIndex,
          firstLinkIndex,
        };
      });
    }

    async function exerciseQueryPanels(fieldKeys, scopeSelector) {
      const inlineHandlers = await page
        .locator(scopeSelector)
        .evaluate((root) => {
          const matches = [];
          for (const el of root.querySelectorAll("*")) {
            for (const attr of el.getAttributeNames()) {
              if (attr.toLowerCase().startsWith("on")) {
                matches.push(`${el.tagName.toLowerCase()}[${attr}]`);
              }
            }
          }
          return matches;
        });
      expect(inlineHandlers).toEqual([]);

      for (const key of fieldKeys) {
        const flagBtn = page.locator(`#query-flag-${key}`);
        const panel = page.locator(`#query-panel-${key}`);
        const closeBtn = panel.locator(".btn-close-panel");

        await expect(flagBtn).toBeVisible();
        await expect(flagBtn).toHaveAttribute("aria-expanded", "false");
        await expect(panel).toBeHidden();

        const flagBox = await flagBtn.boundingBox();
        expect(flagBox?.width ?? 0).toBeGreaterThanOrEqual(48);
        expect(flagBox?.height ?? 0).toBeGreaterThanOrEqual(48);

        await flagBtn.click();
        await expect(panel).toBeVisible();
        await expect(flagBtn).toHaveAttribute("aria-expanded", "true");

        const closeBox = await closeBtn.boundingBox();
        expect(closeBox?.width ?? 0).toBeGreaterThanOrEqual(48);
        expect(closeBox?.height ?? 0).toBeGreaterThanOrEqual(48);

        await closeBtn.click();
        await expect(panel).toBeHidden();
        await expect(flagBtn).toHaveAttribute("aria-expanded", "false");
      }
    }

    // 1. Demo portal bundle (port 5175): strict same-origin CSP + slider + all 10 query panels
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
              pain_scale: {
                label: "Symptom Severity (Visual Analog Scale 0-10)",
                type: "visual_analog",
                required: true,
                min: 0,
                max: 10,
                step: 1,
                min_label: "No Pain (0)",
                max_label: "Worst Possible (10)",
              },
              has_symptoms: {
                label: "Are you experiencing any new physical symptoms today?",
                type: "choice_single",
                options: ["Yes", "No"],
              },
            },
          },
          {
            id: "inst_weekly_symptoms",
            name: "Weekly Symptoms & eCOA Checklist",
            description:
              "Please complete this survey detailing any adverse clinical signs experienced during the week.",
            items: {
              severity: {
                label: "Overall symptom severity this week",
                type: "choice_single",
                options: ["None", "Mild", "Moderate", "Severe"],
              },
              restricted_activity: {
                label: "Did symptoms restrict your daily activities?",
                type: "choice_single",
                options: ["Yes", "No"],
              },
              missed_doses: {
                label: "Did you miss any medication doses?",
                type: "choice_single",
                options: ["Yes", "No"],
              },
            },
          },
        ]),
      });
    });

    await page.route("**/econsent/templates/**", async (route) => {
      const url = route.request().url();
      if (url.includes("approved-content")) {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            template_id: "template-icf",
            template_name: "Main Informed Consent Form",
            version_index: 1,
            language_code: "en",
            clauses: [
              {
                clause_id: "clause-risk",
                title: "Risks & Side Effects",
                text: "There is a risk of mild temporary headache.",
                version_index: 1,
              },
            ],
            workflow_steps: [
              { type: "comprehension_check" },
              { type: "signature_placeholder" },
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
            questions: [
              {
                id: "q_headache",
                text: "What is a potential temporary side effect?",
                options: ["Headache", "Nausea", "Vision Loss"],
              },
              {
                id: "q_withdraw",
                text: "Can you withdraw from the study at any time?",
                options: ["Yes", "No"],
              },
            ],
            expected_answers: {
              q_headache: "Headache",
              q_withdraw: "Yes",
            },
            threshold_policy: { min_correct: 2 },
          }),
        });
      }
    });

    await page.goto("http://127.0.0.1:5175/subject-portal/");
    await completeInitialPinSetupIfOpen(page);

    const demoCsp = await inspectHeadCsp(page);
    expect(demoCsp.cspIndex).toBeGreaterThanOrEqual(0);
    if (demoCsp.firstScriptIndex >= 0) {
      expect(demoCsp.cspIndex).toBeLessThan(demoCsp.firstScriptIndex);
    }
    if (demoCsp.firstLinkIndex >= 0) {
      expect(demoCsp.cspIndex).toBeLessThan(demoCsp.firstLinkIndex);
    }
    expect(demoCsp.content).toContain("script-src 'self'");
    expect(demoCsp.content).toContain("connect-src 'self'");
    expect(demoCsp.content).toContain("frame-src 'self'");
    expect(demoCsp.content).not.toContain("localhost");
    expect(demoCsp.content).not.toContain("127.0.0.1");

    // inst_daily_diary: #pain_scale slider + 5 query panels
    await page.locator("#task-card-assign_01 .btn-start-task").click();
    await expect(page.locator("#view-questionnaire")).toBeVisible();

    const painSlider = page.locator("#pain_scale");
    const painDisplay = page.locator("#pain_scale_display");
    await expect(painSlider).toBeVisible();
    await painSlider.fill("7");
    await expect(painDisplay).toHaveText("Value: 7");

    await exerciseQueryPanels(
      ["vssbp", "vsdpb", "vshr", "pain_scale", "has_symptoms"],
      "#questionnaire-form-container"
    );

    // Open #query-panel-pain_scale to run WCAG 2.2 AA audit on the open query panel state
    await page.locator("#query-flag-pain_scale").click();
    await expect(page.locator("#query-panel-pain_scale")).toBeVisible();
    expect(
      await getControlBorderContrast(page, "#query-flag-pain_scale")
    ).toBeGreaterThanOrEqual(3);
    const openPanelAudit = await new AxeBuilder({ page })
      .include("#view-questionnaire")
      .withTags(WCAG_AA_TAGS)
      .analyze();
    expect(openPanelAudit.violations).toEqual([]);
    if (captureScreenshots) {
      await page.screenshot({ path: "/tmp/subject-portal-widgets-csp.png" });
    }
    await page.locator("#query-panel-pain_scale .btn-close-panel").click();
    await expect(page.locator("#query-panel-pain_scale")).toBeHidden();

    // inst_weekly_symptoms: 3 query panels
    await page.click("#btn-back-to-tasks");
    await expect(page.locator("#view-tasks")).toBeVisible();
    await page.locator("#task-card-assign_02 .btn-start-task").click();
    await expect(page.locator("#questionnaire-title")).toContainText(
      "Weekly Symptoms & eCOA Checklist"
    );
    await exerciseQueryPanels(
      ["severity", "restricted_activity", "missed_doses"],
      "#questionnaire-form-container"
    );

    // ConsentView MOCK_COMPREHENSION_CHECK: 2 query panels (total = 10)
    await page.click("#tab-btn-consent button");
    await expect(page.locator("#view-consent")).toBeVisible();
    await exerciseQueryPanels(
      ["q_headache", "q_withdraw"],
      "#consent-questions-container"
    );

    // 2. Non-demo portal build (port 5174): OIDC origin allowed in connect-src and frame-src
    await page.route(
      "http://127.0.0.1:8080/realms/cadence/.well-known/openid-configuration",
      async (route) => {
        await route.fulfill({
          status: 200,
          headers: {
            "Access-Control-Allow-Origin": "*",
            "Content-Type": "application/json",
          },
          body: JSON.stringify({
            issuer: "http://127.0.0.1:8080/realms/cadence",
            authorization_endpoint:
              "http://127.0.0.1:8080/realms/cadence/protocol/openid-connect/auth",
          }),
        });
      }
    );

    await page.goto("http://127.0.0.1:5174/subject-portal/");
    await completeInitialPinSetupIfOpen(page);

    const nonDemoCsp = await inspectHeadCsp(page);
    expect(nonDemoCsp.cspIndex).toBeGreaterThanOrEqual(0);
    if (nonDemoCsp.firstScriptIndex >= 0) {
      expect(nonDemoCsp.cspIndex).toBeLessThan(nonDemoCsp.firstScriptIndex);
    }
    expect(nonDemoCsp.content).toContain(
      "connect-src 'self' http://127.0.0.1:8080"
    );
    expect(nonDemoCsp.content).toContain(
      "frame-src 'self' http://127.0.0.1:8080"
    );

    const oidcDiscoveryStatus = await page.evaluate(async () => {
      const res = await fetch(
        "http://127.0.0.1:8080/realms/cadence/.well-known/openid-configuration"
      );
      return res.status;
    });
    expect(oidcDiscoveryStatus).toBe(200);
  });
});
