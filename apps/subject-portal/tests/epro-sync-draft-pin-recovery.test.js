import "fake-indexeddb/auto";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { nextTick } from "vue";

import {
  MAX_PIN_UNLOCK_ATTEMPTS,
  SITE_CONTACT_STORAGE_KEY,
  cancelPINReset,
  checkPINWrapper,
  clearAllSubmissions,
  confirmCancelQuestionnaire,
  confirmPINReset,
  dismissCancelQuestionnaire,
  formatAnswersSummary,
  getDraftAssignmentIds,
  getSiteSupportContact,
  handlePINSetupSubmit,
  handlePINUnlockSubmit,
  hydrateDraftPresence,
  hydrateSiteSupportContact,
  humanizeValidationError,
  initializeApp,
  maskParticipantIdentifier,
  reauthenticateForPINReset,
  refreshSubmissionsState,
  renderInbox,
  renderSyncQueueList,
  renderTasks,
  requestCancelQuestionnaire,
  requestPINReset,
  resetSyncRetryInfo,
  saveAndLeaveQuestionnaire,
  showView,
  startQuestionnaire,
  state,
  syncOfflineQueue,
  unmountApp,
} from "../src/index.js";
import {
  clearInMemoryKey,
  clearLockedOfflineData,
  clearSessionKey,
  getAllSubmissions,
  getDraft,
  getInMemorySessionKey,
  getQueuedSubmissions,
  initSessionKey,
  openDatabase,
  queueSubmission,
  saveDraft,
  setActiveUserId,
} from "../src/sync-queue.js";

async function settle() {
  await nextTick();
  await new Promise((r) => setTimeout(r, 0));
  await nextTick();
}

