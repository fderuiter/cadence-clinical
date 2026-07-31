import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import DocumentManagerView from "../../src/views/DocumentManagerView.vue";
import { useEtmfStore } from "../../src/stores/etmf";
import TmfBinderTree from "../../src/components/etmf/TmfBinderTree.vue";
import DocumentGrid from "../../src/components/etmf/DocumentGrid.vue";
import PdfPreviewModal from "../../src/components/etmf/PdfPreviewModal.vue";

// Mock etmfService so no real network requests are made
vi.mock("../../src/api/etmf", () => ({
  etmfService: {
    getDocuments: vi.fn(() => Promise.resolve([
      {
        id: "DOC-100",
        study_id: "STUDY-USDM-001",
        zone: 1,
        section: "01.01",
        artifact_code: "01.01.01",
        artifact_type: "Clinical Trial Protocol",
        filename: "protocol_v1_draft.pdf",
        mime_type: "application/pdf",
        created_at: "2026-08-01T12:00:00Z",
        created_by: "fderuiter",
        version_index: 1,
        status: "DRAFT",
        reason_for_change: "Initial version",
      }
    ])),
    ingestDocument: vi.fn(() => Promise.resolve({
      status: "success",
      document_id: "DOC-101",
      version_index: 1
    })),
  }
}));

describe("DocumentManagerView.vue Component Integration Tests", () => {
  let pinia;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
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

    // Explicitly await the store seeding / update cycle
    await store.fetchBinderTree();
    await wrapper.vm.$nextTick();

    // Check that Zones are rendered inside the tree
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

    // Seed/mock the tree layout to ensure children are expanded
    await store.fetchBinderTree();
    await wrapper.vm.$nextTick();

    // Simulate finding the artifact node component/element and selecting it
    const tmfTree = wrapper.findComponent(TmfBinderTree);
    expect(tmfTree.exists()).toBe(true);

    // Emit select-artifact directly from TmfBinderTree VM
    await tmfTree.vm.$emit("select-artifact", "01.01.01");

    expect(fetchDocsSpy).toHaveBeenCalledWith("01.01.01");
  });

  it("opens secure PDF Preview modal when document row is clicked in grid", async () => {
    const store = useEtmfStore(pinia);

    // Pre-populate some document metadata state
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
          signing_reason: "Consent approval"
        }
      }
    ];

    const wrapper = mount(DocumentManagerView, {
      global: {
        plugins: [pinia],
      },
    });

    // Explicitly await the store seeding / update cycle
    await store.fetchBinderTree();
    await wrapper.vm.$nextTick();

    // Check that document exists in grid
    expect(wrapper.find(".filename").text()).toBe("icf_v1.0_signed.pdf");
    expect(wrapper.find(".status-badge").text()).toBe("APPROVED");

    // Check modal does not exist yet
    expect(wrapper.findComponent(PdfPreviewModal).exists()).toBe(false);

    // Click preview button inside DocumentGrid component VM
    const grid = wrapper.findComponent(DocumentGrid);
    await grid.vm.$emit("preview", store.documentsList[0]);

    // Modal should now be opened
    const modal = wrapper.findComponent(PdfPreviewModal);
    expect(modal.exists()).toBe(true);

    // Verify secure watermark overlay contains correct user name and classification
    expect(modal.text()).toContain("Frans de Ruiter");
    expect(modal.text()).toContain("FINAL APPROVED");
    expect(modal.text()).toContain("Informed Consent Form");

    // Close modal
    await modal.find(".close-modal-btn").trigger("click");
    expect(wrapper.findComponent(PdfPreviewModal).exists()).toBe(false);
  });
});
