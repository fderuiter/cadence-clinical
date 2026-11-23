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
const PROD_PORTAL_URL = "http://127.0.0.1:5174/subject-portal/";
const DEMO_PORTAL_URL = "http://127.0.0.1:5175/subject-portal/";

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
  test.use({
    baseURL: "http://127.0.0.1:5175/subject-portal/",
    serviceWorkers: "block",
  });

  test.beforeEach(async ({ page }) => {
    await page.route("**/reconsent/pending/**", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify([]),
      });
    });

    // Intercept API calls to supply live mock data when running in authenticated mode
    await page.route("**/api/v1/**/assignments/subject/*", async (route) => {
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

    await page.route("**/api/v1/**/instruments", async (route) => {
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

    await page.route("**/api/v1/**/compliance", async (route) => {
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

    await page.route("**/api/v1/**/notifications", async (route) => {
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

  test("should successfully bypass third-party login, expose only My Wallet with zero unbacked startup API calls, and pass WCAG 2.1 AA audits on landing page", async ({
    page,
  }) => {
    const unbackedStartupRequests = [];
    page.on("request", (req) => {
      const url = req.url();
      if (
        url.includes("/api/v1/interop/") ||
        url.includes("/api/v1/econsent/")
      ) {
        unbackedStartupRequests.push(url);
      }
    });

    await page.goto(PROD_PORTAL_URL);
    await completeInitialPinSetupIfOpen(page);

    // Verify name is rendered based on injected storage mock credentials
    await expect(page.locator("#session-subject-id")).toContainText(
      "subject_mocked_001"
    );

    // Outside demo mode, authenticated participants land on /wallet and see only My Wallet
    await expect(page).toHaveURL(/\/subject-portal\/wallet(\?|$)/);
    await expect(page.locator("#view-wallet")).toBeVisible();
    await expect(page.locator('.nav-tabs [role="tab"]')).toHaveCount(1);
    await expect(page.locator("#btn-tab-btn-wallet")).toBeVisible();
    expect(unbackedStartupRequests).toEqual([]);

    // Ensure landing page header is loaded
    await expect(page.locator(".portal-header h1")).toContainText("My Cadence");

    await page.screenshot({
      path: "/tmp/subject-portal-production-wallet-only.png",
      fullPage: true,
    });

    // Run Axe automated accessibility audit on the whole page
    const auditResults = await new AxeBuilder({ page })
      .withTags(WCAG_AA_TAGS)
      .analyze();

    expect(auditResults.violations).toEqual([]);
  });

  test("should pass accessibility audits in questionnaire form view", async ({
    page,
  }) => {
    await page.goto(DEMO_PORTAL_URL);
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
    await page.goto(DEMO_PORTAL_URL);
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
    await page.goto(DEMO_PORTAL_URL);
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
    await page.goto(DEMO_PORTAL_URL);
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
    await page.goto(PROD_PORTAL_URL);
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

    // 3. Unavailable workflow guard outside demo mode across all 6 demo-gated screens
    for (const demoTabId of [
      "tasks",
      "compliance",
      "inbox",
      "consent",
      "lay-summaries",
    ]) {
      await expect(page.locator(`#tab-btn-${demoTabId}`)).toHaveCount(0);
    }

    for (const blockedPath of [
      "tasks",
      "questionnaire",
      "compliance",
      "inbox",
      "consent",
      "lay-summaries",
    ]) {
      await page.goto(`${PROD_PORTAL_URL}${blockedPath}`);
      await completeInitialPinSetupIfOpen(page);
      await expect(page.locator("#view-unavailable")).toBeVisible();
      await expect(page.locator("#btn-unavailable-return-tasks")).toContainText(
        "Go to My Wallet"
      );
      if (blockedPath === "tasks") {
        await page.screenshot({
          path: "/tmp/subject-portal-unavailable-guard.png",
          fullPage: true,
        });
      }
      const unavailableAudit = await new AxeBuilder({ page })
        .include("#view-unavailable")
        .withTags(WCAG_AA_TAGS)
        .analyze();
      expect(unavailableAudit.violations).toEqual([]);
      await page.click("#btn-unavailable-return-tasks");
      await expect(page.locator("#view-wallet")).toBeVisible();
      await expect(page).toHaveURL(/\/subject-portal\/wallet(\?|$)/);
    }

    // 4. Form wrappers and numeric input attributes in demo mode
    await page.goto(DEMO_PORTAL_URL);
    await completeInitialPinSetupIfOpen(page);
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
    await page.goto("http://127.0.0.1:5174/subject-portal/");
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

    // Sign out from PinUnlockModal transitions cleanly to unauthenticated unavailable notice + login prompt
    await page.evaluate(() => sessionStorage.clear());
    await page.click("#btn-pin-unlock-signout");
    await expect(page.locator("#view-unavailable")).toBeVisible();
    await expect(page.locator("#btn-unavailable-signin")).toBeVisible();
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

    await page.goto(DEMO_PORTAL_URL);
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

  test("should pass WCAG 2.2 AA contrast and SC 1.4.11 >= 3:1 control border audits on demo-mode WalletView (including selected receipt), ConsentView, and LaySummariesView", async ({
    page,
  }) => {
    const captureScreenshots = process.env.CAPTURE_OVERLAY_SCREENSHOTS === "1";
    await page.route(
      "**/payments/participant/me/expenses/ocr-extract",
      async (route) => {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            ocrReadable: true,
            merchantName: "Metro Parking Garage",
            expenseDate: "2026-11-20",
            grossAmount: 18.5,
            taxDeduction: 0,
            netAmount: 18.5,
            category: "PARKING",
            confidenceScore: 0.96,
          }),
        });
      }
    );

    await page.goto("http://127.0.0.1:5175/subject-portal/");
    await completeInitialPinSetupIfOpen(page);

    // 1. WalletView: gradient stipend card, receipt upload zone, selected receipt state, and expense form controls
    await page.click("#tab-btn-wallet button");
    await expect(page.locator("#view-wallet")).toBeVisible();
    const walletAudit = await new AxeBuilder({ page })
      .include("#view-wallet")
      .withTags(WCAG_AA_TAGS)
      .analyze();
    expect(walletAudit.violations).toEqual([]);

    const receiptDropzone = page.locator("#receipt-upload-dropzone");
    await expect(receiptDropzone).toBeVisible();
    await expect(receiptDropzone.locator('[role="button"]')).toHaveCount(0);
    await expect(
      receiptDropzone.locator(
        "button.clinical-dropzone-browse-btn[type='button']"
      )
    ).toBeVisible();

    // Select a receipt image and verify SC 2.5.3 Label in Name + non-hidden filename + sibling Remove Receipt button
    const tinyPngBase64 =
      "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
    await page.locator("#receipt-file-input").setInputFiles({
      name: "parking-receipt-nov20.png",
      mimeType: "image/png",
      buffer: Buffer.from(tinyPngBase64, "base64"),
    });

    const selectedFilename = page.locator("#receipt-selected-filename");
    await expect(selectedFilename).toBeVisible();
    await expect(selectedFilename).toContainText("parking-receipt-nov20.png");
    const filenameHiddenFromAt = await selectedFilename.evaluate(
      (el) => el.closest('[aria-hidden="true"]') !== null
    );
    expect(filenameHiddenFromAt).toBe(false);
    await expect(
      receiptDropzone.locator(
        "button.clinical-dropzone-remove-btn[type='button']"
      )
    ).toBeVisible();

    const walletSelectedReceiptAudit = await new AxeBuilder({ page })
      .include("#view-wallet")
      .withTags(WCAG_AA_TAGS)
      .analyze();
    expect(walletSelectedReceiptAudit.violations).toEqual([]);
    if (captureScreenshots) {
      await page.screenshot({ path: "/tmp/wallet-selected-receipt-a11y.png" });
    }

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
    await page.goto(DEMO_PORTAL_URL);
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

    // Outside demo mode, only My Wallet is rendered in the tablist and gated views use region + aria-label with zero dangling aria-labelledby
    await page.goto(PROD_PORTAL_URL);
    await completeInitialPinSetupIfOpen(page);
    await expect(page.locator('.nav-tabs [role="tab"]')).toHaveCount(1);
    await expect(page.locator("#btn-tab-btn-wallet")).toHaveAttribute(
      "aria-selected",
      "true"
    );
    await expect(page.locator("#btn-tab-btn-wallet")).toHaveAttribute(
      "tabindex",
      "0"
    );
    const nonDemoStates = await page.evaluate(() => {
      const danglingLabelledBy = Array.from(
        document.querySelectorAll("[aria-labelledby]")
      )
        .filter((el) =>
          el
            .getAttribute("aria-labelledby")
            .trim()
            .split(/\s+/)
            .some((id) => !document.getElementById(id))
        )
        .map((el) => el.id || el.tagName);
      const layView = document.getElementById("view-lay-summaries");
      return {
        layRole: layView?.getAttribute("role"),
        layAriaLabel: layView?.getAttribute("aria-label"),
        layHasLabelledBy: layView?.hasAttribute("aria-labelledby"),
        danglingLabelledBy,
      };
    });
    expect(nonDemoStates.layRole).toBe("region");
    expect(nonDemoStates.layAriaLabel).toBe(
      "Plain-Language Clinical Trial Results"
    );
    expect(nonDemoStates.layHasLabelledBy).toBe(false);
    expect(nonDemoStates.danglingLabelledBy).toEqual([]);

    // In demo mode, all 6 portal tabs support roving ArrowRight navigation and pass Axe
    await page.goto(DEMO_PORTAL_URL);
    await completeInitialPinSetupIfOpen(page);

    const expectedTabs = [
      { id: "btn-tab-btn-tasks", panel: "view-tasks" },
      { id: "btn-tab-btn-compliance", panel: "view-compliance" },
      { id: "btn-tab-btn-inbox", panel: "view-inbox" },
      { id: "btn-tab-btn-consent", panel: "view-consent" },
      { id: "btn-tab-btn-wallet", panel: "view-wallet" },
      { id: "btn-tab-btn-lay-summaries", panel: "view-lay-summaries" },
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

      const viewStates = await page.evaluate((activePanelId) => {
        const allViews = Array.from(document.querySelectorAll(".portal-view"));
        const danglingLabelledBy = Array.from(
          document.querySelectorAll("[aria-labelledby]")
        )
          .filter((el) =>
            el
              .getAttribute("aria-labelledby")
              .trim()
              .split(/\s+/)
              .some((id) => !document.getElementById(id))
          )
          .map((el) => el.id || el.tagName);
        const layView = document.getElementById("view-lay-summaries");
        return {
          activeHidden: document
            .getElementById(activePanelId)
            .hasAttribute("hidden"),
          activeInert: document
            .getElementById(activePanelId)
            .hasAttribute("inert"),
          inactiveAllHiddenAndInert: allViews
            .filter((el) => el.id !== activePanelId)
            .every(
              (el) => el.hasAttribute("hidden") && el.hasAttribute("inert")
            ),
          layRole: layView?.getAttribute("role"),
          layHasLabelledBy: layView?.hasAttribute("aria-labelledby"),
          danglingLabelledBy,
        };
      }, current.panel);

      expect(viewStates.activeHidden).toBe(false);
      expect(viewStates.activeInert).toBe(false);
      expect(viewStates.inactiveAllHiddenAndInert).toBe(true);
      expect(viewStates.layRole).toBe("tabpanel");
      expect(viewStates.layHasLabelledBy).toBe(true);
      expect(viewStates.danglingLabelledBy).toEqual([]);

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
    await page.goto(DEMO_PORTAL_URL);
    await completeInitialPinSetupIfOpen(page);

    // Queue an offline submission so `.has-queued` and `.sync-badge` render
    await page.context().setOffline(true);
    await page.locator("#task-card-assign_01 .btn-start-task").click();
    await page.fill("#vssbp", "118");
    await page.fill("#vsdpb", "76");
    await page.fill("#vshr", "68");
    await page.click("#btn-submit-questionnaire");
    await page.fill("#sign-username", "subject_mocked_001");
    await page.fill("#sign-password", "246810");
    await page.click("#btn-modal-sign");
    await expect(page.locator("#portal-sign-modal")).toBeHidden();
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

  test("should focus visible view headings on tab activation, render #inbox-container visibly with Enter/Space actions, and preserve #portal-ledger-timeline focus", async ({
    page,
  }) => {
    const captureScreenshots = process.env.CAPTURE_PORTAL_SCREENSHOTS === "1";

    await page.route("**/notifications/*/acknowledge", async (route) => {
      const url = route.request().url();
      const idMatch = url.match(/notifications\/([^/]+)\/acknowledge/);
      const id = idMatch ? idMatch[1] : "notif_01";
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          id,
          subject_id: "subject_mocked_001",
          message: "Acknowledged reminder",
          channel: "IN_APP",
          is_read: true,
          read_at: new Date().toISOString(),
        }),
      });
    });

    await page.goto(DEMO_PORTAL_URL);
    await completeInitialPinSetupIfOpen(page);

    // Clicking Inbox tab focuses visible #inbox-view-heading and renders #inbox-container visibly
    await page.locator("#btn-tab-btn-inbox").click();
    await expect(page.locator("#view-inbox")).toBeVisible();
    await expect(page.locator("#inbox-view-heading")).toBeFocused();

    const inboxContainer = page.locator("#inbox-container");
    await expect(inboxContainer).toBeVisible();

    const inboxAudit = await new AxeBuilder({ page })
      .include("#view-inbox")
      .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"])
      .analyze();
    expect(inboxAudit.violations).toEqual([]);

    const firstAck = page.locator("#notif-card-notif_01 .btn-acknowledge");
    await expect(firstAck).toBeVisible();
    await firstAck.focus();
    if (captureScreenshots) {
      await page.screenshot({
        path: "/tmp/subject-portal-inbox-and-ledger-focus.png",
      });
    }
    await page.keyboard.press("Enter");

    const secondAck = page.locator("#notif-card-notif_02 .btn-acknowledge");
    await expect(secondAck).toBeFocused();
    await page.keyboard.press("Space");
    await expect(page.locator("#notif-card-notif_02")).toBeFocused();

    // Clicking Compliance tab focuses #compliance-view-heading and passes scrollable-region-focusable on #portal-ledger-timeline
    await page.locator("#btn-tab-btn-compliance").click();
    await expect(page.locator("#view-compliance")).toBeVisible();
    await expect(page.locator("#compliance-view-heading")).toBeFocused();
    await expect(page.locator("#portal-ledger-timeline")).toHaveAttribute(
      "tabindex",
      "0"
    );

    const complianceAudit = await new AxeBuilder({ page })
      .include("#view-compliance")
      .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"])
      .analyze();
    expect(complianceAudit.violations).toEqual([]);
  });

  test("should guard untouched VAS sliders, allow deliberate 0 scores, wire view-questionnaire tabpanel ARIA, and flush drafts on tab switch", async ({
    page,
  }) => {
    await page.route("**/instruments", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify([
          {
            id: "inst_daily_diary",
            name: "Daily Health & Vital Diary",
            description:
              "Please record your blood pressure, pain level, and optional fatigue score.",
            items: {
              vssbp: {
                label: "Systolic Blood Pressure (mmHg)",
                type: "numeric",
                required: true,
                min: 50,
                max: 250,
              },
              pain_scale: {
                label: "Current Pain Level (0 = No Pain, 10 = Worst Pain)",
                type: "visual_analog",
                required: true,
                min: 0,
                max: 10,
                step: 1,
                min_label: "0 - No Pain",
                max_label: "10 - Worst Possible Pain",
              },
              fatigue_scale: {
                label: "Optional Fatigue Score (0 = None, 10 = Severe)",
                type: "visual_analog",
                required: false,
                min: 0,
                max: 10,
                step: 1,
                min_label: "0 - None",
                max_label: "10 - Severe",
              },
            },
          },
        ]),
      });
    });

    await page.goto(DEMO_PORTAL_URL);
    await completeInitialPinSetupIfOpen(page);

    await page.locator("#task-card-assign_01 .btn-start-task").click();

    const questionnaireView = page.locator("#view-questionnaire");
    const tasksTab = page.locator("#btn-tab-btn-tasks");
    await expect(questionnaireView).toBeVisible();
    await expect(questionnaireView).toHaveAttribute("role", "tabpanel");
    await expect(questionnaireView).toHaveAttribute("tabindex", "0");
    await expect(questionnaireView).toHaveAttribute(
      "aria-labelledby",
      "btn-tab-btn-tasks"
    );
    await expect(tasksTab).toHaveAttribute(
      "aria-controls",
      "view-questionnaire"
    );

    const painSlider = page.locator("#pain_scale");
    const painBadge = page.locator("#pain_scale_display");
    const fatigueSlider = page.locator("#fatigue_scale");
    await expect(painBadge).toHaveText("Not answered");
    await expect(painSlider).toHaveAttribute("data-touched", "false");
    await expect(painSlider).toHaveAttribute("aria-valuetext", "Not answered");

    const sliderBox = await painSlider.boundingBox();
    expect(sliderBox.height).toBeGreaterThanOrEqual(48);

    // Fill numeric field, leave required VAS slider untouched -> submit must reject
    await page.fill("#vssbp", "124");
    await page.click("#btn-submit-questionnaire");

    await expect(page.locator("#field-container-pain_scale")).toHaveClass(
      /has-error/
    );
    await expect(page.locator("#questionnaire-form-error")).toBeVisible();

    const untouchedAudit = await new AxeBuilder({ page })
      .withTags(WCAG_AA_TAGS)
      .analyze();
    expect(untouchedAudit.violations).toEqual([]);

    await page.screenshot({
      path: "/tmp/vas-slider-untouched-validation.png",
      fullPage: true,
    });

    // Deliberately select 0 via keyboard Home key
    await painSlider.focus();
    await page.keyboard.press("Home");
    await expect(painSlider).toHaveAttribute("data-touched", "true");
    await expect(painSlider).not.toHaveAttribute("aria-valuetext");
    await expect(painBadge).toHaveText("Value: 0");
    await expect(fatigueSlider).toHaveAttribute("data-touched", "false");

    // Immediately switch tabs via TabBar (no 1500ms wait) and return to Tasks
    await page.click("#btn-tab-btn-compliance");
    await expect(page.locator("#view-compliance")).toBeVisible();
    await expect(questionnaireView).toBeHidden();
    await expect(tasksTab).toHaveAttribute("aria-controls", "view-tasks");

    await page.click("#btn-tab-btn-tasks");
    const resumeBtn = page.locator("#task-card-assign_01 .btn-start-task");
    await expect(resumeBtn).toContainText("Resume Survey");

    // Reopen survey and verify both numeric input and deliberate 0 VAS score restored
    await resumeBtn.click();
    await expect(page.locator("#vssbp")).toHaveValue("124");
    await expect(painSlider).toHaveAttribute("data-touched", "true");
    await expect(painBadge).toHaveText("Value: 0");
    await expect(fatigueSlider).toHaveAttribute("data-touched", "false");

    await page.screenshot({
      path: "/tmp/vas-slider-zero-selected-and-draft-resume.png",
      fullPage: true,
    });

    // Validate & open signature modal cleanly with deliberate 0 required VAS + untouched optional VAS
    await page.click("#btn-submit-questionnaire");
    await expect(page.locator("#portal-sign-modal")).toBeVisible();

    const modalAudit = await new AxeBuilder({ page })
      .withTags(WCAG_AA_TAGS)
      .analyze();
    expect(modalAudit.violations).toEqual([]);
  });

  test("should scroll all five modals at 320x568 and 320x256 while preserving PIN Escape lock and wrapping #btn-disburse-full-amount", async ({
    page,
  }) => {
    let reconsentEnabled = false;
    await page.route("**/econsent/reconsent/pending/**", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(
          reconsentEnabled
            ? [
                {
                  id: "req_amendment_scroll_v2",
                  study_id: "STUDY-01",
                  protocol_version: "2.0",
                  change_summary:
                    "Updated safety monitoring schedule and participant stipend disbursement options.",
                },
              ]
            : []
        ),
      });
    });

    await page.route("**/api/v1/auth/signature-verification", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ sig_token: "e2e_reconsent_sig_token" }),
      });
    });
    await page.route(
      "**/api/v1/execution/amendments/reconsent",
      async (route) => {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({ persisted: true }),
        });
      }
    );
    await page.route(
      "**/api/v1/econsent/reconsent/complete/**",
      async (route) => {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({ completed: true }),
        });
      }
    );

    await page.route(
      "**/api/v1/payments/participant/me/wallet",
      async (route) => {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            wallet: {
              id: "wallet_mock_001",
              study_id: "study_001",
              site_id: "site_001",
              current_balance: 50,
              lifetime_stipends: 50,
              lifetime_reimbursements: 0,
              lifetime_disbursements: 0,
              w9_status: "NOT_REQUIRED",
              currency: "USD",
            },
            recent_transactions: [],
            expense_claims: [],
          }),
        });
      }
    );

    async function expectButtonReachableInViewport(
      selector,
      viewportWidth,
      viewportHeight
    ) {
      const locator = page.locator(selector);
      await expect(locator).toBeVisible();
      await locator.scrollIntoViewIfNeeded();
      const box = await locator.boundingBox();
      expect(box, `Missing boundingBox for ${selector}`).not.toBeNull();
      expect(box.x).toBeGreaterThanOrEqual(-0.5);
      expect(box.x + box.width).toBeLessThanOrEqual(viewportWidth + 0.5);
      expect(box.y).toBeGreaterThanOrEqual(-0.5);
      expect(box.y + box.height).toBeLessThanOrEqual(viewportHeight + 0.5);
    }

    for (const viewport of [
      { width: 320, height: 568 },
      { width: 320, height: 256 },
    ]) {
      await page.setViewportSize(viewport);

      // Ensure PinSetupModal is open at the start of each viewport iteration
      // (PIN setup/unlock run in non-demo authenticated mode; demo builds use an ephemeral session key)
      reconsentEnabled = false;
      await page.goto("");
      const pinSetupModal = page.locator("#portal-pin-setup-modal");
      const pinUnlockModal = page.locator("#portal-pin-unlock-modal");
      await Promise.race([
        pinSetupModal.waitFor({ state: "visible", timeout: 5000 }),
        pinUnlockModal.waitFor({ state: "visible", timeout: 5000 }),
      ]);
      if (await pinUnlockModal.isVisible().catch(() => false)) {
        await page.locator("#btn-pin-unlock-reset").scrollIntoViewIfNeeded();
        await page.click("#btn-pin-unlock-reset");
        await expect(page.locator("#pin-reset-confirmation")).toBeVisible();
        await page.locator("#btn-pin-reset-confirm").scrollIntoViewIfNeeded();
        await page.click("#btn-pin-reset-confirm");
      }

      // 1. #portal-pin-setup-modal
      await expect(pinSetupModal).toBeVisible();
      await page.keyboard.press("Escape");
      await expect(pinSetupModal).toBeVisible();

      await expectButtonReachableInViewport(
        "#btn-pin-setup-signout",
        viewport.width,
        viewport.height
      );
      await expectButtonReachableInViewport(
        "#btn-pin-setup-confirm",
        viewport.width,
        viewport.height
      );

      if (viewport.height === 256) {
        // Verify Sign Out remains clickable at 320x256 while Escape is blocked
        await page.locator("#btn-pin-setup-signout").click();
        await expect(pinSetupModal).toBeHidden();
        // Restore mock session to continue testing remaining modals at 320x256
        await page.evaluate(() => {
          sessionStorage.setItem("mock_user_id", "subject_mocked_001");
          sessionStorage.setItem("mock_token", "mock_jwt_token_xyz_123");
        });
        await page.reload();
        if (await pinUnlockModal.isVisible().catch(() => false)) {
          await page.locator("#btn-pin-unlock-reset").scrollIntoViewIfNeeded();
          await page.click("#btn-pin-unlock-reset");
          await page.locator("#btn-pin-reset-confirm").scrollIntoViewIfNeeded();
          await page.click("#btn-pin-reset-confirm");
        }
        await expect(pinSetupModal).toBeVisible();
      }

      await page.locator("#setup-pin").scrollIntoViewIfNeeded();
      await page.fill("#setup-pin", "246810");
      await page.locator("#confirm-setup-pin").scrollIntoViewIfNeeded();
      await page.fill("#confirm-setup-pin", "246810");
      await page.locator("#btn-pin-setup-confirm").scrollIntoViewIfNeeded();
      await page.click("#btn-pin-setup-confirm");
      await expect(pinSetupModal).toBeHidden();

      // 2. #portal-pin-unlock-modal and #pin-reset-confirmation
      await page.reload();
      await expect(pinUnlockModal).toBeVisible();
      await page.keyboard.press("Escape");
      await expect(pinUnlockModal).toBeVisible();

      await expectButtonReachableInViewport(
        "#btn-pin-unlock-reset",
        viewport.width,
        viewport.height
      );
      await expectButtonReachableInViewport(
        "#btn-pin-unlock-signout",
        viewport.width,
        viewport.height
      );
      await expectButtonReachableInViewport(
        "#btn-pin-unlock-confirm",
        viewport.width,
        viewport.height
      );

      // Open #pin-reset-confirmation and verify recovery actions stay reachable and clickable
      await page.locator("#btn-pin-unlock-reset").scrollIntoViewIfNeeded();
      await page.click("#btn-pin-unlock-reset");
      await expect(page.locator("#pin-reset-confirmation")).toBeVisible();
      await page.keyboard.press("Escape");
      await expect(pinUnlockModal).toBeVisible();

      const resetPrimarySelector = (await page
        .locator("#btn-pin-reset-confirm")
        .count())
        ? "#btn-pin-reset-confirm"
        : "#btn-pin-reauth-oidc";
      await expectButtonReachableInViewport(
        resetPrimarySelector,
        viewport.width,
        viewport.height
      );
      await expectButtonReachableInViewport(
        "#btn-pin-reset-cancel",
        viewport.width,
        viewport.height
      );
      await expectButtonReachableInViewport(
        "#btn-pin-reset-signout",
        viewport.width,
        viewport.height
      );

      await page.locator("#btn-pin-reset-cancel").scrollIntoViewIfNeeded();
      await page.click("#btn-pin-reset-cancel");
      await expect(page.locator("#pin-unlock-form")).toBeVisible();

      await page.locator("#unlock-pin").scrollIntoViewIfNeeded();
      await page.fill("#unlock-pin", "246810");
      await page.locator("#btn-pin-unlock-confirm").scrollIntoViewIfNeeded();
      await page.click("#btn-pin-unlock-confirm");
      await expect(pinUnlockModal).toBeHidden();

      // 3. #portal-reconsent-modal (demo-gated consent/tasks screens run on DEMO_PORTAL_URL)
      reconsentEnabled = true;
      await page.goto(DEMO_PORTAL_URL);
      await completeInitialPinSetupIfOpen(page);

      const reconsentModal = page.locator("#portal-reconsent-modal");
      await expect(reconsentModal).toBeVisible();
      await expectButtonReachableInViewport(
        "#btn-reconsent-cancel",
        viewport.width,
        viewport.height
      );
      await expectButtonReachableInViewport(
        "#btn-reconsent-submit",
        viewport.width,
        viewport.height
      );
      await page.locator("#btn-reconsent-cancel").scrollIntoViewIfNeeded();
      await page.click("#btn-reconsent-cancel");
      await expect(reconsentModal).toBeHidden();
      await expect(
        page.locator("#task-card-assign_01 .btn-start-task")
      ).toBeDisabled();
      reconsentEnabled = false;
      await page.locator("#btn-open-reconsent-modal").scrollIntoViewIfNeeded();
      await page.click("#btn-open-reconsent-modal");
      await expect(reconsentModal).toBeVisible();
      await page.check("#reconsent_q_changes_opt_0");
      await page.check("#reconsent_q_voluntary_opt_0");
      await page.fill("#reconsent-sign-password", "DemoAmendmentPass123!");
      await page.locator("#btn-reconsent-submit").scrollIntoViewIfNeeded();
      await page.click("#btn-reconsent-submit");
      await expect(reconsentModal).toBeHidden();

      // 4. #portal-sign-modal (imperative display toggling + credential clearing)
      const startTaskBtn = page.locator("#task-card-assign_01 .btn-start-task");
      await startTaskBtn.scrollIntoViewIfNeeded();
      await startTaskBtn.click();
      await page.fill("#vssbp", "120");
      await page.fill("#vsdpb", "80");
      await page.fill("#vshr", "70");
      const submitQuestionnaireBtn = page.locator("#btn-submit-questionnaire");
      await submitQuestionnaireBtn.scrollIntoViewIfNeeded();
      await submitQuestionnaireBtn.click();

      const signModal = page.locator("#portal-sign-modal");
      await expect(signModal).toBeVisible();
      await expectButtonReachableInViewport(
        "#btn-modal-cancel",
        viewport.width,
        viewport.height
      );
      await expectButtonReachableInViewport(
        "#btn-modal-sign",
        viewport.width,
        viewport.height
      );
      await page.locator("#sign-password").scrollIntoViewIfNeeded();
      await page.fill("#sign-password", "TempPin999!");
      await page.keyboard.press("Escape");
      await expect(signModal).toBeHidden();
      await expect(page.locator("#sign-password")).toHaveValue("");

      // 5. #wallet-disburse-modal + #btn-disburse-full-amount wrapping
      await page.click("#btn-tab-btn-wallet");
      await expect(page.locator("#view-wallet")).toBeVisible();
      const disburseBtn = page.locator("#btn-disburse-balance");
      await disburseBtn.scrollIntoViewIfNeeded();
      await disburseBtn.click();

      const walletDisburseModal = page.locator("#wallet-disburse-modal");
      await expect(walletDisburseModal).toBeVisible();

      const fullAmountBtn = page.locator("#btn-disburse-full-amount");
      await fullAmountBtn.scrollIntoViewIfNeeded();
      const modalCardBox = await walletDisburseModal
        .locator(".modal")
        .boundingBox();
      const fullAmountBox = await fullAmountBtn.boundingBox();
      expect(modalCardBox).not.toBeNull();
      expect(fullAmountBox).not.toBeNull();
      expect(fullAmountBox.x).toBeGreaterThanOrEqual(modalCardBox.x - 0.5);
      expect(fullAmountBox.x + fullAmountBox.width).toBeLessThanOrEqual(
        modalCardBox.x + modalCardBox.width + 0.5
      );
      expect(fullAmountBox.x + fullAmountBox.width).toBeLessThanOrEqual(
        viewport.width + 0.5
      );

      await expectButtonReachableInViewport(
        "#btn-disburse-cancel",
        viewport.width,
        viewport.height
      );
      await expectButtonReachableInViewport(
        "#btn-disburse-confirm",
        viewport.width,
        viewport.height
      );

      await page.locator("#btn-disburse-cancel").scrollIntoViewIfNeeded();
      await page.click("#btn-disburse-cancel");
      await expect(walletDisburseModal).toBeHidden();
    }
  });

  test("should place #task-card-assign_01 above the fold on 375x812 with bottom-anchored TabBar and collapsible ParticipantCard details", async ({
    page,
  }) => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.goto(DEMO_PORTAL_URL);
    await completeInitialPinSetupIfOpen(page);

    const navTabs = page.locator(".nav-tabs");
    await expect(navTabs).toBeVisible();
    const navBox = await navTabs.boundingBox();
    expect(navBox).not.toBeNull();
    expect(Math.abs(navBox.y + navBox.height - 812)).toBeLessThanOrEqual(2);

    // Every visible tab button preserves >= 48px touch target height
    const tabButtons = page.locator('.nav-tabs [role="tab"]');
    const tabCount = await tabButtons.count();
    expect(tabCount).toBeGreaterThanOrEqual(4);
    for (let i = 0; i < tabCount; i++) {
      const tBox = await tabButtons.nth(i).boundingBox();
      expect(tBox).not.toBeNull();
      expect(tBox.height).toBeGreaterThanOrEqual(48);
    }

    // Secondary session details are collapsed by default below 768px
    const mobileToggle = page.locator("#btn-mobile-session-toggle");
    const secondaryDetails = page.locator("#participant-secondary-details");
    await expect(mobileToggle).toBeVisible();
    await expect(mobileToggle).toHaveAttribute("aria-expanded", "false");
    await expect(secondaryDetails).toBeHidden();

    // main#main-content and #task-card-assign_01 sit above the initial fold and above the bottom nav bar
    const demoBannerBox = await page
      .locator("#demo-mode-banner")
      .boundingBox()
      .catch(() => null);
    const mainBox = await page.locator("main#main-content").boundingBox();
    const firstTaskBox = await page
      .locator("#task-card-assign_01")
      .boundingBox();
    expect(mainBox).not.toBeNull();
    expect(firstTaskBox).not.toBeNull();
    expect(mainBox.y).toBeGreaterThanOrEqual(0);
    expect(mainBox.y - (demoBannerBox?.height ?? 0)).toBeLessThan(350);
    expect(firstTaskBox.y).toBeGreaterThanOrEqual(0);
    expect(firstTaskBox.y + firstTaskBox.height).toBeLessThanOrEqual(
      navBox.y + 0.5
    );
    expect(firstTaskBox.y + firstTaskBox.height).toBeLessThanOrEqual(812);

    // Expanding mobile toggle reveals secondary session details and site contacts
    await mobileToggle.click();
    await expect(mobileToggle).toHaveAttribute("aria-expanded", "true");
    await expect(secondaryDetails).toBeVisible();
    await expect(page.locator("#btn-logout")).toBeVisible();
    await expect(page.locator("#participant-site-contacts")).toBeVisible();

    const mobileAudit = await new AxeBuilder({ page })
      .withTags(WCAG_AA_TAGS)
      .analyze();
    expect(mobileAudit.violations).toEqual([]);

    await mobileToggle.click();
    await expect(mobileToggle).toHaveAttribute("aria-expanded", "false");
    await expect(secondaryDetails).toBeHidden();
  });

  test("should keep all six Lucide tabs reachable on 320px-480px phones, prevent ComplianceView overflow below 1024px, align 768x1024 tablet shell, keep SyncDrawer overlay-only, and cap .portal-main at 800px", async ({
    page,
  }) => {
    // 1. Mobile 6-tab reachability, 16px aria-hidden SVGs, 48px targets, and 200% text zoom (320x640 & 480x800)
    await page.setViewportSize({ width: 320, height: 640 });
    await page.goto(DEMO_PORTAL_URL);
    await completeInitialPinSetupIfOpen(page);

    const allSixTabIds = [
      "tasks",
      "compliance",
      "inbox",
      "consent",
      "wallet",
      "lay-summaries",
    ];

    for (const mobileWidth of [320, 480]) {
      await page.setViewportSize({ width: mobileWidth, height: 640 });
      const navOverflowX = await page
        .locator(".nav-tabs")
        .evaluate((el) => getComputedStyle(el).overflowX);
      expect(navOverflowX).toBe("auto");

      const tabButtons = page.locator('.nav-tabs [role="tab"]');
      await expect(tabButtons).toHaveCount(6);

      for (const tabId of allSixTabIds) {
        const btn = page.locator(`#btn-tab-btn-${tabId}`);
        await btn.scrollIntoViewIfNeeded();
        const box = await btn.boundingBox();
        expect(box).not.toBeNull();
        expect(box.height).toBeGreaterThanOrEqual(48);
        expect(box.width).toBeGreaterThanOrEqual(48);
        expect(box.x).toBeGreaterThanOrEqual(-0.5);
        expect(box.x + box.width).toBeLessThanOrEqual(mobileWidth + 0.5);

        const iconCheck = await btn.evaluate((el) => {
          const svg = el.querySelector("svg.lucide");
          const fontSizePx = parseFloat(getComputedStyle(el).fontSize);
          return {
            hasSvg: Boolean(svg),
            ariaHidden: svg?.getAttribute("aria-hidden"),
            svgWidth: svg?.getAttribute("width"),
            svgHeight: svg?.getAttribute("height"),
            hasEmoji: /\p{Extended_Pictographic}/u.test(el.textContent || ""),
            fontSizePx,
          };
        });
        expect(iconCheck).toEqual({
          hasSvg: true,
          ariaHidden: "true",
          svgWidth: "16",
          svgHeight: "16",
          hasEmoji: false,
          fontSizePx: expect.any(Number),
        });
        expect(iconCheck.fontSizePx).toBeGreaterThanOrEqual(12);
      }

      // Click the 6th tab on narrow phone to confirm horizontal scroll + activation
      const lastTab = page.locator("#btn-tab-btn-lay-summaries");
      await lastTab.scrollIntoViewIfNeeded();
      await lastTab.click();
      await expect(page.locator("#view-lay-summaries")).toBeVisible();

      const firstTab = page.locator("#btn-tab-btn-tasks");
      await firstTab.scrollIntoViewIfNeeded();
      await firstTab.click();
      await expect(page.locator("#view-tasks")).toBeVisible();

      const docScrollWidth = await page.evaluate(
        () => document.documentElement.scrollWidth
      );
      expect(docScrollWidth).toBeLessThanOrEqual(mobileWidth);
    }

    // Verify 200% text zoom at 320x640 keeps all 6 tabs scrollable without page horizontal overflow
    await page.setViewportSize({ width: 320, height: 640 });
    await page.evaluate(() => {
      document.documentElement.style.fontSize = "32px";
    });
    await page.locator("#btn-tab-btn-lay-summaries").scrollIntoViewIfNeeded();
    const zoomedLastTabBox = await page
      .locator("#btn-tab-btn-lay-summaries")
      .boundingBox();
    expect(zoomedLastTabBox).not.toBeNull();
    expect(zoomedLastTabBox.x).toBeGreaterThanOrEqual(-0.5);
    expect(zoomedLastTabBox.x + zoomedLastTabBox.width).toBeLessThanOrEqual(
      320.5
    );
    expect(
      await page.evaluate(() => document.documentElement.scrollWidth)
    ).toBeLessThanOrEqual(320);
    await page.evaluate(() => {
      document.documentElement.style.fontSize = "";
    });

    // 2. ComplianceView single-column stacking below 1024px & zero horizontal overflow (320px, 950px, 1024px)
    await page.locator("#btn-tab-btn-compliance").scrollIntoViewIfNeeded();
    await page.click("#btn-tab-btn-compliance");
    await expect(page.locator("#view-compliance")).toBeVisible();

    for (const vp of [
      { width: 320, height: 640 },
      { width: 950, height: 800 },
    ]) {
      await page.setViewportSize(vp);
      const scoreBox = await page
        .locator("#view-compliance .compliance-score-card")
        .boundingBox();
      const historyBox = await page
        .locator("#view-compliance .history-card")
        .boundingBox();
      expect(scoreBox).not.toBeNull();
      expect(historyBox).not.toBeNull();
      expect(historyBox.y).toBeGreaterThanOrEqual(
        scoreBox.y + scoreBox.height - 1
      );
      expect(historyBox.x + historyBox.width).toBeLessThanOrEqual(
        vp.width + 0.5
      );
      expect(
        await page.evaluate(() => document.documentElement.scrollWidth)
      ).toBeLessThanOrEqual(vp.width);
    }

    await page.setViewportSize({ width: 1024, height: 768 });
    const desktopScoreBox = await page
      .locator("#view-compliance .compliance-score-card")
      .boundingBox();
    const desktopHistoryBox = await page
      .locator("#view-compliance .history-card")
      .boundingBox();
    expect(desktopScoreBox).not.toBeNull();
    expect(desktopHistoryBox).not.toBeNull();
    expect(desktopHistoryBox.x).toBeGreaterThan(
      desktopScoreBox.x + desktopScoreBox.width
    );
    expect(
      await page.evaluate(() => document.documentElement.scrollWidth)
    ).toBeLessThanOrEqual(1024);

    // 3. Portrait tablet 768x1024: hidden mobile session toggle + above-the-fold main content
    await page.click("#btn-tab-btn-tasks");
    await expect(page.locator("#view-tasks")).toBeVisible();
    await page.setViewportSize({ width: 768, height: 1024 });

    await expect(page.locator("#btn-mobile-session-toggle")).toBeHidden();
    const tabletNavBox = await page.locator(".portal-navigation").boundingBox();
    const tabletMainBox = await page.locator("main#main-content").boundingBox();
    const tabletTaskBox = await page
      .locator("#task-card-assign_01")
      .boundingBox();
    expect(tabletNavBox).not.toBeNull();
    expect(tabletMainBox).not.toBeNull();
    expect(tabletTaskBox).not.toBeNull();
    expect(tabletMainBox.x).toBeGreaterThan(tabletNavBox.x + 200);
    expect(tabletMainBox.y).toBeLessThan(260);
    expect(tabletTaskBox.y + tabletTaskBox.height).toBeLessThan(600);

    const tabletAudit = await new AxeBuilder({ page })
      .withTags(WCAG_AA_TAGS)
      .analyze();
    expect(tabletAudit.violations).toEqual([]);

    // 4. SyncDrawer pure overlay at 1024x768 (zero #app padding-right shift or .portal-main squeeze)
    await page.setViewportSize({ width: 1024, height: 768 });
    const mainBeforeDrawer = await page
      .locator("main#main-content")
      .boundingBox();
    expect(mainBeforeDrawer).not.toBeNull();

    await page.click("#btn-global-sync-drawer");
    await expect(page.locator("#global-sync-drawer")).toHaveClass(/active/);

    const drawerLayoutCheck = await page.evaluate(() => {
      const appEl = document.getElementById("app");
      return {
        hasDrawerOpenClass: appEl.classList.contains("drawer-open"),
        paddingRight: getComputedStyle(appEl).paddingRight,
      };
    });
    expect(drawerLayoutCheck).toEqual({
      hasDrawerOpenClass: false,
      paddingRight: "0px",
    });

    const mainDuringDrawer = await page
      .locator("main#main-content")
      .boundingBox();
    expect(mainDuringDrawer).not.toBeNull();
    expect(
      Math.abs(mainDuringDrawer.width - mainBeforeDrawer.width)
    ).toBeLessThan(1);

    await page.keyboard.press("Escape");
    await expect(page.locator("#global-sync-drawer")).not.toHaveClass(/active/);

    // 5. Wide desktop 1440x900: .portal-main capped at 800px across tasks, questionnaire, and consent
    await page.setViewportSize({ width: 1440, height: 900 });
    const wideMainBox = await page.locator("main#main-content").boundingBox();
    expect(wideMainBox).not.toBeNull();
    expect(wideMainBox.width).toBeGreaterThanOrEqual(799);
    expect(wideMainBox.width).toBeLessThanOrEqual(800.5);

    await page.click("#task-card-assign_01 .btn-start-task");
    await expect(page.locator("#view-questionnaire")).toBeVisible();
    const questionnaireCardBox = await page
      .locator("#view-questionnaire .questionnaire-card")
      .boundingBox();
    expect(questionnaireCardBox).not.toBeNull();
    expect(questionnaireCardBox.width).toBeLessThanOrEqual(800.5);

    await page.click("#btn-tab-btn-consent");
    await expect(page.locator("#view-consent")).toBeVisible();
    const consentMainBox = await page
      .locator("main#main-content")
      .boundingBox();
    expect(consentMainBox).not.toBeNull();
    expect(consentMainBox.width).toBeLessThanOrEqual(800.5);
  });

  test("should wrap PortalHeader and ReconsentBanner within 320px at 320x640 with zero horizontal overflow", async ({
    page,
  }) => {
    await page.route("**/econsent/reconsent/pending/**", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify([
          {
            id: "req_reflow_320",
            study_id: "STUDY-01",
            protocol_version: "2.1",
            change_summary:
              "Updated cardiology safety visit window and reimbursement terms.",
          },
        ]),
      });
    });

    await page.setViewportSize({ width: 320, height: 640 });
    await page.goto(DEMO_PORTAL_URL);
    await completeInitialPinSetupIfOpen(page);

    const reconsentModal = page.locator("#portal-reconsent-modal");
    if (await reconsentModal.isVisible().catch(() => false)) {
      await page.locator("#btn-reconsent-cancel").scrollIntoViewIfNeeded();
      await page.click("#btn-reconsent-cancel");
      await expect(reconsentModal).toBeHidden();
    }

    const header = page.locator(".portal-header");
    const banner = page.locator("#reconsent-urgent-banner");
    const bannerBtn = page.locator("#btn-open-reconsent-modal");
    await expect(header).toBeVisible();
    await expect(banner).toBeVisible();
    await expect(bannerBtn).toBeVisible();

    for (const locator of [header, banner, bannerBtn]) {
      const box = await locator.boundingBox();
      expect(box).not.toBeNull();
      expect(box.x).toBeGreaterThanOrEqual(-0.5);
      expect(box.x + box.width).toBeLessThanOrEqual(320.5);
    }

    const scrollWidth = await page.evaluate(
      () => document.documentElement.scrollWidth
    );
    expect(scrollWidth).toBeLessThanOrEqual(320);

    const reflowAudit = await new AxeBuilder({ page })
      .withTags(WCAG_AA_TAGS)
      .analyze();
    expect(reflowAudit.violations).toEqual([]);
  });
});
