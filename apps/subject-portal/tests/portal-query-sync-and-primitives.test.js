import "fake-indexeddb/auto";
import { beforeEach, describe, expect, it, vi } from "vitest";

import {
  clearInMemoryKey,
  initSessionKey,
  openDatabase,
  queueSubmission,
  saveDraft,
  setActiveUserId,
  updateSubmissionStatus,
} from "../src/sync-queue.js";
import {
  closePeekRecord,
  hydrateSessionScopeFromClaims,
  initializeApp,
  loadConsentDetails,
  openSignatureModal,
  renderCompliance,
  renderConsentUI,
  renderInbox,
  renderLedger,
  resolveReconsentStudyId,
  showView,
  startQuestionnaire,
  state,
  submitReconsentSignature,
  unmountApp,
} from "../src/index.js";
import {
  getPortalRouter,
  resetPortalRouterForTests,
} from "../src/router/index.js";
import {
  formatConsentEditionLabel,
  humanizeExpenseReviewNotes,
  resolveFriendlyStudyTitle,
} from "../src/utils/participantCopy.js";

const flushUi = () => new Promise((resolve) => setTimeout(resolve, 25));

function encodeFakeJwt(payload) {
  const header = btoa(JSON.stringify({ alg: "none", typ: "JWT" }));
  const body = btoa(JSON.stringify(payload));
  return `${header}.${body}.sig`; // pragma: allowlist secret
}

