import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import * as uiModule from "ui";

vi.mock("ui", async (importOriginal) => {
  const actual = await importOriginal();
  return {
    ...actual,
    encryptAESGCM: vi.fn(actual.encryptAESGCM),
    decryptAESGCM: vi.fn(actual.decryptAESGCM),
  };
});

import {
  state,
  handlePINSetupSubmit,
  handlePINUnlockSubmit,
  renderSyncQueueList,
  refreshSyncViews,
  emitSyncLifecycleEvent,
  resetSyncRetryInfo,
  logout,
} from "../src/index.js";
import {
  clearAllSubmissions,
  clearInMemoryKey,
  clearLockedOfflineData,
  clearSessionKey,
  clearSessionKeyFromDB,
  closeAllDatabases,
  closeDatabase,
  createNonExtractableCryptoKey,
  deleteDraft,
  flagLockedEntriesForPinReset,
  getActiveUserId,
  getAllSubmissions,
  getDraft,
  getDraftAssignmentIds,
  getInMemorySessionKey,
  getQueuedSubmissions,
  initSessionKey,
  openDatabase,
  queueSubmission,
  saveDraft,
  setActiveUserId,
  setInMemorySessionKey,
  updateSubmissionStatus,
  bulkUpdateSubmissionStatuses,
} from "../src/sync-queue.js";

function setupPortalDom() {
  document.body.innerHTML = `
    <div id="app">
      <input id="setup-pin" type="password" />
      <input id="confirm-setup-pin" type="password" />
      <input id="unlock-pin" type="password" />
      <button id="btn-sync-now" type="button">Sync Now</button>
      <span id="sync-badge">0</span>
      <span id="online-indicator"></span>
      <span id="online-text"></span>
      <ul id="sync-queue-list"></ul>
      <div id="tasks-list-container"></div>
      <table aria-label="Compliance"><tbody id="compliance-history-tbody"></tbody></table>
    </div>
  `;
}

