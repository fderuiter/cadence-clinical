import "fake-indexeddb/auto";
import fs from "fs";
import os from "os";
import path from "path";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createApp, nextTick } from "vue";
import { build as viteBuild } from "vite";

import ParticipantCard from "../src/components/ParticipantCard.vue";
import PinSetupModal from "../src/components/PinSetupModal.vue";
import PinUnlockModal from "../src/components/PinUnlockModal.vue";
import ReconsentModal from "../src/components/ReconsentModal.vue";
import SignatureModal from "../src/components/SignatureModal.vue";
import {
  checkPendingReconsent,
  clearAllSubmissions,
  closeReconsentModal,
  closeSignatureModal,
  expireConsentLiveness,
  handlePINSetupSubmit,
  handlePINUnlockSubmit,
  initializeApp,
  isConsentLivenessExpired,
  logout,
  openReconsentModal,
  openSignatureModal,
  prepareSignOutGuardState,
  renderTasks,
  saveActiveDraftProgress,
  saveAndLeaveQuestionnaire,
  startQuestionnaire,
  state,
  submitReconsentSignature,
  syncOfflineQueue,
  unmountApp,
  verifyAndSubmitSignature,
  verifyBiometricLiveness,
} from "../src/index.js";
import { requestQuestionnaireSignature } from "../src/features/questionnaire.js";
import {
  getPortalRouter,
  resetPortalRouterForTests,
} from "../src/router/index.js";
import { pinia } from "../src/stores/pinia.js";
import {
  clearInMemoryKey,
  clearLockedOfflineData,
  clearSessionKey,
  getAllSubmissions,
  getDraft,
  initSessionKey,
  openDatabase,
  queueSubmission,
  saveDraft,
  setActiveUserId,
  setTrackedLocalQueueActivity,
} from "../src/sync-queue.js";

function mountComponent(Component) {
  const host = document.createElement("div");
  document.body.appendChild(host);
  const app = createApp(Component);
  app.use(pinia);
  app.mount(host);
  return {
    host,
    query: (selector) => host.querySelector(selector),
    queryAll: (selector) => Array.from(host.querySelectorAll(selector)),
    unmount() {
      app.unmount();
      host.remove();
    },
  };
}

const settle = async () => {
  await nextTick();
  await new Promise((resolve) => setTimeout(resolve, 20));
  await nextTick();
};

