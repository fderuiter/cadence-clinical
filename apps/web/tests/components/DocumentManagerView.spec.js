import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import DocumentManagerView from "../../src/views/DocumentManagerView.vue";
import { etmfService } from "../../src/api/etmf";
import { useAuthStore } from "../../src/stores/auth";
import { useEtmfStore } from "../../src/stores/etmf";
import { useToastStore } from "../../src/stores/toasts";
import TmfBinderTree from "../../src/components/etmf/TmfBinderTree.vue";
import DocumentGrid from "../../src/components/etmf/DocumentGrid.vue";
import PdfPreviewModal from "../../src/components/etmf/PdfPreviewModal.vue";
import { signInAs } from "../helpers/signIn.js";

vi.mock("../../src/api/etmf", () => ({
  etmfService: {
    getDocuments: vi.fn(() =>
      Promise.resolve([
        {
          id: "DOC-100",
          study_id: "STUDY-USDM-001",
          site_id: "SITE-101",
          zone: 1,
          section: "01.01",
          artifact_code: "01.01.01",
          artifact_type: "Clinical Trial Protocol",
          filename: "protocol_v1_draft.pdf",
          mime_type: "application/pdf",
          created_at: "2026-08-01T12:00:00Z",
          created_by: "fderuiter",
          version_index: 1,
          status: "TECHNICAL_QC",
          reason_for_change: "Initial version",
        },
      ])
    ),
    getQcQueue: vi.fn(() => Promise.resolve([])),
    classifyIntelligence: vi.fn(() =>
      Promise.resolve({
        confidence_tier: "HIGH",
        primary_classification: {
          zone: 5,
          section: "05.02",
          artifact_code: "05.02.02",
          artifact_type: "FDA Form 1572",
          confidence: 0.96,
        },
      })
    ),
    stageQcDocument: vi.fn(),
    submitQcReview: vi.fn(() =>
      Promise.resolve({
        id: "DOC-100",
        status: "APPROVED",
      })
    ),
  },
}));