describe("Session CryptoKey, IndexedDB handle, and submission projection caching", () => {
  beforeEach(async () => {
    vi.useRealTimers();
    resetSyncRetryInfo();
    clearInMemoryKey();
    closeAllDatabases();
    setActiveUserId(null);

    state.session.userId = "subj_cache_01";
    state.session.token = "jwt_portal_valid"; // pragma: allowlist secret
    state.session.isDemoMode = false;
    state.session.isOfflineMode = false;
    state.isOnline = true;
    state.submissions = [];
    state.assignments = [];
    state.instruments = {};
    state.pinSetup.isOpen = false;
    state.pinSetup.pin = "";
    state.pinSetup.confirmPin = "";
    state.pinSetup.error = "";
    state.pinUnlock.isOpen = false;
    state.pinUnlock.pin = "";
    state.pinUnlock.error = "";
    state.pinUnlock.failedAttempts = 0;
    state.pinUnlock.isLockedOut = false;

    setActiveUserId("subj_cache_01");
    await clearLockedOfflineData("subj_cache_01");
    closeAllDatabases();
    setupPortalDom();
    vi.clearAllMocks();
  });

  afterEach(() => {
    resetSyncRetryInfo();
    vi.useRealTimers();
    vi.restoreAllMocks();
    clearInMemoryKey();
    closeAllDatabases();
    setActiveUserId(null);
  });

  it("stores a non-extractable AES-GCM CryptoKey on PIN setup and unlock and executes 0 importKey calls across field encryptions/decryptions", async () => {
    const importKeySpy = vi.spyOn(globalThis.crypto.subtle, "importKey");

    state.pinSetup.pin = "246810";
    state.pinSetup.confirmPin = "246810";
    await handlePINSetupSubmit();

    const setupKey = getInMemorySessionKey();
    expect(setupKey).not.toBeNull();
    expect(setupKey.type).toBe("secret");
    expect(setupKey.extractable).toBe(false);
    expect(setupKey.algorithm?.name).toBe("AES-GCM");

    // Simulate reload: drop volatile key, then unlock with PIN
    clearInMemoryKey();
    importKeySpy.mockClear();

    state.pinUnlock.pin = "246810";
    await handlePINUnlockSubmit();

    const unlockedKey = getInMemorySessionKey();
    expect(unlockedKey).not.toBeNull();
    expect(unlockedKey.type).toBe("secret");
    expect(unlockedKey.extractable).toBe(false);
    expect(unlockedKey.algorithm?.name).toBe("AES-GCM");

    // Non-extractable session key was imported exactly once during unlock
    const nonExtractableAesImports = importKeySpy.mock.calls.filter(
      (args) =>
        args[0] === "raw" &&
        args[2]?.name === "AES-GCM" &&
        args[3] === false &&
        Array.isArray(args[4]) &&
        args[4].includes("encrypt") &&
        args[4].includes("decrypt")
    );
    expect(nonExtractableAesImports).toHaveLength(1);

    // Zero importKey calls across subsequent submissions and drafts
    importKeySpy.mockClear();
    await saveDraft("assign_01", { vssbp: "120", vsdpb: "78" });
    await queueSubmission({
      subject_id: "subj_cache_01",
      diary_id: "inst_daily_diary",
      assignment_id: "assign_01",
      answers: { vssbp: "120", vsdpb: "78", notes: ["morning"] },
      change_reason: "Initial entry",
      username: "subj_cache_01",
    });
    const draft = await getDraft("assign_01");
    const queued = await getQueuedSubmissions();
    const all = await getAllSubmissions();

    expect(draft).toEqual({ vssbp: "120", vsdpb: "78" });
    expect(queued).toHaveLength(1);
    expect(all).toHaveLength(1);
    expect(importKeySpy).toHaveBeenCalledTimes(0);
  });

  it("memoizes CryptoKey conversion when raw Uint8Array or ArrayBuffer keys are supplied directly", async () => {
    const importKeySpy = vi.spyOn(globalThis.crypto.subtle, "importKey");

    expect(await createNonExtractableCryptoKey(null)).toBeNull();

    const rawBuffer = new Uint8Array(32).fill(7).buffer;
    const fromBuffer = await createNonExtractableCryptoKey(rawBuffer);
    expect(fromBuffer.extractable).toBe(false);
    expect(await createNonExtractableCryptoKey(fromBuffer)).toBe(fromBuffer);

    // Invalid AES key length falls back cleanly without throwing
    const shortKey = new Uint8Array([1, 2, 3]);
    expect(await createNonExtractableCryptoKey(shortKey)).toBe(shortKey);

    // Raw 32-byte Uint8Array via setInMemorySessionKey imports once and reuses across reads/writes
    const rawSessionBytes = new Uint8Array(32).fill(11);
    setInMemorySessionKey(rawSessionBytes);
    // Re-setting identical reference preserves memoized state
    setInMemorySessionKey(rawSessionBytes);
    importKeySpy.mockClear();

    await queueSubmission({
      subject_id: "subj_cache_01",
      diary_id: "inst_daily_diary",
      assignment_id: "assign_raw_01",
      answers: { vssbp: "118" },
      change_reason: "Raw key path",
      username: "subj_cache_01",
    });
    expect(importKeySpy).toHaveBeenCalledTimes(1);

    // Subsequent draft/queue operations reuse the memoized CryptoKey (0 extra importKey calls)
    importKeySpy.mockClear();
    await saveDraft("assign_raw_01", { vssbp: "118" });
    expect(await getDraft("assign_raw_01")).toEqual({ vssbp: "118" });
    closeAllDatabases();
    setInMemorySessionKey(rawSessionBytes);
    await saveDraft("assign_raw_02", { vssbp: "119" });
    expect(await getDraft("assign_raw_02")).toEqual({ vssbp: "119" });
    expect(importKeySpy).toHaveBeenCalledTimes(0);
  });

  it("reuses one open IDBDatabase handle per participant scope and closes/evicts on close, versionchange, or user switch", async () => {
    const openSpy = vi.spyOn(indexedDB, "open");

    // Concurrent opens deduplicate in-flight open requests
    const [db1, db2, db3] = await Promise.all([
      openDatabase("subj_cache_01"),
      openDatabase("subj_cache_01"),
      openDatabase(),
    ]);
    expect(db1).toBe(db2);
    expect(db2).toBe(db3);
    expect(openSpy).toHaveBeenCalledTimes(1);

    await initSessionKey(new Uint8Array(32).fill(3), "subj_cache_01");
    expect(openSpy).toHaveBeenCalledTimes(1);

    await queueSubmission({
      subject_id: "subj_cache_01",
      diary_id: "inst_daily_diary",
      assignment_id: "assign_db_01",
      answers: { vssbp: "122" },
      change_reason: "Handle reuse check",
      username: "subj_cache_01",
    });
    await getQueuedSubmissions();
    await getAllSubmissions();
    expect(openSpy).toHaveBeenCalledTimes(1);

    // Trigger onversionchange -> evicts handle so next openDatabase reopens cleanly
    db1.onversionchange?.();
    const reopenedAfterVersionChange = await openDatabase("subj_cache_01");
    expect(reopenedAfterVersionChange).not.toBe(db1);
    expect(openSpy).toHaveBeenCalledTimes(2);

    // Trigger onclose -> evicts handle
    reopenedAfterVersionChange.onclose?.();
    const reopenedAfterOnClose = await openDatabase("subj_cache_01");
    expect(reopenedAfterOnClose).not.toBe(reopenedAfterVersionChange);
    expect(openSpy).toHaveBeenCalledTimes(3);

    // Calling db.close() directly also evicts the cached handle
    reopenedAfterOnClose.close();
    const reopenedAfterDirectClose = await openDatabase("subj_cache_01");
    expect(openSpy).toHaveBeenCalledTimes(4);
    expect(reopenedAfterDirectClose).toBeDefined();

    // Switching active user closes prior participant DB and opens new participant scope
    setActiveUserId("subj_cache_02");
    expect(getActiveUserId()).toBe("subj_cache_02");
    await openDatabase();
    expect(openSpy).toHaveBeenCalledTimes(5);
    expect(openSpy.mock.calls[4][0]).toBe("SubjectPortalSyncDB_subj_cache_02");

    closeDatabase("subj_cache_02");
    await openDatabase("subj_cache_02");
    expect(openSpy).toHaveBeenCalledTimes(6);

    await clearLockedOfflineData("subj_cache_02");
  });

  it("returns shallow copies from queue readers and mutations and invalidates cache on tampering, clear, reset, lock, and logout", async () => {
    await initSessionKey(new Uint8Array(32).fill(5), "subj_cache_01");

    await queueSubmission({
      subject_id: "subj_cache_01",
      diary_id: "inst_daily_diary",
      assignment_id: "assign_mut_01",
      answers: { vssbp: "126", tags: ["am", "fasting"] },
      change_reason: "Cache immutability",
      username: "subj_cache_01",
    });

    uiModule.decryptAESGCM.mockClear();

    // Cached reads hit decryptedSubmissionCache (0 decryptAESGCM calls) and return isolated copies
    const firstRead = await getQueuedSubmissions();
    expect(uiModule.decryptAESGCM).toHaveBeenCalledTimes(0);
    firstRead[0].status = "MUTATED_BY_CALLER";
    firstRead[0].answers.vssbp = "999";
    firstRead[0].answers.tags.push("corrupted");

    const secondRead = await getAllSubmissions();
    expect(uiModule.decryptAESGCM).toHaveBeenCalledTimes(0);
    expect(secondRead[0].status).toBe("QUEUED");
    expect(secondRead[0].answers).toEqual({
      vssbp: "126",
      tags: ["am", "fasting"],
    });

    // updateSubmissionStatus updates status without re-decrypting and returns isolated copy
    const updated = await updateSubmissionStatus(1, "LOCAL_QUARANTINED", {
      error: "Out of range",
      resolved_answers: { vssbp: "126" },
    });
    expect(uiModule.decryptAESGCM).toHaveBeenCalledTimes(0);
    updated.resolved_answers.vssbp = "000";

    const afterSingleUpdate = await getAllSubmissions();
    expect(afterSingleUpdate[0].status).toBe("LOCAL_QUARANTINED");
    expect(afterSingleUpdate[0].resolved_answers).toEqual({ vssbp: "126" });

    // bulkUpdateSubmissionStatuses also reuses cached decrypted fields and isolates copies
    const bulkUpdated = await bulkUpdateSubmissionStatuses([
      {
        sequence_number: 1,
        status: "QUEUED",
        additionalFields: { error: null },
      },
    ]);
    expect(uiModule.decryptAESGCM).toHaveBeenCalledTimes(0);
    bulkUpdated[0].answers.vssbp = "777";
    expect((await getQueuedSubmissions())[0].answers.vssbp).toBe("126");

    // Clearing the session key immediately hides plaintext PII behind DECRYPTION_ERROR
    clearSessionKey("subj_cache_01");
    clearInMemoryKey();
    const whileLocked = await getAllSubmissions();
    expect(whileLocked).toHaveLength(1);
    expect(whileLocked[0].status).toBe("DECRYPTION_ERROR");
    expect(whileLocked[0].locked).toBe(true);
    expect(whileLocked[0].answers).toBeUndefined();
    expect(whileLocked[0].subject_id).toBeUndefined();
    expect(whileLocked[0].username).toBeUndefined();

    // Tampering ciphertext in IndexedDB invalidates cache on next read under valid key
    await initSessionKey(new Uint8Array(32).fill(5), "subj_cache_01");
    expect((await getAllSubmissions())[0].answers.vssbp).toBe("126");

    const db = await openDatabase("subj_cache_01");
    await new Promise((resolve) => {
      const tx = db.transaction("submissions", "readwrite");
      const store = tx.objectStore("submissions");
      const getReq = store.get(1);
      getReq.onsuccess = () => {
        const rec = getReq.result;
        rec.answers = "tampered-ciphertext-payload";
        store.put(rec);
      };
      tx.oncomplete = resolve;
    });

    const tamperedRead = await getAllSubmissions();
    expect(tamperedRead[0].status).toBe("DECRYPTION_ERROR");
    expect(tamperedRead[0].answers).toBeUndefined();

    // Flagging entries for PIN reset scrubs PII and marks unrecoverable
    await flagLockedEntriesForPinReset("subj_cache_01");
    await initSessionKey(new Uint8Array(32).fill(8), "subj_cache_01");
    const resetRead = await getAllSubmissions();
    expect(resetRead[0].status).toBe("DECRYPTION_ERROR");
    expect(resetRead[0].unrecoverable).toBe(true);
    expect(resetRead[0].answers).toBeUndefined();

    // clearAllSubmissions empties store and cache
    await clearAllSubmissions();
    expect(await getAllSubmissions()).toEqual([]);

    // Logout wipes session key, submissions state, and closes participant DB
    await queueSubmission({
      subject_id: "subj_cache_01",
      diary_id: "inst_daily_diary",
      assignment_id: "assign_logout_01",
      answers: { vssbp: "121" },
      change_reason: "Before logout",
      username: "subj_cache_01",
    });
    await refreshSyncViews();
    expect(state.submissions).toHaveLength(1);

    const guardedLogout = await logout();
    expect(guardedLogout).toEqual({ signedOut: false, guarded: true });
    expect(getInMemorySessionKey()).not.toBeNull();

    await logout({ confirmed: true });
    expect(getInMemorySessionKey()).toBeNull();
    expect(state.submissions).toEqual([]);
  });

  it("handles IndexedDB open failures gracefully across draft and session-key cleanup helpers after closing cached handles", async () => {
    await initSessionKey(new Uint8Array(32).fill(9), "subj_cache_01");
    closeAllDatabases();

    const originalOpen = indexedDB.open;
    indexedDB.open = () => {
      throw new Error("Simulated cold IndexedDB failure");
    };

    try {
      expect(await saveDraft("assign_err", { vssbp: "120" })).toBe(false);
      expect(await getDraft("assign_err")).toBeNull();
      expect(await getDraftAssignmentIds("subj_cache_01")).toEqual([]);
      await expect(deleteDraft("assign_err")).resolves.toBeUndefined();
      await expect(
        clearSessionKeyFromDB("subj_cache_01")
      ).resolves.toBeUndefined();
    } finally {
      indexedDB.open = originalOpen;
    }
  });

  it("executes at most 1,501 decryptAESGCM calls and 1 indexedDB.open call across cold PIN unlock + online sync at N=500 submissions", async () => {
    state.pinSetup.pin = "135790";
    state.pinSetup.confirmPin = "135790";
    await handlePINSetupSubmit();

    const activeKey = getInMemorySessionKey();
    const db = await openDatabase("subj_cache_01");
    const encSubjectId = await uiModule.encryptAESGCM(
      "subj_cache_01",
      activeKey
    );
    const encUsername = await uiModule.encryptAESGCM(
      "subj_cache_01",
      activeKey
    );

    // Seed N=500 encrypted QUEUED submissions (1,500 encrypted PII fields) in one transaction
    const encryptedRecords = [];
    for (let i = 1; i <= 500; i++) {
      const encAnswers = await uiModule.encryptAESGCM(
        { vssbp: String(110 + (i % 25)), vsdpb: "76" },
        activeKey
      );
      encryptedRecords.push({
        sequence_number: i,
        client_id: "client-bench-500",
        subject_id: encSubjectId,
        diary_id: "inst_daily_diary",
        version_index: 1,
        assignment_id: `assign_${i}`,
        device_timestamp: "2026-11-23T08:00:00.000Z",
        answers: encAnswers,
        change_reason: "Offline entry",
        username: encUsername,
        status: "QUEUED",
        resolved_answers: null,
        resolved_at: null,
        error: null,
      });
    }

    await new Promise((resolve, reject) => {
      const tx = db.transaction("submissions", "readwrite");
      const store = tx.objectStore("submissions");
      for (const rec of encryptedRecords) {
        store.put(rec);
      }
      tx.oncomplete = resolve;
      tx.onerror = () => reject(tx.error);
    });

    // Mock online batch sync endpoint acknowledging all 500 records
    globalThis.fetch = vi.fn(async (url) => {
      const urlStr = String(url);
      if (urlStr.includes("epro/sync")) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            results: encryptedRecords.map((rec) => ({
              sequence_number: rec.sequence_number,
              status: "SUBMITTED",
            })),
          }),
        };
      }
      return {
        ok: true,
        status: 200,
        json: async () => [],
      };
    });

    // Simulate cold browser reload: drop session key, decrypted cache, and open IDB handles
    clearInMemoryKey();
    closeAllDatabases();

    uiModule.decryptAESGCM.mockClear();
    const importKeySpy = vi.spyOn(globalThis.crypto.subtle, "importKey");
    const openSpy = vi.spyOn(indexedDB, "open");

    state.pinUnlock.pin = "135790";
    await handlePINUnlockSubmit();

    // Wait for background syncOfflineQueue(unlockedSnapshot) triggered by handlePINUnlockSubmit
    await vi.waitFor(() => {
      expect(state.submissions).toHaveLength(500);
      expect(state.submissions.every((s) => s.status === "SUBMITTED")).toBe(
        true
      );
    });

    // 1 master-key unwrap + 1,500 field decryptions = 1,501 total decryptAESGCM calls
    expect(uiModule.decryptAESGCM.mock.calls.length).toBeLessThanOrEqual(1501);
    expect(uiModule.decryptAESGCM).toHaveBeenCalledTimes(1501);
    expect(openSpy).toHaveBeenCalledTimes(1);

    // Session CryptoKey (encrypt+decrypt) imported once during unlock; 0 times during 1,500 field decryptions
    const sessionImports = importKeySpy.mock.calls.filter(
      (args) =>
        args[0] === "raw" &&
        args[2]?.name === "AES-GCM" &&
        args[3] === false &&
        Array.isArray(args[4]) &&
        args[4].includes("encrypt") &&
        args[4].includes("decrypt")
    );
    expect(sessionImports).toHaveLength(1);
    // Total importKey calls = 1 PBKDF2 + 1 KWK unwrap + 1 session key = 3 (0 across 1,500 field decryptions)
    expect(importKeySpy).toHaveBeenCalledTimes(3);

    // Both SyncDrawer state.submissions and #sync-queue-list DOM reflect synced snapshot
    const listItems = document.querySelectorAll(
      "#sync-queue-list .task-item[data-sync-item-key]"
    );
    expect(listItems).toHaveLength(500);
    expect(document.getElementById("sync-badge").textContent).toBe("0");
  }, 20000);

  it("executes 0 decryptAESGCM calls, 0 indexedDB.open calls, and 0 #sync-queue-list.innerHTML replacements across 31 retry countdown ticks at N=500", async () => {
    await initSessionKey(new Uint8Array(32).fill(4), "subj_cache_01");
    const activeKey = getInMemorySessionKey();
    const db = await openDatabase("subj_cache_01");

    const encSubjectId = await uiModule.encryptAESGCM(
      "subj_cache_01",
      activeKey
    );
    const encUsername = await uiModule.encryptAESGCM(
      "subj_cache_01",
      activeKey
    );

    // Seed 498 SUBMITTED + 2 QUEUED = 500 submissions
    await new Promise((resolve, reject) => {
      const tx = db.transaction("submissions", "readwrite");
      const store = tx.objectStore("submissions");
      for (let i = 1; i <= 500; i++) {
        store.put({
          sequence_number: i,
          client_id: "client-countdown-500",
          subject_id: encSubjectId,
          diary_id: "inst_daily_diary",
          version_index: 1,
          assignment_id: `assign_cd_${i}`,
          device_timestamp: "2026-11-23T08:00:00.000Z",
          answers: null,
          change_reason: "Countdown entry",
          username: encUsername,
          status: i <= 498 ? "SUBMITTED" : "QUEUED",
          resolved_answers: null,
          resolved_at: i <= 498 ? "2026-11-23T08:01:00.000Z" : null,
          error: null,
        });
      }
      tx.oncomplete = resolve;
      tx.onerror = () => reject(tx.error);
    });

    const baseTimeMs = new Date("2026-11-23T08:10:00.000Z").getTime();
    await renderSyncQueueList();

    vi.useFakeTimers({
      toFake: ["setInterval", "clearInterval", "Date"],
    });
    vi.setSystemTime(baseTimeMs);

    const listEl = document.getElementById("sync-queue-list");
    expect(
      listEl.querySelectorAll(".task-item[data-sync-item-key]")
    ).toHaveLength(500);

    const innerHtmlDescriptor = Object.getOwnPropertyDescriptor(
      Element.prototype,
      "innerHTML"
    );
    let innerHtmlReplacements = 0;
    Object.defineProperty(listEl, "innerHTML", {
      configurable: true,
      get() {
        return innerHtmlDescriptor.get.call(this);
      },
      set(val) {
        innerHtmlReplacements += 1;
        innerHtmlDescriptor.set.call(this, val);
      },
    });

    uiModule.decryptAESGCM.mockClear();
    const openSpy = vi.spyOn(indexedDB, "open");

    emitSyncLifecycleEvent("sync:retry-scheduled", {
      attempt: 3,
      maxRetries: 5,
      nextRetryDelay: 35_000,
      nextRetryTimestamp: baseTimeMs + 35_000,
      queuedCount: 2,
    });

    for (let tick = 0; tick < 31; tick++) {
      await vi.advanceTimersByTimeAsync(1000);
    }

    expect(uiModule.decryptAESGCM).toHaveBeenCalledTimes(0);
    expect(openSpy).toHaveBeenCalledTimes(0);
    expect(innerHtmlReplacements).toBe(0);

    // Queued items reflect updated 4s remaining countdown (35s - 31s = 4s)
    const queuedDescs = Array.from(
      listEl.querySelectorAll(
        '.task-item[data-sync-status-kind="QUEUED"] [data-sync-status-desc]'
      )
    ).map((el) => el.textContent);
    expect(queuedDescs).toHaveLength(2);
    expect(queuedDescs[0]).toContain("in 4s");

    // Submitted items remain untouched
    const submittedSampleDesc = listEl.querySelector(
      '.task-item[data-sync-status-kind="SUBMITTED"] [data-sync-status-desc]'
    );
    expect(submittedSampleDesc.textContent).not.toContain("in 4s");
  });
});
