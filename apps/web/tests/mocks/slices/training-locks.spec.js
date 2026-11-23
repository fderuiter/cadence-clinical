import { describe, expect, it } from "vitest";
import { useDemoSliceServer } from "./sliceTestHarness.js";

describe("MSW domain slice: training-locks", () => {
  const api = useDemoSliceServer();

  it("enforces kernel lock readiness blockers, commits/releases locks, and resets former demoLocks closure state", async () => {
    const studyRef = "study:00000000-0000-0000-0000-000000000001"; // deid-ignore
    const readiness = await api.requestJson(
      `/api/v1/kernel/studies/${encodeURIComponent(studyRef)}/locks/readiness`
    );
    expect(readiness.status).toBe(200);
    expect(readiness.data.ready_for_lock).toBe(false);
    expect(readiness.data.open_query_count).toBeGreaterThanOrEqual(1);

    const blockedHardLock = await api.requestJson("/api/v1/kernel/locks", {
      method: "POST",
      headers: { "X-Change-Reason": "Attempt premature hard lock" },
      body: JSON.stringify({
        study_ref: studyRef,
        scope_ref: studyRef,
        kind: "Locked",
      }),
    });
    expect(blockedHardLock.status).toBe(409);

    const frozenLock = await api.requestJson("/api/v1/kernel/locks", {
      method: "POST",
      headers: { "X-Change-Reason": "Interim analysis freeze" },
      body: JSON.stringify({
        study_ref: studyRef,
        scope_ref: studyRef,
        kind: "Frozen",
      }),
    });
    expect(frozenLock.status).toBe(201);
    expect(frozenLock.data.kind).toBe("Frozen");
    expect(api.store.state.demoLocks.length).toBe(1);

    const released = await api.requestJson(
      `/api/v1/kernel/locks/${encodeURIComponent(frozenLock.data.ref)}/release`,
      {
        method: "POST",
        headers: { "X-Change-Reason": "Interim analysis complete" },
        body: JSON.stringify({ study_ref: studyRef }),
      }
    );
    expect(released.status).toBe(200);
    expect(released.data.released).toBe(true);

    const batchAssign = await api.requestJson(
      "/api/v1/training/curricula/batch-assign",
      {
        method: "POST",
        body: JSON.stringify({
          study_id: "DEMO-01",
          curriculum_id: "curr-demo-01",
          user_ids: ["crc.site101", "pi.site101"],
        }),
      }
    );
    expect(batchAssign.status).toBe(200);
    expect(batchAssign.data.total_assigned).toBe(1);

    api.store.reset();
    expect(api.store.state.demoLocks).toEqual([]);
    const resetAssignments = await api.requestJson(
      "/api/v1/training/assignments"
    );
    expect(resetAssignments.data.length).toBe(1);
  });
});
