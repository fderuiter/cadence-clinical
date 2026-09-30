import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createRouter, createWebHistory } from "vue-router";
import RulesView from "@/views/RulesView.vue";
import { useAuthStore } from "@/stores/auth";
import { useClinicalStore } from "@/stores/clinical";
import { useToastStore } from "@/stores/toasts.js";
import { apiClient } from "@/api/apiClient";

vi.mock("@/api/apiClient", () => ({
  apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}));

const router = createRouter({
  history: createWebHistory(),
  routes: [{ path: "/rules", component: RulesView }],
});

const RULE = {
  id: "rule_1",
  type: "skip_logic",
  target_field: "pulse_details",
  target_form: "form_vs",
  action: "show",
  condition: { type: "constant", value: true },
  compiled_xpath: "/clinical_data/form_vs/pulse > 100",
};

function httpError(status: number) {
  const err = new Error(`Request failed with status ${status}`) as Error & {
    status: number;
  };
  err.status = status;
  return err;
}

const STATUSES = [500, 502, 504];

// #4523: failed rules/query API calls must never be shown or recorded as successful.
describe("RulesView API failure handling (#4523)", () => {
  let pinia: any;
  let clinicalStore: any;
  let alertSpy: any;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.isDemoMode = false;
    authStore.rawRoles = ["Data Manager"];
    clinicalStore = useClinicalStore();

    vi.mocked(apiClient.get).mockReset().mockResolvedValue([RULE]);
    vi.mocked(apiClient.post)
      .mockReset()
      .mockImplementation((url: string) =>
        url.includes("/preview") || url.includes("/validate")
          ? Promise.resolve({ xpath: "x", failures: [], circular_cycles: [] })
          : Promise.resolve({})
      );
    vi.mocked(apiClient.put).mockReset().mockResolvedValue({});
    vi.mocked(apiClient.delete).mockReset().mockResolvedValue(null);
    alertSpy = vi.spyOn(window, "alert").mockImplementation(() => {});
  });

  afterEach(() => {
    alertSpy.mockRestore();
  });

  async function mountView() {
    const wrapper = mount(RulesView, { global: { plugins: [pinia, router] } });
    await flushPromises();
    return wrapper;
  }

  function ledgerActions() {
    return clinicalStore.ledgerBlocks.map((b: any) => b.action);
  }

  function expectErrorToast(title: string, status: number) {
    const toasts = useToastStore().toasts;
    expect(toasts).toHaveLength(1);
    expect(toasts[0]).toMatchObject({ tone: "error", title });
    expect(toasts[0].message).toContain(`HTTP ${status}`);
  }

  function expectRefusal(title: string) {
    expect(useToastStore().toasts).toEqual([
      expect.objectContaining({ tone: "error", title }),
    ]);
    expect(alertSpy).not.toHaveBeenCalled();
  }

  it("refuses candidate adjudication, which no service serves, and keeps the query a CANDIDATE", async () => {
    const wrapper: any = await mountView();
    const candidate = wrapper.vm.dashboardQueries.find(
      (q: any) => q.id === "q-cand-1"
    );

    wrapper.vm.promptAdjudicateCandidate();
    await flushPromises();

    expect(candidate.status).toBe("CANDIDATE");
    expect(ledgerActions()).not.toContain("QUERY_ADJUDICATION");
    expect(wrapper.vm.showReasonModal).toBe(false);
    expectRefusal("Adjudicating a candidate query isn't available yet");
  });

  it.each(STATUSES)(
    "rule save HTTP %i records no RULE_SAVE block, no success alert, keeps the editor open",
    async (status) => {
      const wrapper: any = await mountView();
      vi.mocked(apiClient.post).mockImplementation((url: string) =>
        url.includes("/preview") || url.includes("/validate")
          ? Promise.resolve({ xpath: "x", failures: [], circular_cycles: [] })
          : Promise.reject(httpError(status))
      );
      wrapper.vm.showEditor = true;
      wrapper.vm.targetField = "pulse";
      await flushPromises();
      wrapper.vm.promptSaveRule();

      await wrapper.vm.confirmChangeReason("New pulse safety rule");
      await flushPromises();

      expect(ledgerActions()).not.toContain("RULE_SAVE");
      expect(alertSpy).not.toHaveBeenCalled();
      expect(wrapper.vm.showEditor).toBe(true);
      expect(wrapper.vm.activeRules.map((r: any) => r.id)).toEqual(["rule_1"]);
      expect(wrapper.text()).not.toContain("Local fallback compiled");
      expectErrorToast("Rule save failed", status);
    }
  );

  it("rule save sends only the change reason, never a client-side gateway signature", async () => {
    const wrapper: any = await mountView();
    wrapper.vm.targetField = "pulse";
    await flushPromises();
    wrapper.vm.promptSaveRule();

    await wrapper.vm.confirmChangeReason("New pulse safety rule");

    const saveCall = vi
      .mocked(apiClient.post)
      .mock.calls.find(([url]) => url === "/api/v1/studies/study_1/rules");
    expect(saveCall).toBeDefined();
    expect((saveCall![2] as any).headers).toEqual({
      "X-Change-Reason": "New pulse safety rule",
    });
    for (const call of [
      ...vi.mocked(apiClient.get).mock.calls,
      ...vi.mocked(apiClient.post).mock.calls,
    ]) {
      const headers = (call[call.length - 1] as any)?.headers ?? {};
      expect(headers).not.toHaveProperty("X-Gateway-Signature");
      expect(headers).not.toHaveProperty("X-User-Id");
    }
    expect(ledgerActions()).toContain("RULE_SAVE");
    expect(useToastStore().toasts).toHaveLength(0);
  });

  it.each(STATUSES)(
    "rule delete HTTP %i records no RULE_DELETE block and no success alert",
    async (status) => {
      vi.mocked(apiClient.delete).mockRejectedValue(httpError(status));
      const wrapper: any = await mountView();
      wrapper.vm.promptDeleteRule("rule_1");

      await wrapper.vm.confirmChangeReason("Obsolete rule");
      await flushPromises();

      expect(apiClient.delete).toHaveBeenCalledTimes(1);
      expect(ledgerActions()).not.toContain("RULE_DELETE");
      expect(alertSpy).not.toHaveBeenCalled();
      expect(wrapper.vm.activeRules.map((r: any) => r.id)).toEqual(["rule_1"]);
      expectErrorToast("Rule deletion failed", status);
    }
  );

  it("rule delete success still writes RULE_DELETE", async () => {
    const wrapper: any = await mountView();
    wrapper.vm.promptDeleteRule("rule_1");

    await wrapper.vm.confirmChangeReason("Obsolete rule");

    expect(ledgerActions()).toContain("RULE_DELETE");
    expect(useToastStore().toasts).toHaveLength(0);
  });

  it("refuses the anomaly scan, which no service serves, instead of reporting a result", async () => {
    const wrapper: any = await mountView();

    wrapper.vm.triggerAnomalyScan();

    expectRefusal("Scanning for cross-domain anomalies isn't available yet");
  });
});