describe("Harden Offline Shell, PIN Sync, Guarded Sign-Out, Split Signatures, Re-Consent & Liveness Expiry", () => {
  const originalFetch = globalThis.fetch;
  const originalOnLineDescriptor = Object.getOwnPropertyDescriptor(
    window.navigator,
    "onLine"
  );

  beforeEach(async () => {
    resetPortalRouterForTests();
    window.__MOCK_TEST_ENV__ = true;
    localStorage.clear();
    sessionStorage.clear();
    clearInMemoryKey();
    setTrackedLocalQueueActivity(false);

    for (const uid of ["SUBJ-101-001", "subj_auth_01", "subject_001", null]) {
      await clearSessionKey(uid);
      await clearLockedOfflineData(uid);
    }

    state.session.userId = "subj_auth_01";
    state.session.studyId = "CADENCE-101";
    state.session.siteId = "SITE-101";
    state.session.token = null;
    state.session.isDemoMode = false;
    state.session.isOfflineMode = false;
    state.session.sessionExpired = false;

    state.pinSetup.isOpen = false;
    state.pinSetup.pin = "";
    state.pinSetup.confirmPin = "";
    state.pinSetup.error = "";

    state.pinUnlock.isOpen = false;
    state.pinUnlock.pin = "";
    state.pinUnlock.error = "";
    state.pinUnlock.confirmingReset = false;
    state.pinUnlock.failedAttempts = 0;
    state.pinUnlock.isLockedOut = false;
    state.pinUnlock.requiresReauth = false;

    state.activeQuestionnaire = null;
    state.draftPresence = {};
    state.assignments = [];
    state.submissions = [];
    state.notifications = [];
    state.instruments = {};
    state.pendingReconsent = null;
    state.reconsentModalOpen = false;
    state.reconsentModalError = "";
    state.reconsentComprehensionAnswers = {};
    state.signatureModalOpen = false;
    state.consentSigned = false;
    state.consentComprehensionPassed = false;
    state.consentPassed = false;
    state.livenessVerified = false;
    state.consentLivenessToken = null;
    state.consentLivenessExpiresAt = null;
    state.signOutGuardOpen = false;

    Object.defineProperty(window.navigator, "onLine", {
      value: true,
      configurable: true,
    });
  });

  afterEach(() => {
    unmountApp();
    globalThis.fetch = originalFetch;
    if (originalOnLineDescriptor) {
      Object.defineProperty(
        window.navigator,
        "onLine",
        originalOnLineDescriptor
      );
    }
    vi.restoreAllMocks();
    vi.useRealTimers();
    document.body.innerHTML = "";
  });

  it("AC1 (Req 1 & 2): injects hashed bundle assets into built sw.js, tolerates missing dev entrypoints during install, and serves cached index.html on cold offline navigation", async () => {
    const tmpOutDir = fs.mkdtempSync(
      path.join(os.tmpdir(), "subject-portal-sw-build-")
    );
    try {
      await viteBuild({
        root: path.resolve(__dirname, ".."),
        configFile: path.resolve(__dirname, "../vite.config.js"),
        logLevel: "silent",
        build: {
          outDir: tmpOutDir,
          emptyOutDir: true,
        },
      });

      const builtSwPath = path.join(tmpOutDir, "sw.js");
      expect(fs.existsSync(builtSwPath)).toBe(true);
      const builtSw = fs.readFileSync(builtSwPath, "utf8");

      const assetMatches = [
        ...builtSw.matchAll(/\$\{SW_BASE\}(assets\/[^"`']+\.(?:js|css))/g),
      ].map((m) => m[1]);
      expect(assetMatches.length).toBeGreaterThanOrEqual(2);
      for (const relAsset of assetMatches) {
        expect(fs.existsSync(path.join(tmpOutDir, relAsset))).toBe(true);
        expect(relAsset).not.toMatch(/\.map$/);
      }

      // Evaluate public/sw.js when /src/style.css and /src/index.js return HTTP 404
      const unbuiltSw = fs.readFileSync(
        path.resolve(__dirname, "../public/sw.js"),
        "utf8"
      );
      const listeners = {};
      const storedByUrl = new Map();
      const mockCache = {
        put: vi.fn(async (req, res) => {
          const urlKey = typeof req === "string" ? req : req.url;
          storedByUrl.set(urlKey, res);
        }),
        match: vi.fn(async (req) => {
          const urlKey = typeof req === "string" ? req : req.url;
          return storedByUrl.get(urlKey) || null;
        }),
        addAll: vi.fn(async (urls) => {
          if (urls.some((u) => u.includes("/src/"))) {
            throw new TypeError("Failed to fetch /subject-portal/src/index.js");
          }
          for (const url of urls) {
            storedByUrl.set(url, { status: 200, body: `cached:${url}` });
          }
        }),
      };
      const mockCaches = {
        open: vi.fn().mockResolvedValue(mockCache),
        keys: vi.fn().mockResolvedValue([]),
        delete: vi.fn().mockResolvedValue(true),
        match: vi.fn(async (req) => {
          const urlKey = typeof req === "string" ? req : req.url;
          const pathname = urlKey.startsWith("http")
            ? new URL(urlKey).pathname
            : urlKey;
          return storedByUrl.get(pathname) || null;
        }),
      };
      const swSelf = {
        location: {
          origin: "http://localhost:5174",
          pathname: "/subject-portal/sw.js",
          search: "",
        },
        addEventListener: vi.fn((evt, cb) => {
          listeners[evt] = cb;
        }),
        skipWaiting: vi.fn(),
        clients: { claim: vi.fn() },
      };
      globalThis.fetch = vi.fn(async (url) => {
        if (String(url).includes("/src/")) {
          return { ok: false, status: 404 };
        }
        return {
          ok: true,
          status: 200,
          type: "basic",
          clone() {
            return this;
          },
        };
      });

      const runSW = new Function(
        "self",
        "caches",
        "Response",
        "Request",
        "setTimeout",
        "clearTimeout",
        unbuiltSw
      );
      runSW(
        swSelf,
        mockCaches,
        class MockResponse {
          constructor(body, opts = {}) {
            this.body = body;
            this.status = opts.status || 200;
          }
          clone() {
            return this;
          }
        },
        class MockRequest {},
        setTimeout,
        clearTimeout
      );

      let installPromise;
      listeners.install({
        waitUntil: (p) => {
          installPromise = p;
        },
      });
      await installPromise;

      expect(storedByUrl.has("/subject-portal/index.html")).toBe(true);
      expect(storedByUrl.has("/subject-portal/manifest.json")).toBe(true);

      // Cold offline navigation to deep route falls back to cached /subject-portal/index.html
      globalThis.fetch = vi
        .fn()
        .mockRejectedValue(new Error("Network offline"));
      let navResponsePromise;
      listeners.fetch({
        request: {
          method: "GET",
          mode: "navigate",
          url: "http://localhost:5174/subject-portal/tasks",
        },
        respondWith: (p) => {
          navResponsePromise = p;
        },
      });
      const navResponse = await navResponsePromise;
      expect(navResponse?.body).toBe("cached:/subject-portal/index.html");
    } finally {
      fs.rmSync(tmpOutDir, { recursive: true, force: true });
    }
  });

  it("AC2 & AC3 (Req 3 & 4): opens PIN unlock before startup sync, emits sync:locked with zero sync:success events while items remain locked, and enables authenticated autoDisburse on PIN unlock", async () => {
    const userId = "subj_auth_01";
    localStorage.setItem("oidc_user_id", userId);
    state.session.userId = userId;
    state.session.token = "valid-oidc-jwt"; // pragma: allowlist secret
    setActiveUserId(userId);

    // Establish wrapped PIN key and queue an encrypted diary submission
    state.pinSetup.isOpen = true;
    state.pinSetup.pin = "246810";
    state.pinSetup.confirmPin = "246810";
    await handlePINSetupSubmit();

    await queueSubmission({
      subject_id: userId,
      diary_id: "inst_daily_diary",
      assignment_id: "assign_locked_01",
      answers: { vssbp: "118", vsdpb: "76" },
      change_reason: "Completed offline before reload",
      username: userId,
    });

    // Simulate browser reload while online: clear volatile key
    clearInMemoryKey();

    const syncSuccessSpy = vi.fn();
    const syncLockedSpy = vi.fn();
    window.addEventListener("sync:success", syncSuccessSpy);
    window.addEventListener("sync:locked", syncLockedSpy);

    const callOrder = [];
    globalThis.fetch = vi.fn(async (url) => {
      const pathStr = String(url);
      if (pathStr.includes("epro/sync")) {
        callOrder.push("epro/sync");
        return {
          ok: true,
          status: 200,
          json: async () => ({ results: [{ status: "CREATED" }] }),
        };
      }
      if (pathStr.includes("payments/participant/me/diary-stipend")) {
        callOrder.push("diary-stipend");
        return {
          ok: true,
          status: 200,
          json: async () => ({ creditedAmount: 25, currentBalance: 25 }),
        };
      }
      return {
        ok: true,
        status: 200,
        json: async () => [],
      };
    });

    try {
      document.body.innerHTML = `<div id="app"></div>`;
      await initializeApp();
      await settle();

      // PIN unlock modal is opened before startup sync, and zero sync:success events fire while locked
      expect(state.pinUnlock.isOpen).toBe(true);
      expect(syncSuccessSpy).not.toHaveBeenCalled();
      expect(syncLockedSpy).toHaveBeenCalled();
      expect(state.syncStatusText).toMatch(/PIN|locked/i);
      expect(callOrder).not.toContain("epro/sync");

      // Unlocking the PIN in an authenticated session syncs queued items and triggers stipend autoDisburse
      state.pinUnlock.pin = "246810";
      await handlePINUnlockSubmit();
      await settle();

      expect(state.pinUnlock.isOpen).toBe(false);
      expect(callOrder).toContain("epro/sync");
      expect(callOrder).toContain("diary-stipend");
      expect(syncSuccessSpy).toHaveBeenCalledTimes(1);
    } finally {
      window.removeEventListener("sync:success", syncSuccessSpy);
      window.removeEventListener("sync:locked", syncLockedSpy);
    }
  });

  it("AC4 (Req 5 & 6): returns explicit boolean from saveDraft and preserves activeQuestionnaire without 'Draft saved' toast when persistence fails", async () => {
    const alertSpy = vi.spyOn(window, "alert").mockImplementation(() => {});
    clearInMemoryKey();

    // Missing session key -> saveDraft returns explicit false
    const missingKeyResult = await saveDraft("assign_fail_01", {
      vssbp: "124",
    });
    expect(missingKeyResult).toBe(false);

    // IndexedDB write error -> saveDraft returns explicit false
    await initSessionKey("valid-key-seed", "subj_auth_01");
    expect(await saveDraft("assign_ok_01", { vssbp: "120" })).toBe(true);

    clearInMemoryKey();
    document.body.innerHTML = `
      <div id="view-tasks"></div>
      <div id="view-questionnaire">
        <div id="questionnaire-form-error" style="display:none;"></div>
        <form id="questionnaire-form">
          <input id="vssbp" value="132" />
        </form>
      </div>
      <div id="portal-toast"></div>
    `;

    const assignment = {
      id: "assign_fail_01",
      instrument_id: "inst_01",
      status: "PENDING",
      has_draft: false,
    };
    state.assignments = [assignment];
    state.currentView = "view-questionnaire";
    state.activeQuestionnaire = {
      assignment,
      instrument: {
        id: "inst_01",
        items: { vssbp: { type: "number", label: "Systolic BP" } },
      },
      answers: { vssbp: "132" },
    };

    const persisted = await saveAndLeaveQuestionnaire();
    expect(persisted).toBe(false);
    expect(state.activeQuestionnaire).not.toBeNull();
    expect(state.activeQuestionnaire?.answers).toEqual({ vssbp: "132" });
    expect(assignment.has_draft).toBe(false);
    expect(state.draftPresence["assign_fail_01"]).toBeFalsy();
    expect(state.currentView).toBe("view-questionnaire");

    const errorBanner = document.getElementById("questionnaire-form-error");
    expect(errorBanner?.style.display).toBe("block");
    expect(errorBanner?.getAttribute("role")).toBe("alert");
    expect(errorBanner?.textContent).toMatch(
      /could not save your (?:survey )?draft/i
    );

    const toastText =
      document.getElementById("portal-toast")?.textContent || "";
    expect(toastText).not.toMatch(/Draft saved/i);
    expect(alertSpy).not.toHaveBeenCalled();
  });

  it("AC5 (Req 7 & 8): opens SignOutConfirmModal on all four sign-out buttons and guards direct logout() when drafts or queued/locked submissions exist", async () => {
    const userId = "subj_auth_01";
    state.session.userId = userId;
    state.session.token = "valid-token"; // pragma: allowlist secret
    setActiveUserId(userId);
    await initSessionKey("guard-pin-seed", userId);

    await queueSubmission({
      subject_id: userId,
      diary_id: "inst_daily_diary",
      assignment_id: "assign_guard_01",
      answers: { vssbp: "120" },
      change_reason: "Queued offline",
      username: userId,
    });
    // Lock item to verify DECRYPTION_ERROR records are also guarded
    clearInMemoryKey();
    await prepareSignOutGuardState();

    // Direct unconfirmed logout() refuses to wipe local session while locked/queued items exist
    const unconfirmedResult = await logout();
    expect(unconfirmedResult).toEqual({ signedOut: false, guarded: true });
    expect(state.session.userId).toBe(userId);

    // 1. ParticipantCard #btn-logout
    const card = mountComponent(ParticipantCard);
    await settle();
    card.query("#btn-logout").click();
    await vi.waitFor(() => {
      expect(
        card.query('[data-testid="signout-confirm-modal"]')
      ).not.toBeNull();
    });
    card.query('[data-testid="signout-stay-btn"]').click();
    await settle();
    expect(state.session.userId).toBe(userId);
    card.unmount();

    // 2. PinSetupModal #btn-pin-setup-signout
    state.pinSetup.isOpen = true;
    const pinSetup = mountComponent(PinSetupModal);
    await settle();
    pinSetup.query("#btn-pin-setup-signout").click();
    await vi.waitFor(() => {
      expect(
        pinSetup.query('[data-testid="signout-confirm-modal"]')
      ).not.toBeNull();
    });
    pinSetup.query('[data-testid="signout-stay-btn"]').click();
    await settle();
    expect(state.session.userId).toBe(userId);
    state.pinSetup.isOpen = false;
    pinSetup.unmount();

    // 3. PinUnlockModal #btn-pin-unlock-signout (standard unlock view)
    state.pinUnlock.isOpen = true;
    state.pinUnlock.requiresReauth = false;
    const pinUnlock = mountComponent(PinUnlockModal);
    await settle();
    pinUnlock.query("#btn-pin-unlock-signout").click();
    await vi.waitFor(() => {
      expect(
        pinUnlock.query('[data-testid="signout-confirm-modal"]')
      ).not.toBeNull();
    });
    pinUnlock.query('[data-testid="signout-stay-btn"]').click();
    await settle();
    expect(state.session.userId).toBe(userId);

    // 4. PinUnlockModal #btn-pin-reset-signout (reset confirmation view)
    state.pinUnlock.confirmingReset = true;
    await settle();
    pinUnlock.query("#btn-pin-reset-signout").click();
    await vi.waitFor(() => {
      expect(
        pinUnlock.query('[data-testid="signout-confirm-modal"]')
      ).not.toBeNull();
    });
    pinUnlock.query('[data-testid="signout-confirm-btn"]').click();
    await vi.waitFor(() => {
      expect(state.session.userId).toBeNull();
    });
    pinUnlock.unmount();
  });

  it("AC3 & AC6 (Req 4, 9 & 10): separates online Account Password from offline Security PIN, enforces username match, verifies online passwords & offline PINs, and wipes credentials", async () => {
    const userId = "subj_auth_01";
    state.session.userId = userId;
    state.session.token = "valid-oidc-token"; // pragma: allowlist secret
    state.session.isOfflineMode = false;
    setActiveUserId(userId);

    // Setup wrapped PIN ("654321") for local encryption & offline PIN verification
    state.pinSetup.isOpen = true;
    state.pinSetup.pin = "654321";
    state.pinSetup.confirmPin = "654321";
    await handlePINSetupSubmit();

    const sigWrapper = mountComponent(SignatureModal);
    await settle();

    state.activeQuestionnaire = {
      assignment: { id: "assign_epro_01", status: "PENDING" },
      instrument: {
        id: "inst_daily_diary",
        items: { vssbp: { type: "number", label: "Systolic BP" } },
      },
      answers: { vssbp: "120" },
    };
    state.assignments = [state.activeQuestionnaire.assignment];

    // Online authenticated ePRO -> Account Password (text inputmode)
    openSignatureModal("epro");
    await settle();
    expect(
      sigWrapper.query('label[for="sign-password"]')?.textContent
    ).toContain("Account Password");
    expect(
      sigWrapper.query('label[for="sign-password"]')?.textContent
    ).not.toContain("Security PIN");
    expect(sigWrapper.query("#sign-password")?.getAttribute("inputmode")).toBe(
      "text"
    );

    // Reject mismatched username and wipe password immediately
    sigWrapper.query("#sign-username").value = "other_subject";
    sigWrapper.query("#sign-password").value = "WrongUserSecret!"; // pragma: allowlist secret
    await verifyAndSubmitSignature();
    expect(sigWrapper.query("#sign-password").value).toBe("");
    expect(sigWrapper.query("#modal-error-banner")?.textContent).toMatch(
      /must match the signed-in participant/i
    );

    // Reject invalid online Account Password via /api/v1/auth/signature-verification
    globalThis.fetch = vi.fn(async (url) => {
      if (String(url).includes("api/v1/auth/signature-verification")) {
        return {
          ok: false,
          status: 401,
          json: async () => ({ detail: "Invalid account credentials" }),
        };
      }
      return { ok: true, status: 200, json: async () => ({}) };
    });
    sigWrapper.query("#sign-username").value = userId;
    sigWrapper.query("#sign-password").value = "WrongPassword!"; // pragma: allowlist secret
    await verifyAndSubmitSignature();
    expect(sigWrapper.query("#sign-password").value).toBe("");
    expect(state.signatureModalOpen).toBe(true);
    expect(sigWrapper.query("#modal-error-banner")?.textContent).toMatch(
      /Invalid account credentials|Signature Verification Failed/i
    );

    // Valid online Account Password -> verifies step-up token, queues & syncs with autoDisburse
    const onlineEndpoints = [];
    globalThis.fetch = vi.fn(async (url, init) => {
      const urlStr = String(url);
      onlineEndpoints.push({ url: urlStr, body: init?.body });
      if (urlStr.includes("api/v1/auth/signature-verification")) {
        return {
          ok: true,
          status: 200,
          json: async () => ({ sig_token: "epro-sig-jwt-01" }),
        };
      }
      if (urlStr.includes("epro/sync")) {
        return {
          ok: true,
          status: 200,
          json: async () => ({ results: [{ status: "CREATED" }] }),
        };
      }
      if (urlStr.includes("payments/participant/me/diary-stipend")) {
        return {
          ok: true,
          status: 200,
          json: async () => ({ creditedAmount: 15, currentBalance: 15 }),
        };
      }
      return { ok: true, status: 200, json: async () => [] };
    });

    sigWrapper.query("#sign-username").value = userId;
    sigWrapper.query("#sign-password").value = "CorrectOidcPassword!"; // pragma: allowlist secret
    await verifyAndSubmitSignature();
    await settle();

    expect(state.signatureModalOpen).toBe(false);
    const stepUpCall = onlineEndpoints.find((c) =>
      c.url.includes("api/v1/auth/signature-verification")
    );
    expect(stepUpCall).toBeDefined();
    expect(JSON.parse(stepUpCall.body)).toMatchObject({
      username: userId,
      action: "/api/v1/interop/epro/submit",
      semantic_action: "collection.sign",
    });
    expect(
      onlineEndpoints.some((c) =>
        c.url.includes("payments/participant/me/diary-stipend")
      )
    ).toBe(true);

    // Offline ePRO mode -> Security PIN (numeric inputmode) + PBKDF2 wrapped key verification
    state.session.isOfflineMode = true;
    Object.defineProperty(window.navigator, "onLine", {
      value: false,
      configurable: true,
    });
    state.activeQuestionnaire = {
      assignment: { id: "assign_epro_offline_02", status: "PENDING" },
      instrument: {
        id: "inst_daily_diary",
        items: { vssbp: { type: "number", label: "Systolic BP" } },
      },
      answers: { vssbp: "116" },
    };
    state.assignments = [state.activeQuestionnaire.assignment];

    openSignatureModal("epro");
    await settle();
    expect(
      sigWrapper.query('label[for="sign-password"]')?.textContent
    ).toContain("Security PIN");
    expect(
      sigWrapper.query('label[for="sign-password"]')?.textContent
    ).not.toContain("Account Password");
    expect(sigWrapper.query("#sign-password")?.getAttribute("inputmode")).toBe(
      "numeric"
    );

    // Wrong offline PIN is rejected and wiped
    sigWrapper.query("#sign-username").value = userId;
    sigWrapper.query("#sign-password").value = "000000";
    await verifyAndSubmitSignature();
    expect(sigWrapper.query("#sign-password").value).toBe("");
    expect(state.signatureModalOpen).toBe(true);
    expect(sigWrapper.query("#modal-error-banner")?.textContent).toMatch(
      /Incorrect Security PIN|Invalid Security PIN/i
    );

    // Correct offline PIN ("654321") signs and queues encrypted submission locally
    sigWrapper.query("#sign-username").value = userId;
    sigWrapper.query("#sign-password").value = "654321";
    await verifyAndSubmitSignature();
    await settle();
    expect(state.signatureModalOpen).toBe(false);
    const allSubs = await getAllSubmissions();
    expect(
      allSubs.some((s) => s.assignment_id === "assign_epro_offline_02")
    ).toBe(true);

    closeSignatureModal();
    sigWrapper.unmount();
  });

  it("AC6 & AC7 (Req 9, 11 & 12): renders redlined clause diffs and comprehension checks in ReconsentModal, keeps surveys & ePRO signing blocked on close while permitting mid-session draft saves, and unlocks upon passing checks & signing", async () => {
    const userId = "subj_auth_01";
    state.session.userId = userId;
    state.session.token = "valid-oidc-token"; // pragma: allowlist secret
    setActiveUserId(userId);
    await initSessionKey("reconsent-draft-seed", userId);

    document.body.innerHTML = `
      <div id="tasks-loading" style="display:none;"></div>
      <div id="tasks-failure" style="display:none;"></div>
      <div id="tasks-error-msg"></div>
      <div id="tasks-list-container"></div>
      <div id="questionnaire-form-error" style="display:none;"></div>
      <form id="questionnaire-form">
        <input id="vssbp" value="121" />
      </form>
      <div id="portal-toast"></div>
    `;

    state.instruments = {
      inst_01: {
        id: "inst_01",
        name: "Daily Symptom Diary",
        items: { vssbp: { type: "number", label: "Systolic BP" } },
      },
    };
    state.assignments = [
      {
        id: "assign_open_01",
        instrument_id: "inst_01",
        status: "PENDING",
        due_date: new Date(Date.now() + 86400000).toISOString(),
        has_draft: false,
      },
    ];

    // Participant already has a questionnaire open when re-consent arrives mid-session
    state.activeQuestionnaire = {
      assignment: state.assignments[0],
      instrument: state.instruments.inst_01,
      answers: { vssbp: "121" },
    };

    state.pendingReconsent = {
      id: "amend_v2_01",
      study_id: "CADENCE-101",
      protocol_version: "2.1",
      change_summary: "Added Week 8 remote blood pressure check.",
      clause_diffs: [
        {
          id: "clause_schedule",
          title: "Section 3: Study Visit Schedule",
          previous_text: "Participants complete 4 clinic visits over 6 weeks.",
          updated_text:
            "Participants complete 4 clinic visits and 1 remote blood pressure check at Week 8.",
        },
      ],
      comprehension_questions: [
        {
          id: "q_week8",
          text: "What was added in Protocol Version 2.1?",
          options: [
            "A Week 8 remote blood pressure check",
            "Daily inpatient hospital stays",
          ],
          correct_option: "A Week 8 remote blood pressure check",
        },
      ],
    };

    openReconsentModal();
    const reconsentWrapper = mountComponent(ReconsentModal);
    await settle();

    // Verify Account Password label (no Security PIN conflation) and differential redlines (<del> / <ins>)
    expect(
      reconsentWrapper.query('label[for="reconsent-sign-password"]')
        ?.textContent
    ).toContain("Account Password");
    expect(
      reconsentWrapper.query('label[for="reconsent-sign-password"]')
        ?.textContent
    ).not.toContain("Security PIN");
    expect(reconsentWrapper.host.querySelector("del")?.textContent).toContain(
      "4 clinic visits over 6 weeks"
    );
    expect(reconsentWrapper.host.querySelector("ins")?.textContent).toContain(
      "Week 8"
    );
    expect(
      reconsentWrapper.query("#reconsent-comprehension-section")
    ).not.toBeNull();

    // Closing ReconsentModal keeps pendingReconsent active and survey starts + ePRO signing blocked
    closeReconsentModal();
    await settle();
    expect(state.reconsentModalOpen).toBe(false);
    expect(state.pendingReconsent).not.toBeNull();

    renderTasks();
    const taskBtn = document.querySelector(".btn-start-task");
    expect(taskBtn?.hasAttribute("disabled")).toBe(true);
    expect(taskBtn?.textContent).toContain("Re-Consent Required");

    // Mid-session draft saving on the already-open questionnaire MUST still succeed
    const draftSavedMidSession = await saveActiveDraftProgress({
      showFeedback: true,
    });
    expect(draftSavedMidSession).toBe(true);
    expect(await getDraft("assign_open_01")).toEqual({ vssbp: "121" });

    // Attempting to sign the open questionnaire or start a new survey is blocked
    requestQuestionnaireSignature();
    expect(state.signatureModalOpen).toBe(false);

    state.activeQuestionnaire = null;
    await startQuestionnaire("assign_open_01");
    expect(state.activeQuestionnaire).toBeNull();

    // Direct router navigation to /questionnaire?assignmentId=... is also redirected while pendingReconsent is active
    state.session.isDemoMode = true;
    const portalRouter = getPortalRouter();
    await portalRouter.push({
      name: "questionnaire",
      query: { assignmentId: "assign_open_01" },
    });
    expect(portalRouter.currentRoute.value.name).toBe("tasks");
    expect(state.activeQuestionnaire).toBeNull();
    state.session.isDemoMode = false;

    // Re-open ReconsentModal: wrong comprehension answer blocks signing
    openReconsentModal();
    await settle();
    state.reconsentComprehensionAnswers = {
      q_week8: "Daily inpatient hospital stays",
    };
    state.reconsentForm.username = userId;
    state.reconsentForm.password = "ValidPassword123!"; // pragma: allowlist secret
    await submitReconsentSignature();
    expect(state.reconsentModalError).toMatch(
      /Comprehension check not passed/i
    );
    expect(state.pendingReconsent).not.toBeNull();

    // Correct comprehension answer + valid Account Password unlocks tasks
    globalThis.fetch = vi.fn(async (url) => {
      const urlStr = String(url);
      if (urlStr.includes("api/v1/auth/signature-verification")) {
        return {
          ok: true,
          status: 200,
          json: async () => ({ sig_token: "reconsent-sig-token" }),
        };
      }
      if (urlStr.includes("api/v1/execution/amendments/reconsent")) {
        return {
          ok: true,
          status: 200,
          json: async () => ({ persisted: true }),
        };
      }
      return { ok: true, status: 200, json: async () => ({ ok: true }) };
    });

    state.reconsentComprehensionAnswers = {
      q_week8: "A Week 8 remote blood pressure check",
    };
    state.reconsentForm.username = userId;
    state.reconsentForm.password = "ValidPassword123!"; // pragma: allowlist secret
    await submitReconsentSignature();
    await settle();

    expect(state.pendingReconsent).toBeNull();
    expect(state.reconsentModalOpen).toBe(false);
    const unlockedTaskBtn = document.querySelector(".btn-start-task");
    expect(unlockedTaskBtn?.hasAttribute("disabled")).toBe(false);

    reconsentWrapper.unmount();
  });

  it("AC8 (Req 13 & 14): persists consentLivenessExpiresAt, disables #btn-trigger-consent-sign and updates #liveness-status-banner on expiry, and blocks stale capture-consent submissions", async () => {
    vi.useFakeTimers();
    const baseNow = new Date("2026-11-23T16:00:00.000Z");
    vi.setSystemTime(baseNow);

    const userId = "subj_auth_01";
    state.session.userId = userId;
    state.session.token = "valid-oidc-token"; // pragma: allowlist secret
    state.consentComprehensionPassed = true;
    state.consentPassed = true;

    document.body.innerHTML = `
      <div id="consent-content-wrapper">
        <div id="consent-template-title"></div>
        <div id="consent-metadata-display"></div>
        <div id="consent-clauses-container"></div>
        <div id="consent-comprehension-card">
          <div id="consent-questions-container"></div>
        </div>
        <div id="consent-liveness-card">
          <div id="liveness-status-banner" class="banner-info">Pending verification</div>
          <button id="btn-verify-liveness" type="button">Verify Identity</button>
        </div>
        <div id="consent-signature-card">
          <p id="consent-sign-readiness-hint"></p>
          <button id="btn-trigger-consent-sign" type="button" disabled>Sign Consent Form</button>
        </div>
      </div>
      <div id="portal-sign-modal" style="display:none;">
        <h3 id="portal-modal-title"></h3>
        <div id="modal-error-banner" style="display:none;"></div>
        <select id="sign-reason">
          <option value="Initial Informed Consent">Initial Informed Consent</option>
        </select>
        <textarea id="sign-reason-custom"></textarea>
        <input id="sign-username" value="${userId}" />
        <input id="sign-password" value="ValidOidcPassword!" />
      </div>
      <div id="portal-toast"></div>
    `;
    state.consentContent = {
      template_name: "Informed Consent Form",
      study_id: "CADENCE-101",
      protocol_version: "1.0",
      version_index: 1,
      language_code: "en",
      clauses: [],
    };

    const expiresAtIso = new Date(
      baseNow.getTime() + 15 * 60 * 1000
    ).toISOString();
    globalThis.fetch = vi.fn(async (url) => {
      if (String(url).includes("verify-liveness")) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            status: "VERIFIED",
            is_live: true,
            liveness_token: "live_tok_15m",
            expires_at: expiresAtIso,
          }),
        };
      }
      return { ok: true, status: 200, json: async () => ({}) };
    });

    await verifyBiometricLiveness();
    const signBtn = document.getElementById("btn-trigger-consent-sign");
    const banner = document.getElementById("liveness-status-banner");
    const readinessHint = document.getElementById(
      "consent-sign-readiness-hint"
    );

    expect(state.consentLivenessToken).toBe("live_tok_15m");
    expect(state.consentLivenessExpiresAt).toBe(expiresAtIso);
    expect(isConsentLivenessExpired()).toBe(false);
    expect(signBtn.disabled).toBe(false);
    expect(readinessHint.textContent).toContain("All checks are complete");

    // Open signature modal right before expiry, then advance past 15 minutes
    openSignatureModal("consent");
    expect(state.signatureModalOpen).toBe(true);

    vi.advanceTimersByTime(15 * 60 * 1000 + 1000);
    await Promise.resolve();

    expect(signBtn.disabled).toBe(true);
    expect(banner.getAttribute("role")).toBe("alert");
    expect(banner.textContent).toMatch(/expired/i);
    expect(readinessHint.textContent).toContain("Step 2: Quick Camera Check");

    // Even if consentLivenessToken was still set when verifyAndSubmitSignature is invoked with an expired timestamp, capture-consent is never called
    state.consentLivenessToken = "stale_live_tok";
    state.consentLivenessExpiresAt = new Date(Date.now() - 5000).toISOString();
    document.getElementById("sign-username").value = userId;
    document.getElementById("sign-password").value = "ValidOidcPassword!"; // pragma: allowlist secret

    const captureSpy = vi.fn();
    globalThis.fetch = vi.fn(async (url) => {
      if (String(url).includes("capture-consent")) {
        captureSpy();
      }
      return {
        ok: true,
        status: 200,
        json: async () => ({ sig_token: "consent-sig" }),
      };
    });

    await verifyAndSubmitSignature();
    expect(captureSpy).not.toHaveBeenCalled();
    expect(state.consentLivenessToken).toBeNull();
    expect(signBtn.disabled).toBe(true);
  });
});
