import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";

async function completeInitialPinSetupIfOpen(page, pin = "246810") {
  const pinSetupModal = page.locator("#portal-pin-setup-modal");
  const pinUnlockModal = page.locator("#portal-pin-unlock-modal");
  await Promise.race([
    pinSetupModal.waitFor({ state: "visible", timeout: 4000 }),
    pinUnlockModal.waitFor({ state: "visible", timeout: 4000 }),
  ]).catch(() => {});

  if (await pinSetupModal.isVisible().catch(() => false)) {
    await page.fill("#setup-pin", pin);
    await page.fill("#confirm-setup-pin", pin);
    await page.click("#btn-pin-setup-confirm");
    await expect(pinSetupModal).toBeHidden();
  } else if (await pinUnlockModal.isVisible().catch(() => false)) {
    await page.fill("#unlock-pin", pin);
    await page.click("#btn-pin-unlock-confirm");
    await expect(pinUnlockModal).toBeHidden();
  }
}

const DEMO_PORTAL_URL = "http://127.0.0.1:5175/subject-portal/";

const PAIN_MOBILITY_INSTRUMENT = {
  id: "inst_pain_mobility",
  version_index: 1,
  name: "Daily Pain & Mobility Check",
  description: "Rate your current joint discomfort and daily mobility status.",
  items: {
    pain_vas: {
      label: "Current Joint Pain Severity (0 = None, 10 = Worst)",
      type: "vas",
      required: true,
      min: 0,
      max: 10,
    },
    mobility_status: {
      label: "Were you able to walk outdoors comfortably today?",
      type: "choice_single",
      required: true,
      options: ["Yes, without discomfort", "With mild discomfort", "No"],
    },
  },
};

async function setupBasePortalRoutes(page, options = {}) {
  const lazyInstrumentGate = options.lazyInstrumentGate ?? null;

  await page.route("**/reconsent/pending/**", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ pending: false }),
    });
  });

  await page.route("**/api/v1/**/assignments/subject/*", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify([
        {
          id: "assign_vas_01",
          subject_id: "subject_mocked_001",
          instrument_id: "inst_pain_mobility",
          instrument_name: "Daily Pain & Mobility Check",
          due_at: new Date(Date.now() + 4 * 3600 * 1000).toISOString(),
          end_date: new Date(Date.now() + 24 * 3600 * 1000).toISOString(),
          status: "PENDING",
        },
      ]),
    });
  });

  await page.route("**/subjects/*/instruments", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(
        lazyInstrumentGate ? [] : [PAIN_MOBILITY_INSTRUMENT]
      ),
    });
  });

  await page.route("**/interop/instruments/*", async (route) => {
    if (lazyInstrumentGate) {
      await lazyInstrumentGate;
    }
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(PAIN_MOBILITY_INSTRUMENT),
    });
  });

  await page.route("**/api/v1/**/compliance", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        subject_id: "subject_mocked_001",
        compliance_rate: 92,
        completed_count: 11,
        pending_count: 1,
        overdue_count: 0,
        assignments: [],
      }),
    });
  });

  await page.route("**/api/v1/**/notifications", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify([]),
    });
  });

  await page.route("**/econsent/documents/**", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify([
        {
          id: "consent_doc_01",
          title: "Phase III Cardiology Trial Informed Consent",
          version: "2.1",
          language: "en",
          content_html:
            "<p>This clinical trial evaluates participant safety and quality of life.</p>",
          comprehension_questions: [
            {
              id: "cq_1",
              question: "Is participation in this study voluntary?",
              options: ["Yes, voluntary at all times", "No, mandatory"],
              correct_index: 0,
            },
          ],
        },
      ]),
    });
  });

  await page.addInitScript(() => {
    sessionStorage.setItem("mock_user_id", "subject_mocked_001");
    sessionStorage.setItem("mock_token", "mock_jwt_token_xyz_123"); // pragma: allowlist secret
  });
}

