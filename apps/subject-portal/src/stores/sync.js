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
      remainingSeconds: 0,
      sequenceNumber: null,
      isRetrying: false,
    },
    __syncRetryTimer: null,
    __syncCountdownTimer: null,
  }),
  getters: {
    queuedSubmissionsCount: (s) =>
      s.submissions
        ? s.submissions.filter(
            (x) =>
              x.status === "QUEUED" ||
              x.status === "LOCAL_QUARANTINED" ||
              x.status === "QUARANTINED" ||
              ((x.status === "DECRYPTION_ERROR" || Boolean(x.locked)) &&
                !x.unrecoverable)
          ).length
        : 0,
    quarantinedSubmissionsCount: (s) =>
      s.submissions
        ? s.submissions.filter(
            (x) =>
              x.status === "LOCAL_QUARANTINED" || x.status === "QUARANTINED"
          ).length
        : 0,
    isSyncFailed: (s) =>
      Boolean(
        (s.syncStatusText &&
          s.syncStatusText.toLowerCase().includes("failed")) ||
        (s.submissions &&
          s.submissions.some(
            (x) =>
              x.status === "LOCAL_QUARANTINED" || x.status === "QUARANTINED"
          ))
      ),
  },
  actions: {
    toggleSyncDrawer() {
      this.isSyncDrawerOpen = !this.isSyncDrawerOpen;
    },
  },
});
