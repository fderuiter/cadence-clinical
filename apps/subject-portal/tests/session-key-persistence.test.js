import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach } from "vitest";
import { encryptAESGCM, decryptAESGCM, deriveSessionKey } from "ui";
import {
  initSessionKey,
  saveSessionKeyToDB,
  getSessionKeyFromDB,
  restoreSessionKeyFromDB,
  clearSessionKey,
  clearInMemoryKey,
  getInMemorySessionKey,
  openDatabase,
  saveDraft,
  getDraft,
  queueSubmission,
  getQueuedSubmissions,
  setActiveUserId,
} from "../src/sync-queue.js";
import {
  initializeApp,
  logout,
  state,
  handlePINSetupSubmit,
  handlePINUnlockSubmit,
} from "../src/index.js";

async function readRawConfigEntry(userId, key) {
  const db = await openDatabase(userId);
  return new Promise((resolve, reject) => {
    const tx = db.transaction("config", "readonly");
    const req = tx.objectStore("config").get(key);
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error);
  });
}

describe("ADR-2360 Memory-Only PIN Encryption Key & Per-User Isolation", () => {
  beforeEach(async () => {
    localStorage.clear();
    sessionStorage.clear();
    clearInMemoryKey();
    await clearSessionKey("user_test_01");
    await clearSessionKey("user_test_02");
    await clearSessionKey();

    for (const uid of ["user_test_01", "user_test_02", null]) {
      const db = await openDatabase(uid);
      await new Promise((resolve) => {
        const tx = db.transaction(
          ["config", "drafts", "submissions"],
          "readwrite"
        );
        tx.objectStore("config").clear();
        tx.objectStore("drafts").clear();
        tx.objectStore("submissions").clear();
        tx.oncomplete = resolve;
      });
    }
  });

  it("ADR-2360: initSessionKey keeps key strictly in memory and never writes unwrapped 'session_key' to IndexedDB", async () => {
    const userId = "user_test_01";
    await initSessionKey("secret-session-material", userId);

    const inMemoryKey = getInMemorySessionKey();
    expect(inMemoryKey).toBeDefined();
    expect(inMemoryKey).not.toBeNull();

    // Unwrapped session_key must NEVER exist in IndexedDB
    const persistedValue = await getSessionKeyFromDB(userId);
    expect(persistedValue).toBeNull();

    const rawEntry = await readRawConfigEntry(userId, "session_key");
    expect(rawEntry).toBeUndefined();

    // saveSessionKeyToDB is a deprecated no-op that purges any legacy key
    await saveSessionKeyToDB(inMemoryKey, userId);
    expect(await readRawConfigEntry(userId, "session_key")).toBeUndefined();
  });

  it("ADR-2360: restoreSessionKeyFromDB purges legacy unwrapped 'session_key' entries and returns null", async () => {
    const userId = "user_test_01";
    const db = await openDatabase(userId);
    await new Promise((resolve) => {
      const tx = db.transaction("config", "readwrite");
      tx.objectStore("config").put({
        key: "session_key",
        value: new Uint8Array(32),
      });
      tx.oncomplete = resolve;
    });

    clearInMemoryKey();
    const restoredKey = await restoreSessionKeyFromDB(userId);
    expect(restoredKey).toBeNull();
    expect(getInMemorySessionKey()).toBeNull();
    expect(await readRawConfigEntry(userId, "session_key")).toBeUndefined();
  });

  it("prompts for PIN unlock on offline browser reload instead of auto-restoring an unwrapped key", async () => {
    const userId = "user_test_01";
    localStorage.setItem("oidc_user_id", userId);
    state.session.userId = userId;
    state.session.isDemoMode = false;
    setActiveUserId(userId);

    // Establish wrapped master key via PIN setup
    state.pinSetup.isOpen = true;
    state.pinSetup.pin = "246810";
    state.pinSetup.confirmPin = "246810";
    await handlePINSetupSubmit();
    expect(getInMemorySessionKey()).toBeInstanceOf(Uint8Array);

    // Simulate full browser reload offline: wipe memory
    clearInMemoryKey();
    expect(getInMemorySessionKey()).toBeNull();
    window.__MOCK_TEST_ENV__ = true;
    state.session.isOfflineMode = true;

    await initializeApp();

    // Key remains null until user enters PIN, and PinUnlockModal is opened
    expect(getInMemorySessionKey()).toBeNull();
    expect(state.pinUnlock.isOpen).toBe(true);
  });

  it("keeps encrypted drafts and queued diaries intact across reload and unlocks them after PIN entry", async () => {
    const userId = "user_test_01";
    localStorage.setItem("oidc_user_id", userId);
    state.session.userId = userId;
    state.session.isDemoMode = false;
    state.session.isOfflineMode = true;
    setActiveUserId(userId);

    // Setup PIN
    state.pinSetup.isOpen = true;
    state.pinSetup.pin = "135790";
    state.pinSetup.confirmPin = "135790";
    await handlePINSetupSubmit();

    const assignmentId = "assign_offline_01";
    const answers = { vssbp: "120", vsdpb: "80" };
    await saveDraft(assignmentId, answers);
    await queueSubmission({
      subject_id: userId,
      diary_id: "inst_daily_diary",
      assignment_id: assignmentId,
      answers,
      change_reason: "Offline submission before reload",
      username: userId,
    });

    // Simulate browser reload (memory cleared)
    clearInMemoryKey();
    expect(getInMemorySessionKey()).toBeNull();

    // While locked, draft cannot be read in plaintext and queued item reports DECRYPTION_ERROR without mutating DB
    expect(await getDraft(assignmentId)).toBeNull();
    const lockedItems = await getQueuedSubmissions();
    expect(lockedItems).toHaveLength(1);
    expect(lockedItems[0].status).toBe("DECRYPTION_ERROR");

    // Unlock with valid PIN
    state.pinUnlock.isOpen = true;
    state.pinUnlock.pin = "135790";
    await handlePINUnlockSubmit();

    expect(state.pinUnlock.isOpen).toBe(false);
    expect(await getDraft(assignmentId)).toEqual(answers);
    const unlockedItems = await getQueuedSubmissions();
    expect(unlockedItems).toHaveLength(1);
    expect(unlockedItems[0].status).toBe("QUEUED");
    expect(unlockedItems[0].answers).toEqual(answers);
  });

  it("Per-User Database Separation: isolates wrapped keys and encrypted data per SubjectPortalSyncDB_${userId}", async () => {
    const info = "cadence-subject-portal-offline-v1";
    const keyUserA = await deriveSessionKey("pin-user-A", "user_test_01", info);
    const keyUserB = await deriveSessionKey("pin-user-B", "user_test_02", info);

    const encryptedWithA = await encryptAESGCM(
      { data: "user_a_secret" },
      keyUserA
    );
    await expect(decryptAESGCM(encryptedWithA, keyUserB)).rejects.toThrow();

    await initSessionKey("pin-user-A", "user_test_01");
    await saveDraft("shared_assign", { secret: "only_user_a" }); // pragma: allowlist secret

    await initSessionKey("pin-user-B", "user_test_02");
    expect(await getDraft("shared_assign")).toBeNull();
  });

  it("logout clears active in-memory key and cached OIDC identity", async () => {
    const userId = "user_test_01";
    localStorage.setItem("oidc_user_id", userId);
    await initSessionKey("secret-session-material", userId);
    state.session.userId = userId;

    expect(getInMemorySessionKey()).not.toBeNull();
    expect(localStorage.getItem("oidc_user_id")).toBe(userId);

    await logout();

    expect(localStorage.getItem("oidc_user_id")).toBeNull();
    expect(getInMemorySessionKey()).toBeNull();
    expect(await getSessionKeyFromDB(userId)).toBeNull();
  });
});
