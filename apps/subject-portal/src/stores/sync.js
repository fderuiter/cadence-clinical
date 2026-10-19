// Offline submission queue status shown in the sync drawer and header button.
import { defineStore } from "pinia";

export const useSyncStore = defineStore("sync", {
  state: () => ({
    submissions: [],
    isSyncDrawerOpen: false,
    syncStatusText: "Online. All submissions synchronized.",
    autoSyncSuspended: false,
    syncRetryInfo: {
      attempt: 0,
      maxRetries: 5,
      nextRetryDelay: 0,
      nextRetryTimestamp: null,
      sequenceNumber: null,
      isRetrying: false,
    },
    __syncRetryTimer: null,
  }),
  getters: {
    queuedSubmissionsCount: (s) =>
      s.submissions
        ? s.submissions.filter((x) => x.status === "QUEUED").length
        : 0,
    isSyncFailed: (s) =>
      s.syncStatusText && s.syncStatusText.toLowerCase().includes("failed"),
  },
  actions: {
    toggleSyncDrawer() {
      this.isSyncDrawerOpen = !this.isSyncDrawerOpen;
    },
  },
});
