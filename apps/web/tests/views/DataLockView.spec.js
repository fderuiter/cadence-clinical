import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import DataLockView from "@/views/DataLockView.vue";

describe("DataLockView component", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.stubEnv("VITE_DEMO_MODE", "true");
    vi.stubGlobal("fetch", vi.fn());
  });

  it("applies optimistic state transition immediately on submitLock", async () => {
    let resolveLockFetch;
    const lockFetchPromise = new Promise((resolve) => {
      resolveLockFetch = resolve;
    });

    global.fetch.mockImplementation(async (url) => {
      if (url.includes("/lock")) {
        await lockFetchPromise;
        return {
          ok: true,
          json: async () => ({
            lock_id: "LOCK-SERVER-101",
            status: "HARD_LOCK",
          }),
        };
      }
      return {
        ok: true,
        json: async () => [],
      };
    });

    const wrapper = mount(DataLockView);

    wrapper.vm.lockForm = {
      scope_type: "SITE",
      scope_id: "SITE-101",
      study_id: "STUDY-001",
      action: "HARD_LOCK",
      reason: "PI Request",
    };

    // Before submit: site-101 is UNLOCKED
    expect(wrapper.vm.studyTree.sites[0].status).toBe("UNLOCKED");

    const submitPromise = wrapper.vm.submitLock();

    // Immediately after calling submit, before fetch resolves:
    // SITE-101 and all descendant nodes transition to optimistic pending lock state
    expect(wrapper.vm.studyTree.sites[0].status).toBe("PENDING_LOCK");
    expect(wrapper.vm.studyTree.sites[0].subjects[0].status).toBe(
      "PENDING_LOCK"
    );

    resolveLockFetch();
    await submitPromise;

    // After fetch resolves successfully:
    expect(wrapper.vm.studyTree.sites[0].status).toBe("HARD_LOCK");
  });

  it("reverts to pre-operation snapshot automatically on server error", async () => {
    global.fetch.mockImplementation(async (url) => {
      if (url.includes("/lock")) {
        return {
          ok: false,
          status: 500,
          statusText: "Internal Server Error",
        };
      }
      return {
        ok: true,
        json: async () => [],
      };
    });

    const wrapper = mount(DataLockView);

    wrapper.vm.lockForm = {
      scope_type: "SITE",
      scope_id: "SITE-101",
      study_id: "STUDY-001",
      action: "HARD_LOCK",
      reason: "PI Request",
    };

    expect(wrapper.vm.studyTree.sites[0].status).toBe("UNLOCKED");

    await wrapper.vm.submitLock();

    // Due to 500 server error, state automatically reverts back to UNLOCKED snapshot
    expect(wrapper.vm.studyTree.sites[0].status).toBe("UNLOCKED");
    expect(wrapper.vm.studyTree.sites[0].subjects[0].status).toBe("UNLOCKED");
  });
});
