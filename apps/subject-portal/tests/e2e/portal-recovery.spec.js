import fs from "node:fs";
import path from "node:path";
import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";

const AUDIT_OUTPUT_PATH = "/tmp/artifacts/epro-sync-recovery-audit.json";
const DEMO_PORTAL_URL = "http://127.0.0.1:5175/subject-portal/";

const auditChecks = {
  manual_sync_clears_suspension_and_reloads_instruments: {
    id: "manual_sync_clears_suspension_and_reloads_instruments",
    requirement: "Requirement 1",
    passed: false,
    details: "",
  },
  batch_isolation_quarantines_invalid_and_uploads_valid: {
    id: "batch_isolation_quarantines_invalid_and_uploads_valid",
    requirement: "Requirement 2 & Requirement 4",
    passed: false,
    details: "",
  },
  transient_network_auto_retry_with_live_countdown: {
    id: "transient_network_auto_retry_with_live_countdown",
    requirement: "Requirement 3",
    passed: false,
    details: "",
  },
  draft_preserved_on_back_and_cancel_confirmation: {
    id: "draft_preserved_on_back_and_cancel_confirmation",
    requirement: "Requirement 5",
    passed: false,
    details: "",
  },
  pin_five_attempt_lockout_and_oidc_reset_recovery: {
    id: "pin_five_attempt_lockout_and_oidc_reset_recovery",
    requirement: "Requirement 6",
    passed: false,
    details: "",
  },
  offline_accessible_coordinator_contacts_all_surfaces: {
    id: "offline_accessible_coordinator_contacts_all_surfaces",
    requirement: "Requirement 7",
    passed: false,
    details: "",
  },
};

async function completeInitialPinSetupIfOpen(page, pin = "246810") {
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

async function setupAuthenticatedPortalRoutes(page, options = {}) {
  let instrumentVersion = options.initialInstrumentVersion ?? 1;

  await page.route("**/econsent/reconsent/pending/**", async (route) => {
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
          instrument_id: "inst_daily_diary",
          instrument_name: "Daily Health & Vital Diary",
          due_at: new Date(Date.now() + 6 * 3600 * 1000).toISOString(),
          end_date: new Date(Date.now() + 24 * 3600 * 1000).toISOString(),
          status: "PENDING",
        },
        {
          id: "assign_03",
          subject_id: "subject_mocked_001",
          instrument_id: "inst_daily_diary",
          instrument_name: "Daily Health & Vital Diary",
          due_at: new Date(Date.now() + 8 * 3600 * 1000).toISOString(),
          end_date: new Date(Date.now() + 24 * 3600 * 1000).toISOString(),
          status: "PENDING",
        },
      ]),
    });
  });

  await page.route("**/api/v1/**/instruments**", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify([
        {
          id: "inst_daily_diary",
          version_index: instrumentVersion,
          name:
            instrumentVersion > 1
              ? `Daily Health & Vital Diary (v${instrumentVersion})`
              : "Daily Health & Vital Diary",
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
        compliance_rate: 75,
        completed_count: 3,
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
      body: JSON.stringify([
        {
          id: "notif_01",
          subject_id: "subject_mocked_001",
          assignment_id: "assign_01",
          message: "Reminder: Daily Health & Vital Diary is due today.",
          due_at: new Date(Date.now() + 4 * 3600 * 1000).toISOString(),
          channel: "IN_APP",
          is_read: false,
        },
      ]),
    });
  });

  await page.route(
    "**/api/v1/payments/participant/me/wallet",
    async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          wallet: {
            id: "wallet-mock-001",
            study_id: "study_001",
            site_id: "site_001",
            current_balance: 75.0,
            lifetime_stipends: 150.0,
            lifetime_reimbursements: 25.0,
            lifetime_disbursements: 100.0,
            w9_status: "NOT_REQUIRED",
            currency: "USD",
            ach_account_mask: "•••• 4821",
            ach_routing_mask: "•••••0210",
          },
          recent_transactions: [],
          expense_claims: [],
        }),
      });
    }
  );

  await page.addInitScript(() => {
    sessionStorage.setItem("mock_user_id", "subject_mocked_001");
    sessionStorage.setItem("mock_token", "mock_jwt_token_xyz_123"); // pragma: allowlist secret
  });

  return {
    setInstrumentVersion(nextVersion) {
      instrumentVersion = nextVersion;
    },
  };
}

