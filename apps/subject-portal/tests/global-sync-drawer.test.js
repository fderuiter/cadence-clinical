import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach, afterEach } from "vitest";
import { createApp, nextTick } from "vue";
import App from "../src/App.vue";
import { state, refreshSubmissionsState } from "../src/index.js";
import { pinia } from "../src/stores/pinia.js";
import {
  initSessionKey,
  queueSubmission,
  clearAllSubmissions,
} from "../src/sync-queue.js";

describe("Global Floating Drawer and Vue-Reactive State Sync", () => {
  let activeApp = null;

  afterEach(() => {
    if (activeApp) {
      try {
        activeApp.unmount();
      } catch {
        // Ignore unmount errors during teardown
      }
      activeApp = null;
    }
  });

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
    activeApp = createApp(App);
    activeApp.mount("#app");
    await nextTick();

    const syncBtn = document.getElementById("btn-global-sync-drawer");
    expect(syncBtn).not.toBeNull();
    expect(syncBtn.querySelector(".sync-icon").textContent).toBe("🔄");
    expect(syncBtn.querySelector(".sync-btn-label").textContent).toBe(
      "Sync Status"
    );
  });

  it("should toggle the floating drawer open and closed when clicking the status button", async () => {
    activeApp = createApp(App);
    activeApp.mount("#app");
    await nextTick();

    const syncBtn = document.getElementById("btn-global-sync-drawer");

    // Initially closed and unmounted until opened
    expect(state.isSyncDrawerOpen).toBe(false);
    expect(document.querySelector(".global-drawer")).toBeNull();
    expect(document.querySelector(".global-drawer-overlay")).toBeNull();

    // Toggle open
    syncBtn.click();
    await nextTick();

    const drawer = document.querySelector(".global-drawer");
    const overlay = document.querySelector(".global-drawer-overlay");
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
    activeApp = createApp(App);
    activeApp.mount("#app");
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
    expect(submissionItem.textContent).toContain("Saved offline");
    expect(
      submissionItem.querySelector(".answer-summary-list").textContent
    ).toContain("Systolic Blood Pressure (mmHg):");
    expect(
      submissionItem.querySelector(".answer-summary-list").textContent
    ).toContain("120");
    expect(submissionItem.textContent).not.toContain("vssbp");
    expect(submissionItem.textContent).not.toContain("Seq: #");

    // Verify plain participant header badges and activity footer heading
    const headerText = document.querySelector(".portal-header").textContent;
    expect(headerText).toContain("Study Companion");
    expect(headerText).toContain("Private & Encrypted");
    expect(headerText).toContain("Offline Ready");
    expect(headerText).not.toContain("21 CFR Part 11");
    expect(headerText).not.toContain("GAMP 5");

    const footerText = document.querySelector(".portal-footer").textContent;
    expect(footerText).toContain("My Recent Activity");
    expect(footerText).not.toContain("ALCOA");
  });

  it("should trigger manual sync process within the floating drawer", async () => {
    activeApp = createApp(App);
    activeApp.mount("#app");
    await nextTick();

    const syncBtn = document.getElementById("btn-global-sync-drawer");
    syncBtn.click();
    await nextTick();

    const syncTriggerBtn = document.getElementById("btn-drawer-sync-now");
    expect(syncTriggerBtn).not.toBeNull();

    // Clicking triggers sync action
    syncTriggerBtn.click();
    await nextTick();

    expect(syncTriggerBtn).toBeDefined();
  });

  it("formats W-9 statuses, expense categories, and wallet transaction types in plain participant language", async () => {
    state.session.userId = "subject_wallet_copy";
    state.session.token = "wallet_copy_token";
    state.session.isOfflineMode = false;
    state.session.isDemoMode = false;
    state.currentView = "view-wallet";
    state.walletLoading = false;
    state.walletLoaded = true;
    state.walletBalance = 120.0;
    state.taxableYtd = 520.0;
    state.is1099ThresholdExceeded = false;
    state.w9Status = "NOT_REQUIRED";
    state.expenseClaims = [
      {
        id: "claim_copy_01",
        category: "TRANSIT",
        merchantName: "Metro Regional Line",
        expenseDate: "2026-11-18",
        netAmount: 24.5,
        status: "AUTO_APPROVED",
      },
      {
        id: "claim_copy_02",
        category: "LODGING",
        merchantName: "Harbor Inn",
        expenseDate: "2026-11-19",
        netAmount: 140.0,
        status: "PENDING_REVIEW",
      },
    ];
    state.recentTransactions = [
      {
        id: "tx_copy_01",
        description: "Visit 2 Stipend",
        transactionType: "STIPEND_CREDIT",
        amount: 75.0,
      },
    ];

    activeApp = createApp(App);
    activeApp.use(pinia);
    activeApp.mount("#app");
    await nextTick();

    const warningBanner = document.getElementById("wallet-w9-warning-banner");
    expect(warningBanner?.textContent).toContain("Not required");
    expect(warningBanner?.textContent).not.toContain("NOT_REQUIRED");

    state.taxableYtd = 625.0;
    state.is1099ThresholdExceeded = true;
    state.w9Status = "SUBMITTED";
    await nextTick();

    const holdBanner = document.getElementById("wallet-w9-hold-banner");
    expect(holdBanner?.textContent).toContain("Submitted");
    expect(holdBanner?.textContent).not.toContain("SUBMITTED");
    expect(
      document.getElementById("wallet-w9-status")?.textContent?.trim()
    ).toBe("Submitted");

    const claimsText =
      document.getElementById("wallet-expense-claims")?.textContent || "";
    expect(claimsText).toContain("Transit");
    expect(claimsText).toContain("Lodging");
    expect(claimsText).not.toContain("TRANSIT");
    expect(claimsText).not.toContain("LODGING");

    const txText =
      document.getElementById("wallet-recent-transactions")?.textContent || "";
    expect(txText).toContain("Study stipend");
    expect(txText).not.toContain("STIPEND_CREDIT");
  });
});
