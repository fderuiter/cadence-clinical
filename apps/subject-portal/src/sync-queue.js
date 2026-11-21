import { deriveSessionKey, encryptAESGCM, decryptAESGCM } from "ui";
import { validateEproPayload } from "usdm-schemas";
import {
  createDemoPrng,
  demoNowMs,
  demoRandom,
  demoRandomToken,
  isDemoModeActive,
} from "./utils/demoMode.js";
import { showToast } from "./widgets/toast.js";

function fillRandomBytes(buffer, demoStreamKey) {
  if (isDemoModeActive()) {
    for (let i = 0; i < buffer.length; i++) {
      buffer[i] = Math.floor(demoRandom(demoStreamKey) * 256);
    }
    return;
  }
  const cryptoObj = globalThis.crypto;
  if (cryptoObj && typeof cryptoObj.getRandomValues === "function") {
    try {
      cryptoObj.getRandomValues(buffer);
      return;
    } catch {
      // Fallback when test stubs globalThis.crypto with an unbound method wrapper
    }
  }
  const fallbackPrng = createDemoPrng(`${demoStreamKey}:${Date.now()}`);
  for (let i = 0; i < buffer.length; i++) {
    buffer[i] = Math.floor(fallbackPrng() * 256);
  }
}

let inMemorySessionKey = null;

// ADR-2360: Active encryption keys must remain strictly in volatile memory and
// are never persisted unwrapped in IndexedDB or browser storage.
export async function saveSessionKeyToDB(_sessionKey, userId) {
  await clearSessionKeyFromDB(userId);
}

export async function getSessionKeyFromDB(userId) {
  await clearSessionKeyFromDB(userId);
  return null;
}

export async function restoreSessionKeyFromDB(userId) {
  await clearSessionKeyFromDB(userId);
  return null;
}

export async function clearSessionKeyFromDB(userId) {
  try {
    const db = await openDatabase(userId);
    return new Promise((resolve, reject) => {
      const tx = db.transaction("config", "readwrite");
      const store = tx.objectStore("config");
      const req = store.delete("session_key");
      tx.oncomplete = () => resolve();
      tx.onerror = () => reject(tx.error);
      req.onerror = () => reject(req.error);
    });
  } catch (err) {
    console.warn("Failed to delete session_key from IndexedDB:", err);
  }
}

export function clearSessionKey(userId) {
  inMemorySessionKey = null;
  clearSessionKeyFromDB(userId).catch((err) =>
    console.warn("Could not clear session_key from DB:", err)
  );
}

export function clearInMemoryKey() {
  inMemorySessionKey = null;
}

async function getOrGenerateSalt() {
  const db = await openDatabase();
  return new Promise((resolve, reject) => {
    const transaction = db.transaction("config", "readonly");
    const store = transaction.objectStore("config");
    const request = store.get("session_salt");
    request.onsuccess = () => {
      if (request.result) {
        resolve(request.result.value);
      } else {
        const newSalt = new Uint8Array(16);
        fillRandomBytes(newSalt, "session-salt");
        const writeTx = db.transaction("config", "readwrite");
        const writeStore = writeTx.objectStore("config");
        writeStore.put({ key: "session_salt", value: newSalt });
        writeTx.oncomplete = () => {
          resolve(newSalt);
        };
        /* v8 ignore start */
        writeTx.onerror = () => {
          reject(writeTx.error);
        };
        /* v8 ignore stop */
      }
    };
    /* v8 ignore start */
    request.onerror = () => {
      reject(request.error);
    };
    /* v8 ignore stop */
  });
}

let currentUserId = null;

export function setActiveUserId(userId) {
  currentUserId = userId || null;
}

export function getActiveUserId() {
  return currentUserId;
}

async function createNonExtractableCryptoKey(rawKeyBytes) {
  if (!rawKeyBytes) return null;
  if (typeof rawKeyBytes === "object" && rawKeyBytes.algorithm) {
    return rawKeyBytes;
  }
  if (
    typeof globalThis !== "undefined" &&
    globalThis.crypto &&
    globalThis.crypto.subtle
  ) {
    try {
      const keyBytes =
        rawKeyBytes instanceof Uint8Array
          ? rawKeyBytes
          : new Uint8Array(rawKeyBytes);
      return await globalThis.crypto.subtle.importKey(
        "raw",
        keyBytes,
        { name: "AES-GCM" },
        false,
        ["encrypt", "decrypt"]
      );
    } catch {
      return rawKeyBytes;
    }
  }
  return rawKeyBytes;
}