test.describe("Idempotent Wallet Recovery, Payout Confirmation, and 48px Touch Targets", () => {
  test.use({ serviceWorkers: "block" });

  test("renders wallet loading skeleton, EUR currency formatting across header & cards, expenseClaims history, and $600 W-9 hold banner", async ({
    page,
  }) => {
    const nativeDialogs = [];
    page.on("dialog", async (dialog) => {
      nativeDialogs.push(dialog.message());
      await dialog.dismiss();
    });

    await setupBasePortalRoutes(page);

    let releaseWalletResponse;
    const walletGate = new Promise((resolve) => {
      releaseWalletResponse = resolve;
    });

    await page.route(
      "**/api/v1/payments/participant/me/wallet",
      async (route) => {
        await walletGate;
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            wallet: {
              id: "wal_eur_001",
              studyId: "study_001",
              siteId: "site_001",
              currency: "EUR",
              currentBalance: 145.5,
              lifetimeStipends: 625.0,
              lifetimeReimbursements: 60.5,
              lifetimeDisbursements: 540.0,
              taxableYtd: 625.0,
              is1099ThresholdExceeded: true,
              w9Status: "REQUIRED",
              achAccountMask: "DE89-***4321",
              virtualCardMask: "**** **** **** 8842",
            },
            expenseClaims: [
              {
                id: "claim_eur_01",
                category: "TRANSIT",
                merchantName: "Eurostar Regional Rail",
                expenseDate: "2026-11-15",
                grossAmount: 42.5,
                netAmount: 42.5,
                status: "AUTO_APPROVED",
                autoApproved: true,
                disbursementReference: "ACH-EUR-9012",
              },
              {
                id: "claim_eur_02",
                category: "LODGING",
                merchantName: "Clinic Partner Hotel",
                expenseDate: "2026-11-10",
                grossAmount: 135.0,
                netAmount: 135.0,
                status: "PENDING_REVIEW",
                autoApproved: false,
                reviewNotes: "Awaiting coordinator verification for lodging.",
              },
            ],
            recentTransactions: [
              {
                id: "tx_eur_01",
                description: "Visit 4 Clinical Stipend",
                transactionType: "STIPEND_CREDIT",
                amount: 145.5,
              },
            ],
          }),
        });
      }
    );

    await page.goto("");

    // Production landing opens /wallet directly; while wallet response is in flight, skeleton renders instead of "$0.00 USD"
    const skeleton = page.locator("#wallet-loading-skeleton");
    await expect(skeleton).toBeVisible();
    await expect(page.locator("#view-wallet")).not.toContainText("$0.00 USD");

    releaseWalletResponse();
    await completeInitialPinSetupIfOpen(page);

    await expect(skeleton).toBeHidden();
    await expect(page.locator("#wallet-balance-amount")).toHaveText("€145.50");
    await expect(page.locator("#session-wallet-balance")).toHaveText("€145.50");
    await expect(page.locator("#wallet-lifetime-stipends")).toHaveText(
      "€625.00"
    );
    await expect(page.locator("#wallet-taxable-ytd")).toHaveText("€625.00");

    // $600 Form W-9 hold banner is active and disables payout button
    const w9HoldBanner = page.locator("#wallet-w9-hold-banner");
    await expect(w9HoldBanner).toBeVisible();
    await expect(w9HoldBanner).toHaveAttribute("role", "alert");
    await expect(w9HoldBanner).toContainText(
      "Form W-9 Verification Hold Active"
    );
    await expect(page.locator("#btn-disburse-balance")).toBeDisabled();

    // Expense claims history renders both rows with EUR currency formatting
    const claimRows = page.locator(
      "#wallet-expense-claims .wallet-expense-claim-row"
    );
    await expect(claimRows).toHaveCount(2);
    await expect(page.locator("#wallet-expense-claims")).toContainText(
      "Eurostar Regional Rail"
    );
    await expect(page.locator("#wallet-expense-claims")).toContainText(
      "€42.50"
    );
    await expect(page.locator("#wallet-expense-claims")).toContainText(
      "€135.00"
    );

    const axeResults = await new AxeBuilder({ page })
      .include("#view-wallet")
      .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"])
      .analyze();
    expect(axeResults.violations).toEqual([]);

    // Verify paired dark-theme semantic tokens also pass WCAG 2.2 AA contrast
    await page.evaluate(() => {
      document.documentElement.setAttribute("data-theme", "dark");
    });
    const darkAxeResults = await new AxeBuilder({ page })
      .include("#view-wallet")
      .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"])
      .analyze();
    expect(darkAxeResults.violations).toEqual([]);
    await page.evaluate(() => {
      document.documentElement.removeAttribute("data-theme");
    });
    expect(nativeDialogs).toEqual([]);

    await page.screenshot({
      path: "/tmp/wallet-overview-claims-w9-hold.png",
      fullPage: true,
    });
  });

  test("recovers from failed OCR via same-file retry or manual entry with native date input, sends Idempotency-Key, and resets claim form", async ({
    page,
  }) => {
    const nativeDialogs = [];
    page.on("dialog", async (dialog) => {
      nativeDialogs.push(dialog.message());
      await dialog.dismiss();
    });

    await setupBasePortalRoutes(page);

    await page.route(
      "**/api/v1/payments/participant/me/wallet",
      async (route) => {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            wallet: {
              id: "wal_usd_001",
              studyId: "study_001",
              siteId: "site_001",
              currency: "USD",
              currentBalance: 125.0,
              lifetimeStipends: 250.0,
              lifetimeReimbursements: 0.0,
              lifetimeDisbursements: 125.0,
              taxableYtd: 250.0,
              is1099ThresholdExceeded: false,
              w9Status: "NOT_REQUIRED",
              achAccountMask: "ACH-***6789",
              virtualCardMask: "**** **** **** 4321",
            },
            expenseClaims: [],
            recentTransactions: [],
          }),
        });
      }
    );

    let ocrAttempts = 0;
    await page.route(
      "**/api/v1/payments/participant/me/expenses/ocr-extract",
      async (route) => {
        ocrAttempts += 1;
        if (ocrAttempts === 1) {
          await route.fulfill({
            status: 422,
            contentType: "application/json",
            body: JSON.stringify({
              detail: "OCR engine could not read blurred receipt image.",
            }),
          });
          return;
        }
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            ocrReadable: true,
            merchantName: "City Metro Cafe",
            expenseDate: "2026-11-18",
            grossAmount: 28.5,
            netAmount: 28.5,
            category: "MEAL",
            confidenceScore: 0.94,
          }),
        });
      }
    );

    const capturedExpenseRequests = [];
    await page.route(
      "**/api/v1/payments/participant/me/expenses",
      async (route) => {
        const req = route.request();
        const body = req.postDataJSON();
        capturedExpenseRequests.push({
          headerKey: req.headers()["idempotency-key"],
          bodyKey: body.idempotencyKey,
          merchantName: body.merchantName,
          grossAmount: body.grossAmount,
        });
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            id: "claim_idem_001",
            status: "AUTO_APPROVED",
            approvalDecision: "AUTO_APPROVED",
            autoApproved: true,
            merchantName: body.merchantName,
            category: body.category,
            expenseDate: body.expenseDate,
            grossAmount: body.grossAmount,
            netAmount: body.grossAmount,
            disbursementReference: "ACH-CLAIM-7788",
          }),
        });
      }
    );

    await page.goto("");
    await completeInitialPinSetupIfOpen(page);
    await page.click("#btn-tab-btn-wallet");
    await expect(page.locator("#wallet-balance-amount")).toHaveText("$125.00");

    const receiptPayload = {
      name: "blurry-receipt.svg",
      mimeType: "image/svg+xml",
      buffer: Buffer.from("<svg xmlns='http://www.w3.org/2000/svg'></svg>"),
    };

    // Attempt 1: OCR fails -> file input cleared for same-file retry & manual review card opens
    await page.setInputFiles("#receipt-file-input", receiptPayload);
    await expect(page.locator("#wallet-error-banner")).toBeVisible();
    await expect(page.locator("#ocr-warning-banner")).toBeVisible();
    await expect(page.locator("#ocr-review-card")).toBeVisible();
    await expect(page.locator("#ocr-expense-date")).toHaveAttribute(
      "type",
      "date"
    );

    // Select MEAL category in manual fallback to expose #ocr-meal-allowance-check for screenshot & touch-target check
    await page.selectOption("#ocr-category", "MEAL");
    const mealCheckbox = page.locator("#ocr-meal-allowance-check");
    await expect(mealCheckbox).toBeVisible();
    const mealBox = await mealCheckbox.boundingBox();
    expect(mealBox.height).toBeGreaterThanOrEqual(48);

    await page.screenshot({
      path: "/tmp/wallet-ocr-manual-recovery.png",
      fullPage: true,
    });

    // Attempt 2: Selecting the exact same receipt file again fires a fresh extraction because input.value was cleared
    await page.setInputFiles("#receipt-file-input", receiptPayload);
    await expect.poll(() => ocrAttempts).toBe(2);
    await expect(page.locator("#ocr-merchant-name")).toHaveValue(
      "City Metro Cafe"
    );
    await expect(page.locator("#ocr-expense-date")).toHaveValue("2026-11-18");
    await expect(page.locator("#ocr-gross-amount")).toHaveValue("28.5");

    // Submit claim -> sends matching Idempotency-Key header & body, resets form, and appends to expenseClaims
    await page.click("#btn-confirm-reimburse");
    await expect(page.locator("#expense-auto-approved-banner")).toBeVisible();
    await expect(page.locator("#ocr-review-card")).toBeHidden();

    expect(capturedExpenseRequests).toHaveLength(1);
    expect(capturedExpenseRequests[0].headerKey).toBeTruthy();
    expect(capturedExpenseRequests[0].headerKey).toBe(
      capturedExpenseRequests[0].bodyKey
    );

    await expect(
      page.locator("#wallet-expense-claims .wallet-expense-claim-row")
    ).toHaveCount(1);
    await expect(page.locator("#wallet-expense-claims")).toContainText(
      "City Metro Cafe"
    );

    // Clicking manual entry button afterward opens a clean reset form
    await page.click("#btn-manual-expense-entry");
    await expect(page.locator("#ocr-review-card")).toBeVisible();
    await expect(page.locator("#ocr-merchant-name")).toHaveValue("");
    await expect(page.locator("#ocr-expense-date")).toHaveValue("");
    expect(nativeDialogs).toEqual([]);
  });

  test("opens focus-trapped payout confirmation modal with DIRECT_ACH / VIRTUAL_CARD choice and partial amount validation", async ({
    page,
  }) => {
    const nativeDialogs = [];
    page.on("dialog", async (dialog) => {
      nativeDialogs.push(dialog.message());
      await dialog.dismiss();
    });

    await setupBasePortalRoutes(page);

    await page.route(
      "**/api/v1/payments/participant/me/wallet",
      async (route) => {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            wallet: {
              id: "wal_disb_001",
              studyId: "study_001",
              siteId: "site_001",
              currency: "USD",
              currentBalance: 125.0,
              lifetimeStipends: 250.0,
              lifetimeReimbursements: 0.0,
              lifetimeDisbursements: 125.0,
              taxableYtd: 500.0,
              is1099ThresholdExceeded: false,
              w9Status: "NOT_REQUIRED",
              achAccountMask: "ACH-***6789",
              virtualCardMask: "**** **** **** 4321",
            },
            expenseClaims: [],
            recentTransactions: [],
          }),
        });
      }
    );

    const capturedDisburseCalls = [];
    await page.route(
      "**/api/v1/payments/participant/me/disburse",
      async (route) => {
        const req = route.request();
        const body = req.postDataJSON();
        capturedDisburseCalls.push({
          headerKey: req.headers()["idempotency-key"],
          body,
        });
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            disbursementId: "disb_vcard_01",
            disbursementType: body.disbursementType,
            amount: body.amount,
            currency: "USD",
            currentBalance: 75.0,
            destinationMask: "**** **** **** 4321",
            referenceNumber: "VCARD-5001",
          }),
        });
      }
    );

    await page.goto("");
    await completeInitialPinSetupIfOpen(page);
    await page.click("#btn-tab-btn-wallet");

    // Approaching $600 warning banner shows when taxableYtd >= 450 without W-9 verified
    await expect(page.locator("#wallet-w9-warning-banner")).toBeVisible();

    // Clicking Disburse opens confirmation modal WITHOUT firing immediate payout
    const triggerBtn = page.locator("#btn-disburse-balance");
    await triggerBtn.click();
    expect(capturedDisburseCalls).toHaveLength(0);

    const modal = page.locator("#wallet-disburse-modal");
    await expect(modal).toBeVisible();
    await expect(modal).toHaveAttribute("role", "dialog");
    await expect(modal).toHaveAttribute("aria-modal", "true");

    // Escape closes modal and restores focus to #btn-disburse-balance
    await page.keyboard.press("Escape");
    await expect(modal).toBeHidden();
    await expect(triggerBtn).toBeFocused();

    // Re-open modal, choose VIRTUAL_CARD, validate out-of-range amount inline
    await triggerBtn.click();
    await expect(modal).toBeVisible();
    await page.check("#disburse-method-card");
    await page.fill("#disburse-amount-input", "200");
    await page.click("#btn-disburse-confirm");

    const modalError = page.locator("#disburse-modal-error");
    await expect(modalError).toBeVisible();
    await expect(modalError).toHaveAttribute("role", "alert");
    await expect(modalError).toContainText("exceeds your available balance");
    expect(capturedDisburseCalls).toHaveLength(0);

    // Enter valid partial payout ($50.00), run Axe WCAG check, capture screenshot, and confirm
    await page.fill("#disburse-amount-input", "50.00");
    const modalAxe = await new AxeBuilder({ page })
      .include("#wallet-disburse-modal")
      .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"])
      .analyze();
    expect(modalAxe.violations).toEqual([]);

    await page.screenshot({
      path: "/tmp/wallet-disburse-confirmation-modal.png",
      fullPage: true,
    });

    await page.click("#btn-disburse-confirm");
    await expect(modal).toBeHidden();
    expect(capturedDisburseCalls).toHaveLength(1);
    expect(capturedDisburseCalls[0].headerKey).toBeTruthy();
    expect(capturedDisburseCalls[0].body.disbursementType).toBe("VIRTUAL_CARD");
    expect(capturedDisburseCalls[0].body.amount).toBe(50);

    await expect(page.locator("#wallet-balance-amount")).toHaveText("$75.00");
    await expect(page.locator("#session-wallet-balance")).toHaveText("$75.00");
    await expect(
      page.locator("#wallet-disbursement-confirmation")
    ).toContainText("$50.00");
    expect(nativeDialogs).toEqual([]);
  });

  test("disables .btn-start-task during survey load, avoids native alerts, and enforces >= 48px height on all five audited controls", async ({
    page,
  }) => {
    const nativeDialogs = [];
    page.on("dialog", async (dialog) => {
      nativeDialogs.push(dialog.message());
      await dialog.dismiss();
    });

    let releaseInstrument;
    const instrumentGate = new Promise((resolve) => {
      releaseInstrument = resolve;
    });

    await setupBasePortalRoutes(page, { lazyInstrumentGate: instrumentGate });
    await page.route(
      "**/api/v1/payments/participant/me/wallet",
      async (route) => {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            wallet: {
              id: "wal_touch_001",
              studyId: "study_001",
              siteId: "site_001",
              currency: "USD",
              currentBalance: 80.0,
              lifetimeStipends: 80.0,
              lifetimeReimbursements: 0.0,
              lifetimeDisbursements: 0.0,
              taxableYtd: 80.0,
              is1099ThresholdExceeded: false,
              w9Status: "NOT_REQUIRED",
            },
            expenseClaims: [],
            recentTransactions: [],
          }),
        });
      }
    );

    await page.goto(DEMO_PORTAL_URL);
    await completeInitialPinSetupIfOpen(page);

    // Control 1: .btn-sync-status >= 48px
    const syncBtnBox = await page.locator(".btn-sync-status").boundingBox();
    expect(syncBtnBox.height).toBeGreaterThanOrEqual(48);

    // .btn-start-task disables and displays busy text while loading instrument
    const startBtn = page.locator("#task-card-assign_vas_01 .btn-start-task");
    await expect(startBtn).toBeVisible();
    await startBtn.click();
    await expect(startBtn).toBeDisabled();
    await expect(startBtn).toHaveAttribute("aria-busy", "true");
    await expect(startBtn).toContainText("Loading Survey...");
    releaseInstrument();

    await expect(page.locator("#view-questionnaire")).toBeVisible();

    // Control 2: .vas-slider-input >= 48px
    const sliderBox = await page.locator(".vas-slider-input").boundingBox();
    expect(sliderBox.height).toBeGreaterThanOrEqual(48);

    // Control 3: label-wrapped .radio-option >= 48px
    const radioLabel = page.locator("label.radio-option").first();
    await expect(radioLabel).toBeVisible();
    const radioBox = await radioLabel.boundingBox();
    expect(radioBox.height).toBeGreaterThanOrEqual(48);

    // Submitting incomplete survey uses inline alert/toast rather than window.alert()
    await page.click("#btn-submit-questionnaire");
    expect(nativeDialogs).toEqual([]);

    await page.screenshot({
      path: "/tmp/questionnaire-48px-touch-targets.png",
      fullPage: true,
    });

    // Navigate to Consent view to measure Control 4: #consent-lang-selector >= 48px
    await page.click("#btn-tab-btn-consent");
    await expect(page.locator("#view-consent")).toBeVisible();
    const langSelectBox = await page
      .locator("#consent-lang-selector")
      .boundingBox();
    expect(langSelectBox.height).toBeGreaterThanOrEqual(48);

    // Navigate to Wallet view & open manual MEAL entry to measure Control 5: #ocr-meal-allowance-check >= 48px
    await page.click("#btn-tab-btn-wallet");
    await expect(page.locator("#view-wallet")).toBeVisible();
    await page.click("#btn-manual-expense-entry");
    await page.selectOption("#ocr-category", "MEAL");
    const mealCheckBox = await page
      .locator("#ocr-meal-allowance-check")
      .boundingBox();
    expect(mealCheckBox.height).toBeGreaterThanOrEqual(48);

    expect(nativeDialogs).toEqual([]);
  });

  test("enforces >= 48px x 48px targets across portal buttons, radio options, dropzone actions, form fields, sync drawer close, and ledger summaries plus >= 16px (1rem) inputs across 390x844 and 1280x800", async ({
    page,
  }) => {
    await setupBasePortalRoutes(page);
    await page.route(
      "**/api/v1/payments/participant/me/wallet",
      async (route) => {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            wallet: {
              id: "wal_touch_002",
              studyId: "study_001",
              siteId: "site_001",
              currency: "USD",
              currentBalance: 80.0,
              lifetimeStipends: 80.0,
              lifetimeReimbursements: 0.0,
              lifetimeDisbursements: 0.0,
              taxableYtd: 80.0,
              is1099ThresholdExceeded: false,
              w9Status: "NOT_REQUIRED",
            },
            expenseClaims: [],
            recentTransactions: [],
          }),
        });
      }
    );

    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto(DEMO_PORTAL_URL);
    await completeInitialPinSetupIfOpen(page);

    async function expectBoxAtLeast48(locator, label) {
      await expect(locator, `${label} should be visible`).toBeVisible();
      const box = await locator.boundingBox();
      expect(box, `Missing box for ${label}`).not.toBeNull();
      expect(
        box.height,
        `${label} height (${box.height}) < 48px`
      ).toBeGreaterThanOrEqual(47.5);
      expect(
        box.width,
        `${label} width (${box.width}) < 48px`
      ).toBeGreaterThanOrEqual(47.5);
    }

    async function expectPortalVisibleInputsAtLeast16px(label) {
      const sub16 = await page.evaluate(() => {
        const bad = [];
        for (const el of document.querySelectorAll("input, select, textarea")) {
          if (el.classList.contains("sr-only")) continue;
          if (
            el instanceof HTMLInputElement &&
            ["hidden", "checkbox", "radio", "range", "file"].includes(el.type)
          ) {
            continue;
          }
          const style = window.getComputedStyle(el);
          if (
            style.display === "none" ||
            style.visibility === "hidden" ||
            parseFloat(style.opacity) === 0
          ) {
            continue;
          }
          const rect = el.getBoundingClientRect();
          if (rect.width <= 2 || rect.height <= 2) continue;
          const px = parseFloat(style.fontSize);
          if (px < 16) {
            bad.push(`${el.tagName.toLowerCase()}#${el.id || el.name}=${px}px`);
          }
        }
        return bad;
      });
      expect(sub16, `Sub-16px inputs on ${label}: ${sub16.join(", ")}`).toEqual(
        []
      );
    }

    // Check navigation tabs, sync button, and sync drawer close button at 390x844
    await expectBoxAtLeast48(page.locator(".btn-sync-status"), "Sync status");
    await expectBoxAtLeast48(
      page.locator(".nav-item button").first(),
      "Bottom nav tab"
    );
    await page.click("#btn-global-sync-drawer");
    await expectBoxAtLeast48(
      page.locator("#btn-close-sync-drawer"),
      "Close sync drawer"
    );
    await page.click("#btn-close-sync-drawer");

    // Open questionnaire and check clinical-fields.js radio options + inputs
    const startBtn = page.locator("#task-card-assign_vas_01 .btn-start-task");
    await expectBoxAtLeast48(startBtn, "Start survey button");
    await startBtn.click();
    await expect(page.locator("#view-questionnaire")).toBeVisible();
    await expectBoxAtLeast48(
      page.locator("label.radio-option").first(),
      "Questionnaire radio option"
    );
    await expectPortalVisibleInputsAtLeast16px("390x844 Questionnaire");

    // Check Wallet dropzone browse button, manual expense form fields, and disburse button
    await page.click("#btn-tab-btn-wallet");
    await expect(page.locator("#view-wallet")).toBeVisible();
    await expectBoxAtLeast48(
      page.locator("#btn-disburse-balance"),
      "Disburse balance button"
    );
    await expectBoxAtLeast48(
      page.locator("#receipt-upload-dropzone .clinical-dropzone-browse-btn"),
      "Dropzone browse button"
    );
    await page.click("#btn-manual-expense-entry");
    await expectBoxAtLeast48(
      page.locator("#ocr-category"),
      "Expense category select"
    );
    await expectBoxAtLeast48(
      page.locator("#ocr-merchant-name"),
      "Merchant name input"
    );
    await expectBoxAtLeast48(
      page.locator("#ocr-gross-amount"),
      "Gross amount input"
    );
    await expectPortalVisibleInputsAtLeast16px("390x844 Wallet expense form");

    // Seed an activity ledger entry if none rendered yet, and verify .ledger-verification-details summary >= 48px x 48px
    const ledgerSummary = page
      .locator(".ledger-verification-details summary")
      .first();
    if ((await ledgerSummary.count()) === 0) {
      await page.evaluate(() => {
        const timeline = document.getElementById("portal-ledger-timeline");
        if (timeline) {
          timeline.innerHTML = `<div class="ledger-item"><details class="ledger-verification-details"><summary>Verification code</summary><div class="ledger-hash"><code>sha256:demo</code></div></details></div>`;
        }
      });
    }
    await expectBoxAtLeast48(ledgerSummary, "Ledger verification summary");

    // Verify 48px portal profile persists on 1280x800 desktop viewport as well
    await page.setViewportSize({ width: 1280, height: 800 });
    await expectBoxAtLeast48(
      page.locator("#btn-disburse-balance"),
      "Desktop 1280x800 Disburse button"
    );
    await expectBoxAtLeast48(
      page.locator("#ocr-merchant-name"),
      "Desktop 1280x800 Merchant input"
    );
    await expectBoxAtLeast48(ledgerSummary, "Desktop 1280x800 Ledger summary");
    await expectPortalVisibleInputsAtLeast16px("1280x800 Wallet expense form");
  });
});
