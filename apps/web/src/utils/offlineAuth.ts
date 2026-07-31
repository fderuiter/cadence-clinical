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
    const cryptoObj = getCrypto();
    const salt = cryptoObj.getRandomValues(new Uint8Array(16));
    const iv = cryptoObj.getRandomValues(new Uint8Array(16));

    const key = await deriveKey(pin, salt);
    const { ciphertext, iv: finalIv } = await encryptData(session, key, iv);

    const db = await openDatabase();
    return new Promise<void>((resolve, reject) => {
      const tx = db.transaction("offline_auth_keys", "readwrite");
      const store = tx.objectStore("offline_auth_keys");
      store.put({
        id: "session",
        salt,
        iv: finalIv,
        ciphertext,
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
  async unlockOfflineSession(pin: string): Promise<OfflineSession> {
    const db = await openDatabase();
    const record = await new Promise<any>((resolve, reject) => {
      const tx = db.transaction("offline_auth_keys", "readonly");
      const store = tx.objectStore("offline_auth_keys");
      const request = store.get("session");
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

    const { salt, iv, ciphertext } = record;
    const key = await deriveKey(pin, salt);
    const session = (await decryptData(ciphertext, key, iv)) as OfflineSession;

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
  }

  /**
   * Clears the stored offline session from IndexedDB and memory.
   */
  async clearOfflineSession(): Promise<void> {
    const db = await openDatabase();
    return new Promise<void>((resolve, reject) => {
      const tx = db.transaction("offline_auth_keys", "readwrite");
      const store = tx.objectStore("offline_auth_keys");
      store.delete("session");
      tx.oncomplete = () => {
        this.activeSession = null;
        resolve();
      };
      tx.onerror = () => {
        reject(tx.error);
      };
    });
  }
}
