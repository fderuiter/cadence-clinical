import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";

const WCAG_AA_TAGS = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"];

async function completePinModalIfOpen(page, pin = "246810") {
  const pinSetupModal = page.locator("#portal-pin-setup-modal");
  const pinUnlockModal = page.locator("#portal-pin-unlock-modal");
  await Promise.race([
    pinSetupModal.waitFor({ state: "visible", timeout: 3500 }),
    pinUnlockModal.waitFor({ state: "visible", timeout: 3500 }),
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

test.describe("Mobile 390x844 Query-Synced Routes, Breadcrumbs, 90dvh Peek Sheet & Gate Guidance", () => {
  test.use({
    viewport: { width: 390, height: 844 },
    serviceWorkers: "block",
  });

  test("preserves cold-reload query params, renders friendly breadcrumbs, opens 90dvh peek bottom sheet with focus return, and exposes W-9 + consent recovery links", async ({
    page,
  }) => {
    await page.route("**/reconsent/pending/**", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify([]),
      });
    });

    await page.route("**/assignments/subject/*", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify([
          {
            id: "assign_01",
            subject_id: "subject_mocked_001",
            study_id: "CADENCE-101",
            instrument_id: "inst_daily_diary",
            instrument_name: "Daily Health & Vital Diary",
            due_at: new Date(Date.now() + 4 * 3600 * 1000).toISOString(),
            end_date: new Date(Date.now() + 24 * 3600 * 1000).toISOString(),
            status: "PENDING",
          },
        ]),
      });
    });

    await page.route("**/instruments**", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify([
          {
            id: "inst_daily_diary",
            name: "Daily Health & Vital Diary",
            description: "Record your daily blood pressure and comfort check.",
            items: {
              vssbp: {
                label: "Systolic Blood Pressure (mmHg)",
                type: "number",
                required: true,
                min: 70,
                max: 220,
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
          compliance_rate: 94,
          completed_count: 15,
          pending_count: 1,
          overdue_count: 0,
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
            id: "notif_mobile_01",
            subject_id: "subject_mocked_001",
            assignment_id: "assign_01",
            message:
              "Friendly reminder: your Daily Health & Vital Diary is open.",
            due_at: new Date(Date.now() + 4 * 3600 * 1000).toISOString(),
            channel: "IN_APP",
            is_read: false,
          },
        ]),
      });
    });

    await page.route("**/econsent/templates/**", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          id: "consent_doc_mobile_01",
          study_id: "CADENCE-101",
          title: "Cardiology Companion Study Informed Consent",
          version: "2.1",
          language_code: "en",
          clauses: [
            {
              id: "clause_1",
              title: "Voluntary Participation",
              body_html:
                "<p>Taking part in this research study is completely your choice.</p>",
            },
          ],
          comprehension_questions: [
            {
              id: "cq_voluntary",
              question_text: "Can you leave the study at any time?",
              options: ["Yes, at any time", "No"],
              correct_option_index: 0,
            },
          ],
        }),
      });
    });

    await page.route("**/payments/participant/me/wallet", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          wallet: {
            id: "wal_mobile_001",
            studyId: "CADENCE-101",
            siteId: "SITE-101",
            currency: "USD",
            currentBalance: 120.0,
            lifetimeStipends: 640.0,
            lifetimeReimbursements: 32.5,
            lifetimeDisbursements: 520.0,
            taxableYtd: 640.0,
            is1099ThresholdExceeded: true,
            w9Status: "REQUIRED",
            achAccountMask: "****4321",
          },
          expenseClaims: [
            {
              id: "claim_mobile_01",
              category: "PARKING",
              merchantName: "Hospital Garage",
              expenseDate: "2026-11-20",
              grossAmount: 32.5,
              netAmount: 32.5,
              status: "PENDING_REVIEW",
              autoApproved: false,
              reviewNotes:
                "OCR confidence 0.62 below 0.90 auto-approval threshold; routed to coordinator review.",
            },
          ],
          recentTransactions: [
            {
              id: "tx_mobile_01",
              description: "Visit 3 Stipend",
              transactionType: "STIPEND_CREDIT",
              amount: 120.0,
            },
          ],
        }),
      });
    });

    await page.addInitScript(() => {
      sessionStorage.setItem("mock_user_id", "subject_mocked_001");
      sessionStorage.setItem("mock_token", "mock_jwt_token_xyz_123"); // pragma: allowlist secret
    });

    // 1. Cold deep-link reload onto /questionnaire?assignmentId=assign_01
    await page.goto("questionnaire?assignmentId=assign_01");
    await completePinModalIfOpen(page);

    await expect(page.locator("#view-questionnaire")).toBeVisible();
    await expect(page.locator("#questionnaire-title")).toContainText(
      "Daily Health & Vital Diary"
    );
    expect(page.url()).toContain("assignmentId=assign_01");

    const breadcrumbs = page.locator('nav[aria-label="Breadcrumb"]');
    await expect(breadcrumbs).toBeVisible();
    await expect(breadcrumbs).not.toContainText("CADENCE-101");
    await expect(breadcrumbs).not.toContainText("STUDY-01");
    await expect(breadcrumbs.locator('[aria-current="page"]')).toContainText(
      "Daily Health & Vital Diary"
    );

    // 2. Reminders tab + 90dvh Peek Sheet + focus restoration
    await page.goto("inbox");
    await completePinModalIfOpen(page);
    const notifPeekBtn = page.locator("#btn-peek-notif-notif_mobile_01");
    await expect(notifPeekBtn).toBeVisible();
    await notifPeekBtn.click();

    const peekPanel = page.locator(".clinical-drawer-panel");
    await expect(peekPanel).toBeVisible();
    expect(page.url()).toContain("notifId=notif_mobile_01");

    const panelBox = await peekPanel.boundingBox();
    expect(panelBox).not.toBeNull();
    expect(panelBox.height).toBeLessThanOrEqual(Math.ceil(0.91 * 844));

    const closeBtnBox = await page
      .locator(".clinical-drawer-close-btn")
      .boundingBox();
    expect(closeBtnBox).not.toBeNull();
    expect(closeBtnBox.width).toBeGreaterThanOrEqual(48);
    expect(closeBtnBox.height).toBeGreaterThanOrEqual(48);

    if (process.env.CAPTURE_PORTAL_SCREENSHOTS === "1") {
      await page.screenshot({
        path: "/tmp/portal-mobile-peek-sheet.png",
        fullPage: false,
      });
    }

    const peekAxe = await new AxeBuilder({ page })
      .include(".clinical-drawer-panel")
      .withTags(WCAG_AA_TAGS)
      .analyze();
    expect(peekAxe.violations).toEqual([]);

    await page.click(".clinical-drawer-close-btn");
    await expect(peekPanel).toBeHidden();
    expect(page.url()).not.toContain("notifId=");
    await expect(notifPeekBtn).toBeFocused();

    // 3. Consent tab: friendly study label + disabled sign button aria-describedby + jump links
    await page.click("#btn-tab-btn-consent");
    await expect(page.locator("#view-consent")).toBeVisible();
    await expect(page.locator("#consent-metadata-display")).toBeVisible();
    await expect(page.locator("#consent-metadata-display")).not.toContainText(
      "CADENCE-101"
    );
    const signConsentBtn = page.locator("#btn-trigger-consent-sign");
    await expect(signConsentBtn).toBeDisabled();
    await expect(signConsentBtn).toHaveAttribute(
      "aria-describedby",
      "consent-sign-readiness-hint"
    );
    await expect(page.locator("#consent-sign-readiness-hint")).toBeVisible();
    await expect(page.locator("#link-jump-comprehension")).toBeVisible();
    await expect(page.locator("#link-jump-liveness")).toBeVisible();

    if (process.env.CAPTURE_PORTAL_SCREENSHOTS === "1") {
      await page.screenshot({
        path: "/tmp/portal-mobile-consent-readiness.png",
        fullPage: false,
      });
    }

    // 4. Demo server (port 5175): cold reload on /wallet?claimId=claim_mobile_01 + $600 W-9 hold guidance + plain-language OCR note
    await page.goto(
      "http://127.0.0.1:5175/subject-portal/#/wallet?claimId=claim_mobile_01"
    );
    await completePinModalIfOpen(page);

    // Cold reload with ?claimId=claim_mobile_01 restores Wallet view and opens Peek Sheet with humanized OCR copy
    await expect(peekPanel).toBeVisible();
    await expect(peekPanel).not.toContainText("0.90");
    await page.click(".clinical-drawer-close-btn");
    await expect(peekPanel).toBeHidden();

    await expect(page.locator("#view-wallet")).toBeVisible();

    const w9Banner = page.locator("#wallet-w9-hold-banner");
    await expect(w9Banner).toBeVisible();
    await expect(page.locator("#btn-disburse-balance")).toBeDisabled();
    await expect(page.locator("#btn-disburse-balance")).toHaveAttribute(
      "aria-describedby",
      /wallet-w9-hold/
    );
    await expect(page.locator("#link-w9-coordinator-support")).toBeVisible();

    // Plain-language OCR copy (never exposes raw "0.90")
    await expect(page.locator("#view-wallet")).not.toContainText("0.90");

    if (process.env.CAPTURE_PORTAL_SCREENSHOTS === "1") {
      await page.screenshot({
        path: "/tmp/portal-mobile-w9-hold.png",
        fullPage: false,
      });
    }

    const walletAxe = await new AxeBuilder({ page })
      .include("#view-wallet")
      .withTags(WCAG_AA_TAGS)
      .analyze();
    expect(walletAxe.violations).toEqual([]);
  });
});
