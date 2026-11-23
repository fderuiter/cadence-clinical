import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { nextTick } from "vue";
import {
  state,
  initializeApp,
  unmountApp,
  showView,
  loadConsentDetails,
  verifyBiometricLiveness,
  submitConsentAnswers,
  openSignatureModal,
  verifyAndSubmitSignature,
  openReconsentModal,
  submitReconsentSignature,
  startQuestionnaire,
  validateActiveQuestionnaire,
  syncOfflineQueue,
  handlePINSetupSubmit,
  handlePINUnlockSubmit,
  requestPINReset,
  cancelPINReset,
  confirmPINReset,
  extractReceiptOcr,
  submitExpenseClaim,
} from "../src/index.js";
import { getPortalRouter } from "../src/router/index.js";
import {
  clearInMemoryKey,
  clearSessionKey,
  getInMemorySessionKey,
  openDatabase,
  queueSubmission,
  getQueuedSubmissions,
  saveDraft,
  getDraft,
  setActiveUserId,
} from "../src/sync-queue.js";

const settle = () => new Promise((resolve) => setTimeout(resolve, 30));

async function clearUserStores(userId) {
  const db = await openDatabase(userId);
  await new Promise((resolve) => {
    const tx = db.transaction(
      ["submissions", "config", "instruments", "assignments", "drafts"],
      "readwrite"
    );
    tx.objectStore("submissions").clear();
    tx.objectStore("config").clear();
    tx.objectStore("instruments").clear();
    tx.objectStore("assignments").clear();
    tx.objectStore("drafts").clear();
    tx.oncomplete = resolve;
  });
}