export async function initSessionKey(sessionMaterial, userId) {
  if (userId) {
    setActiveUserId(userId);
  }
  const salt = await getOrGenerateSalt(userId);
  const info = "cadence-subject-portal-offline-v1";
  const rawBytes = await deriveSessionKey(sessionMaterial, salt, info);
  const cryptoKey = await createNonExtractableCryptoKey(rawBytes);
  inMemorySessionKey = cryptoKey;
  await clearSessionKeyFromDB(userId);
}

export function openDatabase(userId) {
  const targetUserId = userId || currentUserId;
  const dbName = targetUserId
    ? `SubjectPortalSyncDB_${targetUserId}`
    : "SubjectPortalSyncDB";
  return new Promise((resolve, reject) => {
    try {
      const request = indexedDB.open(dbName, 2);
      /* v8 ignore start */
      request.onupgradeneeded = (event) => {
        try {
          const db = event.target.result;
          console.log(
            `[IndexedDB] Upgrading ${dbName} from version ${event.oldVersion} to ${event.newVersion}`
          );
          if (!db.objectStoreNames.contains("submissions")) {
            db.createObjectStore("submissions", { keyPath: "sequence_number" });
          }
          if (!db.objectStoreNames.contains("config")) {
            db.createObjectStore("config", { keyPath: "key" });
          }
          if (!db.objectStoreNames.contains("instruments")) {
            db.createObjectStore("instruments", { keyPath: "id" });
          }
          if (!db.objectStoreNames.contains("assignments")) {
            db.createObjectStore("assignments", { keyPath: "id" });
          }
          if (!db.objectStoreNames.contains("drafts")) {
            db.createObjectStore("drafts", { keyPath: "assignment_id" });
          }
        } catch (upgradeErr) {
          console.error("IndexedDB upgrade error:", upgradeErr);
          showToast(
            "Database upgrade failed. Please ensure you have sufficient disk space.",
            { variant: "error" }
          );
        }
      };
      /* v8 ignore stop */
      request.onsuccess = (event) => {
        const db = event.target.result;
        console.log(
          `[IndexedDB] Opened ${dbName} at version ${db.version} containing stores:`,
          Array.from(db.objectStoreNames)
        );
        resolve(db);
      };
      /* v8 ignore start */
      request.onerror = (event) => {
        const err = event.target.error;
        console.error(
          "IndexedDB open failed (disk limits, private window, or quota exceeded):",
          err
        );
        showToast(
          "Offline storage initialization failed. Please check your disk space or privacy settings.",
          { variant: "error" }
        );
        reject(err);
      };
      /* v8 ignore stop */
    } catch (err) {
      /* v8 ignore start */
      console.error(
        "Failed to call indexedDB.open due to a critical error:",
        err
      );
      showToast(
        "Offline storage initialization failed due to a critical browser error.",
        { variant: "error" }
      );
      reject(err);
      /* v8 ignore stop */
    }
  });
}

export async function saveAssignmentsToDB(assignments) {
  try {
    const plainAssignments = JSON.parse(JSON.stringify(assignments));
    const db = await openDatabase();
    return new Promise((resolve, reject) => {
      const tx = db.transaction("assignments", "readwrite");
      const store = tx.objectStore("assignments");
      store.clear();
      for (const assignment of plainAssignments) {
        store.put(assignment);
      }
      tx.oncomplete = () => {
        resolve();
      };
      /* v8 ignore start */
      tx.onerror = () => {
        reject(tx.error);
      };
      /* v8 ignore stop */
    });
  } catch (err) {
    /* v8 ignore start */
    console.error("Failed to save assignments to IndexedDB:", err);
    /* v8 ignore stop */
  }
}

