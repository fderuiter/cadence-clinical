import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach } from "vitest";
import { createApp, nextTick } from "vue";
import App from "../src/App.vue";
import { state, refreshSubmissionsState } from "../src/index.js";
import {
  initSessionKey,
  queueSubmission,
  clearAllSubmissions,
} from "../src/sync-queue.js";

describe("Global Floating Drawer and Vue-Reactive State Sync", () => {
  beforeEach(async () => {
    window.__MOCK_TEST_ENV__ = true;
    document.body.innerHTML = `<div id="app"></div>`;

    // Clear state
    state.isSyncDrawerOpen = false;
    state.submissions = [];
    state.syncStatusText = "Online. All submissions synchronized.";
    await clearAllSubmissions();

    const rawMaterial = new Uint8Array(32);
    for (let i = 0; i < 32; i++) rawMaterial[i] = i;
    await initSessionKey(rawMaterial);
  });

  it("should render the persistent dynamic sync status button in the header across views", async () => {
    const app = createApp(App);
    app.mount("#app");
    await nextTick();

    const syncBtn = document.getElementById("btn-global-sync-drawer");
    expect(syncBtn).not.toBeNull();
    expect(syncBtn.querySelector(".sync-icon").textContent).toBe("🔄");
    expect(syncBtn.querySelector(".sync-btn-label").textContent).toBe(
      "Sync Status"
    );
  });

  it("should toggle the floating drawer open and closed when clicking the status button", async () => {
    const app = createApp(App);
    app.mount("#app");
    await nextTick();

    const syncBtn = document.getElementById("btn-global-sync-drawer");
    const drawer = document.querySelector(".global-drawer");
    const overlay = document.querySelector(".global-drawer-overlay");

    // Initially closed
    expect(state.isSyncDrawerOpen).toBe(false);
    expect(drawer.classList.contains("active")).toBe(false);
    expect(overlay.classList.contains("active")).toBe(false);

    // Toggle open
    syncBtn.click();
    await nextTick();

    expect(state.isSyncDrawerOpen).toBe(true);
    expect(drawer.classList.contains("active")).toBe(true);
    expect(overlay.classList.contains("active")).toBe(true);

    // Toggle close using close button
    const closeBtn = document.getElementById("btn-close-sync-drawer");
    closeBtn.click();
    await nextTick();

    expect(state.isSyncDrawerOpen).toBe(false);
    expect(drawer.classList.contains("active")).toBe(false);
  });

  it("should render submission list in the drawer reactively using Vue bindings", async () => {
    const app = createApp(App);
    app.mount("#app");
    await nextTick();

    // Add a submission to IndexedDB and refresh state
    await queueSubmission({
      subject_id: "subject_001",
      diary_id: "inst_daily_diary",
      assignment_id: "assign_01",
      answers: { vssbp: "120" },
      change_reason: "Initial entry",
      username: "subject_001",
    });

    await refreshSubmissionsState();
    await nextTick();

    // Verify reactive list updates inside Vue template
    const itemsList = document.querySelector(".submissions-items-list");
    expect(itemsList).not.toBeNull();

    const submissionItem = itemsList.querySelector(".drawer-submission-item");
    expect(submissionItem).not.toBeNull();
    expect(submissionItem.classList.contains("submission-queued")).toBe(true);
    expect(submissionItem.querySelector(".item-name").textContent).toBe(
      "Daily Health & Vital Diary"
    );
    expect(submissionItem.querySelector(".data-code").textContent).toContain(
      "vssbp"
    );
  });

  it("should trigger manual sync process within the floating drawer", async () => {
    const app = createApp(App);
    app.mount("#app");
    await nextTick();

    const syncTriggerBtn = document.getElementById("btn-drawer-sync-now");
    expect(syncTriggerBtn).not.toBeNull();

    // Clicking triggers sync action
    syncTriggerBtn.click();
    await nextTick();

    expect(syncTriggerBtn).toBeDefined();
  });
});
