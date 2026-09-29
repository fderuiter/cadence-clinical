import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import AggregateSafetyConsole from "../src/components/clinical/AggregateSafetyConsole.vue";
import { useToastStore } from "../src/stores/toasts";

/**
 * #4523: periodic-report compilation, QPPV sign-off and regulatory triage report server failures instead of
 * inventing report ids, snapshot hashes, signature manifests or locally derived SUSAR clocks.
 */
describe("AggregateSafetyConsole failure handling (#4523)", () => {
  let pinia;
  let routes;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    routes = {};
    vi.stubGlobal(
      "fetch",
      vi.fn((url) => {
        const key = Object.keys(routes).find((r) => String(url).includes(r));
        if (key) return Promise.resolve(routes[key]());
        return Promise.resolve(new Response("{}", { status: 200 }));
      })
    );
    vi.spyOn(console, "error").mockImplementation(() => {});
    vi.spyOn(console, "warn").mockImplementation(() => {});
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  const failWith = (status) => () =>
    new Response(JSON.stringify({ title: "Upstream failure" }), { status });

  async function mountConsole() {
    const wrapper = mount(AggregateSafetyConsole, {
      global: { plugins: [pinia] },
    });
    await flushPromises();
    return wrapper;
  }

  function expectErrorToast(titleFragment, status) {
    const toast = useToastStore().toasts.find((t) =>
      t.title.includes(titleFragment)
    );
    expect(toast).toMatchObject({ tone: "error" });
    expect(toast.message).toContain(`HTTP ${status}`);
  }

  describe.each([500, 502, 504])("HTTP %i", (status) => {
    it("compileReport invents no report id, snapshot hash or counts", async () => {
      routes["/periodic-reports/compile"] = failWith(status);
      const wrapper = await mountConsole();
      const before = {
        id: wrapper.vm.activeReportId,
        hash: wrapper.vm.snapshotHash,
        cases: wrapper.vm.frozenCasesCount,
        status: wrapper.vm.reportStatus,
      };

      await wrapper.vm.compileReport();

      expect(wrapper.vm.activeReportId).toBe(before.id);
      expect(wrapper.vm.snapshotHash).toBe(before.hash);
      expect(wrapper.vm.frozenCasesCount).toBe(before.cases);
      expect(wrapper.vm.reportStatus).toBe(before.status);
      expect(wrapper.vm.isCompiling).toBe(false);
      expectErrorToast("compilation failed", status);
    });

    it("executeQppvSignoff does not approve or produce a signature manifest", async () => {
      routes["/qppv-sign"] = failWith(status);
      const wrapper = await mountConsole();
      wrapper.vm.qppvSecret = "secret";
      wrapper.vm.isQppvModalOpen = true;
      const statusBefore = wrapper.vm.reportStatus;

      await wrapper.vm.executeQppvSignoff();
      await wrapper.vm.$nextTick();

      expect(wrapper.vm.reportStatus).toBe(statusBefore);
      expect(wrapper.vm.reportStatus).not.toBe("APPROVED");
      expect(wrapper.vm.qppvManifest).toBeNull();
      expect(wrapper.vm.isQppvModalOpen).toBe(true);
      expect(wrapper.find(".qppv-manifest-card").exists()).toBe(false);
      expectErrorToast("QPPV sign-off failed", status);
    });

    it("runTriageSimulation shows no locally derived SUSAR result", async () => {
      routes["/safety/rules/evaluate"] = failWith(status);
      const wrapper = await mountConsole();
      wrapper.vm.simSeriousnessDeath = true;
      wrapper.vm.simCausality = "RELATED";
      wrapper.vm.simExpectedness = "UNEXPECTED";

      await wrapper.vm.runTriageSimulation();
      await wrapper.vm.$nextTick();

      expect(wrapper.vm.simulationResult).toBeNull();
      expect(wrapper.find(".sim-results-card").exists()).toBe(false);
      expectErrorToast("triage evaluation failed", status);
    });
  });

  it("network errors are reported the same way", async () => {
    routes["/qppv-sign"] = () => {
      throw new TypeError("Failed to fetch");
    };
    const wrapper = await mountConsole();
    wrapper.vm.qppvSecret = "secret";

    await wrapper.vm.executeQppvSignoff();

    expect(wrapper.vm.qppvManifest).toBeNull();
    expect(wrapper.vm.reportStatus).not.toBe("APPROVED");
    expect(useToastStore().toasts[0]).toMatchObject({ tone: "error" });
  });

  it("successful QPPV sign-off still approves with the server manifest", async () => {
    const manifest = {
      qppv_user_id: "qppv.01",
      manifest_id: "sig_server_1",
      verification_status: "VALID",
    };
    routes["/qppv-sign"] = () =>
      new Response(JSON.stringify({ qppv_manifest: manifest }), {
        status: 200,
      });
    const wrapper = await mountConsole();
    wrapper.vm.qppvSecret = "secret";

    await wrapper.vm.executeQppvSignoff();

    expect(wrapper.vm.reportStatus).toBe("APPROVED");
    expect(wrapper.vm.qppvManifest).toEqual(manifest);
    expect(useToastStore().toasts).toHaveLength(0);
  });

  it("successful triage shows the server evaluation", async () => {
    const result = { case_id: "C-1", is_susar: false, evaluations: [] };
    routes["/safety/rules/evaluate"] = () =>
      new Response(JSON.stringify(result), { status: 200 });
    const wrapper = await mountConsole();

    await wrapper.vm.runTriageSimulation();

    expect(wrapper.vm.simulationResult).toEqual(result);
  });
});