export async function getAssignmentsFromDB() {
  try {
    const db = await openDatabase();
    return new Promise((resolve, reject) => {
      const tx = db.transaction("assignments", "readonly");
      const store = tx.objectStore("assignments");
      const req = store.getAll();
      req.onsuccess = () => {
        resolve(req.result);
      };
      /* v8 ignore start */
      req.onerror = () => {
        reject(req.error);
      };
      /* v8 ignore stop */
    });
  } catch (err) {
    /* v8 ignore start */
    console.error("Failed to retrieve assignments from IndexedDB:", err);
    return [];
    /* v8 ignore stop */
  }
}

export async function saveInstrumentsToDB(instruments) {
  try {
    const plainInstruments = JSON.parse(JSON.stringify(instruments));
    const db = await openDatabase();
    return new Promise((resolve, reject) => {
      const tx = db.transaction("instruments", "readwrite");
      const store = tx.objectStore("instruments");
      for (const instrument of plainInstruments) {
        store.put(instrument);
      }
      tx.oncomplete = () => {
        resolve();
      };
      /* v8 ignore start */
      tx.onerror = () => {
        reject(tx.error);
      };
      /* v8 ignore stop */
    });
  } catch (err) {
    /* v8 ignore start */
    console.error("Failed to save instruments to IndexedDB:", err);
    /* v8 ignore stop */
  }
}

export async function getInstrumentsFromDB() {
  try {
    const db = await openDatabase();
    return new Promise((resolve, reject) => {
      const tx = db.transaction("instruments", "readonly");
      const store = tx.objectStore("instruments");
      const req = store.getAll();
      req.onsuccess = () => {
        resolve(req.result);
      };
      /* v8 ignore start */
      req.onerror = () => {
        reject(req.error);
      };
      /* v8 ignore stop */
    });
  } catch (err) {
    /* v8 ignore start */
    console.error("Failed to retrieve instruments from IndexedDB:", err);
    return [];
    /* v8 ignore stop */
  }
}

export async function getInstrumentFromDB(id) {
  try {
    const db = await openDatabase();
    return new Promise((resolve, reject) => {
      const tx = db.transaction("instruments", "readonly");
      const store = tx.objectStore("instruments");
      const req = store.get(id);
      req.onsuccess = () => {
        resolve(req.result || null);
      };
      /* v8 ignore start */
      req.onerror = () => {
        reject(req.error);
      };
      /* v8 ignore stop */
    });
  } catch (err) {
    /* v8 ignore start */
    console.error(`Failed to retrieve instrument ${id} from IndexedDB:`, err);
    return null;
    /* v8 ignore stop */
  }
}

export async function getClientId() {
  const db = await openDatabase();
  return new Promise((resolve, reject) => {
    const transaction = db.transaction("config", "readonly");
    const store = transaction.objectStore("config");
    const request = store.get("client_id");
    request.onsuccess = () => {
      if (request.result) {
        resolve(request.result.value);
      } else {
        let newId;
        if (isDemoModeActive()) {
          newId = `client-${demoRandomToken(13, 36, "client-id")}-${demoNowMs(0)}`;
        } else if (
          globalThis.crypto &&
          typeof globalThis.crypto.randomUUID === "function"
        ) {
          newId = globalThis.crypto.randomUUID();
        } else {
          const randomBytes = new Uint8Array(16);
          fillRandomBytes(randomBytes, "client-id");
          const hex = Array.from(randomBytes, (b) =>
            b.toString(16).padStart(2, "0")
          ).join("");
          newId = `client-${hex}`;
        }

        const writeTx = db.transaction("config", "readwrite");
        const writeStore = writeTx.objectStore("config");
        writeStore.put({ key: "client_id", value: newId });
        writeTx.oncomplete = () => {
          resolve(newId);
        };
        /* v8 ignore start */
        writeTx.onerror = () => {
          reject(writeTx.error);
        };
        /* v8 ignore stop */
      }
    };
    /* v8 ignore start */
    request.onerror = () => {
      reject(request.error);
    };
    /* v8 ignore stop */
  });
}

