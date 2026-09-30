import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import AmendmentDiffView from "../src/views/AmendmentDiffView.vue";
import { apiClient, ApiError } from "../src/api/apiClient";
import { designerService } from "../src/api/designer";
import { useClinicalStore } from "../src/stores/clinical";
import { useToastStore } from "../src/stores/toasts";

vi.mock("../src/api/apiClient", async (importOriginal) => {
  const actual = await importOriginal();
  return {
    ...actual,
    apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
  };
});

vi.mock("../src/api/designer", () => ({
  designerService: {
    fetchAmendmentDiff: vi.fn(),
    createAmendmentBranch: vi.fn(),
  },
}));

const httpError = (status) =>
  new ApiError(`Request failed with status ${status}`, status, "Error", {
    title: "Upstream failure",
  });

/**
 * #4523: amendment publication and re-consent report server failures. Nothing is published, no gate is
 * cleared, no ledger block or success banner is produced, and seeded diff data is not shown as verified.
 */
describe("AmendmentDiffView failure handling (#4523)", () => {
  let pinia;
  let store;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    store = useClinicalStore();
    vi.mocked(apiClient.get).mockResolvedValue({ categories: {} });
    vi.mocked(apiClient.post).mockResolvedValue({ status: "SUCCESS" });
    vi.mocked(designerService.fetchAmendmentDiff).mockResolvedValue({});
    vi.mocked(designerService.createAmendmentBranch).mockResolvedValue({});
    vi.spyOn(console, "error").mockImplementation(() => {});
    vi.spyOn(console, "warn").mockImplementation(() => {});
  });

  afterEach(() => {
    vi.clearAllMocks();
    vi.restoreAllMocks();
  });

  async function mountView() {
    const wrapper = mount(AmendmentDiffView, {
      global: { plugins: [pinia] },
    });
    await flushPromises();
    return wrapper;
  }

  function lastToast() {
    const toasts = useToastStore().toasts;
    return toasts[toasts.length - 1];
  }

  function expectNoSuccess(wrapper, status) {
    expect(wrapper.vm.notificationBanner?.type).not.toBe("success");
    expect(wrapper.vm.notificationBanner?.type).toBe("error");
    expect(lastToast()).toMatchObject({ tone: "error" });
    expect(lastToast().message).toContain(`HTTP ${status}`);
  }

  describe.each([500, 502, 504])("HTTP %i", (status) => {
    it("submitCreateAmendment does not publish when the Designer branch fails", async () => {
      const wrapper = await mountView();
      wrapper.vm.selectedAmendedVersion = "1.0.0";
      wrapper.vm.newAmendment.target_version = "3.0.0";
      wrapper.vm.showCreateModal = true;
      vi.mocked(designerService.createAmendmentBranch).mockRejectedValue(
        httpError(status)
      );

      await wrapper.vm.submitCreateAmendment();

      expect(apiClient.post).not.toHaveBeenCalledWith(
        "/api/v1/execution/amendments/publish",
        expect.anything()
      );
      expect(wrapper.vm.selectedAmendedVersion).toBe("1.0.0");
      expect(wrapper.vm.showCreateModal).toBe(true);
      expectNoSuccess(wrapper, status);
    });

    it("submitCreateAmendment does not switch version when publish fails", async () => {
      const wrapper = await mountView();
      wrapper.vm.selectedAmendedVersion = "1.0.0";
      wrapper.vm.newAmendment.target_version = "3.0.0";
      wrapper.vm.showCreateModal = true;
      vi.mocked(apiClient.post).mockRejectedValue(httpError(status));

      await wrapper.vm.submitCreateAmendment();

      expect(wrapper.vm.selectedAmendedVersion).toBe("1.0.0");
      expect(wrapper.vm.showCreateModal).toBe(true);
      expectNoSuccess(wrapper, status);
    });

    it("submitReconsent keeps the gate locked", async () => {
      const clearSpy = vi.spyOn(store, "clearReconsentGate");
      const ledgerSpy = vi.spyOn(store, "addLedgerBlock");
      const wrapper = await mountView();
      const subject = { id: "SUBJ-102", isGated: true };
      wrapper.vm.activeModalSubject = subject;
      wrapper.vm.showReconsentModal = true;
      vi.mocked(apiClient.post).mockRejectedValue(httpError(status));

      await wrapper.vm.submitReconsent();

      expect(clearSpy).not.toHaveBeenCalled();
      expect(ledgerSpy).not.toHaveBeenCalled();
      expect(wrapper.vm.showReconsentModal).toBe(true);
      expectNoSuccess(wrapper, status);
    });

    it("executeBulkReconsent marks no subject signed and writes no ledger block", async () => {
      const ledgerSpy = vi.spyOn(store, "addLedgerBlock");
      // Outside a demo build the store holds no sample subjects (#4828), so give it two gated ones.
      store.subjects = ["SUBJ-102", "SUBJ-103"].map((id) => ({
        id,
        status: "ACTIVE",
        active_protocol_version: "1.0.0",
        isGated: true,
      }));
      const wrapper = await mountView();
      const gated = store.subjects.filter((s) => s.isGated).slice(0, 2);
      expect(gated.length).toBeGreaterThan(0);
      const snapshot = JSON.parse(JSON.stringify(store.subjects));
      wrapper.vm.selectedSubjectIds = gated.map((s) => s.id);
      wrapper.vm.showBulkReconsentModal = true;
      vi.mocked(apiClient.post).mockRejectedValue(httpError(status));

      await wrapper.vm.executeBulkReconsent();

      expect(JSON.parse(JSON.stringify(store.subjects))).toEqual(snapshot);
      expect(ledgerSpy).not.toHaveBeenCalled();
      expect(wrapper.vm.selectedSubjectIds).toEqual(gated.map((s) => s.id));
      expect(wrapper.vm.showBulkReconsentModal).toBe(true);
      expectNoSuccess(wrapper, status);
    });

    it("a failed diff load shows an error state instead of the seeded diff", async () => {
      vi.mocked(designerService.fetchAmendmentDiff).mockRejectedValue(
        httpError(status)
      );
      const wrapper = await mountView();
      wrapper.vm.activeTab = "graph";
      await wrapper.vm.$nextTick();

      const error = wrapper.find('[data-testid="diff-load-error"]');
      expect(error.exists()).toBe(true);
      expect(error.text()).toContain(`HTTP ${status}`);
      expect(wrapper.find(".impact-summary-header-card").exists()).toBe(false);
      expect(wrapper.text()).not.toContain("Operational Burden Delta");
      expect(lastToast()).toMatchObject({ tone: "error" });
    });

    it("a failed subject-impact load shows an error state in the wizard", async () => {
      vi.mocked(apiClient.get).mockRejectedValue(httpError(status));
      const wrapper = await mountView();
      wrapper.vm.activeTab = "wizard";
      wrapper.vm.showCreateModal = true;
      wrapper.vm.goToWizardStep(3);
      await flushPromises();

      expect(wrapper.vm.wizardImpactData).toBeNull();
      const error = wrapper.find('[data-testid="wizard-impact-load-error"]');
      expect(error.exists()).toBe(true);
      expect(wrapper.text()).not.toContain(
        "Active execution pipeline verified"
      );
      expect(wrapper.find('[data-testid="impact-load-error"]').exists()).toBe(
        false // dashboard tab is not active
      );
      expect(lastToast()).toMatchObject({ tone: "error" });
    });
  });

  it("successful single re-consent clears the gate and shows success", async () => {
    const clearSpy = vi
      .spyOn(store, "clearReconsentGate")
      .mockResolvedValue(undefined);
    const wrapper = await mountView();
    wrapper.vm.activeModalSubject = { id: "SUBJ-102" };
    wrapper.vm.showReconsentModal = true;

    await wrapper.vm.submitReconsent();

    expect(clearSpy).toHaveBeenCalledWith(
      "SUBJ-102",
      "ECONSENT",
      expect.any(String)
    );
    expect(wrapper.vm.notificationBanner.type).toBe("success");
    expect(wrapper.vm.showReconsentModal).toBe(false);
    expect(useToastStore().toasts).toHaveLength(0);
  });

  it("successful publish switches version and shows success", async () => {
    const wrapper = await mountView();
    wrapper.vm.newAmendment.target_version = "3.0.0";

    await wrapper.vm.submitCreateAmendment();

    expect(apiClient.post).toHaveBeenCalledWith(
      "/api/v1/execution/amendments/publish",
      expect.objectContaining({ version_number: "3.0.0" })
    );
    expect(wrapper.vm.selectedAmendedVersion).toBe("3.0.0");
    expect(wrapper.vm.notificationBanner.type).toBe("success");
  });
});
