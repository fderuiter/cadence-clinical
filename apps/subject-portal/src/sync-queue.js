import { deriveSessionKey, encryptAESGCM, decryptAESGCM } from "ui/signing";
import {
  createDemoPrng,
  demoNowMs,
  demoRandom,
  demoRandomToken,
  isDemoModeActive,
} from "./utils/demoMode.js";
import {
  hasTrackedLocalQueueActivity,
  setTrackedLocalQueueActivity,
} from "./utils/localQueueActivity.js";
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
let inMemoryCryptoKeyCache = null;
const openDbHandles = new Map();
const pendingDbOpens = new Map();
const decryptedSubmissionCache = new Map();

function cloneStructuredValue(value) {
  if (value === null || typeof value !== "object") {
    return value;
  }
  if (Array.isArray(value)) {
    return value.map(cloneStructuredValue);
  }
  const copy = {};
  for (const [k, v] of Object.entries(value)) {
    copy[k] = cloneStructuredValue(v);
  }
  return copy;
}

function cloneSubmissionProjection(record) {
  if (!record || typeof record !== "object") {
    return record;
  }
  const copy = { ...record };
  if ("answers" in copy) {
    copy.answers = cloneStructuredValue(copy.answers);
  }
  if ("resolved_answers" in copy) {
    copy.resolved_answers = cloneStructuredValue(copy.resolved_answers);
  }
  return copy;
}

function clearDecryptedSubmissionCache(dbName = null) {
  if (dbName) {
    decryptedSubmissionCache.delete(dbName);
    return;
  }
  decryptedSubmissionCache.clear();
}

function getDbSubmissionCache(dbName, createIfMissing = false) {
  let dbCache = decryptedSubmissionCache.get(dbName);
  if (!dbCache && createIfMissing) {
    dbCache = new Map();
    decryptedSubmissionCache.set(dbName, dbCache);
  }
  return dbCache || null;
}

function evictCachedDatabase(dbName, expectedDb = null) {
  const existing = openDbHandles.get(dbName);
  if (existing && (!expectedDb || existing === expectedDb)) {
    openDbHandles.delete(dbName);
  }
}

function closeCachedDatabaseByName(dbName) {
  pendingDbOpens.delete(dbName);
  const existing = openDbHandles.get(dbName);
  if (existing) {
    openDbHandles.delete(dbName);
    try {
      existing.close();
    } catch {
      // Ignore errors when closing an already-closed IDB handle
    }
  }
}

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
  inMemoryCryptoKeyCache = null;
  clearDecryptedSubmissionCache();
  clearSessionKeyFromDB(userId).catch((err) =>
    console.warn("Could not clear session_key from DB:", err)
  );
}