describe("DocumentManagerView.vue Component Integration Tests", () => {
  let pinia;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    signInAs(useAuthStore(), ["CRA"], { username: "cra_reviewer" });
    vi.clearAllMocks();
  });

  it("mounts correctly, fetches binder tree, and renders Zones", async () => {
    const store = useEtmfStore(pinia);
    const fetchTreeSpy = vi.spyOn(store, "fetchBinderTree");

    const wrapper = mount(DocumentManagerView, {
      global: {
        plugins: [pinia],
      },
    });

    expect(fetchTreeSpy).toHaveBeenCalled();
    expect(wrapper.find(".document-manager-layout").exists()).toBe(true);
    expect(wrapper.find(".sidebar-binder-tree").exists()).toBe(true);

    await store.fetchBinderTree();
    await wrapper.vm.$nextTick();

    expect(wrapper.text()).toContain("Zone 1:");
    expect(wrapper.text()).toContain("Trial Management");
    expect(wrapper.text()).toContain("Zone 5:");
    expect(wrapper.text()).toContain("Site Management");
  });

  it("triggers fetchDocuments when a tree node artifact is selected", async () => {
    const store = useEtmfStore(pinia);
    const fetchDocsSpy = vi.spyOn(store, "fetchDocuments");

    const wrapper = mount(DocumentManagerView, {
      global: {
        plugins: [pinia],
      },
    });

    await store.fetchBinderTree();
    await wrapper.vm.$nextTick();

    const tmfTree = wrapper.findComponent(TmfBinderTree);
    expect(tmfTree.exists()).toBe(true);

    await tmfTree.vm.$emit("select-artifact", "01.01.01");

    expect(fetchDocsSpy).toHaveBeenCalledWith("01.01.01");
  });

  it("opens secure PDF Preview modal when document row is clicked in grid", async () => {
    const store = useEtmfStore(pinia);

    store.documentsList = [
      {
        id: "DOC-999",
        study_id: "STUDY-USDM-001",
        zone: 5,
        section: "05.02",
        artifact_code: "05.02.05",
        artifact_type: "Informed Consent Form",
        filename: "icf_v1.0_signed.pdf",
        mime_type: "application/pdf",
        created_at: "2026-08-15T09:30:00Z",
        created_by: "fderuiter",
        version_index: 1,
        status: "APPROVED",
        reason_for_change: "Initial signed consent.",
        signer: "Frans de Ruiter",
        signing_timestamp: "2026-08-15T09:35:00Z",
        signature_manifestation: {
          signing_reason: "Consent approval",
        },
      },
    ];

    const wrapper = mount(DocumentManagerView, {
      global: {
        plugins: [pinia],
      },
    });

    await store.fetchBinderTree();
    await wrapper.vm.$nextTick();

    expect(wrapper.find(".filename").text()).toBe("icf_v1.0_signed.pdf");
    expect(wrapper.find(".status-badge").text()).toBe("Approved");
    expect(wrapper.findComponent(PdfPreviewModal).exists()).toBe(false);

    const grid = wrapper.findComponent(DocumentGrid);
    await grid.vm.$emit("preview", store.documentsList[0]);

    const modal = wrapper.findComponent(PdfPreviewModal);
    expect(modal.exists()).toBe(true);
    expect(modal.text()).toContain("Frans de Ruiter");
    expect(modal.text()).toContain("FINAL APPROVED");
    expect(modal.text()).toContain("Informed Consent Form");

    await modal.find(".close-modal-btn").trigger("click");
    expect(wrapper.findComponent(PdfPreviewModal).exists()).toBe(false);
  });

  it("persists AI classification acceptance via submitQcReview and never synthesizes APPROVED on HTTP failure", async () => {
    const store = useEtmfStore(pinia);
    const wrapper = mount(DocumentManagerView, {
      props: { studyId: "STUDY-USDM-001" },
      global: { plugins: [pinia] },
    });
    await store.fetchDocuments("01.01.01");
    await flushPromises();

    const grid = wrapper.findComponent(DocumentGrid);
    await grid.vm.$emit("inspect", store.documentsList[0]);
    await flushPromises();

    vi.mocked(etmfService.submitQcReview).mockRejectedValueOnce(
      new Error("QC review backend failure (HTTP 500)")
    );
    await wrapper.find("#btn-accept-ai-classification").trigger("click");
    await flushPromises();

    expect(etmfService.submitQcReview).toHaveBeenCalledWith(
      "DOC-100",
      expect.objectContaining({
        decision: "ACCEPT",
      })
    );
    expect(store.documentsList[0].status).toBe("TECHNICAL_QC");
    expect(wrapper.vm.inspectedDoc.status).toBe("TECHNICAL_QC");
    expect(useToastStore().toasts.some((t) => t.tone === "error")).toBe(true);

    // Subsequent 200 OK updates status from backend response
    vi.mocked(etmfService.getDocuments).mockResolvedValueOnce([
      {
        ...store.documentsList[0],
        status: "APPROVED",
      },
    ]);
    await wrapper.find("#btn-accept-ai-classification").trigger("click");
    await flushPromises();
    expect(store.documentsList[0].status).toBe("APPROVED");
    expect(wrapper.vm.inspectedDoc.status).toBe("APPROVED");
  });

  it("explains clinical QC review role requirement when signed in as CRC", async () => {
    signInAs(useAuthStore(), ["CRC"], { username: "site_crc" });
    const store = useEtmfStore(pinia);
    const wrapper = mount(DocumentManagerView, {
      global: { plugins: [pinia] },
    });
    await store.fetchDocuments("01.01.01");
    await flushPromises();

    const grid = wrapper.findComponent(DocumentGrid);
    await grid.vm.$emit("inspect", store.documentsList[0]);
    await flushPromises();

    const qcRoleNote = wrapper.find("#etmf-qc-role-note").text();
    expect(qcRoleNote).toContain("Clinical Research Associate (CRA)");
    expect(qcRoleNote).not.toContain("etmf_document:");
    expect(qcRoleNote).not.toContain("etmf_document:transition_clinical_qc");
    expect(
      wrapper.find("#btn-accept-ai-classification").attributes("disabled")
    ).toBeDefined();
  });

  it("reloads scoped documents on mount when cached rows from another study exist and syncs tab, artifactCode, and documentId via router.replace", async () => {
    const store = useEtmfStore(pinia);
    store.currentStudyId = "STUDY-OLD";
    store.currentSiteId = "SITE-OLD";
    store.documentsList = [
      {
        id: "DOC-STALE",
        study_id: "STUDY-OLD",
        site_id: "SITE-OLD",
        zone: 1,
        section: "01.01",
        artifact_code: "01.01.01",
        artifact_type: "Clinical Trial Protocol",
        filename: "stale_study_doc.pdf",
        mime_type: "application/pdf",
        status: "APPROVED",
        version_index: 1,
        created_by: "crc_old",
        created_at: "2026-07-01T10:00:00Z",
      },
    ];

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: "/etmf", name: "etmf", component: DocumentManagerView }],
    });
    const replaceSpy = vi.spyOn(router, "replace");
    await router.push(
      "/etmf?studyId=CADENCE-101&siteId=SITE-101&artifactCode=01.01.01&documentId=DOC-100"
    );
    await router.isReady();

    const wrapper = mount(
      { template: "<router-view />" },
      { global: { plugins: [pinia, router] } }
    );
    await flushPromises();

    expect(etmfService.getDocuments).toHaveBeenCalledWith({
      study_id: "CADENCE-101",
      site_id: "SITE-101",
    });
    expect(wrapper.text()).not.toContain("stale_study_doc.pdf");
    expect(wrapper.text()).toContain("protocol_v1_draft.pdf");

    const view = wrapper.findComponent(DocumentManagerView);
    expect(view.vm.inspectedDoc?.id).toBe("DOC-100");

    await view.vm.closeInspectionDrawer();
    await flushPromises();
    expect(router.currentRoute.value.query.documentId).toBeUndefined();

    await wrapper.find("#btn-etmf-tab-qc-queue").trigger("click");
    await flushPromises();
    expect(replaceSpy).toHaveBeenCalled();
    expect(router.currentRoute.value.query.tab).toBe("qc-queue");
    wrapper.unmount();
  });

  it("queries GET /api/v1/etmf/documents?study_id=CADENCE-101 when opening /etmf?studyId=CADENCE-101 without falling back to STUDY-USDM-001 outside demo mode", async () => {
    const store = useEtmfStore(pinia);
    expect(store.currentStudyId).toBe("");

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: "/etmf", name: "etmf", component: DocumentManagerView }],
    });
    await router.push("/etmf?studyId=CADENCE-101");
    await router.isReady();

    const wrapper = mount(
      { template: "<router-view />" },
      { global: { plugins: [pinia, router] } }
    );
    await flushPromises();

    expect(store.currentStudyId).toBe("CADENCE-101");
    expect(etmfService.getDocuments).toHaveBeenCalledWith({
      study_id: "CADENCE-101",
    });
    for (const [args] of vi.mocked(etmfService.getDocuments).mock.calls) {
      expect(args?.study_id).not.toBe("STUDY-USDM-001");
    }
    wrapper.unmount();
  });
});
