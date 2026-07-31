import { defineStore } from 'pinia';

export type SyncStatus = 'IDLE' | 'SYNCING' | 'CONFLICT_DETECTED' | 'ERROR' | 'COMPLETED';

export interface SyncState {
  status: SyncStatus;
  pendingCount: number;
  conflict: any | null;
}

export const useSyncStore = defineStore('sync', {
  state: (): SyncState => ({
    status: 'IDLE',
    pendingCount: 0,
    conflict: null,
  }),
  actions: {
    setStatus(status: SyncStatus) {
      this.status = status;
    },
    setPendingCount(count: number) {
      this.pendingCount = count;
    },
    setConflict(conflict: any) {
      this.conflict = conflict;
    },
    clearConflict() {
      this.conflict = null;
    }
  }
});