export function clearInMemoryKey() {
  inMemorySessionKey = null;
  inMemoryCryptoKeyCache = null;
  clearDecryptedSubmissionCache();
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

function resolveDbName(userId) {
  const targetUserId = userId !== undefined ? userId : currentUserId;
  return targetUserId
    ? `SubjectPortalSyncDB_${targetUserId}`
    : "SubjectPortalSyncDB";
}

export function setActiveUserId(userId) {
  const nextUserId = userId || null;
  if (nextUserId !== currentUserId) {
    const prevDbName = resolveDbName(currentUserId);
    const nextDbName = resolveDbName(nextUserId);
    clearInMemoryKey();
    if (prevDbName !== nextDbName) {
      closeCachedDatabaseByName(prevDbName);
    }
    currentUserId = nextUserId;
  }
}

export function getActiveUserId() {
  return currentUserId;
}

export function closeDatabase(userId) {
  const dbName = resolveDbName(userId);
  clearDecryptedSubmissionCache(dbName);
  closeCachedDatabaseByName(dbName);
}

export function closeAllDatabases() {
  clearDecryptedSubmissionCache();
  for (const dbName of Array.from(openDbHandles.keys())) {
    closeCachedDatabaseByName(dbName);
  }
  pendingDbOpens.clear();
}

export async function createNonExtractableCryptoKey(rawKeyBytes) {
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

async function getActiveCryptoKey() {
  const activeKey = inMemorySessionKey;
  if (!activeKey) return null;
  if (typeof activeKey === "object" && activeKey.algorithm) {
    return activeKey;
  }
  if (
    inMemoryCryptoKeyCache &&
    inMemoryCryptoKeyCache.sourceKey === activeKey
  ) {
    return inMemoryCryptoKeyCache.cryptoKey;
  }
  const imported = await createNonExtractableCryptoKey(activeKey);
  if (inMemorySessionKey === activeKey) {
    inMemoryCryptoKeyCache = { sourceKey: activeKey, cryptoKey: imported };
  }
  return imported;
}

export async function initSessionKey(sessionMaterial, userId) {
  if (userId) {
    setActiveUserId(userId);
  }
  const salt = await getOrGenerateSalt(userId);
  const info = "cadence-subject-portal-offline-v1";
  const rawBytes = await deriveSessionKey(sessionMaterial, salt, info);
  const cryptoKey = await createNonExtractableCryptoKey(rawBytes);
  if (rawBytes instanceof Uint8Array && cryptoKey !== rawBytes) {
    rawBytes.fill(0);
  }
  setInMemorySessionKey(cryptoKey);
  await clearSessionKeyFromDB(userId);
}

export function openDatabase(userId) {
  const dbName = resolveDbName(
    userId === undefined ? currentUserId : userId || currentUserId
  );
  const cachedDb = openDbHandles.get(dbName);
  if (cachedDb) {
    return Promise.resolve(cachedDb);
  }
  const inFlight = pendingDbOpens.get(dbName);
  if (inFlight) {
    return inFlight;
  }

  const openPromise = new Promise((resolve, reject) => {
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
        const origClose = db.close.bind(db);
        db.close = () => {
          evictCachedDatabase(dbName, db);
          return origClose();
        };
        db.onversionchange = () => {
          evictCachedDatabase(dbName, db);
          try {
            origClose();
          } catch {
            // Ignore close errors during version change
          }
        };
        db.onclose = () => {
          evictCachedDatabase(dbName, db);
        };
        openDbHandles.set(dbName, db);
        pendingDbOpens.delete(dbName);
        console.log(
          `[IndexedDB] Opened ${dbName} at version ${db.version} containing stores:`,
          Array.from(db.objectStoreNames)
        );
        resolve(db);
      };
      /* v8 ignore start */
      request.onerror = (event) => {
        pendingDbOpens.delete(dbName);
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
      pendingDbOpens.delete(dbName);
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

  pendingDbOpens.set(dbName, openPromise);
  return openPromise;
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
  const sessionKeySnapshot = inMemorySessionKey;
  const cryptoKey = await getActiveCryptoKey();
  if (!sessionKeySnapshot || !cryptoKey) {
    throw new Error(
      "Encryption key not initialized. Cannot queue submission securely."
    );
  }

  const { validateEproPayload } = await import("usdm-schemas/epro");
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

  const encAnswers = await encryptAESGCM(answers, cryptoKey);
  const encSubjectId = await encryptAESGCM(subject_id, cryptoKey);
  const encUsername = await encryptAESGCM(username, cryptoKey);

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
      setTrackedLocalQueueActivity(true);
      if (inMemorySessionKey === sessionKeySnapshot && inMemorySessionKey) {
        const dbCache = getDbSubmissionCache(db.name, true);
        const normalizedAnswers =
          answers === undefined
            ? undefined
            : JSON.parse(JSON.stringify(answers));
        const normalizedSubjectId =
          subject_id === undefined
            ? undefined
            : JSON.parse(JSON.stringify(subject_id));
        const normalizedUsername =
          username === undefined
            ? undefined
            : JSON.parse(JSON.stringify(username));
        dbCache.set(sequence_number, {
          sessionKeyRef: sessionKeySnapshot,
          answersCipher: encAnswers,
          subjectIdCipher: encSubjectId,
          usernameCipher: encUsername,
          decryptedAnswers: normalizedAnswers,
          decryptedSubjectId: normalizedSubjectId,
          decryptedUsername: normalizedUsername,
        });
      }
      resolve({ ...submission });
    };
    /* v8 ignore start */
    tx.onerror = () => {
      reject(tx.error);
    };
    /* v8 ignore stop */
  });
}

function projectLockedRecord(
  record,
  errorMessage,
  markUnrecoverable = false,
  stripResolvedAnswers = false
) {
  const locked = { ...record };
  locked.status = "DECRYPTION_ERROR";
  locked.locked = true;
  if (markUnrecoverable) {
    locked.unrecoverable = Boolean(record.unrecoverable);
  }
  locked.error = errorMessage;
  delete locked.answers;
  delete locked.subject_id;
  delete locked.username;
  if (stripResolvedAnswers) {
    delete locked.resolved_answers;
  }
  return locked;
}

async function decryptRecordForDb(dbName, record) {
  if (!record) return record;
  const seq = record.sequence_number;
  const hasSeq = seq !== undefined && seq !== null;

  if (record.unrecoverable === true || record.status === "DECRYPTION_ERROR") {
    if (hasSeq) {
      getDbSubmissionCache(dbName, false)?.delete(seq);
    }
    return projectLockedRecord(
      record,
      record.error ||
        "Locked — enter your security PIN to decrypt queued answers.",
      true,
      true
    );
  }

  const sessionKeySnapshot = inMemorySessionKey;
  const cryptoKey = await getActiveCryptoKey();
  if (!sessionKeySnapshot || !cryptoKey) {
    return projectLockedRecord(
      record,
      "DECRYPTION_ERROR: Missing encryption key"
    );
  }

  const existingCache = hasSeq
    ? getDbSubmissionCache(dbName, false)?.get(seq)
    : null;
  if (
    existingCache &&
    existingCache.sessionKeyRef === sessionKeySnapshot &&
    existingCache.answersCipher === record.answers &&
    existingCache.subjectIdCipher === record.subject_id &&
    existingCache.usernameCipher === record.username
  ) {
    return cloneSubmissionProjection({
      ...record,
      answers: existingCache.decryptedAnswers,
      subject_id: existingCache.decryptedSubjectId,
      username: existingCache.decryptedUsername,
      locked: false,
    });
  }

  try {
    const decAnswers =
      typeof record.answers === "string"
        ? await decryptAESGCM(record.answers, cryptoKey)
        : record.answers;
    const decSubjectId =
      typeof record.subject_id === "string"
        ? await decryptAESGCM(record.subject_id, cryptoKey)
        : record.subject_id;
    const decUsername =
      typeof record.username === "string"
        ? await decryptAESGCM(record.username, cryptoKey)
        : record.username;

    if (inMemorySessionKey !== sessionKeySnapshot || !inMemorySessionKey) {
      return projectLockedRecord(
        record,
        "DECRYPTION_ERROR: Missing encryption key"
      );
    }

    if (hasSeq) {
      const dbCache = getDbSubmissionCache(dbName, true);
      dbCache.set(seq, {
        sessionKeyRef: sessionKeySnapshot,
        answersCipher: record.answers,
        subjectIdCipher: record.subject_id,
        usernameCipher: record.username,
        decryptedAnswers: cloneStructuredValue(decAnswers),
        decryptedSubjectId: cloneStructuredValue(decSubjectId),
        decryptedUsername: cloneStructuredValue(decUsername),
      });
    }

    return cloneSubmissionProjection({
      ...record,
      answers: decAnswers,
      subject_id: decSubjectId,
      username: decUsername,
      locked: false,
    });
  } catch (err) {
    if (hasSeq) {
      getDbSubmissionCache(dbName, false)?.delete(seq);
    }
    return projectLockedRecord(record, `DECRYPTION_ERROR: ${err.message}`);
  }
}

async function readRawSubmissionsFromDB() {
  const db = await openDatabase();
  const records = await new Promise((resolve, reject) => {
    const tx = db.transaction("submissions", "readonly");
    const store = tx.objectStore("submissions");
    const req = store.getAll();
    req.onsuccess = () => {
      resolve(req.result || []);
    };
    /* v8 ignore start */
    req.onerror = () => {
      reject(req.error);
    };
    /* v8 ignore stop */
  });
  return { dbName: db.name, records };
}

export async function getQueuedSubmissions() {
  const { dbName, records } = await readRawSubmissionsFromDB();
  const queued = records.filter((s) => s.status === "QUEUED");
  const decryptedQueued = await Promise.all(
    queued.map((rec) => decryptRecordForDb(dbName, rec))
  );
  decryptedQueued.sort((a, b) => a.sequence_number - b.sequence_number);
  return decryptedQueued;
}

export async function getAllSubmissions() {
  const { dbName, records } = await readRawSubmissionsFromDB();
  const decryptedAll = await Promise.all(
    records.map((rec) => decryptRecordForDb(dbName, rec))
  );
  decryptedAll.sort((a, b) => b.sequence_number - a.sequence_number);
  return decryptedAll;
}

function applySafeStatusMutation(sub, status, additionalFields = {}) {
  sub.status = status;
  sub.resolved_at = new Date().toISOString();
  const safeFields = { ...additionalFields };
  delete safeFields.answers;
  delete safeFields.subject_id;
  delete safeFields.username;
  Object.assign(sub, safeFields);
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
      applySafeStatusMutation(sub, status, additionalFields);
      const putReq = store.put(sub);
      putReq.onsuccess = async () => {
        const decryptedSub = await decryptRecordForDb(db.name, sub);
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
        applySafeStatusMutation(sub, status, additionalFields);

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
        const decrypted = await Promise.all(
          subsToDecrypt.map((sub) => decryptRecordForDb(db.name, sub))
        );
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
      clearDecryptedSubmissionCache(db.name);
      resolve();
    };
    /* v8 ignore start */
    tx.onerror = () => {
      reject(tx.error);
    };
    /* v8 ignore stop */
  });
}

