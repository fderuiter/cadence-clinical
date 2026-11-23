import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { createApp, nextTick } from "vue";
import { useTimingPreferences } from "ui";
import App from "../src/App.vue";
import {
  state,
  syncOfflineQueue,
  resetSyncRetryInfo,
  resetPortalSessionWarning,
  portalSessionCoordinator,
  extendPortalSession,
  showView,
  renderSyncQueueList,
} from "../src/index.js";
import { pinia } from "../src/stores/pinia.js";
import {
  initSessionKey,
  clearSessionKey,
  clearAllSubmissions,
  queueSubmission,
} from "../src/sync-queue.js";
import { refuseUnbackedAction } from "../src/utils/unbackedActions.js";
import { clearToasts, showToast } from "../src/widgets/toast.js";

function makeJwtWithExp(expEpochSeconds, sub = "subject_001") {
  const encode = (obj) =>
    btoa(JSON.stringify(obj))
      .replace(/\+/g, "-")
      .replace(/\//g, "_")
      .replace(/=+$/g, "");
  return `${encode({ alg: "HS256", typ: "JWT" })}.${encode({
    sub,
    exp: expEpochSeconds,
  })}.sig`;
}

describe("Subject portal WCAG 2.2 timing adjustability, stacking toasts, session warnings, & sync milestones", () => {
  let activeApp = null;

  beforeEach(async () => {
    window.__MOCK_TEST_ENV__ = true;
    localStorage.clear();
    sessionStorage.clear();
    useTimingPreferences().setTimingMode("standard");
    resetSyncRetryInfo();
    resetPortalSessionWarning();
    portalSessionCoordinator.reset();
    delete window.keycloakInstance;

    state.session.userId = "subject_wcag_001";
    state.session.token = "wcag_session_token";
    state.session.isOfflineMode = false;
    state.session.isDemoMode = false;
    state.session.sessionExpired = false;
    state.authError = "";
    state.isSyncDrawerOpen = false;
    state.submissions = [];
    state.syncStatusText = "Online. All submissions synchronized.";
    state.syncMilestoneText = "Online. All submissions synchronized.";

    await initSessionKey("wcag-passphrase", "subject_wcag_001");
    await clearAllSubmissions();

    document.body.innerHTML = `<div id="app"></div>`;
    activeApp = createApp(App);
    activeApp.use(pinia);
    activeApp.mount("#app");
    await nextTick();
  });

  afterEach(async () => {
    vi.useRealTimers();
    vi.restoreAllMocks();
    if (activeApp) {
      activeApp.unmount();
      activeApp = null;
    }
    clearToasts();
    resetSyncRetryInfo();
    resetPortalSessionWarning();
    portalSessionCoordinator.reset();
    delete window.keycloakInstance;
    useTimingPreferences().setTimingMode("standard");
    clearSessionKey("subject_wcag_001");
    await clearAllSubmissions();
  });

  it("stacks concurrent portal toasts with individual dismiss & pause controls, scales timing via #portal-timing-mode-select, and persists unbackedAction errors in role=alert", async () => {
    vi.useFakeTimers();

    const timingSelect = document.getElementById("portal-timing-mode-select");
    expect(timingSelect).not.toBeNull();
    expect(timingSelect.style.minHeight || "").toBeDefined();

    // Switch to 10x extended timing mode via ParticipantCard selector
    timingSelect.value = "extended";
    timingSelect.dispatchEvent(new Event("change", { bubbles: true }));
    await nextTick();
    expect(useTimingPreferences().timingMode.value).toBe("extended");

    showToast("Morning diary saved locally.", "success", 1000);
    showToast("Stipend ledger refreshed.", "info", 1000);
    refuseUnbackedAction("Offline biometric enrollment");
    await nextTick();

    const politeRegion = document.querySelector(
      '[data-testid="toast-region-polite"]'
    );
    const assertiveRegion = document.querySelector(
      '[data-testid="toast-region-assertive"]'
    );
    expect(politeRegion.getAttribute("role")).toBe("status");
    expect(politeRegion.getAttribute("aria-live")).toBe("polite");
    expect(assertiveRegion.getAttribute("role")).toBe("alert");
    expect(assertiveRegion.getAttribute("aria-live")).toBe("assertive");

    const politeCards = politeRegion.querySelectorAll(".toast");
    const assertiveCards = assertiveRegion.querySelectorAll(".toast");
    expect(politeCards).toHaveLength(2);
    expect(assertiveCards).toHaveLength(1);
    expect(assertiveCards[0].textContent).toContain(
      "Offline biometric enrollment"
    );

    // Each stacked toast renders its own dismiss button
    expect(politeRegion.querySelectorAll(".toast-dismiss")).toHaveLength(2);

    // Under 10x extended mode, 1000ms base timer stays open at 5000ms
    vi.advanceTimersByTime(5000);
    await nextTick();
    expect(politeRegion.querySelectorAll(".toast")).toHaveLength(2);

    // Explicit pause button freezes countdown past 10000ms
    const firstPauseBtn = politeCards[0].querySelector(".toast-pause-btn");
    expect(firstPauseBtn).not.toBeNull();
    firstPauseBtn.click();
    await nextTick();
    expect(firstPauseBtn.getAttribute("aria-pressed")).toBe("true");

    vi.advanceTimersByTime(6000);
    await nextTick();
    // Second polite toast elapsed at 10000ms; paused first toast + error alert remain
    expect(politeRegion.querySelectorAll(".toast")).toHaveLength(1);
    expect(assertiveRegion.querySelectorAll(".toast")).toHaveLength(1);

    // Switch to manual dismiss ("off")
    timingSelect.value = "off";
    timingSelect.dispatchEvent(new Event("change", { bubbles: true }));
    await nextTick();

    showToast("Manual dismiss notification.", "success", 500);
    await nextTick();
    vi.advanceTimersByTime(30000);
    await nextTick();
    expect(politeRegion.textContent).toContain("Manual dismiss notification.");
  });

  it("opens SessionExpiryModal at <=60s remaining and supports >=10 consecutive session extensions without persisting tokens", async () => {
    vi.useFakeTimers();
    const baseMs = Date.UTC(2026, 10, 23, 18, 0, 0);
    vi.setSystemTime(baseMs);
    const nowSec = Math.floor(baseMs / 1000);

    const mockKeycloak = {
      authenticated: true,
      subject: "subject_wcag_001",
      token: makeJwtWithExp(nowSec + 50, "subject_wcag_001"),
      tokenParsed: { exp: nowSec + 50 },
      updateToken: vi.fn(async () => {
        const nextExp = Math.floor(Date.now() / 1000) + 120;
        mockKeycloak.token = makeJwtWithExp(nextExp, "subject_wcag_001");
        mockKeycloak.tokenParsed.exp = nextExp;
        return true;
      }),
    };
    window.keycloakInstance = mockKeycloak;
    portalSessionCoordinator.attachKeycloak(mockKeycloak);
    portalSessionCoordinator.markAuthenticated(mockKeycloak.token);
    await nextTick();

    expect(state.sessionWarning.isOpen).toBe(true);
    expect(state.sessionWarning.remainingSeconds).toBe(50);

    const modalEl = document.getElementById("portal-session-expiry-modal");
    expect(modalEl).not.toBeNull();
    expect(modalEl.getAttribute("role")).toBe("alertdialog");

    // Verify 10 consecutive extensions (WCAG 2.2 SC 2.2.1 SCR1)
    for (let i = 1; i <= 10; i += 1) {
      const res = await extendPortalSession();
      await nextTick();
      expect(res.ok).toBe(true);
      expect(res.extensionCount).toBe(i);
      expect(state.sessionWarning.isOpen).toBe(false);

      // Advance 65s so remaining validity drops from 120s -> 55s (< 60s lead)
      vi.advanceTimersByTime(65000);
      await nextTick();
      expect(state.sessionWarning.isOpen).toBe(true);
    }

    // Zero raw OIDC tokens persisted to storage
    expect(JSON.stringify(localStorage)).not.toContain(mockKeycloak.token);
    expect(JSON.stringify(sessionStorage)).not.toContain(mockKeycloak.token);
  });

  it("emits exactly one polite live-region announcement per retry attempt over 4.2s and halts countdowns + auto-retry when Pause updates is pressed", async () => {
    showView("view-tasks");
    state.isSyncDrawerOpen = true;
    await nextTick();

    await queueSubmission({
      subject_id: "subject_wcag_001",
      diary_id: "inst_daily_diary",
      assignment_id: "assign_01",
      answers: { vssbp: 120, vsdpb: 78, vshr: 70 },
      change_reason: "Milestone verification",
      username: "subject_wcag_001",
    });
    await renderSyncQueueList();
    await nextTick();

    // Leave setImmediate real so fake-indexeddb IDBRequest callbacks resolve immediately
    vi.useFakeTimers({
      toFake: [
        "setTimeout",
        "clearTimeout",
        "setInterval",
        "clearInterval",
        "Date",
      ],
    });

    let syncAttempts = 0;
    vi.spyOn(globalThis, "fetch").mockImplementation(async (url) => {
      if (String(url).includes("epro/sync")) {
        syncAttempts += 1;
        return {
          ok: false,
          status: 503,
          json: async () => ({ detail: "Gateway temporarily unavailable" }),
        };
      }
      return { ok: true, status: 200, json: async () => ({}) };
    });

    // Observe all polite/assertive live regions across the entire document
    const liveRegions = Array.from(
      document.querySelectorAll('[aria-live], [role="status"]')
    );
    expect(
      document.getElementById("header-sync-badge")?.getAttribute("aria-live") ??
        null
    ).toBeNull();

    const observedMutations = [];
    const observer = new MutationObserver((records) => {
      for (const rec of records) {
        observedMutations.push(rec.target.textContent?.trim() || "");
      }
    });
    for (const region of liveRegions) {
      observer.observe(region, {
        subtree: true,
        childList: true,
        characterData: true,
      });
    }

    try {
      // Trigger Attempt 1 with a 5000ms backoff window
      await syncOfflineQueue(false, { retryDelay: 5000, maxRetries: 4 });
      await nextTick();

      expect(syncAttempts).toBe(1);
      expect(
        document.getElementById("sync-queue-status-text")?.textContent?.trim()
      ).toBe("Retrying... Attempt 1 of 4");
      expect(
        document.getElementById("sync-drawer-status-text")?.textContent?.trim()
      ).toBe("Retrying... Attempt 1 of 4");

      const drawerCountdown = document.getElementById(
        "sync-drawer-retry-countdown"
      );
      const queueCountdown = document.getElementById(
        "sync-queue-retry-countdown"
      );
      expect(drawerCountdown?.getAttribute("aria-hidden")).toBe("true");
      expect(queueCountdown?.getAttribute("aria-hidden")).toBe("true");
      expect(drawerCountdown?.textContent).toContain("5s");

      // Record mutations after Attempt 1 started, then advance 4.2s across 4 ticks (5s -> 4s -> 3s -> 2s -> 1s)
      const countAfterAttemptStart = observedMutations.length;
      expect(countAfterAttemptStart).toBeGreaterThanOrEqual(1);

      await vi.advanceTimersByTimeAsync(4200);
      await nextTick();

      // Zero live-region mutations occurred across the 4.2s countdown!
      expect(observedMutations.length).toBe(countAfterAttemptStart);
      expect(drawerCountdown?.textContent).toContain("1s");
      expect(queueCountdown?.textContent).toContain("1s");

      // Activate "Pause updates" before the 5s retry fires
      const pauseBtn = document.getElementById("btn-drawer-pause-sync");
      const headerPauseBtn = document.getElementById("btn-header-pause-sync");
      expect(pauseBtn).not.toBeNull();
      expect(headerPauseBtn).not.toBeNull();
      expect(pauseBtn.getAttribute("aria-pressed")).toBe("false");
      expect(headerPauseBtn.getAttribute("aria-pressed")).toBe("false");
      pauseBtn.click();
      await nextTick();

      expect(pauseBtn.getAttribute("aria-pressed")).toBe("true");
      expect(headerPauseBtn.getAttribute("aria-pressed")).toBe("true");
      expect(
        document
          .getElementById("btn-pause-sync-updates")
          ?.getAttribute("aria-pressed")
      ).toBe("true");
      expect(
        document.getElementById("sync-drawer-status-text")?.textContent?.trim()
      ).toBe("Automatic sync updates paused.");

      // Advance 15 seconds while paused -> Attempt 2 must NOT fire and countdown stays frozen
      await vi.advanceTimersByTimeAsync(15000);
      await nextTick();
      expect(syncAttempts).toBe(1);

      // Resume updates -> Attempt 2 fires after remaining delay
      pauseBtn.click();
      await nextTick();
      expect(pauseBtn.getAttribute("aria-pressed")).toBe("false");

      await vi.advanceTimersByTimeAsync(2000);
      await new Promise((resolve) => setImmediate(resolve));
      await new Promise((resolve) => setImmediate(resolve));
      await nextTick();
      expect(syncAttempts).toBe(2);
    } finally {
      observer.disconnect();
    }
  });
});
