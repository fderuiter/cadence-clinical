import { useSyncStore } from "../stores/sync";
import { offlineAuthManager } from "./offlineAuth";

export interface PendingDelta {
  deltaId: string;
  entityType: string;
  entityId: string;
  action: "CREATE" | "UPDATE" | "SUBMIT";
  payload: Record<string, any>;
  clientTimestampUtc: string;
  reasonForChange: string;
}

export class IndexedDBManager {
  private baseDbName = "SyncEngineDB";
  private currentUserId: string | null = null;
  private dbName = "SyncEngineDB";
  private storeName = "pending_sync_deltas";
  private useMemory = false;
  private memoryStores: Map<string, Map<string, PendingDelta>> = new Map();

  constructor() {
    if (typeof window === "undefined" || !window.indexedDB) {
      this.useMemory = true;
    }
  }

  public setUserId(userId: string | null): void {
    this.currentUserId = userId || null;
    this.dbName = this.resolveDbName();
  }

  public getUserId(): string | null {
    return this.currentUserId;
  }

  public getDbName(): string {
    return this.resolveDbName();
  }

  private resolveDbName(): string {
    const activeUserId =
      this.currentUserId ||
      offlineAuthManager.getActiveSession()?.userId ||
      (offlineAuthManager.getActiveSession() as any)?.user_id ||
      null;
    return activeUserId
      ? `${this.baseDbName}_${activeUserId}`
      : this.baseDbName;
  }

  private getMemoryStore(): Map<string, PendingDelta> {
    const name = this.getDbName();
    if (!this.memoryStores.has(name)) {
      this.memoryStores.set(name, new Map());
    }
    return this.memoryStores.get(name)!;
  }

  async init(userId?: string): Promise<void> {
    if (userId !== undefined) {
      this.setUserId(userId);
    } else {
      this.dbName = this.resolveDbName();
    }
    if (this.useMemory) return;
    return new Promise((resolve, reject) => {
      const request = window.indexedDB.open(this.getDbName(), 1);
      request.onerror = () => {
        this.useMemory = true; // fallback
        resolve();
      };
      request.onsuccess = () => {
        resolve();
      };
      request.onupgradeneeded = (event: any) => {
        const db = event.target.result;
        if (!db.objectStoreNames.contains(this.storeName)) {
          db.createObjectStore(this.storeName, { keyPath: "deltaId" });
        }
      };
    });
  }

  private getDB(): Promise<IDBDatabase> {
    return new Promise((resolve, reject) => {
      const request = window.indexedDB.open(this.getDbName(), 1);
      request.onerror = () => reject(request.error);
      request.onsuccess = () => resolve(request.result);
    });
  }

  async addDelta(delta: PendingDelta): Promise<void> {
    if (this.useMemory) {
      this.getMemoryStore().set(delta.deltaId, delta);
      return;
    }
    const db = await this.getDB();
    return new Promise((resolve, reject) => {
      const tx = db.transaction(this.storeName, "readwrite");
      const store = tx.objectStore(this.storeName);
      const req = store.put(delta);
      req.onerror = () => reject(req.error);
      req.onsuccess = () => resolve();
    });
  }

  async getDeltas(): Promise<PendingDelta[]> {
    if (this.useMemory) {
      return Array.from(this.getMemoryStore().values());
    }
    const db = await this.getDB();
    return new Promise((resolve, reject) => {
      const tx = db.transaction(this.storeName, "readonly");
      const store = tx.objectStore(this.storeName);
      const req = store.getAll();
      req.onerror = () => reject(req.error);
      req.onsuccess = () => resolve(req.result || []);
    });
  }

  async getDeltasCount(): Promise<number> {
    if (this.useMemory) {
      return this.getMemoryStore().size;
    }
    const db = await this.getDB();
    return new Promise((resolve, reject) => {
      const tx = db.transaction(this.storeName, "readonly");
      const store = tx.objectStore(this.storeName);
      const req = store.count();
      req.onerror = () => reject(req.error);
      req.onsuccess = () => resolve(req.result || 0);
    });
  }

  async clearDeltas(ids: string[]): Promise<void> {
    if (this.useMemory) {
      const store = this.getMemoryStore();
      ids.forEach((id) => store.delete(id));
      return;
    }
    const db = await this.getDB();
    return new Promise((resolve, reject) => {
      const tx = db.transaction(this.storeName, "readwrite");
      const store = tx.objectStore(this.storeName);
      let completed = 0;
      let errored = false;
      if (ids.length === 0) {
        resolve();
        return;
      }
      ids.forEach((id) => {
        const req = store.delete(id);
        req.onerror = () => {
          if (!errored) {
            errored = true;
            reject(req.error);
          }
        };
        req.onsuccess = () => {
          completed++;
          if (completed === ids.length && !errored) {
            resolve();
          }
        };
      });
    });
  }
}

export class ClientSyncEngine {
  private isSyncing = false;
  public dbManager: IndexedDBManager;
  private retryCount = 0;
  private initialRetryDelay = 1000;
  private maxRetryDelay = 30000; // deid-ignore
  private retryTimeoutId: any = null;

  constructor() {
    this.dbManager = new IndexedDBManager();
    if (typeof window !== "undefined") {
      window.addEventListener("online", () => {
        this.flushQueue();
      });
    }
  }