describe("Shared UI primitives, bootstrap ordering, and query-synced portal routes", () => {
  beforeEach(async () => {
    unmountApp();
    resetPortalRouterForTests();
    localStorage.clear();
    sessionStorage.clear();
    clearInMemoryKey();
    window.__MOCK_TEST_ENV__ = true;
    window.history.replaceState({}, "", "/subject-portal/");

    document.body.innerHTML = `<div id="app"></div>`;

    state.session.userId = "subject_001";
    state.session.roles = "Subject";
    state.session.token = null;
    state.session.studyId = "STUDY-CARDIO-2026";
    state.session.siteId = "SITE-BOS-01";
    state.session.isDemoMode = true;
    state.session.isOfflineMode = true;
    state.currentView = "view-tasks";
    state.peekRecord = null;
    state.peekTriggerElement = null;
    state.pendingReconsent = null;
    state.reconsentModalOpen = false;
    state.signatureModalOpen = false;
    state.activeQuestionnaire = null;
    state.isSyncDrawerOpen = false;

    for (const uid of ["subject_001", "subject_cold_01", null]) {
      const db = await openDatabase(uid);
      await new Promise((resolve) => {
        const tx = db.transaction(
          ["config", "drafts", "submissions", "assignments", "instruments"],
          "readwrite"
        );
        tx.objectStore("config").clear();
        tx.objectStore("drafts").clear();
        tx.objectStore("submissions").clear();
        tx.objectStore("assignments").clear();
        tx.objectStore("instruments").clear();
        tx.oncomplete = resolve;
      });
    }

    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      json: () => Promise.resolve({}),
    });
  });

  it("hydrates OIDC studyId/siteId claims via shared callerScope helpers and removes hardcoded STUDY-01 fallbacks", async () => {
    state.session.isDemoMode = false;
    state.session.studyId = null;
    state.session.siteId = null;

    const jwt = encodeFakeJwt({
      sub: "subject_cold_01",
      assigned_studies: ["STUDY-NEURO-402"],
      site_ids: ["SITE-SEA-09"],
    });

    const resolved = hydrateSessionScopeFromClaims(null, jwt);
    expect(resolved).toEqual({
      studyId: "STUDY-NEURO-402",
      siteId: "SITE-SEA-09",
    });
    expect(state.session.studyId).toBe("STUDY-NEURO-402");
    expect(state.session.siteId).toBe("SITE-SEA-09");
    expect(localStorage.getItem("oidc_study_id")).toBe("STUDY-NEURO-402");
    expect(localStorage.getItem("oidc_site_id")).toBe("SITE-SEA-09");

    // Without study context outside demo mode, re-consent never falls back to hardcoded STUDY-01
    localStorage.clear();
    state.session.studyId = null;
    state.pendingReconsent = { id: "req_missing_study", study_id: null };
    state.consentContent = null;
    expect(resolveReconsentStudyId()).toBeNull();

    state.session.isOfflineMode = false;
    state.session.token = "live-token"; // pragma: allowlist secret
    state.reconsentForm.username = "subject_cold_01";
    state.reconsentForm.password = "valid-pass"; // pragma: allowlist secret
    state.reconsentForm.reason = "Protocol Amendment Re-Consent Acknowledgment";

    await submitReconsentSignature();
    expect(state.reconsentModalError).toContain("Study context is unavailable");
    expect(state.reconsentModalError).toContain("Nothing was saved.");
  });

  it("completes session & task hydration before initial route guards evaluate cold /wallet and /questionnaire entries", async () => {
    const userId = "subject_cold_01";
    const jwt = encodeFakeJwt({
      sub: userId,
      study_id: "STUDY-CARDIO-2026",
      site_id: "SITE-BOS-01",
    });

    state.session.isDemoMode = false;
    state.session.isOfflineMode = false;
    state.session.userId = userId;
    state.session.token = jwt;
    setActiveUserId(userId);
    await initSessionKey("cold-entry-secret", userId); // pragma: allowlist secret
    await saveDraft("assign_cold_01", { vssbp: "118", vsdpb: "76" });

    globalThis.fetch = vi.fn().mockImplementation((url) => {
      if (url.includes("assignments/subject/")) {
        return Promise.resolve({
          ok: true,
          json: () =>
            Promise.resolve([
              {
                id: "assign_cold_01",
                subject_id: userId,
                instrument_id: "inst_cold_01",
                status: "PENDING",
                version_index: 1,
              },
            ]),
        });
      }
      if (url.includes("/instruments")) {
        return Promise.resolve({
          ok: true,
          json: () =>
            Promise.resolve([
              {
                id: "inst_cold_01",
                name: "Morning Blood Pressure Check",
                description: "Record your morning vitals.",
                items: {
                  vssbp: {
                    label: "Systolic Blood Pressure (mmHg)",
                    type: "numeric",
                    required: true,
                    min: 60,
                    max: 250,
                  },
                  vsdpb: {
                    label: "Diastolic Blood Pressure (mmHg)",
                    type: "numeric",
                    required: true,
                    min: 40,
                    max: 150,
                  },
                },
              },
            ]),
        });
      }
      if (url.includes("/compliance")) {
        return Promise.resolve({
          ok: true,
          json: () =>
            Promise.resolve({
              compliance_rate: 100,
              completed_count: 1,
              pending_count: 1,
              overdue_count: 0,
              assignments: [],
            }),
        });
      }
      if (url.includes("/notifications")) {
        return Promise.resolve({
          ok: true,
          json: () => Promise.resolve([]),
        });
      }
      if (url.includes("payments/participant/me/summary")) {
        return Promise.resolve({
          ok: true,
          json: () =>
            Promise.resolve({
              walletBalance: 75,
              currency: "USD",
              expenseClaims: [],
              transactions: [],
            }),
        });
      }
      return Promise.resolve({ ok: true, json: () => Promise.resolve([]) });
    });

    // 1. Cold entry directly on /wallet stays on /wallet (not /unavailable) and skips unmounted interop/econsent endpoints outside demo mode
    window.history.replaceState(
      {},
      "",
      "/subject-portal/wallet?studyId=STUDY-CARDIO-2026&siteId=SITE-BOS-01"
    );
    await initializeApp();
    await flushUi();

    const router = getPortalRouter();
    expect(router.currentRoute.value.name).toBe("wallet");
    expect(state.currentView).toBe("view-wallet");
    const nonDemoUrls = globalThis.fetch.mock.calls.map(([u]) => String(u));
    expect(
      nonDemoUrls.some(
        (u) => u.includes("/interop/") || u.includes("/econsent/")
      )
    ).toBe(false);

    // 2. Cold entry directly on /questionnaire?assignmentId=assign_cold_01 (demo-available screen) restores survey & IndexedDB draft
    unmountApp();
    resetPortalRouterForTests();
    document.body.innerHTML = `<div id="app"></div>`;
    state.session.isDemoMode = true;
    window.history.replaceState(
      {},
      "",
      "/subject-portal/questionnaire?assignmentId=assign_cold_01&studyId=STUDY-CARDIO-2026"
    );

    await initializeApp();
    await flushUi();

    const reloadedRouter = getPortalRouter();
    expect(reloadedRouter.currentRoute.value.name).toBe("questionnaire");
    expect(reloadedRouter.currentRoute.value.query.assignmentId).toBe(
      "assign_cold_01"
    );
    expect(document.getElementById("vssbp")?.value).toBe("118");
    expect(document.getElementById("vsdpb")?.value).toBe("76");
  });

  it("validates /questionnaire on every history transition and guards signature modal against null activeQuestionnaire", async () => {
    await initializeApp();
    await flushUi();
    const router = getPortalRouter();

    // Missing or completed assignment redirects cleanly to tasks
    await router.push({
      name: "questionnaire",
      query: { assignmentId: "assign_nonexistent" },
    });
    await flushUi();
    expect(router.currentRoute.value.name).toBe("tasks");

    // Start valid assignment, then mark it completed and simulate Back/Forward history navigation
    await startQuestionnaire("assign_01");
    await flushUi();
    expect(router.currentRoute.value.name).toBe("questionnaire");
    expect(router.currentRoute.value.query.assignmentId).toBe("assign_01");

    await router.push({ name: "compliance" });
    await flushUi();
    expect(router.currentRoute.value.name).toBe("compliance");

    // Mark assign_01 completed so returning via browser Back must redirect to tasks
    const target = state.assignments.find((a) => a.id === "assign_01");
    target.status = "COMPLETED";
    state.activeQuestionnaire = null;

    router.back();
    await flushUi();
    expect(router.currentRoute.value.name).toBe("tasks");
    expect(state.currentView).toBe("view-tasks");

    // Defensive null guard in openSignatureModal when activeQuestionnaire is null on view-questionnaire
    state.currentView = "view-tasks";
    state.activeQuestionnaire = null;
    state.currentView = "view-questionnaire";
    openSignatureModal();
    await flushUi();
    expect(state.signatureModalOpen).toBe(false);
    expect(state.currentView).toBe("view-tasks");
  });

  it("renders accessible friendly study -> section -> record breadcrumbs without exposing protocol codes", async () => {
    await initializeApp();
    await flushUi();

    const nav = document.querySelector('nav[aria-label="Breadcrumb"]');
    expect(nav).not.toBeNull();
    expect(nav.textContent).toContain("Cardiology Companion Study");
    expect(nav.textContent).not.toContain("STUDY-CARDIO-2026");

    const currentCrumb = nav.querySelector('[aria-current="page"]');
    expect(currentCrumb?.textContent).toContain("My Tasks");

    // Open active questionnaire -> 3-level trail with survey title as aria-current="page"
    await startQuestionnaire("assign_01");
    await flushUi();
    const surveyCrumb = nav.querySelector('[aria-current="page"]');
    expect(surveyCrumb?.textContent).toContain("Daily Health & Vital Diary");

    // Clicking the section breadcrumb returns to My Tasks
    const sectionBtn = nav.querySelectorAll(".clinical-breadcrumb-link")[1];
    sectionBtn.click();
    await flushUi();
    expect(state.currentView).toBe("view-tasks");
  });

  it("wires inbox, compliance, wallet, audit ledger, and sync drawer rows to the 90dvh peek bottom sheet with query sync and focus restoration", async () => {
    await initializeApp();
    await initSessionKey("peek-test-secret", "subject_001"); // pragma: allowlist secret
    const queuedRecord = await queueSubmission({
      subject_id: "subject_001",
      diary_id: "inst_daily_diary",
      assignment_id: "assign_01",
      answers: { vssbp: "122" },
      change_reason: "Initial",
      username: "subject_001",
    });
    await updateSubmissionStatus(
      queuedRecord.sequence_number ?? 1,
      "QUARANTINED",
      {
        error:
          "Schema Validation Failed: Systolic Blood Pressure out of bounds",
      }
    );
    await flushUi();

    const router = getPortalRouter();

    // 1. Inbox row opens peek sheet, syncs ?notifId=..., and restores focus on close
    showView("view-inbox");
    renderInbox();
    await flushUi();

    const notifPeekBtn = document.querySelector('[id^="btn-peek-notif-"]');
    expect(notifPeekBtn).not.toBeNull();
    notifPeekBtn.focus();
    notifPeekBtn.click();
    await flushUi();

    expect(state.peekRecord?.kind).toBe("notification");
    expect(router.currentRoute.value.query.notifId).toBeTruthy();
    const peekDialog = document.getElementById("portal-peek-drawer");
    expect(peekDialog).not.toBeNull();
    expect(peekDialog.classList.contains("drawer-bottom-sheet")).toBe(true);

    document.getElementById("btn-peek-close").click();
    await flushUi();
    expect(state.peekRecord).toBeNull();
    expect(router.currentRoute.value.query.notifId).toBeUndefined();
    expect(document.activeElement).toBe(notifPeekBtn);

    // 2. Compliance row opens peek sheet and syncs ?complianceId=...
    showView("view-compliance");
    renderCompliance();
    await flushUi();
    const compRow = document.querySelector('[id^="compliance-row-"]');
    expect(compRow).not.toBeNull();
    compRow.click();
    await flushUi();
    expect(state.peekRecord?.kind).toBe("compliance");
    expect(router.currentRoute.value.query.complianceId).toBeTruthy();
    closePeekRecord({ restoreFocus: false });
    await flushUi();

    // 3. Wallet held claim & transaction rows open peek sheet with coordinator links and plain OCR copy
    state.walletLoaded = true;
    state.expenseClaims = [
      {
        id: "claim_hold_99",
        merchantName: "City Cab Co",
        category: "TRANSIT",
        grossAmount: 42.5,
        currency: "USD",
        status: "PENDING_REVIEW",
        approvalDecision: "MANUAL_REVIEW_REQUIRED",
        ocrConfidence: 0.72,
        reviewNotes:
          "OCR confidence (0.72) is below 0.90 auto-approval threshold; routed to site coordinator review.",
      },
    ];
    state.recentTransactions = [
      {
        id: "tx_stipend_99",
        transactionType: "STIPEND_CREDIT",
        description: "Baseline Visit Stipend",
        amount: 50,
        balanceAfter: 125,
        referenceId: "REF-99",
        createdAt: "2026-03-01T10:00:00Z", // deid-ignore: synthetic test timestamp
      },
    ];
    showView("view-wallet");
    await flushUi();

    const claimRow = document.getElementById("wallet-claim-row-claim_hold_99");
    expect(claimRow).not.toBeNull();
    expect(claimRow.textContent).not.toContain("0.90");
    expect(claimRow.textContent).toContain(
      "could not read every detail on the receipt photo clearly"
    );
    document.getElementById("btn-peek-claim-claim_hold_99").click();
    await flushUi();

    expect(state.peekRecord?.kind).toBe("expense_claim");
    expect(router.currentRoute.value.query.claimId).toBe("claim_hold_99");
    expect(
      document.getElementById("portal-peek-coordinator-links")
    ).not.toBeNull();
    expect(
      document.getElementById("portal-peek-explanation")?.textContent
    ).not.toContain("0.90");

    document.getElementById("btn-peek-open-full-view").click();
    await flushUi();
    expect(state.peekRecord).toBeNull();

    const txRow = document.getElementById("wallet-tx-row-tx_stipend_99");
    expect(txRow).not.toBeNull();
    document.getElementById("btn-peek-tx-tx_stipend_99").click();
    await flushUi();
    expect(state.peekRecord?.kind).toBe("wallet_transaction");
    expect(router.currentRoute.value.query.txId).toBe("tx_stipend_99");
    closePeekRecord({ restoreFocus: false });
    await flushUi();

    // 4. Audit ledger block opens peek sheet with ?ledgerIndex=...
    renderLedger();
    await flushUi();
    const ledgerRow = document.getElementById("ledger-row-0");
    expect(ledgerRow).not.toBeNull();
    ledgerRow.click();
    await flushUi();
    expect(state.peekRecord?.kind).toBe("ledger_entry");
    expect(router.currentRoute.value.query.ledgerIndex).toBe("0");
    closePeekRecord({ restoreFocus: false });
    await flushUi();

    // 5. URL query deep-link (?syncSeq=1) opens quarantined sync item peek with coordinator help
    await router.replace({
      name: "tasks",
      query: { studyId: "STUDY-CARDIO-2026", syncSeq: "1" },
    });
    await flushUi();
    expect(state.peekRecord?.kind).toBe("sync_submission");
    expect(state.peekRecord?.showCoordinatorHelp).toBe(true);
  });

  it("replaces technical consent metadata & OCR copy and wires aria-describedby resolution links on W-9 holds, consent sign, and re-consent banners", async () => {
    await initializeApp();
    await flushUi();

    // 1. Plain-language helpers never leak study codes or 0.90 threshold numbers
    expect(resolveFriendlyStudyTitle("STUDY-ONCO-777")).toBe(
      "Oncology Companion Study"
    );
    expect(formatConsentEditionLabel("v2.4-AMEND-1")).toBe("Edition 2.4");
    expect(
      humanizeExpenseReviewNotes(
        "OCR confidence (0.81) is below 0.90 auto-approval threshold."
      )
    ).not.toContain("0.90");

    // 2. Consent metadata uses plain language and disabled sign button links to unfinished steps
    showView("view-consent");
    await loadConsentDetails();
    state.consentContent = {
      ...state.consentContent,
      study_id: "STUDY-CARDIO-2026",
      protocol_version: "v2.4-AMEND-1",
      document_version: undefined,
    };
    renderConsentUI();
    await flushUi();

    const metaText =
      document.getElementById("consent-metadata-display")?.textContent || "";
    expect(metaText).toContain("Cardiology Companion Study");
    expect(metaText).toContain("Edition 2.4");
    expect(metaText).not.toContain("STUDY-CARDIO-2026");
    expect(metaText).not.toContain("v2.4-AMEND-1");

    const signBtn = document.getElementById("btn-trigger-consent-sign");
    expect(signBtn?.disabled).toBe(true);
    expect(signBtn?.getAttribute("aria-describedby")).toBe(
      "consent-sign-readiness-hint"
    );
    const readinessHint = document.getElementById(
      "consent-sign-readiness-hint"
    );
    expect(readinessHint?.textContent).toContain("Quick Understanding Check");
    expect(readinessHint?.textContent).toContain("Quick Camera Check");
    expect(document.getElementById("link-jump-comprehension")).not.toBeNull();
    expect(document.getElementById("link-jump-liveness")).not.toBeNull();

    // 3. Wallet $600 W-9 hold banner wires aria-describedby and coordinator resolution links
    state.walletLoaded = true;
    state.walletBalance = 150;
    state.taxableYtd = 625;
    state.is1099ThresholdExceeded = true;
    state.w9Status = "REQUIRED";
    showView("view-wallet");
    await flushUi();

    const disburseBtn = document.getElementById("btn-disburse-balance");
    expect(disburseBtn?.disabled).toBe(true);
    expect(disburseBtn?.getAttribute("aria-describedby")).toBe(
      "wallet-w9-hold-banner"
    );
    expect(document.getElementById("link-w9-email-coordinator")).not.toBeNull();
    expect(document.getElementById("link-w9-call-coordinator")).not.toBeNull();

    // 4. Pending re-consent banner provides direct link to review updated clauses alongside modal sign CTA
    state.pendingReconsent = {
      id: "req_amend_02",
      study_id: "STUDY-CARDIO-2026",
      protocol_version: "2.0",
      change_summary: "Added optional home blood pressure check.",
    };
    await flushUi();

    const reviewLink = document.getElementById("link-review-consent-clauses");
    const openModalBtn = document.getElementById("btn-open-reconsent-modal");
    expect(reviewLink).not.toBeNull();
    expect(reviewLink?.getAttribute("aria-describedby")).toBe(
      "reconsent-banner-desc"
    );
    expect(openModalBtn?.getAttribute("aria-describedby")).toBe(
      "reconsent-banner-desc"
    );

    reviewLink.click();
    await flushUi();
    expect(state.currentView).toBe("view-consent");
  });

  it("defers peek bottom sheet focus trap while PIN setup modal is open on cold /wallet?claimId=... hydration and opens after PIN setup", async () => {
    const userId = "subject_cold_01";
    const jwt = encodeFakeJwt({
      sub: userId,
      study_id: "STUDY-CARDIO-2026",
      site_id: "SITE-BOS-01",
    });

    state.session.isDemoMode = false;
    state.session.isOfflineMode = false;
    state.session.userId = userId;
    state.session.token = jwt;
    state.walletLoaded = false;
    state.walletLoading = false;
    state.expenseClaims = [];
    state.recentTransactions = [];
    setActiveUserId(userId);
    clearInMemoryKey();

    let resolveWalletResponse;
    const delayedWalletPromise = new Promise((resolve) => {
      resolveWalletResponse = resolve;
    });

    globalThis.fetch = vi.fn().mockImplementation((url) => {
      if (String(url).includes("payments/participant/me/wallet")) {
        return delayedWalletPromise;
      }
      return Promise.resolve({ ok: true, json: () => Promise.resolve([]) });
    });

    window.history.replaceState(
      {},
      "",
      "/subject-portal/wallet?claimId=claim_cold_01"
    );

    await initializeApp();
    await flushUi();

    // PIN setup modal opens before delayed wallet hydration resolves
    const pinSetupModal = document.getElementById("portal-pin-setup-modal");
    expect(pinSetupModal).not.toBeNull();
    expect(state.pinSetup.isOpen).toBe(true);

    // Resolve delayed wallet response containing ?claimId=claim_cold_01 while PIN modal is open
    resolveWalletResponse({
      ok: true,
      json: () =>
        Promise.resolve({
          wallet: {
            id: "wal_cold_01",
            studyId: "STUDY-CARDIO-2026",
            siteId: "SITE-BOS-01",
            currency: "USD",
            currentBalance: 80,
          },
          expenseClaims: [
            {
              id: "claim_cold_01",
              category: "PARKING",
              merchantName: "Clinic Garage",
              expenseDate: "2026-11-20",
              grossAmount: 24,
              netAmount: 24,
              status: "PENDING_REVIEW",
            },
          ],
          recentTransactions: [],
        }),
    });
    await flushUi();

    // Peek record is hydrated, but the teleported drawer must stay closed so PIN setup is never marked inert
    expect(state.peekRecord?.id).toBe("claim_cold_01");
    expect(document.getElementById("portal-peek-drawer")).toBeNull();
    expect(pinSetupModal.hasAttribute("inert")).toBe(false);
    expect(document.getElementById("app")?.hasAttribute("inert")).toBe(false);

    // Completing PIN setup closes the modal and reveals the hydrated peek drawer with query param intact
    const { handlePINSetupSubmit } = await import("../src/features/pin.js");
    state.pinSetup.pin = "246810";
    state.pinSetup.confirmPin = "246810";
    await handlePINSetupSubmit();
    await flushUi();

    expect(state.pinSetup.isOpen).toBe(false);
    expect(document.getElementById("portal-peek-drawer")).not.toBeNull();
    expect(getPortalRouter().currentRoute.value.query.claimId).toBe(
      "claim_cold_01"
    );
  });
});

