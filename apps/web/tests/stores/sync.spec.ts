import { describe, it, expect, beforeEach } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { useSyncStore } from "../../src/stores/sync";

// #4529: the offline sync status the eCRF shows (utils/syncEngine.ts drives the transitions).
describe("sync store", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  it("starts idle with nothing pending and no conflict", () => {
    const store = useSyncStore();

    expect(store.status).toBe("IDLE");
    expect(store.pendingCount).toBe(0);
    expect(store.conflict).toBeNull();
  });

  it("moves from syncing to a detected conflict and back once the conflict is cleared", () => {
    const store = useSyncStore();
    const conflict = { entityId: "FORM-1", local: { v: 1 }, server: { v: 2 } };

    store.setPendingCount(3);
    store.setStatus("SYNCING");
    store.setConflict(conflict);
    store.setStatus("CONFLICT_DETECTED");

    expect(store.status).toBe("CONFLICT_DETECTED");
    expect(store.conflict).toEqual(conflict);
    expect(store.pendingCount).toBe(3);

    store.clearConflict();
    store.setStatus("IDLE");

    expect(store.conflict).toBeNull();
    expect(store.status).toBe("IDLE");
  });
});