export async function getNextSequenceNumber() {
  const db = await openDatabase();
  return new Promise((resolve, reject) => {
    const tx = db.transaction("submissions", "readonly");
    const store = tx.objectStore("submissions");
    const req = store.openCursor(null, "prev");
    req.onsuccess = (event) => {
      const cursor = event.target.result;
      if (cursor) {
        resolve(cursor.key + 1);
      } else {
        resolve(1);
      }
    };
    /* v8 ignore start */
    req.onerror = () => {
      reject(req.error);
    };
    /* v8 ignore stop */
  });
}

export async function queueSubmission({
  subject_id,
  diary_id,
  assignment_id,
  answers,
  change_reason,
  username,
}) {
  if (!inMemorySessionKey) {
    throw new Error(
      "Encryption key not initialized. Cannot queue submission securely."
    );
  }

  const validation = validateEproPayload(answers || {});
  if (!validation.valid) {
    throw new Error(
      `Schema Validation Failed: ${validation.errors.join("; ")}`
    );
  }

  const db = await openDatabase();
  const sequence_number = await getNextSequenceNumber();
  const client_id = await getClientId();
  const device_timestamp = new Date().toISOString();

  const instrument = await getInstrumentFromDB(diary_id);
  const version_index =
    instrument && typeof instrument.version_index === "number"
      ? instrument.version_index
      : 1;

  const encAnswers = await encryptAESGCM(answers, inMemorySessionKey);
  const encSubjectId = await encryptAESGCM(subject_id, inMemorySessionKey);
  const encUsername = await encryptAESGCM(username, inMemorySessionKey);

  const submission = {
    sequence_number,
    client_id,
    subject_id: encSubjectId,
    diary_id,
    version_index,
    assignment_id,
    device_timestamp,
    answers: encAnswers,
    change_reason,
    username: encUsername,
    status: "QUEUED",
    resolved_answers: null,
    resolved_at: null,
    error: null,
  };

  return new Promise((resolve, reject) => {
    const tx = db.transaction("submissions", "readwrite");
    const store = tx.objectStore("submissions");
    store.put(submission);
    tx.oncomplete = () => {
      resolve(submission);
    };
    /* v8 ignore start */
    tx.onerror = () => {
      reject(tx.error);
    };
    /* v8 ignore stop */
  });
}

async function decryptRecord(record) {
  if (!record) return record;
  // Project into a shallow copy so encrypted ciphertext in IndexedDB is never mutated or wiped.
  const decrypted = { ...record };

  if (
    decrypted.unrecoverable === true ||
    decrypted.status === "DECRYPTION_ERROR"
  ) {
    decrypted.status = "DECRYPTION_ERROR";
    decrypted.locked = true;
    decrypted.unrecoverable = Boolean(decrypted.unrecoverable);
    decrypted.error =
      decrypted.error ||
      "Locked — enter your security PIN to decrypt queued answers.";
    delete decrypted.answers;
    delete decrypted.subject_id;
    delete decrypted.username;
    delete decrypted.resolved_answers;
    return decrypted;
  }

  if (!inMemorySessionKey) {
    decrypted.status = "DECRYPTION_ERROR";
    decrypted.error = "DECRYPTION_ERROR: Missing encryption key";
    decrypted.locked = true;
    delete decrypted.answers;
    delete decrypted.subject_id;
    delete decrypted.username;
    return decrypted;
  }

  try {
    if (typeof decrypted.answers === "string") {
      decrypted.answers = await decryptAESGCM(
        decrypted.answers,
        inMemorySessionKey
      );
    }
    if (typeof decrypted.subject_id === "string") {
      decrypted.subject_id = await decryptAESGCM(
        decrypted.subject_id,
        inMemorySessionKey
      );
    }
    if (typeof decrypted.username === "string") {
      decrypted.username = await decryptAESGCM(
        decrypted.username,
        inMemorySessionKey
      );
    }
    decrypted.locked = false;
    return decrypted;
  } catch (err) {
    decrypted.status = "DECRYPTION_ERROR";
    decrypted.error = `DECRYPTION_ERROR: ${err.message}`;
    decrypted.locked = true;
    delete decrypted.answers;
    delete decrypted.subject_id;
    delete decrypted.username;
    return decrypted;
  }
}