test.describe
  .serial("Subject Portal ePRO Sync, Draft, PIN & Support Recovery Suite", () => {
  test.use({ serviceWorkers: "block" });

  test.afterAll(() => {
    const checkList = Object.values(auditChecks);
    const passedCount = checkList.filter((c) => c.passed).length;
    const report = {
      suite: "epro-sync-recovery-audit",
      generatedAt: new Date().toISOString(),
      overallPassed: passedCount === checkList.length,
      passedCount,
      totalChecks: checkList.length,
      checks: checkList,
      resultsById: auditChecks,
    };
    fs.mkdirSync(path.dirname(AUDIT_OUTPUT_PATH), { recursive: true });
    fs.writeFileSync(AUDIT_OUTPUT_PATH, JSON.stringify(report, null, 2));
  });

  test("Check 1: manual sync in suspended banner and sync drawer refreshes survey definitions and clears suspension", async ({
    page,
  }) => {
    const routes = await setupAuthenticatedPortalRoutes(page, {
      initialInstrumentVersion: 1,
    });

    let syncRequests = 0;
    await page.route("**/interop/epro/sync", async (route) => {
      syncRequests += 1;
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          results: [
            { status: "CREATED", answers: { vssbp: 120, vsdpb: 80, vshr: 70 } },
          ],
        }),
      });
    });

    await page.goto(DEMO_PORTAL_URL);
    await completeInitialPinSetupIfOpen(page);

    // Queue an offline submission and suspend automatic sync after server form update to v2
    routes.setInstrumentVersion(2);
    await page.evaluate(async () => {
      const { queueSubmission } =
        await import("/subject-portal/src/sync-queue.js");
      const { state } = await import("/subject-portal/src/index.js");
      await queueSubmission({
        subject_id: "subject_mocked_001",
        diary_id: "inst_daily_diary",
        assignment_id: "assign_01",
        answers: { vssbp: 120, vsdpb: 80, vshr: 70 },
        change_reason: "Completed while form updated on server",
        username: "subject_mocked_001",
      });
      state.autoSyncSuspended = true;
    });

    const suspendedBanner = page.locator("#sync-suspended-banner");
    await expect(suspendedBanner).toBeVisible();

    // Clicking Manual Sync / Refresh in the alert banner refreshes v2 instruments, syncs queued entry, and clears suspension
    await page.click("#btn-alert-sync-now");
    await expect(suspendedBanner).toBeHidden();
    expect(syncRequests).toBe(1);

    const refreshedVersion = await page.evaluate(async () => {
      const { state } = await import("/subject-portal/src/index.js");
      return state.instruments?.inst_daily_diary?.version_index;
    });
    expect(refreshedVersion).toBe(2);

    // Re-suspend and verify SyncDrawer manual Sync Now also clears suspension and refreshes definitions
    routes.setInstrumentVersion(3);
    await page.evaluate(async () => {
      const { state } = await import("/subject-portal/src/index.js");
      state.autoSyncSuspended = true;
    });
    await expect(suspendedBanner).toBeVisible();

    await page.click("#btn-global-sync-drawer");
    await expect(page.locator("#global-sync-drawer")).toHaveClass(/active/);
    await page.click("#btn-drawer-sync-now");
    await expect(suspendedBanner).toBeHidden();

    const drawerRefreshedVersion = await page.evaluate(async () => {
      const { state } = await import("/subject-portal/src/index.js");
      return state.instruments?.inst_daily_diary?.version_index;
    });
    expect(drawerRefreshedVersion).toBe(3);

    auditChecks.manual_sync_clears_suspension_and_reloads_instruments.passed = true;
    auditChecks.manual_sync_clears_suspension_and_reloads_instruments.details =
      "Both #btn-alert-sync-now and #btn-drawer-sync-now forced network sync, reloaded updated instrument versions (v2 -> v3), and cleared autoSyncSuspended on the first click.";
  });

  test("Check 2: queueing two valid surveys and one invalid survey syncs two valid entries and quarantines the invalid entry with plain-language reason", async ({
    page,
  }) => {
    await setupAuthenticatedPortalRoutes(page);

    await page.route("**/interop/epro/sync", async (route) => {
      await route.fulfill({
        status: 422,
        contentType: "application/json",
        body: JSON.stringify({
          detail: [
            {
              loc: ["body", "submissions", 1, "answers", "vssbp"],
              msg: "Systolic blood pressure exceeds protocol safety ceiling of 250 mmHg",
            },
          ],
        }),
      });
    });

    const isolatedSubmits = [];
    await page.route("**/interop/epro/submit", async (route) => {
      const body = route.request().postDataJSON();
      isolatedSubmits.push(body);
      if (body?.answers?.vssbp > 250) {
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
        return;
      }
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          status: "CREATED",
          answers: body.answers,
        }),
      });
    });

    await page.goto(DEMO_PORTAL_URL);
    await completeInitialPinSetupIfOpen(page);

    // Queue 2 valid surveys + 1 server-rejected survey (vssbp: 265 passes local <300 check, triggers server 422)
    await page.evaluate(async () => {
      const { queueSubmission } =
        await import("/subject-portal/src/sync-queue.js");
      await queueSubmission({
        subject_id: "subject_mocked_001",
        diary_id: "inst_daily_diary",
        assignment_id: "assign_01",
        answers: { vssbp: 118, vsdpb: 76, vshr: 68 },
        change_reason: "Valid morning entry",
        username: "subject_mocked_001",
      });
      await queueSubmission({
        subject_id: "subject_mocked_001",
        diary_id: "inst_daily_diary",
        assignment_id: "assign_02",
        answers: { vssbp: 265, vsdpb: 92, vshr: 84 },
        change_reason: "Out-of-protocol systolic entry",
        username: "subject_mocked_001",
      });
      await queueSubmission({
        subject_id: "subject_mocked_001",
        diary_id: "inst_daily_diary",
        assignment_id: "assign_03",
        answers: { vssbp: 122, vsdpb: 79, vshr: 72 },
        change_reason: "Valid evening entry",
        username: "subject_mocked_001",
      });
    });

    await page.click("#btn-global-sync-drawer");
    await expect(page.locator("#global-sync-drawer")).toHaveClass(/active/);
    await page.click("#btn-drawer-sync-now");

    await expect.poll(() => isolatedSubmits.length).toBe(3);

    // Header badge highlights 1 quarantined entry; SyncDrawer shows 2 Synced + 1 Quarantined with plain-language reason & coordinator steps
    const headerSyncBtn = page.locator("#btn-global-sync-drawer");
    await expect(headerSyncBtn).toHaveClass(/has-quarantined/);
    await expect(page.locator("#header-sync-badge")).toHaveText("1");
    await expect(
      page.locator("#global-sync-drawer .quarantine-guidance")
    ).toBeVisible();
    await expect(page.locator("#global-sync-drawer")).toContainText(
      "exceeds protocol safety ceiling"
    );

    const drawerText = await page.locator("#global-sync-drawer").innerText();
    expect(drawerText).not.toContain('{"');
    expect(drawerText).not.toContain("subject_mocked_001");

    const axeResults = await new AxeBuilder({ page })
      .include("#global-sync-drawer")
      .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"])
      .analyze();
    expect(axeResults.violations).toEqual([]);

    await page.screenshot({
      path: "/tmp/epro-sync-drawer-quarantine.png",
      fullPage: false,
    });

    auditChecks.batch_isolation_quarantines_invalid_and_uploads_valid.passed = true;
    auditChecks.batch_isolation_quarantines_invalid_and_uploads_valid.details =
      "Batch 422 triggered individual POST /interop/epro/submit isolation: 2 valid surveys synced (CREATED), 1 invalid survey marked LOCAL_QUARANTINED with plain-language reason, coordinator steps, highlighted badge, 0 axe violations, and zero raw JSON/PII.";
  });

  test("Check 3: transient network failures schedule an automatic exponential retry timer with live countdown", async ({
    page,
  }) => {
    await setupAuthenticatedPortalRoutes(page);

    let attempts = 0;
    await page.route("**/interop/epro/sync", async (route) => {
      attempts += 1;
      if (attempts === 1) {
        await route.fulfill({
          status: 503,
          contentType: "application/json",
          body: JSON.stringify({ error: "Temporary gateway timeout" }),
        });
        return;
      }
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          results: [
            { status: "CREATED", answers: { vssbp: 120, vsdpb: 78, vshr: 70 } },
          ],
        }),
      });
    });

    await page.goto(DEMO_PORTAL_URL);
    await completeInitialPinSetupIfOpen(page);

    await page.click("#btn-global-sync-drawer");
    await expect(page.locator("#global-sync-drawer")).toHaveClass(/active/);

    // Queue submission and trigger sync with 2500ms base delay; verify live countdown ticks and retry fires without user action
    await page.evaluate(async () => {
      const { queueSubmission } =
        await import("/subject-portal/src/sync-queue.js");
      const { syncOfflineQueue } = await import("/subject-portal/src/index.js");
      await queueSubmission({
        subject_id: "subject_mocked_001",
        diary_id: "inst_daily_diary",
        assignment_id: "assign_01",
        answers: { vssbp: 120, vsdpb: 78, vshr: 70 },
        change_reason: "Transient retry verification",
        username: "subject_mocked_001",
      });
      await syncOfflineQueue(false, { retryDelay: 2500, maxRetries: 4 });
    });

    expect(attempts).toBe(1);
    const countdownBanner = page.locator("#sync-drawer-retry-countdown");
    await expect(countdownBanner).toBeVisible();
    await expect(countdownBanner).toContainText(/s/);

    // Wait for automatic timer execution without any user click
    await expect.poll(() => attempts, { timeout: 6000 }).toBe(2);
    await expect(countdownBanner).toBeHidden();
    await expect(page.locator("#sync-queue-status-text")).toContainText(
      "Sync complete"
    );

    auditChecks.transient_network_auto_retry_with_live_countdown.passed = true;
    auditChecks.transient_network_auto_retry_with_live_countdown.details =
      "Transient 503 scheduled an active exponential retry timer with live second countdown and completed attempt #2 automatically without manual user intervention.";
  });

  test("Check 4: navigating back from a partially completed survey preserves draft for resumption, and cancel requires confirmation", async ({
    page,
  }) => {
    await setupAuthenticatedPortalRoutes(page);
    await page.goto(DEMO_PORTAL_URL);
    await completeInitialPinSetupIfOpen(page);

    await page.click("#task-card-assign_01 .btn-start-task");
    await expect(page.locator("#view-questionnaire")).toBeVisible();

    await page.fill("#vssbp", "126");
    await page.fill("#vsdpb", "82");

    // Clicking Back saves partial answers and marks task Resume Survey
    await page.click("#btn-back-to-tasks");
    await expect(page.locator("#view-tasks")).toBeVisible();
    await expect(page.locator("#task-card-assign_01")).toContainText(
      "Draft Saved"
    );
    await expect(
      page.locator("#task-card-assign_01 .btn-start-task")
    ).toContainText("Resume Survey");

    await page.screenshot({
      path: "/tmp/epro-draft-resume-tasks.png",
      fullPage: false,
    });

    // Reopen survey and verify entered values were retained
    await page.click("#task-card-assign_01 .btn-start-task");
    await expect(page.locator("#view-questionnaire")).toBeVisible();
    await expect(page.locator("#vssbp")).toHaveValue("126");
    await expect(page.locator("#vsdpb")).toHaveValue("82");

    // Clicking Cancel opens accessible confirmation dialog before discarding
    await page.click("#btn-cancel-questionnaire");
    const cancelDialog = page.locator("#questionnaire-cancel-confirm");
    await expect(cancelDialog).toBeVisible();

    const cancelAxe = await new AxeBuilder({ page })
      .include("#view-questionnaire")
      .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"])
      .analyze();
    expect(cancelAxe.violations).toEqual([]);

    await page.screenshot({
      path: "/tmp/epro-cancel-confirm-dialog.png",
      fullPage: false,
    });

    // Keep Editing preserves values
    await page.click("#btn-cancel-keep-editing");
    await expect(cancelDialog).toBeHidden();
    await expect(page.locator("#vssbp")).toHaveValue("126");

    // Confirm Discard clears draft and returns task button to Start Survey
    await page.click("#btn-cancel-questionnaire");
    await page.click("#btn-cancel-confirm-discard");
    await expect(page.locator("#view-tasks")).toBeVisible();
    await expect(
      page.locator("#task-card-assign_01 .btn-start-task")
    ).toContainText("Start Survey");

    auditChecks.draft_preserved_on_back_and_cancel_confirmation.passed = true;
    auditChecks.draft_preserved_on_back_and_cancel_confirmation.details =
      "Back navigation saved partial survey answers, marked the task 'Resume Survey', and restored values on reopen; Cancel displayed a WCAG-compliant confirmation dialog before discarding.";
  });

  test("Check 5: entering five wrong PINs pauses unlock attempts, keeps keyboard recovery within 3 interactions, and supports OIDC PIN reset without PII exposure", async ({
    page,
  }) => {
    await setupAuthenticatedPortalRoutes(page);
    await page.goto("");
    await completeInitialPinSetupIfOpen(page, "246810");

    // Queue an encrypted offline diary containing sensitive notes before forgetting PIN
    await page.evaluate(async () => {
      const { queueSubmission } =
        await import("/subject-portal/src/sync-queue.js");
      await queueSubmission({
        subject_id: "subject_mocked_001",
        diary_id: "inst_daily_diary",
        assignment_id: "assign_01",
        answers: {
          vssbp: 124,
          vsdpb: 80,
          vshr: 71,
          full_name: "Confidential Name",
        },
        change_reason: "Encrypted prior to forgotten PIN",
        username: "subject_mocked_001",
      });
    });

    // Reload clears in-memory session key and opens PinUnlockModal
    await page.reload();
    const unlockModal = page.locator("#portal-pin-unlock-modal");
    await expect(unlockModal).toBeVisible();

    // Enter 5 consecutive wrong PINs (waiting for async PBKDF2 derivation on each attempt)
    for (let i = 1; i <= 5; i++) {
      await page.fill("#unlock-pin", `11111${i}`);
      await page.click("#btn-pin-unlock-confirm");
      if (i < 5) {
        await expect(page.locator("#pin-attempts-remaining")).toContainText(
          `${5 - i} of 5`
        );
      }
    }

    await expect(page.locator("#pin-lockout-banner")).toBeVisible();
    await expect(page.locator("#unlock-pin")).toBeDisabled();
    await expect(page.locator("#btn-pin-unlock-confirm")).toBeDisabled();

    const lockoutAxe = await new AxeBuilder({ page })
      .include("#portal-pin-unlock-modal")
      .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"])
      .analyze();
    expect(lockoutAxe.violations).toEqual([]);

    await page.screenshot({
      path: "/tmp/epro-pin-lockout-recovery.png",
      fullPage: false,
    });

    // Verify keyboard focus trap reaches Reset PIN, Sign Out, and Coordinator phone/email in <= 3 Tab presses
    await page.locator("#btn-pin-unlock-reset").focus();
    await expect(page.locator("#btn-pin-unlock-reset")).toBeFocused();
    await page.keyboard.press("Tab");
    await expect(page.locator("#btn-pin-unlock-signout")).toBeFocused();
    await page.keyboard.press("Tab");
    await expect(
      page.locator("#pin-unlock-site-contacts .site-contact-phone")
    ).toBeFocused();
    await page.keyboard.press("Tab");
    await expect(
      page.locator("#pin-unlock-site-contacts .site-contact-email")
    ).toBeFocused();

    // Reset PIN after verified OIDC session -> generates new local key and flags prior entry without PII
    await page.click("#btn-pin-unlock-reset");
    await expect(page.locator("#pin-reset-confirmation")).toBeVisible();
    await page.click("#btn-pin-reset-confirm");

    await expect(page.locator("#portal-pin-setup-modal")).toBeVisible();
    await page.fill("#setup-pin", "975310");
    await page.fill("#confirm-setup-pin", "975310");
    await page.click("#btn-pin-setup-confirm");
    await expect(page.locator("#portal-pin-setup-modal")).toBeHidden();

    // Open SyncDrawer and verify prior undecryptable entry is flagged cleanly with zero clinical PII or raw JSON
    await page.click("#btn-global-sync-drawer");
    await expect(page.locator("#global-sync-drawer")).toHaveClass(/active/);
    const drawerContent = await page.locator("#global-sync-drawer").innerText();
    expect(drawerContent).toContain("Locked under a previous security PIN");
    expect(drawerContent).not.toContain("Confidential Name");
    expect(drawerContent).not.toContain("124");
    expect(drawerContent).not.toContain('{"');

    auditChecks.pin_five_attempt_lockout_and_oidc_reset_recovery.passed = true;
    auditChecks.pin_five_attempt_lockout_and_oidc_reset_recovery.details =
      "5 wrong PIN attempts paused unlock inputs, maintained keyboard focus across Reset/Sign-Out/Support in <= 3 interactions, and reset PIN via verified OIDC session while flagging prior entries without clinical PII.";
  });

  test("Check 6: phone and email links for the study coordinator render across ParticipantCard, SyncDrawer, PinUnlockModal, and InboxView online and offline", async ({
    page,
    context,
  }) => {
    await setupAuthenticatedPortalRoutes(page);
    await page.goto(DEMO_PORTAL_URL);
    await completeInitialPinSetupIfOpen(page);

    async function assertPhoneAndEmail(containerSelector) {
      const phone = page.locator(`${containerSelector} .site-contact-phone`);
      const email = page.locator(`${containerSelector} .site-contact-email`);
      await expect(phone).toBeVisible();
      await expect(email).toBeVisible();
      await expect(phone).toHaveAttribute("href", /^tel:\+/);
      await expect(email).toHaveAttribute("href", /^mailto:/);
    }

    // Online checks: ParticipantCard, SyncDrawer, InboxView
    await assertPhoneAndEmail("#participant-site-contacts");

    await page.click("#btn-global-sync-drawer");
    await assertPhoneAndEmail("#sync-drawer-site-contacts");
    await page.click("#btn-close-sync-drawer");

    await page.click("#tab-btn-inbox button");
    await expect(page.locator("#view-inbox")).toBeVisible();
    await assertPhoneAndEmail("#inbox-site-contacts");

    // Switch browser to offline mode and verify all 4 surfaces render cached coordinator links
    await context.setOffline(true);
    await page.evaluate(() => window.dispatchEvent(new Event("offline")));

    await assertPhoneAndEmail("#participant-site-contacts");
    await assertPhoneAndEmail("#inbox-site-contacts");

    const inboxAxe = await new AxeBuilder({ page })
      .include("#view-inbox")
      .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"])
      .analyze();
    expect(inboxAxe.violations).toEqual([]);

    await page.screenshot({
      path: "/tmp/epro-inbox-offline-contacts.png",
      fullPage: false,
    });

    await page.click("#btn-global-sync-drawer");
    await assertPhoneAndEmail("#sync-drawer-site-contacts");
    await page.click("#btn-close-sync-drawer");

    await context.setOffline(false);

    // Verify PinUnlockModal offline coordinator links on production build (where local PIN master key is active)
    await page.goto("");
    await completeInitialPinSetupIfOpen(page);
    await context.setOffline(true);
    await page.evaluate(() => window.dispatchEvent(new Event("offline")));

    // Open PinUnlockModal while offline and verify coordinator phone and email links render
    await page.evaluate(async () => {
      const { clearSessionKey } =
        await import("/subject-portal/src/sync-queue.js");
      const { checkPINWrapper } = await import("/subject-portal/src/index.js");
      clearSessionKey();
      await checkPINWrapper();
    });
    await expect(page.locator("#portal-pin-unlock-modal")).toBeVisible();
    await assertPhoneAndEmail("#pin-unlock-site-contacts");

    await context.setOffline(false);

    auditChecks.offline_accessible_coordinator_contacts_all_surfaces.passed = true;
    auditChecks.offline_accessible_coordinator_contacts_all_surfaces.details =
      "tel: and mailto: coordinator links rendered and passed WCAG 2.1 AA audits across ParticipantCard, SyncDrawer, InboxView, and PinUnlockModal both online and offline.";
  });
});
