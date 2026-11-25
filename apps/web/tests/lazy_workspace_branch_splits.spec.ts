import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import { SignOutConfirmModal } from "ui";
import { beforeEach, describe, expect, it, vi } from "vitest";
import SignatureCaptureModal from "../src/components/SignatureCaptureModal.vue";
import CanvasPalette from "../src/components/canvas/CanvasPalette.vue";
import BulkIpKitReconciliationDrawer from "../src/components/clinical/BulkIpKitReconciliationDrawer.vue";
import CtmsDelegationSection from "../src/components/clinical/CtmsDelegationSection.vue";
import CtmsGreenlightSection from "../src/components/clinical/CtmsGreenlightSection.vue";
import CtmsMilestonesSection from "../src/components/clinical/CtmsMilestonesSection.vue";
import CtmsMonitoringSection from "../src/components/clinical/CtmsMonitoringSection.vue";
import RtsmSupplySection from "../src/components/clinical/RtsmSupplySection.vue";
import SiteActivationPanel from "../src/components/clinical/SiteActivationPanel.vue";
import BatchUploadDrawer from "../src/components/etmf/BatchUploadDrawer.vue";
import CraQcQueue from "../src/components/etmf/CraQcQueue.vue";
import ExportJobDrawer from "../src/components/etmf/ExportJobDrawer.vue";
import PdfPreviewModal from "../src/components/etmf/PdfPreviewModal.vue";
import { useAuthStore } from "../src/stores/auth";
import { useEtmfStore } from "../src/stores/etmf";
import CtmsLiveWorkspace from "../src/views/CtmsLiveWorkspace.vue";
import DocumentManagerView from "../src/views/DocumentManagerView.vue";
import StudyCanvasView from "../src/views/StudyCanvasView.vue";
import { signInAs } from "./helpers/signIn.js";

vi.mock("../src/api/etmf", () => ({
  etmfService: {
    getDocuments: vi.fn(() => Promise.resolve([])),
    getQcQueue: vi.fn(() => Promise.resolve([])),
    classifyIntelligence: vi.fn(),
    stageQcDocument: vi.fn(),
    submitQcReview: vi.fn(),
  },
}));

vi.mock("../src/api/executionApi", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("../src/api/executionApi")>();
  return {
    ...actual,
    executionApi: {
      ...actual.executionApi,
      getSiteComplianceStatus: vi.fn(() =>
        Promise.resolve({
          study_id: "CADENCE-101",
          site_id: "SITE-101",
          is_activated: true,
          is_complete: true,
          missing_documents: [],
        })
      ),
      listSiteIpKits: vi.fn(() => Promise.resolve([])),
      listRandomizationSchemes: vi.fn(() =>
        Promise.resolve([
          {
            id: "scheme-001",
            study_id: "CADENCE-101",
            name: "Primary 1:1 Scheme",
            block_size: 4,
            treatment_arms: ["ACTIVE", "PLACEBO"],
            strata_factors: [],
            status: "DRAFT",
            created_by: "other_statistician",
          },
        ])
      ),
      listCgtCertificates: vi.fn(() => Promise.resolve([])),
      listCgtReconciliations: vi.fn(() => Promise.resolve([])),
      listSubjects: vi.fn(() => Promise.resolve({ items: [], total: 0 })),
      getSubjectCounts: vi.fn(() => Promise.resolve({ total: 0, counts: {} })),
    },
  };
});

vi.mock("../src/api/ctmsApi", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../src/api/ctmsApi")>();
  return {
    ...actual,
    ctmsApi: {
      ...actual.ctmsApi,
      getStartupReadiness: vi.fn(() =>
        Promise.resolve({
          study_id: "CADENCE-101",
          site_id: "SITE-101",
          is_ready_for_enrollment: false,
          blocking_reasons: [],
          checks: {},
        })
      ),
      listSiteMilestones: vi.fn(() => Promise.resolve([])),
      listMonitoringVisits: vi.fn(() => Promise.resolve([])),
      listSiteStaffAssignments: vi.fn(() => Promise.resolve([])),
    },
  };
});

vi.mock("../src/api/kernelGovernanceApi", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("../src/api/kernelGovernanceApi")>();
  return {
    ...actual,
    kernelGovernanceApi: {
      ...actual.kernelGovernanceApi,
      listStudySites: vi.fn(() => Promise.resolve([])),
    },
  };
});

