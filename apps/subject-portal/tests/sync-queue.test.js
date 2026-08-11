import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach } from "vitest";
import {
  clearSessionKey,
  clearInMemoryKey,
  initSessionKey,
  openDatabase,
  getClientId,
  getNextSequenceNumber,
  queueSubmission,
  getQueuedSubmissions,
  getAllSubmissions,
  updateSubmissionStatus,
  clearAllSubmissions,
  getInMemorySessionKey,
  setInMemorySessionKey,
  getWrappedMasterKeyConfig,
  saveWrappedMasterKeyConfig,
  saveAssignmentsToDB,
  getAssignmentsFromDB,
  saveInstrumentsToDB,
  getInstrumentsFromDB,
  getInstrumentFromDB,
  bulkUpdateSubmissionStatuses,
} from "../sync-queue.js";

describe("sync-queue secure storage and sync capabilities", () => {
  beforeEach(async () => {
    clearInMemoryKey();
    clearSessionKey();
    await clearAllSubmissions();

    // Clear indexedDB config as well
    const db = await openDatabase();
    await new Promise((resolve) => {
      const tx = db.transaction("config", "readwrite");
      tx.objectStore("config").clear();
      tx.oncomplete = resolve;
    });
  });

  it("should throw error when queueing submission without session key", async () => {
    await expect(
      queueSubmission({
        subject_id: "subject_1",
        diary_id: "inst_daily_diary",
        assignment_id: "assign_01",
        answers: { question1: "answer1" },
        change_reason: "first entry",
        username: "test_user",
      })
    ).rejects.toThrow("Encryption key not initialized");
  });

  it("should initialize session key and successfully encrypt and queue submission", async () => {
    const rawMaterial = new Uint8Array(32);
    for (let i = 0; i < 32; i++) rawMaterial[i] = i;

    await initSessionKey(rawMaterial);

    const submission = await queueSubmission({
      subject_id: "subject_1",
      diary_id: "inst_daily_diary",
      assignment_id: "assign_01",
      answers: { question1: "answer1" },
      change_reason: "first entry",
      username: "test_user",
    });

    expect(submission.sequence_number).toBe(1);
    expect(submission.diary_id).toBe("inst_daily_diary");
    expect(submission.status).toBe("QUEUED");

    // Retrieve and verify decryption
    const queued = await getQueuedSubmissions();
    expect(queued.length).toBe(1);
    expect(queued[0].subject_id).toBe("subject_1");
    expect(queued[0].username).toBe("test_user");
    expect(queued[0].answers).toEqual({ question1: "answer1" });
  });

  it("should get existing clientId from DB or generate a new one", async () => {
    const id1 = await getClientId();
    expect(id1).toBeDefined();

    const id2 = await getClientId();
    expect(id2).toBe(id1);
  });

  it("should get next sequence number correctly", async () => {
    const seq = await getNextSequenceNumber();
    expect(seq).toBe(1);
  });

  it("should handle decryption failure gracefully if key is cleared", async () => {
    const rawMaterial = new Uint8Array(32);
    for (let i = 0; i < 32; i++) rawMaterial[i] = i;

    await initSessionKey(rawMaterial);

    await queueSubmission({
      subject_id: "subject_1",
      diary_id: "inst_daily_diary",
      assignment_id: "assign_01",
      answers: { question1: "answer1" },
      change_reason: "first entry",
      username: "test_user",
    });

    // Clear key to trigger decryption failure
    clearSessionKey();

    const queued = await getQueuedSubmissions();
    expect(queued.length).toBe(1);
    expect(queued[0].status).toBe("DECRYPTION_ERROR");
    expect(queued[0].error).toContain("DECRYPTION_ERROR");
  });

  it("should reject updateSubmissionStatus if submission not found", async () => {
    await expect(updateSubmissionStatus(999, "SUBMITTED")).rejects.toThrow(
      "Submission 999 not found"
    );
  });

  it("should update submission status and return decrypted submission", async () => {
    const rawMaterial = new Uint8Array(32);
    for (let i = 0; i < 32; i++) rawMaterial[i] = i;

    await initSessionKey(rawMaterial);

    await queueSubmission({
      subject_id: "subject_1",
      diary_id: "inst_daily_diary",
      assignment_id: "assign_01",
      answers: { question1: "answer1" },
      change_reason: "first entry",
      username: "test_user",
    });

    const updated = await updateSubmissionStatus(1, "SUBMITTED", {
      resolved_answers: { question1: "answer1" },
    });

    expect(updated.status).toBe("SUBMITTED");
    expect(updated.resolved_answers).toEqual({ question1: "answer1" });

    const all = await getAllSubmissions();
    expect(all.length).toBe(1);
    expect(all[0].status).toBe("SUBMITTED");
  });

  it("should generate salt with fallback when crypto is unavailable", async () => {
    const origCrypto = globalThis.crypto;
    Object.defineProperty(globalThis, "crypto", {
      value: undefined,
      configurable: true,
      writable: true,
    });

    // Triggering database salt generation fallback
    const id1 = await getClientId();
    expect(id1).toBeDefined();

    Object.defineProperty(globalThis, "crypto", {
      value: origCrypto,
      configurable: true,
      writable: true,
    });
  });

  it("should generate clientId with fallback when randomUUID is unavailable", async () => {
    const origCrypto = globalThis.crypto;
    Object.defineProperty(globalThis, "crypto", {
      value: {
        getRandomValues: origCrypto ? origCrypto.getRandomValues : undefined,
      },
      configurable: true,
      writable: true,
    });

    const id = await getClientId();
    expect(id).toContain("client-");

    Object.defineProperty(globalThis, "crypto", {
      value: origCrypto,
      configurable: true,
      writable: true,
    });
  });

  it("should get and set in-memory session key", () => {
    const mockKey = new Uint8Array([1, 2, 3]);
    setInMemorySessionKey(mockKey);
    expect(getInMemorySessionKey()).toBe(mockKey);
  });

  it("should save and retrieve wrapped master key config", async () => {
    const wrappedKey = "mockWrappedKeyBase64String";
    const salt = new Uint8Array([9, 8, 7, 6]);
    await saveWrappedMasterKeyConfig(wrappedKey, salt);

    const retrieved = await getWrappedMasterKeyConfig();
    expect(retrieved.wrappedKey).toBe(wrappedKey);
    expect(Array.from(retrieved.salt)).toEqual(Array.from(salt));
  });

  it("should return nulls if wrapped master key config is not set", async () => {
    const retrieved = await getWrappedMasterKeyConfig();
    expect(retrieved.wrappedKey).toBeNull();
    expect(retrieved.salt).toBeNull();
  });

  it("should save and retrieve assignments correctly", async () => {
    const mockAssignments = [
      { id: "assign_1", instrument_id: "inst_1", status: "PENDING" },
      { id: "assign_2", instrument_id: "inst_2", status: "COMPLETED" },
    ];
    await saveAssignmentsToDB(mockAssignments);

    const retrieved = await getAssignmentsFromDB();
    expect(retrieved.length).toBe(2);
    expect(retrieved[0].id).toBe("assign_1");
    expect(retrieved[1].status).toBe("COMPLETED");
  });

  it("should save, retrieve and find instruments correctly", async () => {
    const mockInstruments = [
      { id: "inst_1", name: "Instrument One", items: {} },
      { id: "inst_2", name: "Instrument Two", items: {} },
    ];
    await saveInstrumentsToDB(mockInstruments);

    const retrievedList = await getInstrumentsFromDB();
    expect(retrievedList.length).toBe(2);

    const foundInstrument = await getInstrumentFromDB("inst_2");
    expect(foundInstrument).not.toBeNull();
    expect(foundInstrument.name).toBe("Instrument Two");

    const notFoundInstrument = await getInstrumentFromDB("inst_non_existent");
    expect(notFoundInstrument).toBeNull();
  });

  it("should bulk update submission statuses correctly", async () => {
    const rawMaterial = new Uint8Array(32);
    for (let i = 0; i < 32; i++) rawMaterial[i] = i;
    await initSessionKey(rawMaterial);

    await queueSubmission({
      subject_id: "subject_1",
      diary_id: "inst_1",
      assignment_id: "assign_1",
      answers: { val: 1 },
      change_reason: "reason",
      username: "user1",
    });

    const emptyRes = await bulkUpdateSubmissionStatuses([]);
    expect(emptyRes).toEqual([]);

    const res = await bulkUpdateSubmissionStatuses([
      { sequence_number: 1, status: "SUBMITTED", additionalFields: { extra: "test" } },
    ]);
    expect(res.length).toBe(1);
    expect(res[0].status).toBe("SUBMITTED");
    expect(res[0].extra).toBe("test");

    await expect(
      bulkUpdateSubmissionStatuses([
        { sequence_number: 999, status: "SUBMITTED" }
      ])
    ).rejects.toThrow("Submission 999 not found");
  });
});
