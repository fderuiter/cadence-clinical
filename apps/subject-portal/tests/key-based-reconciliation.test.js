import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach, vi } from "vitest";
import {
  clearSessionKey,
  clearInMemoryKey,
  initSessionKey,
  openDatabase,
  queueSubmission,
  getAllSubmissions,
  bulkUpdateSubmissionStatuses,
  clearAllSubmissions,
} from "../sync-queue.js";
import { syncOfflineQueue, state } from "../index.js";

describe("Key-Based Sync Reconciliation Integration Tests", () => {
  beforeEach(async () => {
    vi.resetAllMocks();
    clearInMemoryKey();
    clearSessionKey();
    await clearAllSubmissions();

    // Clear indexedDB config
    const db = await openDatabase();
    await new Promise((resolve) => {
      const tx = db.transaction("config", "readwrite");
      tx.objectStore("config").clear();
      tx.oncomplete = resolve;
    });

    // Initialize encryption key
    const rawMaterial = new Uint8Array(32);
    for (let i = 0; i < 32; i++) rawMaterial[i] = i;
    await initSessionKey(rawMaterial);

    // Setup basic DOM elements needed by syncOfflineQueue
    document.body.innerHTML = `
      <div id="sync-queue-status-text">Checking status...</div>
      <div id="sync-queue-list"></div>
    `;

    // Ensure mock environment is active and online
    window.__MOCK_TEST_ENV__ = true;
    state.session.isOfflineMode = false;
  });

  it("should bulk update multiple submission statuses inside a single atomic transaction block", async () => {
    // 1. Queue three submissions
    const sub1 = await queueSubmission({
      subject_id: "subj_01",
      diary_id: "diary_a",
      assignment_id: "assign_01",
      answers: { pain: 2 },
      change_reason: "reason 1",
      username: "user1",
    });
    const sub2 = await queueSubmission({
      subject_id: "subj_01",
      diary_id: "diary_b",
      assignment_id: "assign_02",
      answers: { pain: 5 },
      change_reason: "reason 2",
      username: "user1",
    });
    const sub3 = await queueSubmission({
      subject_id: "subj_01",
      diary_id: "diary_c",
      assignment_id: "assign_03",
      answers: { pain: 8 },
      change_reason: "reason 3",
      username: "user1",
    });

    expect(sub1.sequence_number).toBe(1);
    expect(sub2.sequence_number).toBe(2);
    expect(sub3.sequence_number).toBe(3);

    // 2. Perform bulk update
    const updates = [
      {
        sequence_number: 1,
        status: "CREATED",
        additionalFields: { resolved_answers: { pain: 2 } },
      },
      {
        sequence_number: 2,
        status: "MERGED",
        additionalFields: { resolved_answers: { pain: 6 } },
      },
      {
        sequence_number: 3,
        status: "IGNORED_SERVER_WINS",
        additionalFields: { resolved_answers: { pain: 9 } },
      },
    ];

    const result = await bulkUpdateSubmissionStatuses(updates);
    expect(result.length).toBe(3);
    expect(result[0].status).toBe("CREATED");
    expect(result[1].status).toBe("MERGED");
    expect(result[1].resolved_answers).toEqual({ pain: 6 });
    expect(result[2].status).toBe("IGNORED_SERVER_WINS");

    // 3. Confirm in DB
    const all = await getAllSubmissions();
    // getAllSubmissions returns descending sequence_number order (3, 2, 1)
    expect(all[2].status).toBe("CREATED");
    expect(all[1].status).toBe("MERGED");
    expect(all[0].status).toBe("IGNORED_SERVER_WINS");
  });

  it("should match out-of-order server responses to local queue items precisely using transaction keys", async () => {
    // 1. Create two submissions
    const sub1 = await queueSubmission({
      subject_id: "subj_01",
      diary_id: "diary_a",
      assignment_id: "assign_01",
      answers: { pain: 1 },
      change_reason: "initial",
      username: "user1",
    });
    const sub2 = await queueSubmission({
      subject_id: "subj_01",
      diary_id: "diary_b",
      assignment_id: "assign_02",
      answers: { pain: 4 },
      change_reason: "initial",
      username: "user1",
    });

    const client_id = sub1.client_id;
    expect(sub2.client_id).toBe(client_id);

    // 2. Mock global fetch to return response out-of-order (sequence 2 first, then 1) with key markers
    globalThis.fetch = vi.fn().mockImplementation(() =>
      Promise.resolve({
        ok: true,
        json: () =>
          Promise.resolve({
            status: "success",
            results: [
              {
                status: "MERGED",
                answers: { pain: 5 },
                offline_sync_markers: {
                  sequence_number: 2,
                  client_id: client_id,
                },
              },
              {
                status: "CREATED",
                answers: { pain: 1 },
                offline_sync_markers: {
                  sequence_number: 1,
                  client_id: client_id,
                },
              },
            ],
          }),
      })
    );

    // 3. Trigger sync
    await syncOfflineQueue();

    // 4. Verify local items resolved with correct statuses matching their sequence numbers
    const all = await getAllSubmissions();
    const item1 = all.find((x) => x.sequence_number === 1);
    const item2 = all.find((x) => x.sequence_number === 2);

    expect(item1.status).toBe("CREATED");
    expect(item1.resolved_answers).toEqual({ pain: 1 });

    expect(item2.status).toBe("MERGED");
    expect(item2.resolved_answers).toEqual({ pain: 5 });
  });

  it("should omit unresolved local queue items and keep them QUEUED if omitted from partial server response", async () => {
    // 1. Create three submissions
    const sub1 = await queueSubmission({
      subject_id: "subj_01",
      diary_id: "diary_a",
      assignment_id: "assign_01",
      answers: { pain: 1 },
      change_reason: "initial",
      username: "user1",
    });
    await queueSubmission({
      subject_id: "subj_01",
      diary_id: "diary_b",
      assignment_id: "assign_02",
      answers: { pain: 4 },
      change_reason: "initial",
      username: "user1",
    });
    await queueSubmission({
      subject_id: "subj_01",
      diary_id: "diary_c",
      assignment_id: "assign_03",
      answers: { pain: 7 },
      change_reason: "initial",
      username: "user1",
    });

    const client_id = sub1.client_id;

    // 2. Mock global fetch to only return resolution for sequence 1 and 3, omitting sequence 2
    globalThis.fetch = vi.fn().mockImplementation(() =>
      Promise.resolve({
        ok: true,
        json: () =>
          Promise.resolve({
            status: "success",
            results: [
              {
                status: "CREATED",
                answers: { pain: 1 },
                offline_sync_markers: {
                  sequence_number: 1,
                  client_id: client_id,
                },
              },
              {
                status: "CREATED",
                answers: { pain: 7 },
                offline_sync_markers: {
                  sequence_number: 3,
                  client_id: client_id,
                },
              },
            ],
          }),
      })
    );

    // 3. Trigger sync
    await syncOfflineQueue();

    // 4. Verify sequence 1 & 3 are synced, while sequence 2 remains in QUEUED state
    const all = await getAllSubmissions();
    const item1 = all.find((x) => x.sequence_number === 1);
    const item2 = all.find((x) => x.sequence_number === 2);
    const item3 = all.find((x) => x.sequence_number === 3);

    expect(item1.status).toBe("CREATED");
    expect(item3.status).toBe("CREATED");

    expect(item2.status).toBe("QUEUED");
    expect(item2.resolved_answers).toBeNull();
  });

  it("should gracefully fallback to array index matching when unique keys are entirely absent in server response", async () => {
    // 1. Create two submissions
    await queueSubmission({
      subject_id: "subj_01",
      diary_id: "diary_a",
      assignment_id: "assign_01",
      answers: { pain: 1 },
      change_reason: "initial",
      username: "user1",
    });
    await queueSubmission({
      subject_id: "subj_01",
      diary_id: "diary_b",
      assignment_id: "assign_02",
      answers: { pain: 4 },
      change_reason: "initial",
      username: "user1",
    });

    // 2. Mock global fetch without any tracking keys (simulating legacy API)
    globalThis.fetch = vi.fn().mockImplementation(() =>
      Promise.resolve({
        ok: true,
        json: () =>
          Promise.resolve({
            status: "success",
            results: [
              {
                status: "CREATED",
                answers: { pain: 1 },
              },
              {
                status: "CREATED",
                answers: { pain: 4 },
              },
            ],
          }),
      })
    );

    // 3. Trigger sync
    await syncOfflineQueue();

    // 4. Verify local items are updated based on their array index fallback
    const all = await getAllSubmissions();
    const item1 = all.find((x) => x.sequence_number === 1);
    const item2 = all.find((x) => x.sequence_number === 2);

    expect(item1.status).toBe("CREATED");
    expect(item2.status).toBe("CREATED");
  });

  it("should bypass and exclude undecryptable records from payload compilation, keeping them intact as QUEUED, and retry them when correct session key is restored", async () => {
    // 1. Initialize session key with User A's session material
    const rawMaterialA = new Uint8Array(32);
    for (let i = 0; i < 32; i++) rawMaterialA[i] = i;
    await initSessionKey(rawMaterialA);

    // 2. Queue Submission A under User A's active key
    const subA = await queueSubmission({
      subject_id: "subj_user_a",
      diary_id: "diary_a",
      assignment_id: "assign_01",
      answers: { pain: 1 },
      change_reason: "User A survey",
      username: "userA",
    });

    expect(subA.sequence_number).toBe(1);

    // 3. Switch device rotation: User B logs in (change active session key material)
    const rawMaterialB = new Uint8Array(32);
    for (let i = 0; i < 32; i++) rawMaterialB[i] = i + 100; // different key
    await initSessionKey(rawMaterialB);

    // 4. Queue Submission B under User B's active key
    const subB = await queueSubmission({
      subject_id: "subj_user_b",
      diary_id: "diary_b",
      assignment_id: "assign_02",
      answers: { pain: 5 },
      change_reason: "User B survey",
      username: "userB",
    });

    expect(subB.sequence_number).toBe(2);

    // 5. Mock fetch to record payload and return success result
    let sentPayload = null;
    globalThis.fetch = vi.fn().mockImplementation((url, options) => {
      sentPayload = JSON.parse(options.body);
      return Promise.resolve({
        ok: true,
        json: () =>
          Promise.resolve({
            status: "success",
            results: [
              {
                status: "CREATED",
                answers: { pain: 5 },
                offline_sync_markers: {
                  sequence_number: 2,
                  client_id: subB.client_id,
                },
              },
            ],
          }),
      });
    });

    // 6. Trigger sync
    await syncOfflineQueue();

    // Verify User A's undecryptable record was bypassed and only User B's record was sent in the bulk payload
    expect(sentPayload).not.toBeNull();
    expect(sentPayload.submissions).toHaveLength(1);
    expect(
      sentPayload.submissions[0].offline_sync_markers.sequence_number
    ).toBe(2);

    // Verify local DB states: subB should be CREATED, subA must remain intact as QUEUED (no deletion/migration/corruption)
    const all = await getAllSubmissions();
    const itemA = all.find((x) => x.sequence_number === 1);
    const itemB = all.find((x) => x.sequence_number === 2);

    expect(itemB.status).toBe("CREATED");
    // Since we are currently logged in as User B, calling getAllSubmissions tries to decrypt itemA with User B's key,
    // so in-memory it will be returned with status "DECRYPTION_ERROR".
    expect(itemA.status).toBe("DECRYPTION_ERROR");

    // But let's check that the database still stores its status as "QUEUED" and hasn't overwritten or deleted it.
    // We can restore User A's session key and verify we can read it and retry the sync!
    await initSessionKey(rawMaterialA);

    // Fetching now should successfully decrypt itemA with status "QUEUED"
    const allRestored = await getAllSubmissions();
    const itemARestored = allRestored.find((x) => x.sequence_number === 1);
    expect(itemARestored.status).toBe("QUEUED");
    expect(itemARestored.answers).toEqual({ pain: 1 });

    // Mock fetch for User A's retry sync
    globalThis.fetch = vi.fn().mockImplementation((url, options) => {
      sentPayload = JSON.parse(options.body);
      return Promise.resolve({
        ok: true,
        json: () =>
          Promise.resolve({
            status: "success",
            results: [
              {
                status: "CREATED",
                answers: { pain: 1 },
                offline_sync_markers: {
                  sequence_number: 1,
                  client_id: subA.client_id,
                },
              },
            ],
          }),
      });
    });

    // 7. Trigger sync again under restored User A session
    await syncOfflineQueue();

    // Verify subA successfully synchronized
    expect(sentPayload.submissions).toHaveLength(1);
    expect(
      sentPayload.submissions[0].offline_sync_markers.sequence_number
    ).toBe(1);

    const allFinal = await getAllSubmissions();
    const itemAFinal = allFinal.find((x) => x.sequence_number === 1);
    expect(itemAFinal.status).toBe("CREATED");
    expect(itemAFinal.resolved_answers).toEqual({ pain: 1 });
  });
});
