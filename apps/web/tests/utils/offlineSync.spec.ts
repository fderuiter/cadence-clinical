import {
  vi,
  describe,
  it,
  expect,
  beforeEach,
  afterEach,
  beforeAll,
} from "vitest";
import "fake-indexeddb/auto";
import { createPinia, setActivePinia } from "pinia";
import {
  openSyncDatabase,
  queueEproSubmission,
  flushOfflineQueue,
  OfflineCapturePayload,
} from "../../src/utils/offlineSync";
import { useAuthStore } from "../../src/stores/auth";

// Polyfill window.crypto if needed in JSDOM test environment
beforeAll(() => {
  if (typeof window !== "undefined" && !window.crypto) {
    Object.defineProperty(window, "crypto", {
      value: globalThis.crypto,
      writable: true,
      configurable: true,
    });
  }
});

const mockFetch = vi.fn();
globalThis.fetch = mockFetch;

describe("OfflineSyncQueue and Web Crypto Engine", () => {
  const sessionToken = "session-token-abc-12345";
  const sampleSubmission: Omit<OfflineCapturePayload, "sequence_number"> = {
    id: "sub_diary_1001",
    subject_id: "SUBJ-101",
    diary_id: "DIARY-01",
    device_timestamp: "2026-07-30T10:00:00Z",
    answers: { "VS.SYSBP": 120, "VS.DIABP": 80 },
    client_id: "device-01",
    conflict_strategy: "CLIENT_WINS",
  };

  beforeEach(async () => {
    vi.clearAllMocks();
    mockFetch.mockReset();

    // Initialize Pinia
    const pinia = createPinia();
    setActivePinia(pinia);

    // Clear database before each test
    const db = await openSyncDatabase();
    await new Promise<void>((resolve, reject) => {
      const tx = db.transaction("pending_sync_queue", "readwrite");
      const store = tx.objectStore("pending_sync_queue");
      const request = store.clear();
      tx.oncomplete = () => resolve();
      tx.onerror = () => reject(tx.error);
    });
  });

  it("should encrypt and sign an offline ePRO submission and store it in IndexedDB", async () => {
    // Queue submission
    await queueEproSubmission(sampleSubmission, sessionToken);

    // Verify record is persisted in IndexedDB and is encrypted (cannot be read as plain text)
    const db = await openSyncDatabase();
    const records = await new Promise<any[]>((resolve, reject) => {
      const tx = db.transaction("pending_sync_queue", "readonly");
      const store = tx.objectStore("pending_sync_queue");
      const request = store.getAll();
      request.onsuccess = () => resolve(request.result || []);
      request.onerror = () => reject(request.error);
    });

    expect(records).toHaveLength(1);
    expect(records[0].id).toBe(sampleSubmission.id);
    expect(records[0].encryptedData).toBeDefined();
    expect(typeof records[0].encryptedData).toBe("string");

    // Plaintext key variables shouldn't exist in the outer record object
    expect(records[0].subject_id).toBeUndefined();
    expect(records[0].answers).toBeUndefined();
  });

  it("should successfully decrypt the queue entries and flush them to the server", async () => {
    // 1. Queue submission
    await queueEproSubmission(sampleSubmission, sessionToken);

    // 2. Mock successful fetch response
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({
        status: "success",
        processed_count: 1,
        created_count: 1,
        updated_count: 0,
        ignored_count: 0,
        conflict_count: 0,
        results: [],
      }),
    });

    // 3. Flush queue
    const result = await flushOfflineQueue(sessionToken);

    // 4. Verify fetch request payload structure and signature headers
    expect(mockFetch).toHaveBeenCalledTimes(1);
    const [url, options] = mockFetch.mock.calls[0];
    expect(url).toBe("/api/v1/interop/epro/sync");
    expect(options.method).toBe("POST");
    expect(options.headers["Authorization"]).toBe(`Bearer ${sessionToken}`);

    const body = JSON.parse(options.body);
    expect(body.submissions).toHaveLength(1);
    const sub = body.submissions[0];
    expect(sub.subject_id).toBe(sampleSubmission.subject_id);
    expect(sub.diary_id).toBe(sampleSubmission.diary_id);
    expect(sub.answers).toEqual(sampleSubmission.answers);

    // Verify sync markers exist
    expect(sub.offline_sync_markers).toBeDefined();
    expect(sub.offline_sync_markers.sequence_number).toBe(1);
    expect(sub.offline_sync_markers.client_id).toBe(sampleSubmission.client_id);
    expect(sub.offline_sync_markers.conflict_strategy).toBe(sampleSubmission.conflict_strategy);
    expect(sub.offline_sync_markers.signature).toBeDefined(); // signature should be generated

    // 5. Verify the queue is now empty
    const db = await openSyncDatabase();
    const records = await new Promise<any[]>((resolve, reject) => {
      const tx = db.transaction("pending_sync_queue", "readonly");
      const store = tx.objectStore("pending_sync_queue");
      const request = store.getAll();
      request.onsuccess = () => resolve(request.result || []);
      request.onerror = () => reject(request.error);
    });
    expect(records).toHaveLength(0);
  });

  it("should fail to decrypt and throw an error when using an incorrect session token", async () => {
    // 1. Queue submission with correct session token
    await queueEproSubmission(sampleSubmission, sessionToken);

    // 2. Flush with incorrect/expired session token
    const incorrectToken = "wrong-session-token-xyz";

    await expect(flushOfflineQueue(incorrectToken)).rejects.toThrow(
      "Decryption failed for offline queue item"
    );
  });

  it("should trigger automatic sync flush when window online event is dispatched with active session", async () => {
    // Setup pinia store state
    const authStore = useAuthStore();
    authStore.accessToken = sessionToken;

    // Queue submission
    await queueEproSubmission(sampleSubmission, sessionToken);

    // Mock successful sync
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({ status: "success" }),
    });

    // Simulate online event
    const onlineEvent = new Event("online");
    window.dispatchEvent(onlineEvent);

    // Wait for async lazy-load and execution inside the event handler
    await new Promise((resolve) => setTimeout(resolve, 50));

    expect(mockFetch).toHaveBeenCalledTimes(1);
  });
});