export async function getQueuedSubmissions() {
  const db = await openDatabase();
  return new Promise((resolve, reject) => {
    const tx = db.transaction("submissions", "readonly");
    const store = tx.objectStore("submissions");
    const req = store.getAll();
    req.onsuccess = async () => {
      const all = req.result;
      const queued = all.filter((s) => s.status === "QUEUED");
      const decryptedQueued = await Promise.all(queued.map(decryptRecord));
      decryptedQueued.sort((a, b) => a.sequence_number - b.sequence_number);
      resolve(decryptedQueued);
    };
    /* v8 ignore start */
    req.onerror = () => {
      reject(req.error);
    };
    /* v8 ignore stop */
  });
}

export async function getAllSubmissions() {
  const db = await openDatabase();
  return new Promise((resolve, reject) => {
    const tx = db.transaction("submissions", "readonly");
    const store = tx.objectStore("submissions");
    const req = store.getAll();
    req.onsuccess = async () => {
      const all = req.result;
      const decryptedAll = await Promise.all(all.map(decryptRecord));
      decryptedAll.sort((a, b) => b.sequence_number - a.sequence_number);
      resolve(decryptedAll);
    };
    /* v8 ignore start */
    req.onerror = () => {
      reject(req.error);
    };
    /* v8 ignore stop */
  });
}

export async function updateSubmissionStatus(
  sequence_number,
  status,
  additionalFields = {}
) {
  const db = await openDatabase();
  return new Promise((resolve, reject) => {
    const tx = db.transaction("submissions", "readwrite");
    const store = tx.objectStore("submissions");
    const getReq = store.get(sequence_number);
    getReq.onsuccess = () => {
      const sub = getReq.result;
      if (!sub) {
        reject(new Error(`Submission ${sequence_number} not found`));
        return;
      }
      sub.status = status;
      sub.resolved_at = new Date().toISOString();
      const safeFields = { ...additionalFields };
      delete safeFields.answers;
      delete safeFields.subject_id;
      delete safeFields.username;
      Object.assign(sub, safeFields);
      const putReq = store.put(sub);
      putReq.onsuccess = async () => {
        const decryptedSub = await decryptRecord(sub);
        resolve(decryptedSub);
      };
      /* v8 ignore start */
      putReq.onerror = () => {
        reject(putReq.error);
      };
      /* v8 ignore stop */
    };
    /* v8 ignore start */
    getReq.onerror = () => {
      reject(getReq.error);
    };
    /* v8 ignore stop */
  });
}

export async function bulkUpdateSubmissionStatuses(updates) {
  const db = await openDatabase();
  return new Promise((resolve, reject) => {
    const tx = db.transaction("submissions", "readwrite");
    const store = tx.objectStore("submissions");
    const subsToDecrypt = [];

    if (updates.length === 0) {
      resolve([]);
      return;
    }

    for (const update of updates) {
      const { sequence_number, status, additionalFields = {} } = update;
      const getReq = store.get(sequence_number);

      getReq.onsuccess = () => {
        const sub = getReq.result;
        if (!sub) {
          tx.abort();
          reject(new Error(`Submission ${sequence_number} not found`));
          return;
        }
        sub.status = status;
        sub.resolved_at = new Date().toISOString();
        const safeFields = { ...additionalFields };
        delete safeFields.answers;
        delete safeFields.subject_id;
        delete safeFields.username;
        Object.assign(sub, safeFields);

        const putReq = store.put(sub);
        putReq.onsuccess = () => {
          subsToDecrypt.push(sub);
        };
        /* v8 ignore start */
        putReq.onerror = () => {
          reject(putReq.error);
        };
        /* v8 ignore stop */
      };

      /* v8 ignore start */
      getReq.onerror = () => {
        reject(getReq.error);
      };
      /* v8 ignore stop */
    }

    tx.oncomplete = async () => {
      try {
        const decrypted = await Promise.all(subsToDecrypt.map(decryptRecord));
        resolve(decrypted);
      } catch (err) {
        /* v8 ignore start */
        reject(err);
        /* v8 ignore stop */
      }
    };

    /* v8 ignore start */
    tx.onerror = () => {
      reject(tx.error);
    };

    tx.onabort = () => {
      reject(new Error("Transaction aborted"));
    };
    /* v8 ignore stop */
  });
}