describe("Portal Availability Guards and PIN Security", () => {
  beforeEach(async () => {
    window.__MOCK_TEST_ENV__ = true;
    localStorage.clear();
    sessionStorage.clear();
    clearInMemoryKey();
    await clearSessionKey("subject_guard_01");
    await clearSessionKey("subject_guard_02");
    await clearSessionKey();
    await clearUserStores("subject_guard_01");
    await clearUserStores("subject_guard_02");
    await clearUserStores(null);

    unmountApp();
    document.body.innerHTML = `<div id="app"></div>`;

    state.session.userId = null;
    state.session.token = null;
    state.session.isOfflineMode = false;
    state.session.isDemoMode = false;
    state.session.authError = "";
    state.pinSetup = {
      isOpen: false,
      pin: "",
      confirmPin: "",
      error: "",
      loading: false,
    };
    state.pinUnlock = {
      isOpen: false,
      pin: "",
      error: "",
      loading: false,
      confirmingReset: false,
    };
    state.assignments = [];
    state.instruments = {};
    state.notifications = [];
    state.compliance = null;
    state.consentSigned = false;
    state.consentPassed = false;
    state.livenessVerified = false;
    state.consentLivenessToken = null;
    state.pendingReconsent = null;
    state.reconsentModalOpen = false;
    state.reconsentModalError = "";
    state.walletBalance = null;
    state.expenseError = null;
    state.expenseResult = null;
    state.expenseOcr = {
      loading: false,
      error: null,
      data: null,
      previewUrl: null,
    };
    state.currentView = "view-tasks";
  });

  afterEach(() => {
    unmountApp();
    vi.restoreAllMocks();
  });

  it("AC1 & AC2: prompts unauthenticated production users to sign in with zero synthetic tasks, balances, or overdue alerts", async () => {
    await initializeApp();
    await settle();

    expect(state.session.userId).toBeNull();
    expect(state.session.isDemoMode).toBe(false);
    expect(state.assignments).toEqual([]);
    expect(state.notifications).toEqual([]);
    expect(state.walletBalance).toBeNull();

    // No demo banner in production
    expect(document.getElementById("demo-mode-banner")).toBeNull();

    // Participant card shows truthful unauthenticated state and Sign In button, never static Active or $325.00
    const cardText = document.body.textContent || "";
    expect(
      document.getElementById("session-subject-id")?.textContent
    ).toContain("Not signed in");
    expect(
      document.getElementById("session-auth-status")?.textContent
    ).toContain("Unauthenticated");
    expect(document.getElementById("btn-login")).not.toBeNull();
    expect(cardText).not.toContain("$325.00");
    expect(cardText).not.toContain("4821");
    expect(cardText).not.toContain("Daily Health & Vital Diary");
    expect(cardText).not.toContain("OVERDUE");

    // Tasks view prompts user to sign in
    const signInPrompt = document.getElementById("tasks-signin-prompt");
    expect(signInPrompt).not.toBeNull();
    expect(signInPrompt.textContent).toContain(
      "Sign in to access your study tasks"
    );
    expect(document.getElementById("btn-tasks-login")).not.toBeNull();
  });

  it("AC1: displays a persistent Demo Mode banner labeling synthetic study data in explicit demo mode", async () => {
    state.session.isDemoMode = true;
    await initializeApp();
    await settle();

    const demoBanner = document.getElementById("demo-mode-banner");
    expect(demoBanner).not.toBeNull();
    expect(demoBanner.textContent).toContain("Demo Mode");
    expect(demoBanner.textContent).toContain("synthetic study data");
    expect(
      document.getElementById("session-auth-status")?.textContent
    ).toContain("Demo Mode");
  });

  it("AC3: hides unavailable navigation tabs outside demo builds and shows an unavailable notice with return action on direct URL access", async () => {
    await initializeApp();
    const router = getPortalRouter();
    await router.isReady();
    await settle();

    // Wallet and Trial Results tabs are hidden outside demo mode
    expect(document.getElementById("tab-btn-wallet")).toBeNull();
    expect(document.getElementById("tab-btn-lay-summaries")).toBeNull();
    expect(document.getElementById("tab-btn-tasks")).not.toBeNull();
    expect(document.getElementById("tab-btn-consent")).not.toBeNull();

    // Direct navigation to /wallet redirects to /unavailable with clear explanation and return button
    await router.push("/wallet");
    await settle();
    expect(router.currentRoute.value.path).toBe("/unavailable");
    expect(state.currentView).toBe("view-unavailable");

    const unavailableSection = document.getElementById("view-unavailable");
    expect(unavailableSection).not.toBeNull();
    expect(unavailableSection.classList.contains("active")).toBe(true);
    expect(
      document.getElementById("unavailable-view-heading")?.textContent
    ).toContain("Participant Financial Wallet isn't available yet");

    // Return button brings user back to My Tasks
    const returnBtn = document.getElementById("btn-unavailable-return-tasks");
    expect(returnBtn).not.toBeNull();
    returnBtn.click();
    await settle();
    expect(state.currentView).toBe("view-tasks");
    expect(router.currentRoute.value.path).toBe("/");

    // Direct navigation to /lay-summaries also shows unavailable notice
    await router.push("/lay-summaries");
    await settle();
    expect(router.currentRoute.value.path).toBe("/unavailable");
    expect(
      document.getElementById("unavailable-view-heading")?.textContent
    ).toContain("Plain-Language Clinical Trial Results isn't available yet");

    // LaySummariesView and WalletView contain zero dead receipt/disburse buttons or fake card digits
    expect(document.body.innerHTML).not.toContain("4821");
    expect(document.body.innerHTML).not.toContain("Disburse Available Balance");
    expect(document.body.innerHTML).not.toContain(
      "View Cryptographically Sealed Confirmation Receipt"
    );
  });

  it("Req 3: refuses wallet OCR and expense submission outside demo mode with user-visible error banner", async () => {
    await initializeApp();
    await settle();

    await expect(extractReceiptOcr("sample")).rejects.toThrow(
      /not available in this environment/i
    );
    expect(state.expenseError).toMatch(/not available in this environment/i);

    await nextTick();
    const errorBanner = document.getElementById("wallet-error-banner");
    expect(errorBanner).not.toBeNull();
    expect(errorBanner.textContent).toContain(
      "not available in this environment"
    );

    await expect(submitExpenseClaim()).rejects.toThrow(
      /not available in this environment/i
    );
  });

  it("AC4: blocks eConsent and re-consent submissions when signature verification or persistence is unavailable and leaves consent state unchanged", async () => {
    await initializeApp();
    await settle();

    // 1. Unauthenticated / non-demo consent signing is refused
    await loadConsentDetails();
    expect(state.consentContent).toBeNull();
    expect(document.getElementById("consent-error-msg")?.textContent).toContain(
      "Please sign in"
    );

    await verifyBiometricLiveness(false);
    expect(state.livenessVerified).toBe(false);
    expect(state.consentLivenessToken).toBeNull();

    openSignatureModal("consent");
    document.getElementById("sign-username").value = "subject_guard_01";
    document.getElementById("sign-password").value = "secret-pass"; // pragma: allowlist secret
    await verifyAndSubmitSignature();

    expect(state.consentSigned).toBe(false);
    expect(
      document.getElementById("modal-error-banner")?.textContent
    ).toContain("Nothing was saved");

    // 2. Authenticated consent signing sends gateway-compliant step-up payload and refuses if persistence fails
    state.session.userId = "subject_guard_01";
    state.session.token = "live_jwt_token"; // pragma: allowlist secret
    state.consentLivenessToken = "live_liveness_token";

    const fetchCalls = [];
    globalThis.fetch = vi.fn().mockImplementation(async (url, opts) => {
      fetchCalls.push({ url: String(url), opts });
      if (String(url).includes("/api/v1/auth/signature-verification")) {
        return {
          ok: true,
          json: async () => ({ sig_token: "step_up_sig_token_123" }),
        };
      }
      // Step 2 persistence fails (503 Service Unavailable)
      return {
        ok: false,
        status: 503,
        text: async () => "Consent persistence service unavailable",
      };
    });

    openSignatureModal("consent");
    document.getElementById("sign-username").value = "subject_guard_01";
    document.getElementById("sign-password").value = "secret-pass"; // pragma: allowlist secret
    await verifyAndSubmitSignature();

    expect(fetchCalls.length).toBe(2);
    const stepUpBody = JSON.parse(fetchCalls[0].opts.body);
    expect(stepUpBody.action).toBe(
      "/api/v1/econsent/templates/template-icf/versions/1/capture-consent"
    );
    expect(stepUpBody.semantic_action).toBe("econsent.consent.capture");
    expect(state.consentSigned).toBe(false);
    expect(
      document.getElementById("modal-error-banner")?.textContent
    ).toContain("Signature Verification Rejected");

    // 3. Re-consent refuses when step-up or persistence fails and preserves pendingReconsent
    openReconsentModal();
    const originalPending = state.pendingReconsent;
    state.reconsentForm.username = "subject_guard_01";
    state.reconsentForm.password = "secret-pass"; // pragma: allowlist secret

    globalThis.fetch = vi.fn().mockImplementation(async (url) => {
      if (String(url).includes("/api/v1/auth/signature-verification")) {
        return {
          ok: true,
          json: async () => ({}), // Missing sig_token!
        };
      }
      return { ok: true, json: async () => ({ status: "SIGNED" }) };
    });

    await submitReconsentSignature();
    expect(state.reconsentForm.password).toBe("");
    expect(state.pendingReconsent).toEqual(originalPending);
    expect(state.consentSigned).toBe(false);
    expect(state.reconsentModalError).toContain("Nothing was saved");
  });

  it("Req 5: keeps offline symptom diaries in PENDING_SYNC until server sync confirms persistence", async () => {
    state.session.isDemoMode = true;
    state.session.isOfflineMode = true;
    await initializeApp();
    await settle();

    // Setup PIN so offline submission is encrypted
    state.pinSetup.isOpen = true;
    state.pinSetup.pin = "246810";
    state.pinSetup.confirmPin = "246810";
    await handlePINSetupSubmit();

    await startQuestionnaire("assign_01");
    document.getElementById("vssbp").value = "122";
    document.getElementById("vsdpb").value = "78";
    document.getElementById("vshr").value = "68";
    document.getElementById("has_symptoms_option_1").checked = true;
    const painSlider = document.getElementById("pain_scale");
    painSlider.value = "2";
    painSlider.dispatchEvent(new Event("input", { bubbles: true }));
    expect(validateActiveQuestionnaire()).toBe(true);
    openSignatureModal("epro");

    document.getElementById("sign-username").value = "subject_001";
    document.getElementById("sign-password").value = "246810";
    await verifyAndSubmitSignature();

    const assign = state.assignments.find((a) => a.id === "assign_01");
    expect(assign.status).toBe("PENDING_SYNC");
    expect(
      document.getElementById("tasks-list-container")?.innerHTML
    ).toContain("Pending Sync");

    // Failed sync keeps assignment in PENDING_SYNC
    globalThis.fetch = vi
      .fn()
      .mockRejectedValue(new Error("Gateway unreachable"));
    state.session.isOfflineMode = false;
    await syncOfflineQueue(false, { noRetry: true });
    expect(state.assignments.find((a) => a.id === "assign_01").status).toBe(
      "PENDING_SYNC"
    );

    // Confirmed server sync promotes assignment to COMPLETED
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({
        status: "success",
        results: [
          {
            status: "CREATED",
            id: "srv_sub_01",
            diary_id: "inst_daily_diary",
            answers: { vssbp: "122" },
          },
        ],
      }),
    });
    await syncOfflineQueue();
    expect(state.assignments.find((a) => a.id === "assign_01").status).toBe(
      "COMPLETED"
    );
  });

  it("AC5 & AC6: keeps PIN keys strictly in memory across reload and credential refresh, and supports Sign Out and confirmed Reset PIN in PinUnlockModal", async () => {
    const userId = "subject_guard_01";
    localStorage.setItem("oidc_user_id", userId);
    sessionStorage.setItem("mock_user_id", userId);
    sessionStorage.setItem("mock_token", "jwt_initial_token"); // pragma: allowlist secret

    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => [],
    });

    await initializeApp();
    await settle();

    // First-time authenticated session opens PinSetupModal
    expect(state.pinSetup.isOpen).toBe(true);
    state.pinSetup.pin = "654321";
    state.pinSetup.confirmPin = "654321";
    await handlePINSetupSubmit();
    expect(state.pinSetup.isOpen).toBe(false);
    expect(getInMemorySessionKey()).toBeInstanceOf(Uint8Array);

    // Queue an encrypted offline submission and draft
    await saveDraft("assign_pin_01", { vssbp: "118" });
    await queueSubmission({
      subject_id: userId,
      diary_id: "inst_daily_diary",
      assignment_id: "assign_pin_01",
      answers: { vssbp: "118", vsdpb: "76", vshr: "64", has_symptoms: "No" },
      change_reason: "Offline PIN test",
      username: userId,
    });

    // Simulate credential refresh / identity re-evaluation: wipe memory key and re-check PIN wrapper
    clearInMemoryKey();
    sessionStorage.setItem("mock_token", "jwt_refreshed_token"); // pragma: allowlist secret
    await initializeApp();
    await settle();

    // Must prompt for PIN rather than overwriting with HKDF key
    expect(getInMemorySessionKey()).toBeNull();
    expect(state.pinUnlock.isOpen).toBe(true);

    const unlockModal = document.getElementById("portal-pin-unlock-modal");
    expect(unlockModal).not.toBeNull();
    const signOutBtn = document.getElementById("btn-pin-unlock-signout");
    const resetBtn = document.getElementById("btn-pin-unlock-reset");
    expect(signOutBtn).not.toBeNull();
    expect(resetBtn).not.toBeNull();

    // Wrong PIN fails cleanly and keeps encrypted queued diary intact
    state.pinUnlock.pin = "000000";
    await handlePINUnlockSubmit();
    expect(state.pinUnlock.isOpen).toBe(true);
    expect(state.pinUnlock.error).toContain("Incorrect security PIN");

    // Valid PIN unlocks queued diary cleanly
    state.pinUnlock.pin = "654321";
    await handlePINUnlockSubmit();
    expect(state.pinUnlock.isOpen).toBe(false);
    const unlockedQueue = await getQueuedSubmissions();
    expect(unlockedQueue).toHaveLength(1);
    expect(unlockedQueue[0].answers.vssbp).toBe("118");

    // Lock again and verify Reset PIN requires explicit confirmation warning before clearing locked local entries
    clearInMemoryKey();
    state.pinUnlock.isOpen = true;
    await nextTick();

    requestPINReset();
    await nextTick();
    expect(state.pinUnlock.confirmingReset).toBe(true);
    expect(document.getElementById("pin-reset-warning")?.textContent).toContain(
      "permanently clear"
    );

    cancelPINReset();
    expect(state.pinUnlock.confirmingReset).toBe(false);
    // Data still intact after cancel
    setActiveUserId(userId);
    expect((await getQueuedSubmissions()).length).toBe(1);

    requestPINReset();
    await confirmPINReset();
    await nextTick();

    // Locked local entries and drafts cleared, and PinSetupModal opened to set a new PIN
    expect(state.pinUnlock.isOpen).toBe(false);
    expect(state.pinSetup.isOpen).toBe(true);
    expect(await getQueuedSubmissions()).toEqual([]);
    expect(await getDraft("assign_pin_01")).toBeNull();

    // Also verify Sign Out option from PinUnlockModal clears session cleanly
    state.pinSetup.isOpen = false;
    state.pinUnlock.isOpen = true;
    await nextTick();
    document.getElementById("btn-pin-unlock-signout").click();
    await settle();
    expect(state.session.userId).toBeNull();
    expect(state.pinUnlock.isOpen).toBe(false);
  });
});
