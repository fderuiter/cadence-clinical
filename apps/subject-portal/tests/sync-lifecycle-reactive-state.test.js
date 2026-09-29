import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach, vi } from "vitest";
import { createApp, nextTick } from "vue";
import App from "../src/App.vue";
import {
  state,
  syncOfflineQueue,
  emitSyncLifecycleEvent,
  resetSyncRetryInfo,
  refreshSubmissionsState,
  dispatchApi,
} from "../src/index.js";
import {
  initSessionKey,
  queueSubmission,
  clearAllSubmissions,
} from "../src/sync-queue.js";

describe("Event-driven sync lifecycle emitter with reactive Vue progress state", () => {
  beforeEach(async () => {
    window.__MOCK_TEST_ENV__ = true;
    document.body.innerHTML = `
      <div id="app"></div>
      <div id="sync-queue-status-text"></div>
      <div id="sync-queue-list"></div>
    `;

    // Reset state and IndexedDB
    resetSyncRetryInfo();
    state.session.isOfflineMode = false;
    state.autoSyncSuspended = false;
    state.submissions = [];
    state.syncStatusText = "Online. All submissions synchronized.";
    await clearAllSubmissions();

    const rawMaterial = new Uint8Array(32);
    for (let i = 0; i < 32; i++) rawMaterial[i] = i;
    await initSessionKey(rawMaterial);
  });

  it("dispatches sync:attempt event and updates state.syncRetryInfo during sync attempt", async () => {
    const attemptHandler = vi.fn();
    window.addEventListener("sync:attempt", attemptHandler);

    await queueSubmission({
      subject_id: "subject_001",
      diary_id: "inst_daily_diary",
      assignment_id: "assign_01",
      answers: { vssbp: 120, vsdpb: 80, vshr: 70 },
      change_reason: "Baseline entry",
      username: "subject_001",
    });

    // Mock dispatchApi failure to trigger retry
    const origFetch = globalThis.fetch;
    globalThis.fetch = vi
      .fn()
      .mockRejectedValue(new Error("Network disconnect"));

    await syncOfflineQueue(false, { maxRetries: 5, attempt: 1 });

    expect(attemptHandler).toHaveBeenCalledTimes(1);
    const eventDetail = attemptHandler.mock.calls[0][0].detail;
    expect(eventDetail.attempt).toBe(1);
    expect(eventDetail.maxRetries).toBe(5);
    expect(eventDetail.sequenceNumber).toBe(1);

    expect(state.syncRetryInfo.isRetrying).toBe(true);
    expect(state.syncRetryInfo.attempt).toBe(1);
    expect(state.syncRetryInfo.maxRetries).toBe(5);
    expect(state.syncRetryInfo.sequenceNumber).toBe(1);

    globalThis.fetch = origFetch;
    window.removeEventListener("sync:attempt", attemptHandler);
  });

  it("dispatches sync:retry-scheduled event with backoff delay when transmission fails", async () => {
    const scheduledHandler = vi.fn();
    window.addEventListener("sync:retry-scheduled", scheduledHandler);

    await queueSubmission({
      subject_id: "subject_001",
      diary_id: "inst_daily_diary",
      assignment_id: "assign_01",
      answers: { vssbp: 120, vsdpb: 80, vshr: 70 },
      change_reason: "Baseline entry",
      username: "subject_001",
    });

    const origFetch = globalThis.fetch;
    globalThis.fetch = vi
      .fn()
      .mockRejectedValue(new Error("Network disconnect"));

    await syncOfflineQueue(false, {
      maxRetries: 5,
      attempt: 1,
      retryDelay: 1000,
    });

    expect(scheduledHandler).toHaveBeenCalledTimes(1);
    const detail = scheduledHandler.mock.calls[0][0].detail;
    expect(detail.attempt).toBe(1);
    expect(detail.maxRetries).toBe(5);
    expect(detail.nextRetryDelay).toBe(1000);
    expect(detail.nextRetryTimestamp).toBeGreaterThan(Date.now() - 100);

    expect(state.syncRetryInfo.nextRetryDelay).toBe(1000);
    expect(state.syncStatusText).toContain("Retrying... Attempt 1 of 5");

    globalThis.fetch = origFetch;
    window.removeEventListener("sync:retry-scheduled", scheduledHandler);
  });

  it("dispatches sync:success event and resets state.syncRetryInfo when sync succeeds", async () => {
    const successHandler = vi.fn();
    window.addEventListener("sync:success", successHandler);

    await queueSubmission({
      subject_id: "subject_001",
      diary_id: "inst_daily_diary",
      assignment_id: "assign_01",
      answers: { vssbp: 120, vsdpb: 80, vshr: 70 },
      change_reason: "Baseline entry",
      username: "subject_001",
    });

    const origFetch = globalThis.fetch;
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({
        results: [
          {
            status: "CREATED",
            answers: { vssbp: 120, vsdpb: 80, vshr: 70 },
            sequence_number: 1,
            client_id: "client-001",
          },
        ],
      }),
    });

    // Set initial retry state
    state.syncRetryInfo.isRetrying = true;
    state.syncRetryInfo.attempt = 2;

    await syncOfflineQueue(false);

    expect(successHandler).toHaveBeenCalled();
    expect(state.syncRetryInfo.isRetrying).toBe(false);
    expect(state.syncRetryInfo.attempt).toBe(0);
    expect(state.syncStatusText).toBe("Online. Sync complete.");

    globalThis.fetch = origFetch;
    window.removeEventListener("sync:success", successHandler);
  });

  it("updates App.vue header sync button and item status description reactively during retries", async () => {
    const app = createApp(App);
    app.mount("#app");
    await nextTick();

    await queueSubmission({
      subject_id: "subject_001",
      diary_id: "inst_daily_diary",
      assignment_id: "assign_01",
      answers: { vssbp: 120, vsdpb: 80, vshr: 70 },
      change_reason: "Baseline entry",
      username: "subject_001",
    });

    await refreshSubmissionsState();
    await nextTick();

    // Trigger retry attempt manually via lifecycle emitter
    emitSyncLifecycleEvent("sync:attempt", {
      attempt: 2,
      maxRetries: 5,
      sequenceNumber: 1,
      queuedCount: 1,
    });
    await nextTick();

    // Check header sync button label
    const syncBtnLabel = document.querySelector(".sync-btn-label");
    expect(syncBtnLabel).not.toBeNull();
    expect(syncBtnLabel.textContent).toContain("Retrying (2/5)");

    // Check queued item status description in drawer
    const drawerItem = document.querySelector(".drawer-submission-item");
    expect(drawerItem).not.toBeNull();
    const itemDesc = drawerItem.querySelector(".item-desc");
    expect(itemDesc.textContent).toContain("Retrying... Attempt 2 of 5");
  });
});