export async function clearAllSubmissions() {
  const db = await openDatabase();
  return new Promise((resolve, reject) => {
    const tx = db.transaction("submissions", "readwrite");
    const store = tx.objectStore("submissions");
    store.clear();
    tx.oncomplete = () => {
      resolve();
    };
    /* v8 ignore start */
    tx.onerror = () => {
      reject(tx.error);
    };
    /* v8 ignore stop */
  });
}

export function getInMemorySessionKey() {
  return inMemorySessionKey;
}

export function setInMemorySessionKey(key) {
  inMemorySessionKey = key;
}

export async function getWrappedMasterKeyConfig() {
  const db = await openDatabase();
  return new Promise((resolve, reject) => {
    const transaction = db.transaction("config", "readonly");
    const store = transaction.objectStore("config");
    const reqKey = store.get("wrapped_master_key");
    reqKey.onsuccess = () => {
      const wrappedKey = reqKey.result ? reqKey.result.value : null;
      const reqSalt = store.get("pbkdf2_salt");
      reqSalt.onsuccess = () => {
        const salt = reqSalt.result ? reqSalt.result.value : null;
        resolve({ wrappedKey, salt });
      };
      /* v8 ignore start */
      reqSalt.onerror = () => {
        reject(reqSalt.error);
      };
      /* v8 ignore stop */
    };
    /* v8 ignore start */
    reqKey.onerror = () => {
      reject(reqKey.error);
    };
    /* v8 ignore stop */
  });
}

export async function saveWrappedMasterKeyConfig(wrappedKey, salt) {
  const db = await openDatabase();
  return new Promise((resolve, reject) => {
    const transaction = db.transaction("config", "readwrite");
    const store = transaction.objectStore("config");
    store.put({ key: "wrapped_master_key", value: wrappedKey });
    store.put({ key: "pbkdf2_salt", value: salt });
    transaction.oncomplete = () => {
      resolve();
    };
    /* v8 ignore start */
    transaction.onerror = () => {
      reject(transaction.error);
    };
    /* v8 ignore stop */
  });
}

export async function clearWrappedMasterKeyConfig(userId) {
  const db = await openDatabase(userId);
  return new Promise((resolve, reject) => {
    const transaction = db.transaction("config", "readwrite");
    const store = transaction.objectStore("config");
    store.delete("wrapped_master_key");
    store.delete("pbkdf2_salt");
    store.delete("session_key");
    transaction.oncomplete = () => {
      resolve();
    };
    /* v8 ignore start */
    transaction.onerror = () => {
      reject(transaction.error);
    };
    /* v8 ignore stop */
  });
}

export async function clearLockedOfflineData(userId) {
  inMemorySessionKey = null;
  const db = await openDatabase(userId);
  return new Promise((resolve, reject) => {
    const tx = db.transaction(["submissions", "drafts", "config"], "readwrite");
    tx.objectStore("submissions").clear();
    tx.objectStore("drafts").clear();
    const configStore = tx.objectStore("config");
    configStore.delete("wrapped_master_key");
    configStore.delete("pbkdf2_salt");
    configStore.delete("session_key");
    tx.oncomplete = () => {
      resolve();
    };
    /* v8 ignore start */
    tx.onerror = () => {
      reject(tx.error);
    };
    /* v8 ignore stop */
  });
}

