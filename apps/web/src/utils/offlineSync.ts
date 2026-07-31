import {
  encryptAESGCM,
  decryptAESGCM,
  deriveSessionKey,
  generateCanonicalSignature,
} from "ui";

export interface OfflineCapturePayload {
  id: string; // unique ID for queue entry (e.g. `subj_diary_timestamp`)
  subject_id: string;
  diary_id: string;
  device_timestamp: string;
  answers: Record<string, any>;
  sequence_number: number;
  client_id: string;
  conflict_strategy: string;
  timestamps?: Record<string, string>;
}

export function openSyncDatabase(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const idb =
      typeof indexedDB !== "undefined"
        ? indexedDB
        : (globalThis as any).indexedDB;
    if (!idb) {
      reject(new Error("IndexedDB is not supported"));
      return;
    }
    const request = idb.open("OfflineSyncDB", 1);
    request.onupgradeneeded = (event: any) => {
      const db = event.target.result;
      if (!db.objectStoreNames.contains("pending_sync_queue")) {
        db.createObjectStore("pending_sync_queue", { keyPath: "id" });
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

async function getAllQueueEntriesRaw(db: IDBDatabase): Promise<any[]> {
  return new Promise((resolve, reject) => {
    const tx = db.transaction("pending_sync_queue", "readonly");
    const store = tx.objectStore("pending_sync_queue");
    const request = store.getAll();
    request.onsuccess = () => resolve(request.result || []);
    request.onerror = () => reject(request.error);
  });
}

export async function queueEproSubmission(
  submission: Omit<OfflineCapturePayload, "sequence_number">,
  sessionToken: string
): Promise<void> {
  const db = await openSyncDatabase();
  const existingEntries = await getAllQueueEntriesRaw(db);
  const nextSeq = existingEntries.length + 1;

  const fullPayload: OfflineCapturePayload = {
    ...submission,
    sequence_number: nextSeq,
  };

  // Derive key from sessionToken
  const derivedKey = await deriveSessionKey(
    sessionToken,
    "offline-capture-salt",
    "offline-capture-info"
  );

  // Generate signature
  const timestamps = fullPayload.timestamps || {};
  // ensure timestamps cover all answers
  for (const key of Object.keys(fullPayload.answers)) {
    if (!timestamps[key]) {
      timestamps[key] = fullPayload.device_timestamp;
    }
  }

  const recordForSigning = {
    deduplication_key: `${fullPayload.subject_id}:${fullPayload.diary_id}`,
    data: fullPayload.answers,
    metadata: {
      timestamps: timestamps,
      modified_by: fullPayload.client_id,
    },
  };

  const signature = await generateCanonicalSignature(recordForSigning, derivedKey);

  // Package record with signature
  const recordToEncrypt = {
    ...fullPayload,
    signature,
    timestamps,
  };

  // Encrypt the entire recordToEncrypt
  const encryptedStr = await encryptAESGCM(recordToEncrypt, derivedKey);

  // Store in IndexedDB
  await new Promise<void>((resolve, reject) => {
    const tx = db.transaction("pending_sync_queue", "readwrite");
    const store = tx.objectStore("pending_sync_queue");
    store.put({
      id: fullPayload.id,
      encryptedData: encryptedStr,
    });
    tx.oncomplete = () => resolve();
    tx.onerror = () => reject(tx.error);
  });
}

export async function flushOfflineQueue(
  sessionToken: string
): Promise<{
  processed_count: number;
  created_count: number;
  updated_count: number;
  ignored_count: number;
  conflict_count: number;
  results: any[];
}> {
  const db = await openSyncDatabase();
  const rawEntries = await getAllQueueEntriesRaw(db);

  if (rawEntries.length === 0) {
    return {
      processed_count: 0,
      created_count: 0,
      updated_count: 0,
      ignored_count: 0,
      conflict_count: 0,
      results: [],
    };
  }

  // Derive key from sessionToken
  const derivedKey = await deriveSessionKey(
    sessionToken,
    "offline-capture-salt",
    "offline-capture-info"
  );

  const decryptedSubmissions = [];
  const successfulIds: string[] = [];

  for (const entry of rawEntries) {
    try {
      const decrypted = await decryptAESGCM(entry.encryptedData, derivedKey);
      decryptedSubmissions.push(decrypted);
      successfulIds.push(entry.id);
    } catch (err) {
      console.error(`Failed to decrypt offline queue entry ${entry.id}:`, err);
      throw new Error(
        `Decryption failed for offline queue item ${entry.id}. Invalid key or session expired.`
      );
    }
  }

  // Format the submissions into BulkSyncPayload format
  const submissions = decryptedSubmissions.map((item) => {
    return {
      subject_id: item.subject_id,
      diary_id: item.diary_id,
      device_timestamp: item.device_timestamp,
      answers: item.answers,
      offline_sync_markers: {
        sequence_number: item.sequence_number,
        client_id: item.client_id,
        conflict_strategy: item.conflict_strategy,
        signature: item.signature,
        timestamps: item.timestamps,
      },
    };
  });

  const response = await fetch("/api/v1/interop/epro/sync", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${sessionToken}`,
    },
    body: JSON.stringify({ submissions }),
  });

  if (!response.ok) {
    throw new Error(`Sync request failed with status: ${response.status}`);
  }

  const resultData = await response.json();

  // Clear successfully synced items from IndexedDB
  const deleteTx = db.transaction("pending_sync_queue", "readwrite");
  const deleteStore = deleteTx.objectStore("pending_sync_queue");
  for (const id of successfulIds) {
    deleteStore.delete(id);
  }
  await new Promise<void>((resolve, reject) => {
    deleteTx.oncomplete = () => resolve();
    deleteTx.onerror = () => reject(deleteTx.error);
  });

  return resultData;
}

// Automatically setup online listener for background flushing
if (typeof window !== "undefined") {
  window.addEventListener("online", async () => {
    try {
      // Lazy load Pinia store to avoid circular/init dependency issues
      const { useAuthStore } = await import("../stores/auth");
      const authStore = useAuthStore();
      if (authStore.token) {
        await flushOfflineQueue(authStore.token);
        console.log("Offline pending sync queue successfully flushed on connectivity restore.");
      }
    } catch (err) {
      console.error("Failed to automatically flush offline pending sync queue:", err);
    }
  });
}
