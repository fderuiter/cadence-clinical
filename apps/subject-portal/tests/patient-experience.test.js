import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";

vi.mock("../sync-queue.js", async (importOriginal) => {
  const original = await importOriginal();
  return {
    ...original,
    getQueuedSubmissions: vi.fn().mockImplementation(async () => {
      if (typeof window !== "undefined" && window.__MOCK_GET_QUEUED__) {
        return [
          {
            id: 1,
            subject_id: "subject_test_001",
            diary_id: "inst_daily_diary",
          },
        ];
      }
      return original.getQueuedSubmissions();
    }),
    getAllSubmissions: vi.fn().mockImplementation(async () => {
      if (typeof window !== "undefined" && window.__MOCK_GET_QUEUED__) {
        return [
          {
            id: 1,
            status: "QUEUED",
            device_timestamp: Date.now(),
            diary_id: "diary_01",
          },
        ];
      }
      return original.getAllSubmissions();
    }),
  };
});

describe("Patient Experience & Adaptive Sync Retry Integration", () => {
  beforeEach(async () => {
    vi.resetModules();
    vi.clearAllTimers();
    window.__MOCK_TEST_ENV__ = true;
    window.__MOCK_GET_QUEUED__ = false;
    document.body.innerHTML = `
      <div id="app">
        <div id="tasks-list-container"></div>
        <div id="sync-queue-status-text"></div>
        <div id="sync-queue-list"></div>
      </div>
    `;

    // Clear session key and any mock submissions
    const portal = await import("../index.js");
    portal.state.session.userId = "subject_test_001";
    portal.state.session.isOfflineMode = false;
    portal.state.session.token = null;
    portal.resetRetryDelay();
    await portal.clearAllSubmissions();

    // Clear toasts
    const container = document.getElementById("toast-container");
    if (container) container.remove();

    globalThis.fetch = vi
      .fn()
      .mockRejectedValue(new Error("Network connection dropped"));
  });

  afterEach(() => {
    vi.clearAllTimers();
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it("triggers dynamic CSS toast messages instead of blocking browser alerts on validation error", async () => {
    await import("../index.js");

    // Call alert through overridden window.alert
    window.alert("Please fix all form errors before signing.");

    // Check if toast-container and toast were created
    const toastContainer = document.getElementById("toast-container");
    expect(toastContainer).not.toBeNull();

    const toast = toastContainer.querySelector(".toast");
    expect(toast).not.toBeNull();
    expect(toast.textContent).toBe(
      "Please fix all form errors before signing."
    );
    expect(toast.className).toContain("toast-error");
  });

  it("translates technical database status codes cleanly in the sync log interface", async () => {
    const portal = await import("../index.js");
    const { queueSubmission, updateSubmissionStatus, initSessionKey } =
      await import("../sync-queue.js");

    await initSessionKey(new Uint8Array(32));
    await queueSubmission({
      subject_id: "subject_1",
      diary_id: "inst_daily_diary",
      assignment_id: "assign_01",
      answers: { question1: "answer1" },
      change_reason: "first entry",
      username: "test_user",
    });
    await updateSubmissionStatus(1, "IGNORED_SERVER_WINS");

    await portal.renderSyncQueueList();

    const syncList = document.getElementById("sync-queue-list");
    expect(syncList.innerHTML).toContain(
      "CONFLICT (Ignored) / Updated by system"
    );
    expect(syncList.innerHTML).toContain("Updated by system.");
  });

  it("starts automated background retries with progressively longer delays on network disconnect", async () => {
    const portal = await import("../index.js");

    // Enable mock to return queued item synchronously in the background loop
    window.__MOCK_GET_QUEUED__ = true;

    // Enable offline simulation
    portal.state.session.isOfflineMode = true;

    // Use vitest fake timers
    vi.useFakeTimers();

    portal.resetRetryDelay();
    expect(portal.getRetryDelay()).toBe(2000);

    // Call scheduleBackgroundRetry
    portal.scheduleBackgroundRetry();

    // After scheduling, retryDelay (the delay for the NEXT retry) is doubled to 4000
    expect(portal.getRetryDelay()).toBe(4000);

    // Fast-forward clock by 2000ms to trigger the first retry callback
    await vi.advanceTimersByTimeAsync(2000);

    // Flush any nested async/await promise microtasks to let syncOfflineQueue finish
    for (let i = 0; i < 20; i++) {
      await vi.advanceTimersByTimeAsync(0);
    }

    // After first retry triggers and fails, scheduleBackgroundRetry gets called again automatically
    // This doubles retryDelay (the delay for the NEXT retry) to 8000
    expect(portal.getRetryDelay()).toBe(8000);

    // Fast-forward clock by 4000ms to trigger the second retry callback
    await vi.advanceTimersByTimeAsync(4000);

    // Flush nested promise microtasks again
    for (let i = 0; i < 20; i++) {
      await vi.advanceTimersByTimeAsync(0);
    }

    // After second retry triggers and fails, scheduleBackgroundRetry gets called again automatically
    // This doubles retryDelay to 16000
    expect(portal.getRetryDelay()).toBe(16000);

    portal.resetRetryDelay();
    vi.useRealTimers();

    // Reset mock flag
    window.__MOCK_GET_QUEUED__ = false;
  });
});
