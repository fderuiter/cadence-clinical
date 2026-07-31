import { useSyncStore } from '../stores/sync';

export interface PendingDelta {
  deltaId: string;
  entityType: string;
  entityId: string;
  action: 'CREATE' | 'UPDATE' | 'SUBMIT';
  payload: Record<string, any>;
  clientTimestampUtc: string;
  reasonForChange: string;
}

export class IndexedDBManager {
  private dbName = 'SyncEngineDB';
  private storeName = 'pending_sync_deltas';
  private useMemory = false;
  private memoryStore: Map<string, PendingDelta> = new Map();

  constructor() {
    if (typeof window === 'undefined' || !window.indexedDB) {
      this.useMemory = true;
    }
  }

  async init(): Promise<void> {
    if (this.useMemory) return;
    return new Promise((resolve, reject) => {
      const request = window.indexedDB.open(this.dbName, 1);
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
          db.createObjectStore(this.storeName, { keyPath: 'deltaId' });
        }
      };
    });
  }

  private getDB(): Promise<IDBDatabase> {
    return new Promise((resolve, reject) => {
      const request = window.indexedDB.open(this.dbName, 1);
      request.onerror = () => reject(request.error);
      request.onsuccess = () => resolve(request.result);
    });
  }

  async addDelta(delta: PendingDelta): Promise<void> {
    if (this.useMemory) {
      this.memoryStore.set(delta.deltaId, delta);
      return;
    }
    const db = await this.getDB();
    return new Promise((resolve, reject) => {
      const tx = db.transaction(this.storeName, 'readwrite');
      const store = tx.objectStore(this.storeName);
      const req = store.put(delta);
      req.onerror = () => reject(req.error);
      req.onsuccess = () => resolve();
    });
  }

  async getDeltas(): Promise<PendingDelta[]> {
    if (this.useMemory) {
      return Array.from(this.memoryStore.values());
    }
    const db = await this.getDB();
    return new Promise((resolve, reject) => {
      const tx = db.transaction(this.storeName, 'readonly');
      const store = tx.objectStore(this.storeName);
      const req = store.getAll();
      req.onerror = () => reject(req.error);
      req.onsuccess = () => resolve(req.result || []);
    });
  }

  async getDeltasCount(): Promise<number> {
    if (this.useMemory) {
      return this.memoryStore.size;
    }
    const db = await this.getDB();
    return new Promise((resolve, reject) => {
      const tx = db.transaction(this.storeName, 'readonly');
      const store = tx.objectStore(this.storeName);
      const req = store.count();
      req.onerror = () => reject(req.error);
      req.onsuccess = () => resolve(req.result || 0);
    });
  }

  async clearDeltas(ids: string[]): Promise<void> {
    if (this.useMemory) {
      ids.forEach(id => this.memoryStore.delete(id));
      return;
    }
    const db = await this.getDB();
    return new Promise((resolve, reject) => {
      const tx = db.transaction(this.storeName, 'readwrite');
      const store = tx.objectStore(this.storeName);
      let completed = 0;
      let errored = false;
      if (ids.length === 0) {
        resolve();
        return;
      }
      ids.forEach(id => {
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
    if (typeof window !== 'undefined') {
      window.addEventListener('online', () => {
        this.flushQueue();
      });
    }
  }

  async queueDelta(delta: Omit<PendingDelta, 'deltaId'>): Promise<void> {
    const deltaId = `delta_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`;
    const item: PendingDelta = {
      deltaId,
      ...delta
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
    syncStore.setStatus('SYNCING');

    try {
      await this.dbManager.init();
      const allDeltas = await this.dbManager.getDeltas();
      if (allDeltas.length === 0) {
        syncStore.setStatus('IDLE');
        syncStore.setPendingCount(0);
        this.isSyncing = false;
        this.resetBackoff();
        return;
      }

      // Group deltas into batches of 50 items
      const batchSize = 50;
      const deltasToSync = allDeltas.slice(0, batchSize);

      const clientBatchId = `batch_${Date.now()}`;
      const response = await fetch('/api/v1/offline/sync-batch', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          client_batch_id: clientBatchId,
          device_id: typeof navigator !== 'undefined' ? navigator.userAgent : 'NodeJS/Test',
          deltas: deltasToSync
        })
      });

      if (response.status === 409) {
        // Conflict detected!
        const data = await response.json();
        syncStore.setStatus('CONFLICT_DETECTED');
        syncStore.setConflict({
          clientBatchId,
          conflictItem: data.conflict || deltasToSync[0],
          clientValue: data.clientValue || deltasToSync[0]?.payload || {},
          serverValue: data.serverValue || {}
        });
        this.isSyncing = false;
        return;
      }

      const data = await response.json().catch(() => ({}));
      if (data.status === 'CONFLICT_DETECTED') {
        syncStore.setStatus('CONFLICT_DETECTED');
        syncStore.setConflict({
          clientBatchId,
          conflictItem: data.conflict || deltasToSync[0],
          clientValue: data.clientValue || deltasToSync[0]?.payload || {},
          serverValue: data.serverValue || {}
        });
        this.isSyncing = false;
        return;
      }

      if (response.status === 503) {
        throw new Error('503 Service Unavailable');
      }

      if (!response.ok) {
        throw new Error(`HTTP Error ${response.status}`);
      }

      // Successful sync! Clear these deltas.
      await this.dbManager.clearDeltas(deltasToSync.map(d => d.deltaId));
      const remainingCount = await this.dbManager.getDeltasCount();
      syncStore.setPendingCount(remainingCount);

      this.resetBackoff();

      if (remainingCount > 0) {
        // Continue draining
        this.isSyncing = false;
        setTimeout(() => this.flushQueue(), 0);
      } else {
        syncStore.setStatus('COMPLETED');
        this.isSyncing = false;
      }
    } catch (err: any) {
      syncStore.setStatus('ERROR');
      this.isSyncing = false;
      this.scheduleRetry();
    }
  }

  async resolveConflict(conflictId: string, strategy: 'SERVER_WIN' | 'CLIENT_WIN' | 'MANUAL_REVIEW', reason: string): Promise<void> {
    const syncStore = useSyncStore();
    syncStore.setStatus('SYNCING');

    try {
      const response = await fetch('/api/v1/offline/resolve-conflict', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          delta_id: conflictId,
          strategy,
          reason_for_change: reason
        })
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
        syncStore.setStatus('COMPLETED');
      }
    } catch (err) {
      syncStore.setStatus('ERROR');
      throw err;
    }
  }

  private scheduleRetry() {
    if (this.retryTimeoutId) {
      clearTimeout(this.retryTimeoutId);
    }
    this.retryCount++;
    const delay = Math.min(this.initialRetryDelay * Math.pow(2, this.retryCount), this.maxRetryDelay);
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
