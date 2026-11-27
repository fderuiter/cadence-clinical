import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import AmendmentDiffView from "../src/views/AmendmentDiffView.vue";
import { ApiError } from "../src/api/client";
import { kernelApi } from "../src/api/kernelApi";
import { kernelGovernanceApi } from "../src/api/kernelGovernanceApi";
import { useAuthStore } from "../src/stores/auth";

vi.mock("../src/api/kernelApi", () => ({
  kernelApi: {
    listStudies: vi.fn(),
    listProtocolVersions: vi.fn(),
    getProtocolVersion: vi.fn(),
    getProtocolDesignSummary: vi.fn(async () => null),
    getScheduleOfActivities: vi.fn(),
    amendmentImpact: vi.fn(),
    listProtocolTemplates: vi.fn(async () => []),
    search: vi.fn(async () => []),
  },
  isMasked: (obj) => Boolean(obj?.mask),
  describeKernelError: (err) => err?.message ?? String(err),
}));

vi.mock("../src/api/kernelGovernanceApi", () => ({
  kernelGovernanceApi: {
    forkProtocolVersion: vi.fn(),
    getProtocolApprovalReadiness: vi.fn(),
    submitProtocolVersionForReview: vi.fn(),
    returnProtocolVersionToDraft: vi.fn(),
    approveProtocolVersion: vi.fn(),
    withdrawProtocolVersion: vi.fn(),
    listStudyArtifacts: vi.fn(async () => []),
    registerStudyArtifact: vi.fn(),
    listStudyEffectivities: vi.fn(async () => []),
    declareProtocolEffectivity: vi.fn(),
    changeEffectivityStatus: vi.fn(),
    listStudySites: vi.fn(async () => []),
  },
  PROTOCOL_APPROVE_SEMANTIC_ACTION: "kernel.protocol_version.approve",
  PROTOCOL_WITHDRAW_SEMANTIC_ACTION: "kernel.protocol_version.withdraw",
  protocolVersionApproveAction: (ref) =>
    `/api/v1/kernel/protocol-versions/${String(ref).split("@")[0]}/approve`,
  protocolVersionWithdrawAction: (ref) =>
    `/api/v1/kernel/protocol-versions/${String(ref).split("@")[0]}/withdraw`,
}));

const httpError = (status) =>
  new ApiError(`Request failed with status HTTP ${status}`, status, "Error", {
    title: `HTTP ${status} upstream failure`,
  });

describe("AmendmentDiffView consolidated Clinical Kernel failure handling (#4523)", () => {
  let pinia;
  let router;

  beforeEach(async () => {
    pinia = createPinia();
    setActivePinia(pinia);
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Designer"];

    router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: "/studies/:studyCode?",
          name: "study",
          component: { template: "<div />" },
        },
      ],
    });
    await router.push("/studies/CAD-101");
    await router.isReady();

    vi.mocked(kernelApi.listStudies).mockResolvedValue([
      {
        ref: "study:00000000-0000-0000-0000-000000000001", // deid-ignore
        code: "CAD-101",
        title: "Cadence 101",
      },
    ]);
    vi.mocked(kernelApi.search).mockResolvedValue([
      {
        ref: "study:00000000-0000-0000-0000-000000000001", // deid-ignore
        kind: "study",
        title: "Cadence 101",
        code: "CAD-101",
        detail: null,
        exact_code: true,
        rank: 1,
      },
    ]);
    vi.mocked(kernelApi.listProtocolVersions).mockResolvedValue([
      {
        ref: "protocol_version:00000000-0000-0000-0000-000000000101@1", // deid-ignore
        study: "study:00000000-0000-0000-0000-000000000001", // deid-ignore
        number: 1,
        parent_version: null,
        status: "Approved",
        content_hash: "sha256:v1",
        amendment_intent: null,
        approval_attestation: null,
        withdrawal_reason: null,
        mask: null,
      },
    ]);
    vi.spyOn(console, "error").mockImplementation(() => {});
  });

  afterEach(() => {
    vi.clearAllMocks();
    vi.restoreAllMocks();
  });

  describe.each([500, 502, 504])("HTTP %i", (status) => {
    it("surfaces study list upstream failures without rendering fake versions", async () => {
      vi.mocked(kernelApi.listStudies).mockRejectedValueOnce(httpError(status));
      await router.push("/studies");

      const wrapper = mount(AmendmentDiffView, {
        global: { plugins: [pinia, router] },
      });
      await flushPromises();

      const alert = wrapper.find('[role="alert"]');
      expect(alert.exists()).toBe(true);
      expect(alert.text()).toContain(`HTTP ${status}`);
      expect(wrapper.find("#protocol-versions-section").exists()).toBe(false);
    });

    it("surfaces forkProtocolVersion failures inline without creating a fake Draft version", async () => {
      vi.mocked(kernelGovernanceApi.forkProtocolVersion).mockRejectedValueOnce(
        httpError(status)
      );

      const wrapper = mount(AmendmentDiffView, {
        global: { plugins: [pinia, router] },
      });
      await flushPromises();

      await wrapper.find("#btn-open-fork-amendment").trigger("click");
      await wrapper.find("#fork-amendment-intent").setValue("Add Week 16 PK");
      await wrapper
        .find("#fork-amendment-rationale")
        .setValue("Characterize terminal elimination phase");
      await wrapper
        .find("#fork-amendment-reason")
        .setValue("Fork amendment v2 from v1");
      await wrapper.find("#protocol-fork-form").trigger("submit");
      await flushPromises();

      const errBanner = wrapper.find("#protocol-fork-error");
      expect(errBanner.exists()).toBe(true);
      expect(errBanner.text()).toContain(`HTTP ${status}`);
      expect(wrapper.find("#protocol-fork-card").exists()).toBe(true);
      expect(wrapper.find("#protocol-governance-feedback").exists()).toBe(
        false
      );
    });
  });
});
