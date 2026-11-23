import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { createSSRApp, h, nextTick } from "vue";
import { renderToString } from "vue/server-renderer";
import {
  ClinicalToastHost,
  FileUploadModal,
  SessionExpiryModal,
  TIMING_PREFERENCE_MODES,
  TIMING_PREFERENCE_STORAGE_KEY,
  MIN_SESSION_WARNING_LEAD_SECONDS,
  DEFAULT_SESSION_WARNING_LEAD_SECONDS,
  MIN_REQUIRED_SESSION_EXTENSIONS,
  createMilestoneCountdown,
  createOidcSessionCoordinator,
  createToastController,
  resetTimingPreferencesForTests,
  useTimingPreferences,
} from "../index.js";

async function mountSetupSSR(Component, props = {}, emit = vi.fn()) {
  let state;
  const Harness = {
    setup() {
      state = Component.setup(props, {
        emit,
        attrs: {},
        slots: {},
        expose: () => {},
      });
      return () => h(Component, props);
    },
  };
  const html = await renderToString(createSSRApp(Harness));
  return { state, html };
}

function makeJwtWithExp(expSeconds, sub = "user-123") {
  const header = Buffer.from(
    JSON.stringify({ alg: "HS256", typ: "JWT" })
  ).toString("base64url");
  const payload = Buffer.from(
    JSON.stringify({ sub, exp: expSeconds })
  ).toString("base64url");
  return `${header}.${payload}.signature`;
}

