import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach } from "vitest";
import { encryptAESGCM, decryptAESGCM } from "ui";
import {
  initSessionKey,
  saveSessionKeyToDB,
  getSessionKeyFromDB,
  restoreSessionKeyFromDB,
  clearSessionKeyFromDB,
  clearSessionKey,
  clearInMemoryKey,
  getInMemorySessionKey,
  openDatabase,
  saveDraft,
  getDraft,
  queueSubmission,
  getQueuedSubmissions,
  clearAllSubmissions,
  setActiveUserId,
} from "../src/sync-queue.js";
import { initializeApp, logout, state } from "../src/index.js";

describe("Offline Session Key Persistence in IndexedDB", () => {
  beforeEach(async () => {
    localStorage.clear();
    clearInMemoryKey();
    clearSessionKey("user_test_01");
    clearSessionKey("user_test_02");
    clearSessionKey();

    // Clear indexedDB config store for user_test_01 and default DB
    for (const uid of ["user_test_01", "user_test_02", null]) {
      const db = await openDatabase(uid);
      await new Promise((resolve) => {
        const tx = db.transaction("config", "readwrite");
        tx.objectStore("config").clear();
        tx.oncomplete = resolve;
      });
      const txDrafts = db.transaction("drafts", "readwrite");
      txDrafts.objectStore("drafts").clear();
    }
  });

  it("Requirement 1: initSessionKey writes derived session encryption key to IndexedDB config store under 'session_key'", async () => {
    const userId = "user_test_01";
    await initSessionKey("secret-session-material", userId);

    const inMemoryKey = getInMemorySessionKey();
    expect(inMemoryKey).toBeDefined();
    expect(inMemoryKey).not.toBeNull();

    // Verify key was written to IndexedDB config store under key 'session_key'
    const persistedValue = await getSessionKeyFromDB(userId);
    expect(persistedValue).toBeDefined();
    expect(persistedValue).not.toBeNull();

    // Verify Web Crypto non-extractable constraint where supported
    if (
      typeof CryptoKey !== "undefined" &&
      persistedValue instanceof CryptoKey
    ) {
      expect(persistedValue.extractable).toBe(false);
    }
  });

  it("Requirement 2: restoreSessionKeyFromDB restores session_key from IndexedDB if inMemorySessionKey is null", async () => {
    const userId = "user_test_01";
    await initSessionKey("secret-session-material", userId);

    // Simulate page unload / memory reset
    clearInMemoryKey();
    expect(getInMemorySessionKey()).toBeNull();

    // Restore key
    const restoredKey = await restoreSessionKeyFromDB(userId);
    expect(restoredKey).toBeDefined();
    expect(restoredKey).not.toBeNull();
    expect(getInMemorySessionKey()).toBe(restoredKey);
  });

  it("Requirement 2 & Acceptance Criteria: initializeApp restores inMemorySessionKey on browser restart offline", async () => {
    const userId = "user_test_01";
    localStorage.setItem("oidc_user_id", userId);

    await initSessionKey("secret-session-material", userId);

    // Simulate full browser reload offline: wipe memory & set offline state
    clearInMemoryKey();
    expect(getInMemorySessionKey()).toBeNull();
    window.__MOCK_TEST_ENV__ = true;
    state.session.isOfflineMode = true;

    // Run app bootstrap initialization flow
    await initializeApp();

    expect(getInMemorySessionKey()).not.toBeNull();
  });

  it("Requirement 3 & Acceptance Criteria: saveDraft, getDraft, and queueSubmission succeed after offline browser reload", async () => {
    const userId = "user_test_01";
    localStorage.setItem("oidc_user_id", userId);
    await initSessionKey("secret-session-material", userId);

    // Save initial draft
    const assignmentId = "assign_offline_01";
    const answers = { vssbp: "120", vsdpb: "80" };
    await saveDraft(assignmentId, answers);

    // Simulate full browser reload (memory wipe)
    clearInMemoryKey();
    expect(getInMemorySessionKey()).toBeNull();

    // Verify draft retrieval auto-restores key and decrypts content
    const restoredDraft = await getDraft(assignmentId);
    expect(restoredDraft).toEqual(answers);

    // Simulate memory wipe again and verify queueSubmission auto-restores key and encrypts
    clearInMemoryKey();
    expect(getInMemorySessionKey()).toBeNull();

    const queued = await queueSubmission({
      subject_id: userId,
      diary_id: "inst_daily_diary",
      assignment_id: assignmentId,
      answers: answers,
      change_reason: "Offline submission after reload",
      username: userId,
    });

    expect(queued).toBeDefined();
    expect(queued.sequence_number).toBeGreaterThan(0);

    const queuedItems = await getQueuedSubmissions();
    expect(queuedItems.length).toBe(1);
    expect(queuedItems[0].answers).toEqual(answers);
  });

  it("Per-User Database Separation: obey IndexedDB per-user database separation (SubjectPortalSyncDB_${userId})", async () => {
    await initSessionKey("material-user-A", "user_test_01");
    await initSessionKey("material-user-B", "user_test_02");

    const keyUserA = await getSessionKeyFromDB("user_test_01");
    const keyUserB = await getSessionKeyFromDB("user_test_02");

    expect(keyUserA).toBeDefined();
    expect(keyUserB).toBeDefined();

    // Verify cryptographic separation: ciphertext encrypted with User A's key cannot be decrypted by User B's key
    const encryptedWithA = await encryptAESGCM(
      { data: "user_a_secret" },
      keyUserA
    );
    await expect(decryptAESGCM(encryptedWithA, keyUserB)).rejects.toThrow();
  });

  it("Requirement 4 & Acceptance Criteria: logout clears persisted session_key from IndexedDB and localStorage", async () => {
    const userId = "user_test_01";
    localStorage.setItem("oidc_user_id", userId);
    await initSessionKey("secret-session-material", userId);

    state.session.userId = userId;

    expect(await getSessionKeyFromDB(userId)).not.toBeNull();
    expect(localStorage.getItem("oidc_user_id")).toBe(userId);

    // Execute logout
    await logout();

    expect(localStorage.getItem("oidc_user_id")).toBeNull();
    expect(getInMemorySessionKey()).toBeNull();
    expect(await getSessionKeyFromDB(userId)).toBeNull();
  });
});