vi.mock("../src/api/kernelApi", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../src/api/kernelApi")>();
  return {
    ...actual,
    kernelApi: {
      ...actual.kernelApi,
      search: vi.fn(() => Promise.resolve([])),
      getChildren: vi.fn(() => Promise.resolve([])),
      canI: vi.fn(() =>
        Promise.resolve({ allowed: false, reasons: [], node: null })
      ),
      listProposals: vi.fn(() => Promise.resolve([])),
      listWorkQueues: vi.fn(() => Promise.resolve([])),
      getStudyOverview: vi.fn(() =>
        Promise.resolve({
          study_code: "CADENCE-101",
          title: "Cadence Trial",
          sites: [],
          participants_total: 0,
          open_queries: 0,
        })
      ),
    },
  };
});

describe("Lazy workspace branch and closed overlay mounting gates", () => {
  let pinia: ReturnType<typeof createPinia>;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    vi.clearAllMocks();
    window.localStorage.clear();
  });

  it("mounts only the active tab in CtmsLiveWorkspace on /ctms and /ssu and mounts sibling tabs on selection", async () => {
    signInAs(useAuthStore(), ["CRA"], { username: "cra_user" });
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: "/ctms", component: CtmsLiveWorkspace },
        {
          path: "/ssu",
          component: CtmsLiveWorkspace,
          props: { sectionMode: "startup" },
        },
        { path: "/plans", component: { template: "<div>Plans</div>" } },
      ],
    });
    await router.push("/ctms");
    await router.isReady();

    const ctmsWrapper = mount(CtmsLiveWorkspace, {
      global: { plugins: [pinia, router] },
    });
    await flushPromises();

    expect(ctmsWrapper.findComponent(SiteActivationPanel).exists()).toBe(true);
    expect(ctmsWrapper.findComponent(CtmsGreenlightSection).exists()).toBe(
      false
    );
    expect(ctmsWrapper.findComponent(CtmsMilestonesSection).exists()).toBe(
      false
    );
    expect(ctmsWrapper.findComponent(CtmsMonitoringSection).exists()).toBe(
      false
    );
    expect(ctmsWrapper.findComponent(CtmsDelegationSection).exists()).toBe(
      false
    );
    expect(ctmsWrapper.findComponent(RtsmSupplySection).exists()).toBe(false);

    await ctmsWrapper.find("#ctms-tab-milestones").trigger("click");
    await flushPromises();

    expect(ctmsWrapper.findComponent(SiteActivationPanel).exists()).toBe(false);
    expect(ctmsWrapper.findComponent(CtmsMilestonesSection).exists()).toBe(
      true
    );
    ctmsWrapper.unmount();

    await router.push("/ssu");
    const ssuWrapper = mount(CtmsLiveWorkspace, {
      props: { sectionMode: "startup" },
      global: { plugins: [pinia, router] },
    });
    await flushPromises();

    expect(ssuWrapper.findComponent(CtmsGreenlightSection).exists()).toBe(true);
    expect(ssuWrapper.findComponent(SiteActivationPanel).exists()).toBe(false);
    expect(ssuWrapper.findComponent(CtmsMilestonesSection).exists()).toBe(
      false
    );
    ssuWrapper.unmount();
  });

  it("keeps CraQcQueue, PdfPreviewModal, ExportJobDrawer, and BatchUploadDrawer unmounted on initial /etmf render until opened", async () => {
    signInAs(useAuthStore(), ["CRA"], { username: "cra_user" });
    const etmfStore = useEtmfStore(pinia);
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: "/etmf", component: DocumentManagerView }],
    });
    await router.push("/etmf?studyId=CADENCE-101&siteId=SITE-101");
    await router.isReady();

    const wrapper = mount(DocumentManagerView, {
      global: { plugins: [pinia, router] },
    });
    await flushPromises();

    expect(wrapper.findComponent(CraQcQueue).exists()).toBe(false);
    expect(wrapper.findComponent(PdfPreviewModal).exists()).toBe(false);
    expect(wrapper.findComponent(ExportJobDrawer).exists()).toBe(false);
    expect(wrapper.findComponent(BatchUploadDrawer).exists()).toBe(false);

    // Open and close ExportJobDrawer via store gate
    etmfStore.toggleExportDrawer(true);
    await flushPromises();
    expect(wrapper.findComponent(ExportJobDrawer).exists()).toBe(true);
    etmfStore.toggleExportDrawer(false);
    await flushPromises();
    expect(wrapper.findComponent(ExportJobDrawer).exists()).toBe(false);

    // Open and close BatchUploadDrawer from DocumentGrid toolbar
    await wrapper.find(".batch-upload-trigger-btn").trigger("click");
    await flushPromises();
    expect(wrapper.findComponent(BatchUploadDrawer).exists()).toBe(true);
    await wrapper.findComponent(BatchUploadDrawer).vm.$emit("close");
    await flushPromises();
    expect(wrapper.findComponent(BatchUploadDrawer).exists()).toBe(false);

    // Switch to QC Queue tab to mount CraQcQueue on demand
    await wrapper.find("#btn-etmf-tab-qc-queue").trigger("click");
    await flushPromises();
    expect(wrapper.findComponent(CraQcQueue).exists()).toBe(true);

    wrapper.unmount();
  });

  it("keeps BulkIpKitReconciliationDrawer and SignatureCaptureModal unmounted in RtsmSupplySection until triggered", async () => {
    signInAs(useAuthStore(), ["pharmacist", "unblinded_statistician"], {
      username: "stat_approver",
      assignedStudies: ["CADENCE-101"],
      assignedSites: ["SITE-101"],
    });

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: "/ctms", component: { template: "<div>CTMS</div>" } },
        { path: "/plans", component: { template: "<div>Plans</div>" } },
      ],
    });
    await router.push("/ctms");
    await router.isReady();

    const wrapper = mount(RtsmSupplySection, {
      props: {
        studyCode: "CADENCE-101",
        siteId: "SITE-101",
        lockStudy: true,
      },
      global: {
        plugins: [pinia, router],
        stubs: { Teleport: true },
      },
    });
    await flushPromises();

    expect(wrapper.findComponent(BulkIpKitReconciliationDrawer).exists()).toBe(
      false
    );
    expect(wrapper.findComponent(SignatureCaptureModal).exists()).toBe(false);

    // Trigger BulkIpKitReconciliationDrawer
    await wrapper.find("#btn-open-kit-reconciliation").trigger("click");
    await flushPromises();
    expect(wrapper.findComponent(BulkIpKitReconciliationDrawer).exists()).toBe(
      true
    );
    await wrapper
      .findComponent(BulkIpKitReconciliationDrawer)
      .vm.$emit("close");
    await flushPromises();
    expect(wrapper.findComponent(BulkIpKitReconciliationDrawer).exists()).toBe(
      false
    );

    // Trigger SignatureCaptureModal for draft scheme approval
    await wrapper.find('[data-testid="btn-approve-scheme"]').trigger("click");
    await flushPromises();
    expect(wrapper.findComponent(SignatureCaptureModal).exists()).toBe(true);
    await wrapper.findComponent(SignatureCaptureModal).vm.$emit("cancel");
    await flushPromises();
    expect(wrapper.findComponent(SignatureCaptureModal).exists()).toBe(false);

    wrapper.unmount();
  });

  it("keeps standalone CanvasPalette and SignOutConfirmModal unmounted on StudyCanvasView until opened", async () => {
    const authStore = useAuthStore();
    signInAs(authStore, ["Principal Investigator"], {
      username: "pi_user",
      sub: "user-pi-1",
    });

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: "/studies",
          name: "studies",
          component: { template: "<div>Studies</div>" },
        },
        {
          path: "/studies/:studyCode",
          name: "study",
          component: StudyCanvasView,
        },
        { path: "/plans", component: { template: "<div>Plans</div>" } },
        { path: "/login", component: { template: "<div>Login</div>" } },
      ],
    });
    await router.push("/studies/CADENCE-101");
    await router.isReady();

    const wrapper = mount(StudyCanvasView, {
      global: {
        plugins: [pinia, router],
        provide: { inAppShell: false },
        stubs: { Teleport: true },
      },
    });
    await flushPromises();

    expect(wrapper.findComponent(CanvasPalette).exists()).toBe(false);
    expect(wrapper.findComponent(SignOutConfirmModal).exists()).toBe(false);

    await wrapper.find("#btn-open-palette").trigger("click");
    await flushPromises();
    expect(wrapper.findComponent(CanvasPalette).exists()).toBe(true);
    await wrapper.findComponent(CanvasPalette).vm.$emit("close");
    await flushPromises();
    expect(wrapper.findComponent(CanvasPalette).exists()).toBe(false);

    // Seed a user-scoped draft so standalone sign-out opens SignOutConfirmModal
    window.localStorage.setItem(
      "cadence_user:tenant-1:user-pi-1:ecrf_draft:form-1",
      JSON.stringify({ SYSBP: "120" })
    );
    await wrapper.find("#btn-canvas-logout").trigger("click");
    await flushPromises();
    expect(wrapper.findComponent(SignOutConfirmModal).exists()).toBe(true);

    await wrapper.findComponent(SignOutConfirmModal).vm.$emit("cancel");
    await flushPromises();
    expect(wrapper.findComponent(SignOutConfirmModal).exists()).toBe(false);

    wrapper.unmount();
  });
});