describe("Subject portal field constraints, VAS scale links, error token preservation, and PIN autocomplete (ADR-2679)", () => {
  it("splits data-* containerAttrs from input constraints, links VAS scale IDs, forwards fieldLabel to query flag/panel, and binds PIN autocomplete presets", async () => {
    const fs = await import("node:fs");
    const path = await import("node:path");
    const {
      createClinicalInput,
      createClinicalSlider,
      createClinicalQueryFlag,
      createQueryPanel,
    } = await import("../src/widgets/clinical-fields.js");
    const { markFieldInvalid, clearFieldInvalid } =
      await import("../src/widgets/field-errors.js");
    const {
      OFFLINE_SECURITY_NEW_PIN_INPUT_ATTRS,
      OFFLINE_SECURITY_PIN_INPUT_ATTRS,
    } = await import("ui");

    const host = document.createElement("div");
    host.innerHTML = createClinicalInput(
      "sysbp",
      "Systolic BP",
      "",
      { status: "OPEN", message: "Verify reading" },
      12,
      {
        type: "number",
        required: true,
        min: 50,
        max: 250,
        step: 1,
        "data-cdash": "VSORRES",
        placeholder: "120",
        "aria-describedby": "unit-sysbp",
      }
    );
    document.body.appendChild(host);

    const wrapper = host.querySelector("#field-container-sysbp");
    expect(wrapper.getAttribute("data-cdash")).toBe("VSORRES");
    expect(wrapper.hasAttribute("placeholder")).toBe(false);
    expect(wrapper.hasAttribute("min")).toBe(false);

    const input = wrapper.querySelector("input#sysbp");
    expect(input.required).toBe(true);
    expect(input.getAttribute("aria-required")).toBe("true");
    expect(input.getAttribute("min")).toBe("50");
    expect(input.getAttribute("max")).toBe("250");
    expect(input.getAttribute("step")).toBe("1");
    expect(input.getAttribute("aria-describedby")).toContain("unit-sysbp");

    const qBtn = wrapper.querySelector("#query-flag-sysbp");
    expect(qBtn.getAttribute("aria-label")).toContain("Systolic BP");
    const qPanel = wrapper.querySelector("#query-panel-sysbp");
    expect(qPanel.textContent).toContain("Systolic BP");

    markFieldInvalid("sysbp", "Out of range");
    expect(input.getAttribute("aria-describedby")).toContain("unit-sysbp");
    expect(input.getAttribute("aria-describedby")).toContain("error-msg-sysbp");
    expect(wrapper.querySelector("#error-msg-sysbp").getAttribute("role")).toBe(
      "alert"
    );

    clearFieldInvalid("sysbp");
    expect(input.getAttribute("aria-describedby")).toBe("unit-sysbp");

    const standaloneFlagHtml = createClinicalQueryFlag(
      "diabp",
      { status: "ANSWERED" },
      "Diastolic BP"
    );
    expect(standaloneFlagHtml).toContain("Diastolic BP");
    const standalonePanelHtml = createQueryPanel(
      "diabp",
      { status: "ANSWERED", message: "Check" },
      "Diastolic BP"
    );
    expect(standalonePanelHtml).toContain("Diastolic BP");

    const vasHost = document.createElement("div");
    vasHost.innerHTML = createClinicalSlider(
      "pain_vas",
      "Pain Score",
      "",
      null,
      12,
      {
        min: 0,
        max: 100,
        min_label: "No pain",
        max_label: "Worst pain",
        required: true,
      }
    );
    document.body.appendChild(vasHost);

    const vasInput = vasHost.querySelector('input[type="range"]#pain_vas');
    expect(vasInput.required).toBe(true);
    expect(vasInput.getAttribute("aria-describedby")).toContain(
      "pain_vas_min_label pain_vas_display pain_vas_max_label"
    );
    expect(vasInput.getAttribute("aria-valuetext")).toBe("Not answered");

    markFieldInvalid("pain_vas", "Pain score required");
    expect(vasInput.getAttribute("aria-describedby")).toContain(
      "pain_vas_min_label"
    );
    expect(vasInput.getAttribute("aria-describedby")).toContain(
      "error-msg-pain_vas"
    );
    clearFieldInvalid("pain_vas");
    expect(vasInput.getAttribute("aria-describedby")).toBe(
      "pain_vas_min_label pain_vas_display pain_vas_max_label"
    );

    expect(OFFLINE_SECURITY_NEW_PIN_INPUT_ATTRS.autocomplete).toBe(
      "new-password"
    );
    expect(OFFLINE_SECURITY_PIN_INPUT_ATTRS.autocomplete).toBe(
      "current-password"
    );

    const setupSrc = fs.readFileSync(
      path.resolve(__dirname, "../src/components/PinSetupModal.vue"),
      "utf8"
    );
    const unlockSrc = fs.readFileSync(
      path.resolve(__dirname, "../src/components/PinUnlockModal.vue"),
      "utf8"
    );
    expect(setupSrc).toContain('v-bind="OFFLINE_SECURITY_NEW_PIN_INPUT_ATTRS"');
    expect(unlockSrc).toContain('v-bind="OFFLINE_SECURITY_PIN_INPUT_ATTRS"');

    host.remove();
    vasHost.remove();
  });
});