describe("Client-side ePRO sync, draft preservation, PIN recovery, and offline site contacts", () => {
  const origFetch = globalThis.fetch;

  beforeEach(async () => {
    vi.useRealTimers();
    vi.restoreAllMocks();
    window.__MOCK_TEST_ENV__ = true;
    unmountApp();
    resetSyncRetryInfo();
    localStorage.clear();
    sessionStorage.clear();

    document.body.innerHTML = `
      <div id="app"></div>
      <div id="sync-queue-status-text"></div>
      <div id="sync-queue-list"></div>
    `;

    clearInMemoryKey();
    clearSessionKey("subject_001");
    setActiveUserId("subject_001");
    await clearLockedOfflineData("subject_001");

    state.session = {
      userId: "subject_001",
      roles: "Subject",
      token: "jwt_valid_subject_token", // pragma: allowlist secret
      isOfflineMode: false,
      isDemoMode: false,
    };
    state.authError = "";
    state.autoSyncSuspended = false;
    state.isSyncDrawerOpen = false;
    state.submissions = [];
    state.assignments = [
      {
        id: "assign_01",
        subject_id: "subject_001",
        instrument_id: "inst_daily_diary",
        instrument_name: "Daily Health & Vital Diary",
        status: "PENDING",
        due_at: new Date(Date.now() + 3600 * 1000).toISOString(),
        version_index: 1,
      },
      {
        id: "assign_02",
        subject_id: "subject_001",
        instrument_id: "inst_weekly_symptoms",
        instrument_name: "Weekly Symptoms & eCOA Checklist",
        status: "PENDING",
        due_at: new Date(Date.now() + 7200 * 1000).toISOString(),
        version_index: 1,
      },
    ];
    state.instruments = {
      inst_daily_diary: {
        id: "inst_daily_diary",
        name: "Daily Health & Vital Diary",
        description: "Record your daily vital signs.",
        version_index: 1,
        items: {
          vssbp: {
            label: "Systolic Blood Pressure",
            type: "numeric",
            required: true,
            min: 70,
            max: 250,
          },
          vsdpb: {
            label: "Diastolic Blood Pressure",
            type: "numeric",
            required: true,
            min: 40,
            max: 150,
          },
          pain_vas: {
            label: "Pain VAS Score",
            type: "vas",
            required: false,
            min: 0,
            max: 10,
          },
          has_symptoms: {
            label: "Any new symptoms?",
            type: "choice_single",
            required: true,
            options: ["No", "Yes"],
          },
        },
      },
    };
    state.notifications = [];
    state.draftPresence = {};
    state.questionnaireCancelConfirmOpen = false;
    state.pinSetup = { isOpen: false, pin: "", confirmPin: "", error: "" };
    state.pinUnlock = {
      isOpen: false,
      pin: "",
      error: "",
      confirmingReset: false,
      failedAttempts: 0,
      maxAttempts: 5,
      isLockedOut: false,
      requiresReauth: false,
    };

    const rawMaterial = new Uint8Array(32);
    for (let i = 0; i < 32; i++) rawMaterial[i] = i + 1;
    await initSessionKey(rawMaterial, "subject_001");
  });

  afterEach(() => {
    vi.useRealTimers();
    resetSyncRetryInfo();
    unmountApp();
    globalThis.fetch = origFetch;
  });

  it("Req 1: manual sync in alert banner and sync drawer refreshes survey definitions, syncs suspended queues, and clears suspension", async () => {
    globalThis.fetch = vi.fn().mockImplementation(async (url) => {
      if (String(url).includes("/instruments")) {
        return {
          ok: true,
          status: 200,
          json: async () => [
            {
              id: "inst_daily_diary",
              name: "Daily Health & Vital Diary (v2)",
              description: "Updated form structure",
              version_index: 2,
              items: state.instruments.inst_daily_diary.items,
            },
          ],
        };
      }
      if (String(url).includes("assignments/subject/")) {
        return { ok: true, status: 200, json: async () => state.assignments };
      }
      if (String(url).includes("/compliance")) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            subject_id: "subject_001",
            compliance_rate: 50,
            completed_count: 1,
            pending_count: 1,
            overdue_count: 0,
            assignments: [],
          }),
        };
      }
      if (String(url).includes("/notifications")) {
        return { ok: true, status: 200, json: async () => [] };
      }
      if (String(url).includes("epro/sync")) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            results: [
              {
                status: "CREATED",
                answers: { vssbp: 120, vsdpb: 80 },
              },
            ],
          }),
        };
      }
      return { ok: true, status: 200, json: async () => ({}) };
    });

    sessionStorage.setItem("mock_user_id", "subject_001");
    sessionStorage.setItem("mock_token", "jwt_valid_subject_token"); // pragma: allowlist secret
    await initializeApp();
    await settle();

    // Close PIN setup and re-init in-memory key for submission queueing
    state.pinSetup.isOpen = false;
    const rawMaterial = new Uint8Array(32);
    for (let i = 0; i < 32; i++) rawMaterial[i] = i + 5;
    await initSessionKey(rawMaterial, "subject_001");

    await queueSubmission({
      subject_id: "subject_001",
      diary_id: "inst_daily_diary",
      assignment_id: "assign_01",
      answers: { vssbp: 120, vsdpb: 80 },
      change_reason: "Offline entry before form update",
      username: "subject_001",
    });

    // Suspend auto-sync and verify auto-sync does not run
    state.autoSyncSuspended = true;
    await nextTick();

    const fetchCallsBefore = globalThis.fetch.mock.calls.length;
    await syncOfflineQueue(false);
    expect(globalThis.fetch.mock.calls.length).toBe(fetchCallsBefore);
    expect(state.autoSyncSuspended).toBe(true);

    // Clicking Manual Sync / Refresh in SyncSuspendedBanner forces sync, reloads v2 instrument, and clears suspension
    const bannerBtn = document.getElementById("btn-alert-sync-now");
    expect(bannerBtn).not.toBeNull();
    bannerBtn.click();
    await vi.waitFor(() => {
      expect(state.autoSyncSuspended).toBe(false);
    });
    await settle();

    expect(state.instruments.inst_daily_diary.version_index).toBe(2);
    expect(state.instruments.inst_daily_diary.name).toBe(
      "Daily Health & Vital Diary (v2)"
    );
    const allAfterBanner = await getAllSubmissions();
    expect(allAfterBanner[0].status).toBe("CREATED");

    // Re-suspend with empty queue (e.g., after prior server quarantine) and verify SyncDrawer Sync Now clears suspension
    state.autoSyncSuspended = true;
    state.isSyncDrawerOpen = true;
    await nextTick();

    const drawerBtn = document.getElementById("btn-drawer-sync-now");
    expect(drawerBtn).not.toBeNull();
    drawerBtn.click();
    await vi.waitFor(() => {
      expect(state.autoSyncSuspended).toBe(false);
    });
  });

  it("Req 2 & Req 4: isolates valid surveys via POST epro/submit when multi-item sync fails validation, highlights quarantined badge, and hides raw JSON/PII", async () => {
    sessionStorage.setItem("mock_user_id", "subject_001");
    sessionStorage.setItem("mock_token", "jwt_valid_subject_token"); // pragma: allowlist secret

    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => [],
    });
    await initializeApp();
    await settle();
    state.pinSetup.isOpen = false;

    const rawMaterial = new Uint8Array(32);
    for (let i = 0; i < 32; i++) rawMaterial[i] = i + 9;
    await initSessionKey(rawMaterial, "subject_001");

    state.assignments = [
      {
        id: "assign_v1",
        subject_id: "subject_001",
        instrument_id: "inst_daily_diary",
        status: "PENDING_SYNC",
        version_index: 1,
      },
      {
        id: "assign_bad",
        subject_id: "subject_001",
        instrument_id: "inst_daily_diary",
        status: "PENDING_SYNC",
        version_index: 1,
      },
      {
        id: "assign_v2",
        subject_id: "subject_001",
        instrument_id: "inst_weekly_symptoms",
        status: "PENDING_SYNC",
        version_index: 1,
      },
    ];

    // Queue 2 valid surveys + 1 survey that server rejects with 422 validation error
    await queueSubmission({
      subject_id: "subject_001",
      diary_id: "inst_daily_diary",
      assignment_id: "assign_v1",
      answers: { vssbp: 118, vsdpb: 76, participant_name: "Alice Example" },
      change_reason: "Morning vitals",
      username: "subject_001",
    });
    await queueSubmission({
      subject_id: "subject_001",
      diary_id: "inst_daily_diary",
      assignment_id: "assign_bad",
      answers: { vssbp: 999, vsdpb: 78 },
      change_reason: "Typo vitals",
      username: "subject_001",
    });
    await queueSubmission({
      subject_id: "subject_001",
      diary_id: "inst_weekly_symptoms",
      assignment_id: "assign_v2",
      answers: { pain_score: 3 },
      change_reason: "Weekly symptoms",
      username: "subject_001",
    });

    const calledEndpoints = [];
    globalThis.fetch = vi.fn().mockImplementation(async (url, opts) => {
      calledEndpoints.push(String(url));
      if (String(url).includes("epro/sync")) {
        return {
          ok: false,
          status: 422,
          json: async () => ({
            detail: [
              {
                loc: ["body", "submissions", 1, "answers", "vssbp"],
                msg: "Systolic blood pressure must be 250 mmHg or lower.",
              },
            ],
          }),
        };
      }
      if (String(url).includes("epro/submit")) {
        const body = JSON.parse(opts.body);
        if (body.answers.vssbp === 999) {
          return {
            ok: false,
            status: 422,
            json: async () => ({
              detail:
                "Systolic blood pressure (vssbp) must be between 70 and 250 mmHg.",
            }),
          };
        }
        return {
          ok: true,
          status: 200,
          json: async () => ({
            status: "CREATED",
            answers: body.answers,
          }),
        };
      }
      return { ok: true, status: 200, json: async () => ({}) };
    });

    await syncOfflineQueue(false);
    await settle();

    // Verified batch attempted once, then 3 individual epro/submit calls executed
    expect(calledEndpoints.filter((u) => u.includes("epro/sync"))).toHaveLength(
      1
    );
    expect(
      calledEndpoints.filter((u) => u.includes("epro/submit"))
    ).toHaveLength(3);

    const all = await getAllSubmissions();
    const synced = all.filter((s) => s.status === "CREATED");
    const quarantined = all.filter((s) => s.status === "LOCAL_QUARANTINED");
    expect(synced).toHaveLength(2);
    expect(quarantined).toHaveLength(1);
    expect(quarantined[0].error).toContain("between 70 and 250 mmHg");

    // Valid assignments promoted to COMPLETED, invalid stays PENDING_SYNC
    expect(state.assignments.find((a) => a.id === "assign_v1").status).toBe(
      "COMPLETED"
    );
    expect(state.assignments.find((a) => a.id === "assign_v2").status).toBe(
      "COMPLETED"
    );
    expect(state.assignments.find((a) => a.id === "assign_bad").status).toBe(
      "PENDING_SYNC"
    );

    // Header badge highlights the 1 locally quarantined survey
    const headerBtn = document.getElementById("btn-global-sync-drawer");
    const headerBadge = document.getElementById("header-sync-badge");
    expect(headerBtn.classList.contains("has-quarantined")).toBe(true);
    expect(headerBadge).not.toBeNull();
    expect(headerBadge.textContent.trim()).toBe("1");

    // Drawer displays plain-language reason + coordinator steps, zero raw JSON, and masks PII
    const drawerHtml =
      document.getElementById("global-sync-drawer").textContent;
    expect(drawerHtml).toContain("Needs review");
    expect(drawerHtml).not.toContain("QUARANTINED");
    expect(drawerHtml).toContain("between 70 and 250 mmHg");
    expect(drawerHtml).toContain("contact your study coordinator");
    expect(drawerHtml).not.toContain("{");
    expect(drawerHtml).not.toContain("}");
    expect(drawerHtml).not.toContain("Alice Example");
  });

  it("Req 3: schedules automatic exponential retries without manual intervention and updates live countdown every second", async () => {
    sessionStorage.setItem("mock_user_id", "subject_001");
    sessionStorage.setItem("mock_token", "jwt_valid_subject_token"); // pragma: allowlist secret
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => [],
    });

    await initializeApp();
    await settle();
    state.pinSetup.isOpen = false;

    const rawMaterial = new Uint8Array(32);
    for (let i = 0; i < 32; i++) rawMaterial[i] = i + 3;
    await initSessionKey(rawMaterial, "subject_001");

    await queueSubmission({
      subject_id: "subject_001",
      diary_id: "inst_daily_diary",
      assignment_id: "assign_01",
      answers: { vssbp: 122, vsdpb: 79 },
      change_reason: "Transient drop test",
      username: "subject_001",
    });

    vi.useFakeTimers({ shouldAdvanceTime: true });

    let syncCallCount = 0;
    globalThis.fetch = vi.fn().mockImplementation(async (url) => {
      if (String(url).includes("epro/sync")) {
        syncCallCount += 1;
        if (syncCallCount < 3) {
          throw new Error("Transient 503 network outage");
        }
        return {
          ok: true,
          status: 200,
          json: async () => ({
            results: [
              { status: "CREATED", answers: { vssbp: 122, vsdpb: 79 } },
            ],
          }),
        };
      }
      return { ok: true, status: 200, json: async () => ({}) };
    });

    // Attempt 1 fails -> schedules automatic retry in 4000ms (4s countdown)
    await syncOfflineQueue(false, { retryDelay: 4000, maxRetries: 5 });
    await nextTick();

    expect(syncCallCount).toBe(1);
    expect(state.syncRetryInfo.isRetrying).toBe(true);
    expect(state.syncRetryInfo.remainingSeconds).toBe(4);
    expect(document.querySelector(".sync-btn-label").textContent).toContain(
      "Retrying (1/5) · 4s"
    );
    expect(
      document.getElementById("sync-drawer-retry-countdown").textContent
    ).toContain("4s");

    // Advance 1 second -> live countdown updates to 3s without user action
    await vi.advanceTimersByTimeAsync(1000);
    await nextTick();
    expect(state.syncRetryInfo.remainingSeconds).toBe(3);
    expect(state.syncStatusText).toContain("Next retry in 3s");
    expect(document.querySelector(".sync-btn-label").textContent).toContain(
      "Retrying (1/5) · 3s"
    );

    // Advance remaining 3s -> Attempt 2 fires automatically, fails, schedules exponential 8000ms (8s) retry
    await vi.advanceTimersByTimeAsync(3000);
    await vi.waitFor(() => {
      expect(syncCallCount).toBe(2);
      expect(state.syncRetryInfo.attempt).toBe(2);
      expect(state.syncRetryInfo.nextRetryDelay).toBe(8000);
    });
    expect(state.syncRetryInfo.remainingSeconds).toBeGreaterThanOrEqual(7);
    expect(state.syncRetryInfo.remainingSeconds).toBeLessThanOrEqual(8);

    // Advance 8s -> Attempt 3 fires automatically and succeeds!
    await vi.advanceTimersByTimeAsync(8000);
    await vi.waitFor(() => {
      expect(syncCallCount).toBe(3);
      expect(state.syncRetryInfo.isRetrying).toBe(false);
    });

    const all = await getAllSubmissions();
    expect(all[0].status).toBe("CREATED");
  });

  it("Req 5: navigating Back saves partial answers and marks task Resume Survey, while Cancel requires confirmation before discarding", async () => {
    vi.stubEnv("VITE_DEMO_MODE", "true");
    sessionStorage.setItem("mock_user_id", "subject_001");
    sessionStorage.setItem("mock_token", "jwt_valid_subject_token"); // pragma: allowlist secret
    globalThis.fetch = vi.fn().mockImplementation(async (url) => {
      if (String(url).includes("assignments/subject/")) {
        return { ok: true, status: 200, json: async () => state.assignments };
      }
      if (String(url).includes("/instruments")) {
        return {
          ok: true,
          status: 200,
          json: async () => [state.instruments.inst_daily_diary],
        };
      }
      return { ok: true, status: 200, json: async () => [] };
    });

    await initializeApp();
    await settle();
    state.pinSetup.isOpen = false;

    const rawMaterial = new Uint8Array(32);
    for (let i = 0; i < 32; i++) rawMaterial[i] = i + 11;
    await initSessionKey(rawMaterial, "subject_001");

    // Open survey and enter partial answers
    await startQuestionnaire("assign_01");
    await settle();
    expect(state.currentView).toBe("view-questionnaire");

    document.getElementById("vssbp").value = "124";
    document.getElementById("vsdpb").value = "82";
    const slider = document.getElementById("pain_vas");
    slider.value = "6";
    slider.setAttribute("data-touched", "true");

    // Click Back to My Tasks -> saves draft and shows "Resume Survey"
    await saveAndLeaveQuestionnaire();
    await settle();

    expect(state.currentView).toBe("view-tasks");
    const savedDraft = await getDraft("assign_01");
    expect(savedDraft).toMatchObject({
      vssbp: "124",
      vsdpb: "82",
      pain_vas: "6",
    });
    expect(await getDraftAssignmentIds("subject_001")).toContain("assign_01");

    await hydrateDraftPresence("subject_001");
    renderTasks();
    const taskCard = document.getElementById("task-card-assign_01");
    expect(taskCard.textContent).toContain("Draft Saved");
    expect(taskCard.querySelector(".btn-start-task").textContent).toBe(
      "Resume Survey"
    );

    // Reopen survey -> partial values are restored
    await startQuestionnaire("assign_01");
    await settle();
    expect(document.getElementById("vssbp").value).toBe("124");
    expect(document.getElementById("vsdpb").value).toBe("82");
    expect(document.getElementById("pain_vas").value).toBe("6");

    // Click Cancel -> opens confirmation dialog instead of discarding immediately
    requestCancelQuestionnaire();
    await settle();
    expect(state.questionnaireCancelConfirmOpen).toBe(true);
    const confirmDialog = document.getElementById(
      "questionnaire-cancel-confirm"
    );
    expect(confirmDialog).not.toBeNull();
    expect(await getDraft("assign_01")).not.toBeNull();

    // Choose "Keep Editing" -> stays in survey with draft intact
    dismissCancelQuestionnaire();
    await settle();
    expect(state.questionnaireCancelConfirmOpen).toBe(false);
    expect(state.currentView).toBe("view-questionnaire");
    expect(await getDraft("assign_01")).not.toBeNull();

    // Click Cancel again and confirm discard -> removes draft and resets CTA to "Start Survey"
    requestCancelQuestionnaire();
    await settle();
    await confirmCancelQuestionnaire();
    await settle();

    expect(state.currentView).toBe("view-tasks");
    expect(await getDraft("assign_01")).toBeNull();
    expect(
      document
        .getElementById("task-card-assign_01")
        .querySelector(".btn-start-task").textContent
    ).toBe("Start Survey");
    vi.unstubAllEnvs();
  });

  it("Req 6: caps consecutive failed PIN attempts at 5, traps focus across recovery & support links in <=3 interactions, and resets PIN via OIDC with a new key while flagging prior entries without PII", async () => {
    const userId = "subject_pin_recovery";
    localStorage.setItem("oidc_user_id", userId);
    sessionStorage.setItem("mock_user_id", userId);
    sessionStorage.setItem("mock_token", "jwt_pin_session"); // pragma: allowlist secret

    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => [],
    });

    await initializeApp();
    await settle();

    // Set initial PIN 246810
    expect(state.pinSetup.isOpen).toBe(true);
    state.pinSetup.pin = "246810";
    state.pinSetup.confirmPin = "246810";
    await handlePINSetupSubmit();
    const originalMasterKey = getInMemorySessionKey();
    expect(originalMasterKey).toBeInstanceOf(Uint8Array);

    // Queue an offline submission with clinical data & draft
    await saveDraft("assign_01", { vssbp: "132" });
    await queueSubmission({
      subject_id: userId,
      diary_id: "inst_daily_diary",
      assignment_id: "assign_01",
      answers: { vssbp: "132", vsdpb: "88" },
      change_reason: "Pre-lockout entry",
      username: userId,
    });

    // Simulate locked offline device with expired OIDC token
    clearInMemoryKey();
    sessionStorage.removeItem("mock_token");
    state.session.token = null;
    state.session.isOfflineMode = true;
    await checkPINWrapper();
    await settle();

    expect(state.pinUnlock.isOpen).toBe(true);

    // Enter 5 consecutive wrong PINs
    for (let i = 1; i <= MAX_PIN_UNLOCK_ATTEMPTS; i++) {
      state.pinUnlock.pin = "999999";
      await handlePINUnlockSubmit();
      await nextTick();
      if (i < MAX_PIN_UNLOCK_ATTEMPTS) {
        expect(state.pinUnlock.isLockedOut).toBe(false);
        expect(state.pinUnlock.error).toBe(
          "Incorrect security PIN. Access denied."
        );
      }
    }

    // 5th failure pauses unlock attempts
    expect(state.pinUnlock.failedAttempts).toBe(5);
    expect(state.pinUnlock.isLockedOut).toBe(true);
    expect(document.getElementById("unlock-pin").disabled).toBe(true);
    expect(document.getElementById("btn-pin-unlock-confirm").disabled).toBe(
      true
    );
    expect(document.getElementById("pin-lockout-banner")).not.toBeNull();

    // Even entering the real PIN while locked out stays paused
    state.pinUnlock.pin = "246810";
    await handlePINUnlockSubmit();
    expect(getInMemorySessionKey()).toBeNull();
    expect(state.pinUnlock.isOpen).toBe(true);

    // Verify modal focus trap includes recovery, sign-out, and tel/mailto support links in <=3 interactions
    const modal = document.getElementById("portal-pin-unlock-modal");
    const resetBtn = document.getElementById("btn-pin-unlock-reset");
    const signOutBtn = document.getElementById("btn-pin-unlock-signout");
    const telLink = modal.querySelector('a[href^="tel:"]');
    const mailLink = modal.querySelector('a[href^="mailto:"]');
    expect(resetBtn).not.toBeNull();
    expect(signOutBtn).not.toBeNull();
    expect(telLink).not.toBeNull();
    expect(mailLink).not.toBeNull();

    // Shift+Tab from first enabled element (#btn-pin-unlock-reset) wraps directly to mailto link (1 interaction!)
    resetBtn.focus();
    const shiftTabEvt = new KeyboardEvent("keydown", {
      key: "Tab",
      shiftKey: true,
      bubbles: true,
      cancelable: true,
    });
    modal.dispatchEvent(shiftTabEvt);
    expect(document.activeElement).toBe(mailLink);

    // Tab from last element (mailto link) wraps directly back to #btn-pin-unlock-reset
    const forwardTabEvt = new KeyboardEvent("keydown", {
      key: "Tab",
      shiftKey: false,
      bubbles: true,
      cancelable: true,
    });
    modal.dispatchEvent(forwardTabEvt);
    expect(document.activeElement).toBe(resetBtn);

    // Request PIN reset while unauthenticated -> requires OIDC identity verification
    requestPINReset();
    await settle();
    expect(state.pinUnlock.confirmingReset).toBe(true);
    expect(document.getElementById("btn-pin-reauth-oidc")).not.toBeNull();

    cancelPINReset();
    await settle();
    expect(state.pinUnlock.confirmingReset).toBe(false);

    // Re-open reset and simulate OIDC sign-in verification completing
    requestPINReset();
    await settle();
    state.session.isOfflineMode = false;
    sessionStorage.setItem("mock_user_id", userId);
    sessionStorage.setItem("mock_token", "jwt_reverified_oidc_token"); // pragma: allowlist secret
    await reauthenticateForPINReset();
    await settle();

    // PIN reset closed unlock modal, opened PinSetupModal, cleared drafts, and flagged prior entry without PII
    expect(state.pinUnlock.isOpen).toBe(false);
    expect(state.pinSetup.isOpen).toBe(true);
    expect(await getDraft("assign_01")).toBeNull();
    expect(await getQueuedSubmissions()).toEqual([]);

    // Complete new PIN setup and verify new master key differs from original & flagged entry exposes zero clinical PII
    state.pinSetup.pin = "135790";
    state.pinSetup.confirmPin = "135790";
    await handlePINSetupSubmit();
    await settle();

    const newMasterKey = getInMemorySessionKey();
    expect(newMasterKey).toBeInstanceOf(Uint8Array);
    expect(Array.from(newMasterKey)).not.toEqual(Array.from(originalMasterKey));

    const postResetHistory = await getAllSubmissions();
    expect(postResetHistory).toHaveLength(1);
    expect(postResetHistory[0].status).toBe("DECRYPTION_ERROR");
    expect(postResetHistory[0].unrecoverable).toBe(true);
    expect(postResetHistory[0].answers).toBeUndefined();
    expect(postResetHistory[0].subject_id).toBeUndefined();
    expect(postResetHistory[0].username).toBeUndefined();

    // Inspect raw IndexedDB record to verify ciphertext/PII was scrubbed at rest too
    const db = await openDatabase(userId);
    const rawStored = await new Promise((resolve) => {
      const tx = db.transaction("submissions", "readonly");
      const req = tx.objectStore("submissions").getAll();
      req.onsuccess = () => resolve(req.result);
    });
    expect(rawStored[0].answers).toBeUndefined();
    expect(rawStored[0].subject_id).toBeUndefined();
    expect(JSON.stringify(rawStored[0])).not.toContain("132");
  });

  it("Req 7 & PII Guardrails: renders offline-accessible coordinator phone and email links in participant card, sync drawer, PIN dialog, and inbox while masking PII", async () => {
    // Seed custom site coordinator contact while online, then switch offline
    hydrateSiteSupportContact({
      siteName: "Memorial Trial Site 04",
      phoneDisplay: "+1 (800) 555-0199", // deid-ignore
      phoneHref: "tel:+18005550199", // deid-ignore
      emailDisplay: "coordinator@cadence-clinical.com", // deid-ignore
      emailHref: "mailto:coordinator@cadence-clinical.com", // deid-ignore
    });
    expect(localStorage.getItem(SITE_CONTACT_STORAGE_KEY)).toContain(
      "Memorial Trial Site 04"
    );

    // Offline session with email-shaped userId to test PII masking + offline contact hydration
    state.siteSupportContact = null;
    state.session.userId = "participant.jane@example.org"; // deid-ignore
    state.session.token = null;
    state.session.isOfflineMode = true;
    localStorage.setItem("oidc_user_id", "participant.jane@example.org"); // deid-ignore

    await initializeApp();
    state.pinUnlock.isOpen = true;
    state.isSyncDrawerOpen = true;
    showView("view-inbox");
    renderInbox();
    await settle();

    expect(getSiteSupportContact().siteName).toBe("Memorial Trial Site 04");

    // Verify ParticipantCard masks email PII and exposes tel + mailto
    const subjectIdEl = document.getElementById("session-subject-id");
    expect(subjectIdEl.textContent).toBe("p***@example.org");
    expect(subjectIdEl.textContent).not.toContain("participant.jane");

    const surfaces = [
      "participant-site-contacts",
      "sync-drawer-site-contacts",
      "pin-unlock-site-contacts",
      "inbox-site-contacts",
    ];

    for (const surfaceId of surfaces) {
      const container = document.getElementById(surfaceId);
      expect(container, `Missing surface ${surfaceId}`).not.toBeNull();
      const tel = container.querySelector('a[href^="tel:"]');
      const mail = container.querySelector('a[href^="mailto:"]');
      expect(tel, `Missing tel link in ${surfaceId}`).not.toBeNull();
      expect(mail, `Missing mailto link in ${surfaceId}`).not.toBeNull();
    }

    // Verify helper sanitizers strip raw JSON and PII
    expect(
      maskParticipantIdentifier("Contact pat.smith@example.com or 123-45-6789") // deid-ignore
    ).toBe("Contact p***@example.com or ***-**-6789");
    expect(
      formatAnswersSummary({
        vssbp: 118,
        email: "pat.smith@example.com", // deid-ignore
        full_name: "Pat Smith",
      })
    ).toBe(
      "Systolic Blood Pressure (mmHg): 118, Email: [Protected], Full Name: [Protected]"
    );
    expect(
      humanizeValidationError(
        'Local schema quarantine: Pain score must be <= 10 {"raw":"json"}'
      )
    ).toBe("Pain score must be <= 10");
  });

  it("Pre-transmission USDM schema quarantine isolates invalid queued items while syncing valid siblings", async () => {
    const rawMaterial = new Uint8Array(32);
    for (let i = 0; i < 32; i++) rawMaterial[i] = i + 21;
    await initSessionKey(rawMaterial, "subject_001");

    await queueSubmission({
      subject_id: "subject_001",
      diary_id: "inst_daily_diary",
      assignment_id: "assign_01",
      answers: { age: 34, gender: "F", pain_score: 4 },
      change_reason: "Valid entry",
      username: "subject_001",
    });
    const valid2 = await queueSubmission({
      subject_id: "subject_001",
      diary_id: "inst_weekly_symptoms",
      assignment_id: "assign_02",
      answers: { age: 34, gender: "F", pain_score: 2 },
      change_reason: "Second valid entry",
      username: "subject_001",
    });

    // Simulate legacy/corrupted queued entry stored offline before a tighter USDM rule
    const { encryptAESGCM } = await import("ui");
    const db = await openDatabase("subject_001");
    const badEncAnswers = await encryptAESGCM(
      { age: 12, gender: "F", pain_score: 99 },
      getInMemorySessionKey()
    );
    const badEncSubj = await encryptAESGCM(
      "subject_001",
      getInMemorySessionKey()
    );
    await new Promise((resolve) => {
      const tx = db.transaction("submissions", "readwrite");
      tx.objectStore("submissions").put({
        sequence_number: 3,
        client_id: valid2.client_id,
        subject_id: badEncSubj,
        diary_id: "inst_daily_diary",
        version_index: 1,
        assignment_id: "assign_03",
        device_timestamp: new Date().toISOString(),
        answers: badEncAnswers,
        change_reason: "Invalid legacy entry",
        username: badEncSubj,
        status: "QUEUED",
        resolved_answers: null,
        resolved_at: null,
        error: null,
      });
      tx.oncomplete = resolve;
    });

    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({
        results: [
          {
            status: "CREATED",
            sequence_number: 1,
            client_id: valid2.client_id,
          },
          {
            status: "CREATED",
            sequence_number: 2,
            client_id: valid2.client_id,
          },
        ],
      }),
    });

    await syncOfflineQueue(false);
    await renderSyncQueueList();

    const all = await getAllSubmissions();
    expect(all.filter((s) => s.status === "CREATED")).toHaveLength(2);
    const localQuar = all.find((s) => s.sequence_number === 3);
    expect(localQuar.status).toBe("LOCAL_QUARANTINED");
    expect(localQuar.error).not.toContain("Local schema quarantine:");
    expect(localQuar.error).toContain("between 18 and 110");
  });
});