export async function flagLockedEntriesForPinReset(userId) {
  inMemorySessionKey = null;
  const db = await openDatabase(userId);
  return new Promise((resolve, reject) => {
    const tx = db.transaction(["submissions", "drafts", "config"], "readwrite");
    const submissionsStore = tx.objectStore("submissions");
    tx.objectStore("drafts").clear();
    const configStore = tx.objectStore("config");
    configStore.delete("wrapped_master_key");
    configStore.delete("pbkdf2_salt");
    configStore.delete("session_key");

    let flaggedCount = 0;
    const getAllReq = submissionsStore.getAll();
    getAllReq.onsuccess = () => {
      const records = getAllReq.result || [];
      for (const sub of records) {
        const wasUnsynced =
          sub.status === "QUEUED" ||
          sub.status === "DECRYPTION_ERROR" ||
          Boolean(sub.unrecoverable);
        // Strip encrypted or plaintext PII fields so zero clinical PII remains
        delete sub.answers;
        delete sub.subject_id;
        delete sub.username;
        delete sub.resolved_answers;
        if (wasUnsynced) {
          sub.status = "DECRYPTION_ERROR";
          sub.locked = true;
          sub.unrecoverable = true;
          sub.resolved_at = new Date().toISOString();
          sub.error =
            "Locked under a previous security PIN. Unsent answers were cleared to protect participant privacy — please contact your study coordinator if a survey needs to be reassigned.";
          flaggedCount += 1;
        }
        submissionsStore.put(sub);
      }
    };
    /* v8 ignore start */
    getAllReq.onerror = () => {
      reject(getAllReq.error);
    };
    /* v8 ignore stop */
    tx.oncomplete = () => {
      resolve({ flaggedCount });
    };
    /* v8 ignore start */
    tx.onerror = () => {
      reject(tx.error);
    };
    /* v8 ignore stop */
  });
}

export async function getDraftAssignmentIds(userId) {
  try {
    const db = await openDatabase(userId);
    return new Promise((resolve, reject) => {
      const tx = db.transaction("drafts", "readonly");
      const store = tx.objectStore("drafts");
      const req = store.getAll();
      req.onsuccess = () => {
        const drafts = Array.isArray(req.result) ? req.result : [];
        resolve(
          drafts
            .map((d) => d && d.assignment_id)
            .filter((id) => typeof id === "string" && id.length > 0)
        );
      };
      /* v8 ignore start */
      req.onerror = () => {
        reject(req.error);
      };
      /* v8 ignore stop */
    });
  } catch {
    return [];
  }
}

export async function saveDraft(assignmentId, answers) {
  if (!inMemorySessionKey) {
    console.warn("No active session key. Cannot save draft.");
    return null;
  }
  try {
    const aad = new TextEncoder().encode(assignmentId);
    const encryptedAnswers = await encryptAESGCM(
      answers,
      inMemorySessionKey,
      1,
      aad
    );
    const db = await openDatabase();
    return new Promise((resolve, reject) => {
      const tx = db.transaction("drafts", "readwrite");
      const store = tx.objectStore("drafts");
      store.put({
        assignment_id: assignmentId,
        answers: encryptedAnswers,
        updated_at: new Date().toISOString(),
      });
      tx.oncomplete = () => {
        resolve();
      };
      tx.onerror = () => {
        reject(tx.error);
      };
    });
  } catch (err) {
    console.error("Failed to save draft:", err);
    return null;
  }
}

export async function getDraft(assignmentId) {
  if (!inMemorySessionKey) {
    console.warn("No active session key. Cannot retrieve draft.");
    return null;
  }
  try {
    const db = await openDatabase();
    const draft = await new Promise((resolve, reject) => {
      const tx = db.transaction("drafts", "readonly");
      const store = tx.objectStore("drafts");
      const req = store.get(assignmentId);
      req.onsuccess = () => {
        resolve(req.result || null);
      };
      req.onerror = () => {
        reject(req.error);
      };
    });

    if (!draft) {
      return null;
    }

    const aad = new TextEncoder().encode(assignmentId);
    const decryptedAnswers = await decryptAESGCM(
      draft.answers,
      inMemorySessionKey,
      1,
      aad
    );
    return decryptedAnswers;
  } catch (err) {
    console.error("Failed to decrypt or retrieve draft:", err);
    return null;
  }
}

export async function deleteDraft(assignmentId) {
  try {
    const db = await openDatabase();
    return new Promise((resolve, reject) => {
      const tx = db.transaction("drafts", "readwrite");
      const store = tx.objectStore("drafts");
      store.delete(assignmentId);
      tx.oncomplete = () => {
        resolve();
      };
      tx.onerror = () => {
        reject(tx.error);
      };
    });
  } catch (err) {
    console.error("Failed to delete draft:", err);
  }
}
