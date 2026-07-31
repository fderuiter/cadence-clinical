import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { useSyncStore } from '../../src/stores/sync';
import { ClientSyncEngine, PendingDelta } from '../../src/utils/syncEngine';

// Mock global fetch
const mockFetch = vi.fn();
globalThis.fetch = mockFetch;

describe('ClientSyncEngine and Conflict Resolution Sync Queue', () => {
  let syncStore: any;
  let syncEngine: ClientSyncEngine;

  beforeEach(async () => {
    const pinia = createPinia();
    setActivePinia(pinia);

    syncStore = useSyncStore();
    syncEngine = new ClientSyncEngine();
    await syncEngine.dbManager.init();

    mockFetch.mockReset();
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('flushQueue() drains IndexedDB items and updates pendingCount to 0 on success', async () => {
    const delta: Omit<PendingDelta, 'deltaId'> = {
      entityType: 'FormSubmission',
      entityId: 'SUBJ-001-VS',
      action: 'UPDATE',
      payload: { vssbp: '130' },
      clientTimestampUtc: new Date().toISOString(),
      reasonForChange: 'Initial corrections'
    };

    // Queue an item
    await syncEngine.queueDelta(delta);
    expect(syncStore.pendingCount).toBe(1);

    // Mock successful fetch
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({ status: 'success' })
    });

    // Run flush
    await syncEngine.flushQueue();

    expect(mockFetch).toHaveBeenCalledTimes(1);
    const [url, options] = mockFetch.mock.calls[0];
    expect(url).toBe('/api/v1/offline/sync-batch');
    const body = JSON.parse(options.body);
    expect(body.deltas).toHaveLength(1);
    expect(body.deltas[0].payload.vssbp).toBe('130');

    expect(syncStore.status).toBe('COMPLETED');
    expect(syncStore.pendingCount).toBe(0);

    const remaining = await syncEngine.dbManager.getDeltas();
    expect(remaining).toHaveLength(0);
  });

  it('server HTTP 500 error leaves items in IndexedDB queue for subsequent retry', async () => {
    const delta: Omit<PendingDelta, 'deltaId'> = {
      entityType: 'FormSubmission',
      entityId: 'SUBJ-001-VS',
      action: 'UPDATE',
      payload: { vssbp: '140' },
      clientTimestampUtc: new Date().toISOString(),
      reasonForChange: 'Second check'
    };

    await syncEngine.queueDelta(delta);
    expect(syncStore.pendingCount).toBe(1);

    // Mock server error
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 500,
      json: async () => ({ detail: 'Internal Server Error' })
    });

    await syncEngine.flushQueue();

    expect(mockFetch).toHaveBeenCalledTimes(1);
    expect(syncStore.status).toBe('ERROR');
    expect(syncStore.pendingCount).toBe(1);

    // Ensure it remains in database queue
    const remaining = await syncEngine.dbManager.getDeltas();
    expect(remaining).toHaveLength(1);
    expect(remaining[0].payload.vssbp).toBe('140');
  });

  it('implements exponential backoff retry on failure', async () => {
    const delta: Omit<PendingDelta, 'deltaId'> = {
      entityType: 'FormSubmission',
      entityId: 'SUBJ-001-VS',
      action: 'UPDATE',
      payload: { vssbp: '150' },
      clientTimestampUtc: new Date().toISOString(),
      reasonForChange: 'Third check'
    };

    await syncEngine.queueDelta(delta);

    // First attempt fails with 503
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 503,
      json: async () => ({})
    });

    await syncEngine.flushQueue();
    expect(syncEngine.getRetryCount()).toBe(1);
    expect(syncEngine.getRetryTimeoutId()).not.toBeNull();

    // Trigger timer for retry (first retry backoff delay: ~2000ms)
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({ status: 'success' })
    });

    await vi.runOnlyPendingTimersAsync();

    expect(syncEngine.getRetryCount()).toBe(0);
    expect(syncStore.status).toBe('COMPLETED');
    expect(syncStore.pendingCount).toBe(0);
  });

  it('server conflict response triggers CONFLICT_DETECTED status and sets active conflict in store', async () => {
    const delta: Omit<PendingDelta, 'deltaId'> = {
      entityType: 'FormSubmission',
      entityId: 'SUBJ-001-VS',
      action: 'UPDATE',
      payload: { vssbp: '160' },
      clientTimestampUtc: new Date().toISOString(),
      reasonForChange: 'Conflict check'
    };

    await syncEngine.queueDelta(delta);

    // Mock 409 conflict
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 409,
      json: async () => ({
        status: 'CONFLICT_DETECTED',
        conflict: {
          deltaId: 'dummy_id',
          entityType: 'FormSubmission',
          entityId: 'SUBJ-001-VS',
        },
        clientValue: { vssbp: '160' },
        serverValue: { vssbp: '120', status: 'LOCKED' }
      })
    });

    await syncEngine.flushQueue();

    expect(syncStore.status).toBe('CONFLICT_DETECTED');
    expect(syncStore.conflict).not.toBeNull();
    expect(syncStore.conflict.clientValue.vssbp).toBe('160');
    expect(syncStore.conflict.serverValue.vssbp).toBe('120');

    // Conflict items should still remain in queue until resolved
    const remaining = await syncEngine.dbManager.getDeltas();
    expect(remaining).toHaveLength(1);
  });

  it('resolving the conflict sends the decision and completes sync', async () => {
    const delta: Omit<PendingDelta, 'deltaId'> = {
      entityType: 'FormSubmission',
      entityId: 'SUBJ-001-VS',
      action: 'UPDATE',
      payload: { vssbp: '160' },
      clientTimestampUtc: new Date().toISOString(),
      reasonForChange: 'Conflict check'
    };

    await syncEngine.queueDelta(delta);
    const deltas = await syncEngine.dbManager.getDeltas();
    const targetDeltaId = deltas[0].deltaId;

    // Set conflict in store first
    syncStore.setStatus('CONFLICT_DETECTED');
    syncStore.setConflict({
      conflictItem: deltas[0],
      clientValue: { vssbp: '160' },
      serverValue: { vssbp: '120' }
    });

    // Mock successful resolve-conflict request
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({ status: 'success' })
    });

    await syncEngine.resolveConflict(targetDeltaId, 'SERVER_WIN', 'Overwrite with server record');

    expect(mockFetch).toHaveBeenCalledTimes(1);
    const [url, options] = mockFetch.mock.calls[0];
    expect(url).toBe('/api/v1/offline/resolve-conflict');
    const body = JSON.parse(options.body);
    expect(body.delta_id).toBe(targetDeltaId);
    expect(body.strategy).toBe('SERVER_WIN');
    expect(body.reason_for_change).toBe('Overwrite with server record');

    expect(syncStore.conflict).toBeNull();
    expect(syncStore.status).toBe('COMPLETED');
    expect(syncStore.pendingCount).toBe(0);

    const remaining = await syncEngine.dbManager.getDeltas();
    expect(remaining).toHaveLength(0);
  });
});
