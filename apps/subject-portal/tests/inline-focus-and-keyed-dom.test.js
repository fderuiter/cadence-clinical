import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { nextTick } from "vue";
import {
  initializeApp,
  unmountApp,
  state,
  showView,
  renderTasks,
  renderInbox,
  renderConsentUI,
  startQuestionnaire,
  saveAndLeaveQuestionnaire,
  confirmCancelQuestionnaire,
  openSignatureModal,
  verifyAndSubmitSignature,
  openManualExpenseEntry,
  openDisburseModal,
  emitSyncLifecycleEvent,
  renderSyncQueueList,
  resetSyncRetryInfo,
} from "../src/index.js";
import { logAuditRecord, renderLedger } from "../src/features/audit.js";
import {
  clearAllSubmissions,
  clearInMemoryKey,
  clearSessionKey,
  initSessionKey,
  queueSubmission,
  bulkUpdateSubmissionStatuses,
} from "../src/sync-queue.js";
import { useSessionStore } from "../src/stores/session.js";
import { useWalletStore } from "../src/stores/wallet.js";

const settle = async () => {
  await nextTick();
  await Promise.resolve();
  await nextTick();
  await Promise.resolve();
};

describe("Subject Portal Inline Focus Handoffs, Visible Inbox, and Keyed DOM Updates (ADR-2593)", () => {
  beforeEach(async () => {
    window.__MOCK_TEST_ENV__ = true;
    vi.stubEnv("VITE_DEMO_MODE", "true");
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: () => Promise.resolve([]),
    });
    document.body.innerHTML = `<div id="app"></div>`;
    state.currentView = "view-tasks";
    state.isSyncDrawerOpen = false;
    state.signatureModalOpen = false;
    state.reconsentModalOpen = false;
    state.consentSigned = false;
    clearInMemoryKey();
    clearSessionKey();
    await clearAllSubmissions();
    await initializeApp();
    const session = useSessionStore();
    session.pinSetup.isOpen = false;
    session.pinUnlock.isOpen = false;
    session.session.isDemoMode = true;
    await settle();
  });

  afterEach(async () => {
    resetSyncRetryInfo();
    vi.useRealTimers();
    vi.unstubAllEnvs();
    const session = useSessionStore();
    session.pinSetup.isOpen = false;
    session.pinUnlock.isOpen = false;
    state.isSyncDrawerOpen = false;
    state.signatureModalOpen = false;
    state.reconsentModalOpen = false;
    unmountApp();
    document.body.innerHTML = "";
  });

  it("focuses the active .portal-view heading on showView while preserving TabBar roving arrow-key focus", async () => {
    showView("view-compliance");
    await settle();
    expect(document.activeElement?.id).toBe("compliance-view-heading");

    showView("view-inbox");
    await settle();
    expect(document.activeElement?.id).toBe("inbox-view-heading");

    showView("view-consent");
    await settle();
    expect(document.activeElement?.id).toBe("consent-view-heading");

    showView("view-wallet");
    await settle();
    expect(document.activeElement?.id).toBe("wallet-view-heading");

    // Arrow key navigation on TabBar keeps roving focus on the newly selected tab
    const tasksTab = document.getElementById("btn-tab-btn-tasks");
    showView("view-tasks", { preserveTabFocus: true });
    tasksTab.focus();
    tasksTab.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "ArrowRight",
        bubbles: true,
        cancelable: true,
      })
    );
    await settle();
    expect(document.activeElement?.id).toBe("btn-tab-btn-compliance");
  });

  it("places focus on #unavailable-view-heading and returns focus to visible #tasks-view-heading via return action", async () => {
    showView("view-unavailable");
    await settle();
    expect(document.activeElement?.id).toBe("unavailable-view-heading");

    const returnBtn = document.getElementById("btn-unavailable-return-tasks");
    expect(returnBtn).not.toBeNull();
    returnBtn.click();
    await settle();

    const tasksSection = document.getElementById("view-tasks");
    expect(tasksSection?.classList.contains("active")).toBe(true);
    expect(document.activeElement?.id).toBe("tasks-view-heading");
    expect(document.activeElement).not.toBe(document.body);
  });

  it("restores task card trigger focus when leaving or canceling an open questionnaire", async () => {
    state.tasksLoading = false;
    state.tasksError = null;
    state.assignments = [
      {
        id: "assign_focus_01",
        instrument_id: "inst_vitals",
        title: "Daily Vital Signs",
        status: "PENDING",
        due_date: "2026-12-01T12:00:00Z",
      },
    ];
    state.instruments = {
      inst_vitals: {
        id: "inst_vitals",
        title: "Daily Vital Signs",
        items: {
          vssbp: {
            label: "Systolic BP",
            type: "number",
            required: true,
          },
        },
      },
    };
    showView("view-tasks");
    renderTasks();
    await settle();

    await startQuestionnaire("assign_focus_01");
    await settle();
    expect(state.currentView).toBe("view-questionnaire");

    await saveAndLeaveQuestionnaire();
    await settle();
    const startBtnAfterSave = document.querySelector(
      "#task-card-assign_focus_01 .btn-start-task"
    );
    expect(startBtnAfterSave).not.toBeNull();
    expect(document.activeElement).toBe(startBtnAfterSave);

    await startQuestionnaire("assign_focus_01");
    await settle();
    await confirmCancelQuestionnaire();
    await settle();
    const startBtnAfterCancel = document.querySelector(
      "#task-card-assign_focus_01 .btn-start-task"
    );
    expect(document.activeElement).toBe(startBtnAfterCancel);
  });

  it("displays #inbox-container visibly, activates notifications via Enter/Space, and hands off focus on acknowledge", async () => {
    state.notificationsLoading = false;
    state.notificationsError = null;
    state.notifications = [
      {
        id: "notif-1",
        title: "Morning Diary Reminder",
        message: "Please record your morning symptoms.",
        notification_type: "REMINDER",
        created_at: "2026-11-22T08:00:00Z",
        is_read: false,
      },
      {
        id: "notif-2",
        title: "Clinic Visit Confirmed",
        message: "Your Week 4 visit is scheduled.",
        notification_type: "VISIT",
        created_at: "2026-11-22T09:00:00Z",
        is_read: false,
      },
    ];

    showView("view-inbox");
    renderInbox();
    await settle();

    const container = document.getElementById("inbox-container");
    expect(container?.style.display).toBe("block");

    const firstAckBtn = container.querySelector(
      "#notif-card-notif-1 .btn-acknowledge"
    );
    expect(firstAckBtn).not.toBeNull();
    firstAckBtn.focus();

    // Activate via Enter keydown
    firstAckBtn.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Enter",
        bubbles: true,
        cancelable: true,
      })
    );
    // Focus moves to the remaining unread notification's acknowledge button
    await vi.waitFor(() => {
      const secondAckBtn = container.querySelector(
        "#notif-card-notif-2 .btn-acknowledge"
      );
      expect(secondAckBtn).not.toBeNull();
      expect(document.activeElement).toBe(secondAckBtn);
    });

    const secondAckBtn = container.querySelector(
      "#notif-card-notif-2 .btn-acknowledge"
    );

    // Activate second notification via Space keydown
    secondAckBtn.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: " ",
        bubbles: true,
        cancelable: true,
      })
    );

    // With no unread buttons left, focus lands on the acknowledged card or inbox heading (never BODY)
    await vi.waitFor(() => {
      expect(
        container.querySelector("#notif-card-notif-2 .btn-acknowledge")
      ).toBeNull();
      expect(document.activeElement).not.toBe(document.body);
      expect(
        ["notif-card-notif-2", "inbox-view-heading"].includes(
          document.activeElement?.id || ""
        )
      ).toBe(true);
    });

    // Empty state also keeps #inbox-container visible
    state.notifications = [];
    renderInbox();
    expect(container?.style.display).toBe("block");
  });

  it("focuses #consent-signed-heading when eConsent transitions to signed state and after signature submission", async () => {
    showView("view-consent");
    await settle();

    state.consentContent = {
      template_id: "tpl-icf-1",
      template_name: "Main Study Informed Consent Form",
      study_id: "STUDY-101",
      protocol_version: "1.0",
      version_index: 1,
      language_code: "en",
      clauses: [
        {
          clause_id: "c-1",
          title: "Study Purpose",
          text: "Evaluates daily symptom diaries.",
          version_index: 1,
        },
      ],
    };
    state.consentCheck = { template_id: "tpl-icf-1", questions: [] };
    state.consentSigned = false;
    renderConsentUI();
    await settle();

    const signCard = document.getElementById("consent-signature-card");
    expect(signCard?.style.display).toBe("block");
    const compCard = document.getElementById("consent-comprehension-card");
    compCard?.setAttribute("tabindex", "-1");
    compCard?.focus();
    expect(compCard?.contains(document.activeElement)).toBe(true);

    state.consentSigned = true;
    renderConsentUI();
    await settle();

    const signedCard = document.getElementById("consent-signed-card");
    expect(signedCard?.style.display).toBe("block");
    expect(document.activeElement?.id).toBe("consent-signed-heading");

    // Also verify signature.js ePRO completion moves focus to visible #tasks-view-heading
    const rawMaterial = new Uint8Array(32);
    for (let i = 0; i < 32; i++) rawMaterial[i] = i + 1;
    await initSessionKey(rawMaterial);

    state.consentSigned = false;
    state.activeQuestionnaire = {
      assignment: {
        id: "assign_sig_01",
        instrument_id: "inst_vitals",
        study_id: "STUDY-101",
      },
      instrument: { id: "inst_vitals", title: "Vitals", items: {} },
      answers: { vssbp: 120 },
    };
    showView("view-questionnaire");
    await settle();
    openSignatureModal({ vssbp: 120 }, false);
    await settle();

    document.getElementById("sign-username").value = "subject_mocked_001";
    document.getElementById("sign-password").value = "ValidPin123!";
    await verifyAndSubmitSignature();
    await vi.waitFor(() => {
      expect(state.currentView).toBe("view-tasks");
      expect(document.activeElement?.id).toBe("tasks-view-heading");
    });
  });

  it("focuses OCR review controls and confirmation headings in WalletView across receipt claim and full disbursement", async () => {
    const wallet = useWalletStore();
    wallet.walletLoaded = true;
    wallet.walletLoading = false;
    wallet.walletId = "wallet-portal-1";
    wallet.walletBalance = 50;
    wallet.lifetimeStipends = 100;
    wallet.lifetimeReimbursements = 25;
    wallet.lifetimeDisbursements = 75;
    wallet.w9Status = "NOT_REQUIRED";
    wallet.currency = "USD";

    showView("view-wallet");
    await settle();

    openManualExpenseEntry();
    await settle();
    expect(document.activeElement?.id).toBe("ocr-category");

    // Simulate expense submission completion mounting #expense-auto-approved-heading while #ocr-review-card unmounts
    const confirmReimburseBtn = document.getElementById(
      "btn-confirm-reimburse"
    );
    expect(confirmReimburseBtn).not.toBeNull();
    confirmReimburseBtn.focus();

    wallet.expenseManualEntry = false;
    wallet.expenseOcr = {
      loading: false,
      data: null,
      error: null,
      warning: null,
      previewUrl: null,
    };
    wallet.expenseResult = {
      status: "AUTO_APPROVED",
      auto_approved: true,
      disbursement_reference: "ACH-TEST-9001",
    };
    await settle();

    expect(document.activeElement?.id).toBe("expense-auto-approved-heading");

    // Open payout modal and disburse full balance so #btn-disburse-balance becomes disabled and focus lands on #wallet-disbursement-heading
    const disburseBtn = document.getElementById("btn-disburse-balance");
    expect(disburseBtn).not.toBeNull();
    openDisburseModal(disburseBtn);
    await settle();

    wallet.walletBalance = 0;
    wallet.disbursementConfirmation = {
      amount: 50,
      disbursementReference: "PAYOUT-991",
      disbursementType: "DIRECT_ACH",
    };
    wallet.disburseModalOpen = false;
    await settle();

    expect(document.activeElement?.id).toBe("wallet-disbursement-heading");
  });

  it("preserves .site-contact-inline-link focus across 1000ms retry countdown ticks in sync-status.js (WCAG F55)", async () => {
    const rawMaterial = new Uint8Array(32);
    for (let i = 0; i < 32; i++) rawMaterial[i] = i;
    await initSessionKey(rawMaterial);

    const quarantinedSub = await queueSubmission({
      subject_id: "subj_01",
      diary_id: "inst_vitals",
      assignment_id: "assign_quarantined_01",
      answers: { vssbp: 250 },
      change_reason: "Outlier reading",
      username: "subject_mocked_001",
    });
    await bulkUpdateSubmissionStatuses([
      {
        sequence_number: quarantinedSub.sequence_number,
        status: "QUARANTINED",
        additionalFields: {
          error_message: "Coordinator review required",
        },
      },
    ]);

    const queued = await queueSubmission({
      subject_id: "subj_01",
      diary_id: "inst_vitals",
      assignment_id: "assign_retry_01",
      answers: { vssbp: 122 },
      change_reason: "Morning reading",
      username: "subject_mocked_001",
    });

    state.isSyncDrawerOpen = true;
    await settle();
    await renderSyncQueueList();
    await settle();

    vi.useFakeTimers({
      toFake: ["setInterval", "clearInterval", "Date"],
    });
    const now = Date.now();
    vi.setSystemTime(now);

    emitSyncLifecycleEvent("sync:retry-scheduled", {
      attempt: 1,
      maxRetries: 4,
      delayMs: 5000,
      nextRetryDelay: 5000,
      nextRetryTimestamp: now + 5000,
      sequenceNumber: queued.sequence_number,
    });
    await renderSyncQueueList();

    const contactLink = document.querySelector(
      "#sync-queue-list .site-contact-inline-link"
    );
    expect(contactLink).not.toBeNull();
    contactLink.focus();
    expect(document.activeElement).toBe(contactLink);

    const statusBanner = document.getElementById("sync-queue-status-text");

    vi.advanceTimersByTime(1000);
    await settle();
    expect(document.activeElement).toBe(contactLink);
    expect(statusBanner?.textContent).toContain("4s");

    vi.advanceTimersByTime(1000);
    await settle();
    expect(document.activeElement).toBe(contactLink);
    expect(statusBanner?.textContent).toContain("3s");
  });

  it("preserves <summary> focus and open <details> state across new audit block appends in #portal-ledger-timeline", async () => {
    showView("view-compliance");
    await settle();

    await logAuditRecord("CONSENT_VIEWED", "Reviewed informed consent v1");
    renderLedger();

    const timeline = document.getElementById("portal-ledger-timeline");
    expect(timeline).not.toBeNull();
    const firstDetails = timeline.querySelector(
      "details.ledger-verification-details"
    );
    const firstSummary = firstDetails?.querySelector("summary");
    expect(firstSummary).not.toBeNull();

    firstDetails.open = true;
    firstSummary.focus();
    expect(document.activeElement).toBe(firstSummary);

    await logAuditRecord("DIARY_SAVED", "Saved morning symptom diary");
    await settle();

    expect(
      timeline.querySelectorAll(".ledger-item").length
    ).toBeGreaterThanOrEqual(2);
    expect(firstDetails.isConnected).toBe(true);
    expect(firstDetails.open).toBe(true);
    expect(document.activeElement).toBe(firstSummary);
  });
});
