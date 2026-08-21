import { deriveKey, encryptData, decryptData } from "../services/cryptoStore";
import { apiClient } from "../api/apiClient";

export interface OfflineSession {
  userId: string;
  userRoles: string[];
  offlineToken: string;
  createdAt: string;
  maxOfflineHours: number;
}

const getCrypto = (): Crypto => {
  if (typeof window !== "undefined" && window.crypto) {
    return window.crypto;
  }
  return globalThis.crypto;
};

function openDatabase(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const idb =
      typeof indexedDB !== "undefined"
        ? indexedDB
        : (globalThis as any).indexedDB;
    if (!idb) {
      reject(new Error("IndexedDB is not supported"));
      return;
    }
    const request = idb.open("OfflineAuthDB", 1);
    request.onupgradeneeded = (event: any) => {
      const db = event.target.result;
      if (!db.objectStoreNames.contains("offline_auth_keys")) {
        db.createObjectStore("offline_auth_keys", { keyPath: "id" });
      }
    };
    request.onsuccess = (event: any) => {
      resolve(event.target.result);
    };
    request.onerror = (event: any) => {
      reject(event.target.error);
    };
  });
}

export class OfflineAuthManager {
  private activeSession: OfflineSession | null = null;

  constructor() {
    this.setupOnlineListener();
  }

  /**
   * Retrieves the in-memory active decrypted session.
   */
  getActiveSession(): OfflineSession | null {
    return this.activeSession;
  }

  /**
   * Sets the active session in-memory manually (useful for testing or session restoration).
   */
  setActiveSession(session: OfflineSession | null): void {
    this.activeSession = session;
  }

  /**
   * Automatically re-validates the token when the browser detects transition back to online.
   */
  setupOnlineListener(): void {
    if (typeof window !== "undefined") {
      window.addEventListener("online", async () => {
        if (this.activeSession && this.activeSession.offlineToken) {
          try {
            await apiClient.post("/api/v1/auth/offline-verify", {
              token: this.activeSession.offlineToken,
            });
            console.log(
              "Offline session successfully re-validated with gateway."
            );
          } catch (err) {
            console.error(
              "Failed to re-validate offline session with gateway:",
              err
            );
          }
        }
      });
    }
  }

  /**
   * Encrypts and stores the offline session payload into IndexedDB using AES-GCM 256-bit.
   * Key material is derived from the user PIN and a 16-byte random salt.
   */
  async storeEncryptedSession(
    pin: string,
    session: OfflineSession
  ): Promise<void> {
    if (!/^\d{4,6}$/.test(pin)) {
      throw new Error("PIN must be between 4 and 6 digits");
    }

    const cryptoObj = getCrypto();
    const salt = cryptoObj.getRandomValues(new Uint8Array(16));
    const iv = cryptoObj.getRandomValues(new Uint8Array(16));

    const key = await deriveKey(pin, salt);
    const { ciphertext, iv: finalIv } = await encryptData(session, key, iv);

    const db = await openDatabase();
    const id = session.userId ? `session_${session.userId}` : "session";

    return new Promise<void>((resolve, reject) => {
      const tx = db.transaction("offline_auth_keys", "readwrite");
      const store = tx.objectStore("offline_auth_keys");
      store.put({
        id,
        salt,
        iv: finalIv,
        ciphertext,
        failedAttempts: 0,
        locked: false,
      });
      tx.oncomplete = () => {
        this.activeSession = session;
        resolve();
      };
      tx.onerror = () => {
        reject(tx.error);
      };
    });
  }

  /**
   * Alias of storeEncryptedSession to satisfy dynamic interface variations.
   */
  async saveOfflineSession(
    pin: string,
    session: OfflineSession
  ): Promise<void> {
    return this.storeEncryptedSession(pin, session);
  }

