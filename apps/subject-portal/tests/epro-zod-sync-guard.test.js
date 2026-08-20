import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach } from "vitest";
import {
  clearSessionKey,
  clearInMemoryKey,
  initSessionKey,
  openDatabase,
  queueSubmission,
  getQueuedSubmissions,
  clearAllSubmissions,
} from "../src/sync-queue.js";
import { validateEproPayload, validateEproSubmission } from "usdm-schemas";

describe("Centralized Zod ePRO Schema & Pre-Transmission Sync Guard", () => {
  beforeEach(async () => {
    clearInMemoryKey();
    clearSessionKey();
    await clearAllSubmissions();

    const db = await openDatabase();
    await new Promise((resolve) => {
      const tx = db.transaction("config", "readwrite");
      tx.objectStore("config").clear();
      tx.oncomplete = resolve;
    });

    const rawMaterial = new Uint8Array(32);
    for (let i = 0; i < 32; i++) rawMaterial[i] = i + 1;
    await initSessionKey(rawMaterial);
  });

  describe("Requirement 1 & 3: Pre-Save Local Validation Gate", () => {
    it("should reject ePRO submission with age below 18 and prevent write to IndexedDB", async () => {
      const startTime = performance.now();

      await expect(
        queueSubmission({
          subject_id: "subject_101",
          diary_id: "diary_daily_pain",
          assignment_id: "assign_101",
          answers: { age: 15, gender: "M", pain_score: 5 },
          change_reason: "Daily diary entry",
          username: "patient_alice",
        })
      ).rejects.toThrow(
        "Demographic Validation Error: Participant age must be between 18 and 110."
      );

      const duration = performance.now() - startTime;
      expect(duration).toBeLessThan(50); // Performance constraint < 50ms

      const queued = await getQueuedSubmissions();
      expect(queued.length).toBe(0);
    });

    it("should reject ePRO submission with age above 110 and prevent write to IndexedDB", async () => {
      await expect(
        queueSubmission({
          subject_id: "subject_101",
          diary_id: "diary_daily_pain",
          assignment_id: "assign_101",
          answers: { age: 120, gender: "F", pain_score: 2 },
          change_reason: "Daily diary entry",
          username: "patient_alice",
        })
      ).rejects.toThrow(
        "Demographic Validation Error: Participant age must be between 18 and 110."
      );

      const queued = await getQueuedSubmissions();
      expect(queued.length).toBe(0);
    });

    it("should reject ePRO submission with invalid gender choice", async () => {
      await expect(
        queueSubmission({
          subject_id: "subject_101",
          diary_id: "diary_daily_pain",
          assignment_id: "assign_101",
          answers: { age: 25, gender: "UNKNOWN_GENDER", pain_score: 3 },
          change_reason: "Daily diary entry",
          username: "patient_alice",
        })
      ).rejects.toThrow(
        "Demographic Validation Error: Gender must be one of M, F, or O."
      );

      const queued = await getQueuedSubmissions();
      expect(queued.length).toBe(0);
    });

    it("should reject ePRO submission with pain_score out of bounds (greater than 10)", async () => {
      await expect(
        queueSubmission({
          subject_id: "subject_101",
          diary_id: "diary_daily_pain",
          assignment_id: "assign_101",
          answers: { age: 30, gender: "F", pain_score: 15 },
          change_reason: "Daily diary entry",
          username: "patient_alice",
        })
      ).rejects.toThrow(
        "Clinical Validation Error: Pain score must be between 0 and 10."
      );

      const queued = await getQueuedSubmissions();
      expect(queued.length).toBe(0);
    });

    it("should accept valid ePRO submission and save to local offline storage", async () => {
      const submission = await queueSubmission({
        subject_id: "subject_101",
        diary_id: "diary_daily_pain",
        assignment_id: "assign_101",
        answers: { age: 35, gender: "FEMALE", pain_score: 4 },
        change_reason: "Daily diary entry",
        username: "patient_alice",
      });

      expect(submission.sequence_number).toBe(1);
      expect(submission.status).toBe("QUEUED");

      const queued = await getQueuedSubmissions();
      expect(queued.length).toBe(1);
      expect(queued[0].answers).toEqual({
        age: 35,
        gender: "FEMALE",
        pain_score: 4,
      });
    });
  });

  describe("Requirement 2, 4, 5: Pre-Transmission Schema Gate & Sibling Sync Isolation", () => {
    it("should validate payloads via validateEproPayload helper directly", () => {
      const validRes = validateEproPayload({
        age: 40,
        gender: "M",
        pain_score: 0,
      });
      expect(validRes.valid).toBe(true);
      expect(validRes.errors).toHaveLength(0);

      const invalidRes = validateEproPayload({
        age: 10,
        gender: "X",
        pain_score: 11,
      });
      expect(invalidRes.valid).toBe(false);
      expect(invalidRes.errors.length).toBeGreaterThanOrEqual(3);
    });

    it("should validate full submission payload using validateEproSubmission helper", () => {
      const validSub = validateEproSubmission({
        subject_id: "sub_1",
        diary_id: "diary_1",
        answers: { age: 22, gender: "M", pain_score: 5 },
      });
      expect(validSub.valid).toBe(true);

      const invalidSub = validateEproSubmission({
        subject_id: "sub_1",
        diary_id: "diary_1",
        answers: { age: 12 },
      });
      expect(invalidSub.valid).toBe(false);
    });
  });
});