  async queueDelta(delta: Omit<PendingDelta, "deltaId">): Promise<void> {
    const activeSession = offlineAuthManager.getActiveSession();
    const userId = activeSession?.userId || (activeSession as any)?.user_id;
    if (userId) {
      this.dbManager.setUserId(userId);
    }
    const deltaId = `delta_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`;
    const item: PendingDelta = {
      deltaId,
      ...delta,
    };
    await this.dbManager.init();
    await this.dbManager.addDelta(item);
    const syncStore = useSyncStore();
    const count = await this.dbManager.getDeltasCount();
    syncStore.setPendingCount(count);
  }

  async flushQueue(): Promise<void> {
    if (this.isSyncing) return;
    this.isSyncing = true;
    const syncStore = useSyncStore();
    syncStore.setStatus("SYNCING");

    try {
      // Check for active offline session
      const activeSession = offlineAuthManager.getActiveSession();
      if (!activeSession) {
        syncStore.setStatus("ERROR");
        this.isSyncing = false;
        if (typeof window !== "undefined") {
          window.dispatchEvent(
            new CustomEvent("pin-challenge-required", {
              detail: {
                message:
                  "An active offline session is required to process the queue.",
              },
            })
          );
        }
        return;
      }

      const activeUserId =
        activeSession.userId || (activeSession as any)?.user_id;
      if (activeUserId) {
        this.dbManager.setUserId(activeUserId);
      }

      await this.dbManager.init();
      const allDeltas = await this.dbManager.getDeltas();
      if (allDeltas.length === 0) {
        syncStore.setStatus("IDLE");
        syncStore.setPendingCount(0);
        this.isSyncing = false;
        this.resetBackoff();
        return;
      }

      // Group deltas into batches of 50 items
      const batchSize = 50;
      const deltasToSync = allDeltas.slice(0, batchSize);

      const clientBatchId = `batch_${Date.now()}`;
      const response = await fetch("/api/v1/offline/sync-batch", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          client_batch_id: clientBatchId,
          device_id:
            typeof navigator !== "undefined"
              ? navigator.userAgent
              : "NodeJS/Test",
          deltas: deltasToSync,
        }),
      });

      if (response.status === 409) {
        // Conflict detected!
        const data = await response.json();
        syncStore.setStatus("CONFLICT_DETECTED");
        syncStore.setConflict({
          clientBatchId,
          conflictItem: data.conflict || deltasToSync[0],
          clientValue: data.clientValue || deltasToSync[0]?.payload || {},
          serverValue: data.serverValue || {},
        });
        this.isSyncing = false;
        return;
      }

      const data = await response.json().catch(() => ({}));
      if (data.status === "CONFLICT_DETECTED") {
        syncStore.setStatus("CONFLICT_DETECTED");
        syncStore.setConflict({
          clientBatchId,
          conflictItem: data.conflict || deltasToSync[0],
          clientValue: data.clientValue || deltasToSync[0]?.payload || {},
          serverValue: data.serverValue || {},
        });
        this.isSyncing = false;
        return;
      }

      if (response.status === 503) {
        throw new Error("503 Service Unavailable");
      }

      if (!response.ok) {
        throw new Error(`HTTP Error ${response.status}`);
      }

      // Successful sync! Clear these deltas.
      await this.dbManager.clearDeltas(deltasToSync.map((d) => d.deltaId));
      const remainingCount = await this.dbManager.getDeltasCount();
      syncStore.setPendingCount(remainingCount);

      this.resetBackoff();

      if (remainingCount > 0) {
        // Continue draining
        this.isSyncing = false;
        setTimeout(() => this.flushQueue(), 0);
      } else {
        syncStore.setStatus("COMPLETED");
        this.isSyncing = false;
      }
    } catch (err: any) {
      syncStore.setStatus("ERROR");
      this.isSyncing = false;
      this.scheduleRetry();
    }
  }

  async resolveConflict(
    conflictId: string,
    strategy: "SERVER_WIN" | "CLIENT_WIN" | "MANUAL_REVIEW",
    reason: string
  ): Promise<void> {
    const syncStore = useSyncStore();
    syncStore.setStatus("SYNCING");

    try {
      const response = await fetch("/api/v1/offline/resolve-conflict", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          delta_id: conflictId,
          strategy,
          reason_for_change: reason,
        }),
      });

      if (!response.ok) {
        throw new Error(`Conflict resolution failed: ${response.status}`);
      }

      // Successful resolution! Delete the delta from the queue
      await this.dbManager.clearDeltas([conflictId]);
      syncStore.clearConflict();

      const remainingCount = await this.dbManager.getDeltasCount();
      syncStore.setPendingCount(remainingCount);

      if (remainingCount > 0) {
        // Continue draining remaining deltas
        setTimeout(() => this.flushQueue(), 0);
      } else {
        syncStore.setStatus("COMPLETED");
      }
    } catch (err) {
      syncStore.setStatus("ERROR");
      throw err;
    }
  }

  private scheduleRetry() {
    if (this.retryTimeoutId) {
      clearTimeout(this.retryTimeoutId);
    }
    this.retryCount++;
    const delay = Math.min(
      this.initialRetryDelay * Math.pow(2, this.retryCount),
      this.maxRetryDelay
    );
    this.retryTimeoutId = setTimeout(() => {
      this.flushQueue();
    }, delay);
  }

  private resetBackoff() {
    this.retryCount = 0;
    if (this.retryTimeoutId) {
      clearTimeout(this.retryTimeoutId);
      this.retryTimeoutId = null;
    }
  }

  public getRetryCount(): number {
    return this.retryCount;
  }

  public getRetryTimeoutId(): any {
    return this.retryTimeoutId;
  }
}