  /**
   * Unlocks the offline session payload from IndexedDB by decrypting it using the user PIN.
   * Asserts the session age is within maxOfflineHours (or falls back to 72 hours).
   */
  async unlockOfflineSession(
    pin: string,
    userId?: string
  ): Promise<OfflineSession> {
    const db = await openDatabase();

    let recordId = userId ? `session_${userId}` : "session";

    if (!userId) {
      // Find the first available record with "session_" prefix or fall back to "session"
      const keys = await new Promise<any[]>((resolve, reject) => {
        const tx = db.transaction("offline_auth_keys", "readonly");
        const store = tx.objectStore("offline_auth_keys");
        const request = store.getAllKeys();
        request.onsuccess = () => resolve(request.result || []);
        request.onerror = () => reject(request.error);
      });

      const sessionKey =
        keys.find((k) => typeof k === "string" && k.startsWith("session_")) ||
        keys[0];
      if (sessionKey) {
        recordId = sessionKey;
      }
    }

    const record = await new Promise<any>((resolve, reject) => {
      const tx = db.transaction("offline_auth_keys", "readonly");
      const store = tx.objectStore("offline_auth_keys");
      const request = store.get(recordId);
      request.onsuccess = () => {
        resolve(request.result || null);
      };
      request.onerror = () => {
        reject(request.error);
      };
    });

    if (!record) {
      throw new Error("No offline session found");
    }

    if (
      record.locked === true ||
      (record.failedAttempts !== undefined && record.failedAttempts >= 5)
    ) {
      throw new Error("Key recovery locked. Too many failed attempts.");
    }

    const { salt, iv, ciphertext } = record;

    try {
      const key = await deriveKey(pin, salt);
      const session = (await decryptData(
        ciphertext,
        key,
        iv
      )) as OfflineSession;

      // Reset failed attempts on successful decryption
      record.failedAttempts = 0;
      record.locked = false;

      await new Promise<void>((resolve, reject) => {
        const tx = db.transaction("offline_auth_keys", "readwrite");
        const store = tx.objectStore("offline_auth_keys");
        store.put(record);
        tx.oncomplete = () => resolve();
        tx.onerror = () => reject(tx.error);
      });

      // Assert offline session age is within maximum allowed offline duration (default 72 hours)
      const createdAt = new Date(session.createdAt);
      const now = new Date();
      const diffMs = now.getTime() - createdAt.getTime();
      const diffHours = diffMs / (1000 * 60 * 60);
      const maxHours =
        session.maxOfflineHours !== undefined ? session.maxOfflineHours : 72;

      if (diffHours > maxHours) {
        throw new Error("Offline session expired");
      }

      this.activeSession = session;
      return session;
    } catch (err: any) {
      // Increment failed attempts on decryption error
      const failedAttempts = (record.failedAttempts || 0) + 1;
      const locked = failedAttempts >= 5;

      record.failedAttempts = failedAttempts;
      record.locked = locked;

      await new Promise<void>((resolve, reject) => {
        const tx = db.transaction("offline_auth_keys", "readwrite");
        const store = tx.objectStore("offline_auth_keys");
        store.put(record);
        tx.oncomplete = () => resolve();
        tx.onerror = () => reject(tx.error);
      });

      if (locked) {
        throw new Error("Key recovery locked. Too many failed attempts.", {
          cause: err,
        });
      }

      throw err;
    }
  }

  /**
   * Resets failed attempts and locks for a user session (e.g. for testing/admin recovery).
   */
  async resetFailedAttempts(userId: string): Promise<void> {
    const db = await openDatabase();
    const recordId = `session_${userId}`;
    const record = await new Promise<any>((resolve, reject) => {
      const tx = db.transaction("offline_auth_keys", "readonly");
      const store = tx.objectStore("offline_auth_keys");
      const request = store.get(recordId);
      request.onsuccess = () => resolve(request.result || null);
      request.onerror = () => reject(request.error);
    });

    if (record) {
      record.failedAttempts = 0;
      record.locked = false;
      await new Promise<void>((resolve, reject) => {
        const tx = db.transaction("offline_auth_keys", "readwrite");
        const store = tx.objectStore("offline_auth_keys");
        store.put(record);
        tx.oncomplete = () => resolve();
        tx.onerror = () => reject(tx.error);
      });
    }
  }

  /**
   * Clears the stored offline session from IndexedDB and memory.
   */
  async clearOfflineSession(userId?: string): Promise<void> {
    const db = await openDatabase();
    const idToDelete =
      userId ||
      (this.activeSession?.userId
        ? `session_${this.activeSession.userId}`
        : "session");
    return new Promise<void>((resolve, reject) => {
      const tx = db.transaction("offline_auth_keys", "readwrite");
      const store = tx.objectStore("offline_auth_keys");

      store.delete(idToDelete);

      tx.oncomplete = () => {
        this.activeSession = null;
        resolve();
      };
      tx.onerror = () => {
        reject(tx.error);
      };
    });
  }

  /**
   * Clears all stored offline sessions in the database.
   */
  async clearAllOfflineSessions(): Promise<void> {
    const db = await openDatabase();
    return new Promise<void>((resolve, reject) => {
      const tx = db.transaction("offline_auth_keys", "readwrite");
      const store = tx.objectStore("offline_auth_keys");
      const req = store.clear();
      req.onsuccess = () => {
        this.activeSession = null;
        resolve();
      };
      req.onerror = () => {
        reject(req.error);
      };
    });
  }

  /**
   * Returns list of stored user IDs.
   */
  async getStoredUserIds(): Promise<string[]> {
    const db = await openDatabase();
    const keys = await new Promise<any[]>((resolve, reject) => {
      const tx = db.transaction("offline_auth_keys", "readonly");
      const store = tx.objectStore("offline_auth_keys");
      const request = store.getAllKeys();
      request.onsuccess = () => resolve(request.result || []);
      request.onerror = () => reject(request.error);
    });

    return keys
      .filter((k): k is string => typeof k === "string")
      .map((k) => {
        if (k.startsWith("session_")) {
          return k.slice("session_".length);
        }
        return k; // e.g. "session"
      });
  }

  /**
   * Retrieves lock and attempt state for a user session.
   */
  async getSessionMetadata(
    userId: string
  ): Promise<{ failedAttempts: number; locked: boolean } | null> {
    const db = await openDatabase();
    const recordId =
      userId.startsWith("session_") || userId === "session"
        ? userId
        : `session_${userId}`;
    return new Promise<any>((resolve, reject) => {
      const tx = db.transaction("offline_auth_keys", "readonly");
      const store = tx.objectStore("offline_auth_keys");
      const request = store.get(recordId);
      request.onsuccess = () => {
        if (request.result) {
          resolve({
            failedAttempts: request.result.failedAttempts || 0,
            locked: !!request.result.locked,
          });
        } else {
          resolve(null);
        }
      };
      request.onerror = () => reject(request.error);
    });
  }
}

export const offlineAuthManager = new OfflineAuthManager();