export { hasTrackedLocalQueueActivity, setTrackedLocalQueueActivity };

export function getInMemorySessionKey() {
  return inMemorySessionKey;
}

export function setInMemorySessionKey(key) {
  if (key !== inMemorySessionKey) {
    inMemoryCryptoKeyCache = null;
    clearDecryptedSubmissionCache();
  }
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
  clearInMemoryKey();
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
      setTrackedLocalQueueActivity(false);
      clearDecryptedSubmissionCache(db.name);
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
  clearInMemoryKey();
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
      setTrackedLocalQueueActivity(false);
      clearDecryptedSubmissionCache(db.name);
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
  const cryptoKey = await getActiveCryptoKey();
  if (!cryptoKey) {
    console.warn("No active session key. Cannot save draft.");
    return false;
  }
  try {
    const aad = new TextEncoder().encode(assignmentId);
    const encryptedAnswers = await encryptAESGCM(answers, cryptoKey, 1, aad);
    const db = await openDatabase();
    return await new Promise((resolve, reject) => {
      const tx = db.transaction("drafts", "readwrite");
      const store = tx.objectStore("drafts");
      store.put({
        assignment_id: assignmentId,
        answers: encryptedAnswers,
        updated_at: new Date().toISOString(),
      });
      tx.oncomplete = () => {
        setTrackedLocalQueueActivity(true);
        resolve(true);
      };
      /* v8 ignore start */
      tx.onerror = () => {
        reject(tx.error);
      };
      /* v8 ignore stop */
    });
  } catch (err) {
    console.error("Failed to save draft:", err);
    return false;
  }
}

export async function getDraft(assignmentId) {
  const cryptoKey = await getActiveCryptoKey();
  if (!cryptoKey) {
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
      cryptoKey,
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