describe("WCAG 2.2 Timing Adjustability, Dual Live Regions, Session Warnings, & Milestone Countdowns", () => {
  beforeEach(() => {
    resetTimingPreferencesForTests();
  });

  afterEach(() => {
    resetTimingPreferencesForTests();
    vi.restoreAllMocks();
  });

  describe("useTimingPreferences (WCAG 2.2 SC 2.2.1 G180 / G198)", () => {
    it("supports standard (1x), extended (10x), and off (manual dismiss) modes and persists only non-sensitive mode keys", () => {
      const stored = new Map();
      const origStorage = globalThis.localStorage;
      globalThis.localStorage = {
        getItem: (k) => (stored.has(k) ? stored.get(k) : null),
        setItem: (k, v) => stored.set(k, String(v)),
        removeItem: (k) => stored.delete(k),
      };

      try {
        const prefs = useTimingPreferences();
        expect(prefs.timingMode.value).toBe(TIMING_PREFERENCE_MODES.STANDARD);
        expect(prefs.multiplier.value).toBe(1);
        expect(prefs.isAutoDismissDisabled.value).toBe(false);
        expect(prefs.resolveDurationMs(5000)).toBe(5000);

        prefs.setTimingMode(TIMING_PREFERENCE_MODES.EXTENDED);
        expect(prefs.timingMode.value).toBe("extended");
        expect(prefs.multiplier.value).toBe(10);
        expect(prefs.resolveDurationMs(5000)).toBe(50000);
        expect(stored.get(TIMING_PREFERENCE_STORAGE_KEY)).toBe("extended");

        prefs.setTimingMode(TIMING_PREFERENCE_MODES.OFF);
        expect(prefs.timingMode.value).toBe("off");
        expect(prefs.multiplier.value).toBe(0);
        expect(prefs.isAutoDismissDisabled.value).toBe(true);
        expect(prefs.resolveDurationMs(5000)).toBe(0);

        // Invalid values safely normalize to standard
        prefs.setTimingMode("unexpected");
        expect(prefs.timingMode.value).toBe("standard");
        expect(prefs.resolveDurationMs(3500)).toBe(3500);
      } finally {
        globalThis.localStorage = origStorage;
      }
    });
  });

  describe("createToastController & ClinicalToastHost (WCAG 2.2 SC 2.2.1, 2.2.2, 4.1.3)", () => {
    it("separates polite status notifications from persistent assertive error alerts, pauses on hover/focus/explicit toggle, and scales with timing preferences", async () => {
      vi.useFakeTimers();
      vi.spyOn(console, "error").mockImplementation(() => {});
      let currentTime = 1_000_000;
      try {
        const prefs = useTimingPreferences();
        const controller = createToastController({
          defaultTone: "info",
          defaultTimeoutMs: 4000,
          now: () => currentTime,
          timingPreferences: prefs,
        });

        const errId = controller.apiError(
          "Gateway unavailable",
          Object.assign(new Error("Upstream timeout"), { status: 504 })
        );
        const okId = controller.push({
          tone: "success",
          title: "Document Ingested",
          message: "Saved to eTMF.",
          timeoutMs: 3000,
        });

        expect(controller.assertiveToasts.value).toHaveLength(1);
        expect(controller.politeToasts.value).toHaveLength(1);
        expect(controller.assertiveToasts.value[0].id).toBe(errId);
        expect(controller.assertiveToasts.value[0].ariaRole).toBe("alert");
        expect(controller.assertiveToasts.value[0].timeoutMs).toBe(0);
        expect(controller.politeToasts.value[0].id).toBe(okId);
        expect(controller.politeToasts.value[0].ariaRole).toBe("status");

        const html = await renderToString(
          createSSRApp({
            render: () =>
              h(ClinicalToastHost, {
                controller,
                variantStyle: "portal",
                latestToastId: "toast-container",
              }),
          })
        );

        expect(html).toContain('data-focus-trap-exempt="true"');
        expect(html).toContain('data-portal-toast-host="true"');
        expect(html).toContain('data-testid="toast-region-assertive"');
        expect(html).toContain('data-testid="toast-region-polite"');
        expect(html).toContain('class="toast-pause toast-pause-btn"');
        expect(html).toContain('aria-pressed="false"');

        // Advance 1000ms, then pause via hover -> does not dismiss even after 20s
        currentTime += 1000;
        vi.advanceTimersByTime(1000);
        controller.setHovered(okId, true);
        currentTime += 20000;
        vi.advanceTimersByTime(20000);
        expect(controller.politeToasts.value).toHaveLength(1);

        // Leave hover, then pause via explicit toggle -> stays open
        controller.setHovered(okId, false);
        expect(controller.togglePause(okId)).toBe(true);
        currentTime += 10000;
        vi.advanceTimersByTime(10000);
        expect(controller.politeToasts.value).toHaveLength(1);

        // Resume explicit toggle -> remaining ~2000ms elapses and dismisses polite toast; error persists
        expect(controller.togglePause(okId)).toBe(false);
        currentTime += 1900;
        vi.advanceTimersByTime(1900);
        expect(controller.politeToasts.value).toHaveLength(1);
        currentTime += 200;
        vi.advanceTimersByTime(200);
        expect(controller.politeToasts.value).toHaveLength(0);
        expect(controller.assertiveToasts.value).toHaveLength(1);

        // Extended (10x) mode scales a 2000ms toast to 20000ms
        prefs.setTimingMode("extended");
        await nextTick();
        controller.push({
          tone: "info",
          message: "Extended toast",
          timeoutMs: 2000,
        });
        currentTime += 5000;
        vi.advanceTimersByTime(5000);
        expect(controller.politeToasts.value).toHaveLength(1);
        currentTime += 15100;
        vi.advanceTimersByTime(15100);
        expect(controller.politeToasts.value).toHaveLength(0);

        // Manual ("off") mode disables auto-dismiss completely
        prefs.setTimingMode("off");
        await nextTick();
        controller.push({
          tone: "warning",
          message: "Manual toast",
          timeoutMs: 2000,
        });
        currentTime += 60000;
        vi.advanceTimersByTime(60000);
        expect(controller.politeToasts.value).toHaveLength(1);

        controller.dispose();
      } finally {
        vi.useRealTimers();
      }
    });
  });

  describe("FileUploadModal polite verification, Done action, & pausable 5000ms close timer (WCAG F103 / SC 2.2.1)", () => {
    it("announces SHA-256 verification politely, renders Done in success state, and replaces 800ms close with pausable timing-aware 5000ms timer", async () => {
      vi.useFakeTimers();
      try {
        const fetchMock = vi
          .spyOn(globalThis, "fetch")
          .mockResolvedValueOnce({
            ok: true,
            status: 200,
            json: async () => ({
              file_id: "file_001",
              object_key: "studies/s1/protocol.pdf",
              upload_url: "https://storage.local/put",
            }),
          })
          .mockResolvedValueOnce({
            ok: true,
            status: 200,
          });

        const emit = vi.fn();
        const { state, html: initialHtml } = await mountSetupSSR(
          FileUploadModal,
          {
            modelValue: true,
            studyId: "STUDY-101",
            teleport: false,
          },
          emit
        );

        expect(initialHtml).toContain('data-testid="upload-success-status"');
        expect(initialHtml).toContain('role="status"');
        expect(initialHtml).toContain('aria-live="polite"');

        state.selectedFile.value = {
          name: "protocol-v2.pdf",
          size: 2048,
          type: "application/pdf",
        };
        state.reasonForChange.value = "Initial regulatory protocol upload";
        await state.startUpload();

        expect(fetchMock).toHaveBeenCalledTimes(2);
        expect(state.status.value).toBe("success");
        expect(emit).toHaveBeenCalledWith(
          "uploaded",
          expect.objectContaining({
            fileId: "file_001",
            filename: "protocol-v2.pdf",
          })
        );

        // Must NOT auto-close at 800ms
        vi.advanceTimersByTime(1000);
        expect(emit).not.toHaveBeenCalledWith("close");
        expect(state.status.value).toBe("success");

        // Hover pauses auto-close beyond 5000ms
        state.onModalMouseEnter();
        vi.advanceTimersByTime(10000);
        expect(emit).not.toHaveBeenCalledWith("close");

        // Leaving hover, then explicitly pausing via toggleAutoClosePause keeps modal open
        state.onModalMouseLeave();
        expect(state.toggleAutoClosePause()).toBe(true);
        expect(state.isAutoCloseExplicitlyPaused.value).toBe(true);
        vi.advanceTimersByTime(10000);
        expect(emit).not.toHaveBeenCalledWith("close");

        // Resuming explicit pause resumes remaining duration (~4000ms)
        expect(state.toggleAutoClosePause()).toBe(false);
        expect(state.isAutoCloseExplicitlyPaused.value).toBe(false);
        vi.advanceTimersByTime(3900);
        expect(emit).not.toHaveBeenCalledWith("close");
        vi.advanceTimersByTime(200);
        expect(emit).toHaveBeenCalledWith("close");
      } finally {
        vi.useRealTimers();
      }
    });
  });

  describe("createOidcSessionCoordinator & SessionExpiryModal (WCAG 2.2 SC 2.2.1 SCR16 / SCR1)", () => {
    it("enforces >=20s warningLeadSeconds, checks wall-clock expiry on visibilitychange/focus, and supports >=10 consecutive session extensions", async () => {
      vi.useFakeTimers();
      let wallClockMs = 1_700_000_000_000;
      try {
        expect(MIN_SESSION_WARNING_LEAD_SECONDS).toBe(20);
        expect(DEFAULT_SESSION_WARNING_LEAD_SECONDS).toBe(60);
        expect(MIN_REQUIRED_SESSION_EXTENSIONS).toBe(10);

        const expiringEvents = [];
        const tickEvents = [];
        const extendedEvents = [];
        let warningClearedCount = 0;
        const expiredEvents = [];

        let currentExpSec = Math.floor(wallClockMs / 1000) + 120;
        const kc = {
          authenticated: true,
          token: makeJwtWithExp(currentExpSec),
          refreshToken: "refresh-token",
          updateToken: vi.fn(async () => {
            currentExpSec = Math.floor(wallClockMs / 1000) + 300;
            kc.token = makeJwtWithExp(currentExpSec);
            return true;
          }),
        };

        // Request 5s lead -> clamped to minimum 20s per SCR16
        const coordinator = createOidcSessionCoordinator({
          warningLeadSeconds: 5,
          now: () => wallClockMs,
          onSessionExpiring: (e) => expiringEvents.push(e),
          onSessionExpiryTick: (e) => tickEvents.push(e),
          onSessionExtended: (e) => extendedEvents.push(e),
          onSessionWarningCleared: () => {
            warningClearedCount += 1;
          },
          onSessionExpired: (e) => expiredEvents.push(e),
        });

        expect(coordinator.getWarningLeadSeconds()).toBe(20);
        coordinator.attachKeycloak(kc);
        expect(coordinator.isWarningActive()).toBe(false);

        // Simulate background tab throttling: wall-clock jumps to 15s before expiry WITHOUT setTimeout firing, then foreground check runs
        wallClockMs += 105_000;
        coordinator.evaluateSessionExpiry();
        expect(coordinator.isWarningActive()).toBe(true);
        expect(expiringEvents).toHaveLength(1);
        expect(expiringEvents[0].remainingSeconds).toBe(15);

        // Perform 11 consecutive session extensions (>= 10 required by WCAG SCR1)
        for (let i = 1; i <= 11; i++) {
          const res = await coordinator.extendSession();
          expect(res.ok).toBe(true);
          expect(res.extensionCount).toBe(i);
          expect(coordinator.isWarningActive()).toBe(false);

          // Advance into the next warning window (285s later -> 15s remaining)
          wallClockMs += 285_000;
          coordinator.evaluateSessionExpiry();
          expect(coordinator.isWarningActive()).toBe(true);
        }

        expect(coordinator.getExtensionCount()).toBe(11);
        expect(extendedEvents).toHaveLength(11);
        expect(warningClearedCount).toBeGreaterThanOrEqual(11);
        expect(expiredEvents).toHaveLength(0);

        // Letting wall-clock pass exp without extending expires session cleanly
        wallClockMs += 20_000;
        coordinator.evaluateSessionExpiry();
        expect(coordinator.isSessionExpired()).toBe(true);
        expect(expiredEvents).toHaveLength(1);

        coordinator.dispose();
      } finally {
        vi.useRealTimers();
      }
    });

    it("renders SessionExpiryModal as alertdialog with aria-hidden tabular-nums countdown, static screen-reader summary, Extend Session, and Sign Out actions", async () => {
      const html = await renderToString(
        createSSRApp({
          render: () =>
            h(SessionExpiryModal, {
              open: true,
              remainingSeconds: 45,
              warningLeadSeconds: 60,
              teleport: false,
            }),
        })
      );

      expect(html).toContain('role="alertdialog"');
      expect(html).toContain("Session Expiring Soon");
      expect(html).toContain("0:45");
      expect(html).toContain('aria-hidden="true"');
      expect(html).toContain(
        "Approximately 60 seconds or less remaining before automatic sign-out."
      );
      expect(html).toContain("Extend Session");
      expect(html).toContain("Sign Out");
      expect(html).toContain("Local unsaved drafts in this browser");
    });
  });

  describe("createMilestoneCountdown (WCAG 2.2 SC 2.2.2 & SC 4.1.3)", () => {
    it("emits a single polite milestone per retry attempt while ticking visual seconds every 1s and halting on pause", () => {
      vi.useFakeTimers();
      let nowMs = 50_000;
      try {
        const milestones = [];
        const ticks = [];
        let expiredCount = 0;

        const countdown = createMilestoneCountdown({
          now: () => nowMs,
          onMilestone: (m) => milestones.push(m),
          onTick: (t) => ticks.push(t),
          onExpire: () => {
            expiredCount += 1;
          },
        });

        countdown.start({
          durationMs: 5000,
          attempt: 1,
          maxRetries: 4,
          milestoneMessage: "Retrying... Attempt 1 of 4",
        });

        expect(milestones).toHaveLength(1);
        expect(countdown.milestoneText.value).toBe(
          "Retrying... Attempt 1 of 4"
        );
        expect(countdown.remainingSeconds.value).toBe(5);

        // Over 4.2s of 1s ticks, milestoneText does NOT mutate (still 1 announcement!)
        for (let step = 0; step < 4; step++) {
          nowMs += 1000;
          vi.advanceTimersByTime(1000);
        }
        expect(countdown.remainingSeconds.value).toBe(1);
        expect(milestones).toHaveLength(1);

        // Pausing halts countdown and expiry callback
        countdown.pause("Automatic sync updates paused.");
        expect(countdown.isPaused.value).toBe(true);
        expect(milestones).toHaveLength(2);
        nowMs += 10000;
        vi.advanceTimersByTime(10000);
        expect(expiredCount).toBe(0);

        // Resuming completes remaining ~1s and fires onExpire once
        countdown.resume("Automatic sync updates resumed.");
        expect(countdown.isPaused.value).toBe(false);
        nowMs += 1100;
        vi.advanceTimersByTime(1100);
        expect(expiredCount).toBe(1);

        countdown.complete("Online. Sync complete.");
        expect(countdown.milestoneText.value).toBe("Online. Sync complete.");
      } finally {
        vi.useRealTimers();
      }
    });
  });
});
