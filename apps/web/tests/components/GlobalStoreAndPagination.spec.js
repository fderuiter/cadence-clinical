import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { useEtmfStore } from "../../src/stores/etmf";
import DocumentGrid from "../../src/components/etmf/DocumentGrid.vue";

describe("Global Store Reactive Indexing & Paginated Grid Tests", () => {
  let pinia;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
  });

  describe("useEtmfStore - folderLookup Getter", () => {
    it("dynamically returns folderLookup lookup map for constant-time lookups", async () => {
      const store = useEtmfStore(pinia);
      await store.fetchBinderTree();

      const lookup = store.folderLookup;
      expect(lookup).toBeDefined();
      expect(lookup["zone_1"]).toBeDefined();
      expect(lookup["zone_1"].name).toBe("Trial Management");
      expect(lookup["zone_1"].type).toBe("zone");

      expect(lookup["01.01.01"]).toBeDefined();
      expect(lookup["01.01.01"].name).toBe("Clinical Trial Protocol");
      expect(lookup["01.01.01"].type).toBe("artifact");
      expect(lookup["01.01.01"].parentCode).toBe("01.01");
    });
  });

  describe("DocumentGrid.vue - Pagination and Pre-Formatted Dates", () => {
    const mockDocuments = Array.from({ length: 45 }, (_, i) => ({
      id: `DOC-${i + 1}`,
      study_id: "STUDY-USDM-001",
      zone: 1,
      section: "01.01",
      artifact_code: "01.01.01",
      artifact_type: "Clinical Trial Protocol",
      filename: `protocol_document_${i + 1}.pdf`,
      mime_type: "application/pdf",
      created_at: "2026-08-01T12:00:00Z",
      created_by: "fderuiter",
      version_index: 1,
      status: "APPROVED",
      reason_for_change: "Initial version",
    }));

    it("displays a maximum of 20 document rows per page with active pagination", async () => {
      const store = useEtmfStore(pinia);
      await store.fetchBinderTree();
      store.selectedArtifactId = "01.01.01";

      const wrapper = mount(DocumentGrid, {
        props: {
          documents: mockDocuments,
        },
        global: {
          plugins: [pinia],
        },
      });

      // Verify page limit
      const rows = wrapper.findAll(".document-row");
      expect(rows.length).toBe(20);

      // Verify pre-computed dates
      const firstRowDate = rows[0].find(".date-cell").text();
      expect(firstRowDate).not.toBe("2026-08-01T12:00:00Z"); // It should be formatted!
      expect(firstRowDate).toContain("2026");

      // Verify pagination info text
      const infoText = wrapper.find(".pagination-info").text();
      expect(infoText).toContain("Showing 1 to 20 of 45 documents");

      // Verify page navigation: Next page
      const nextBtn = wrapper.find(".next-btn");
      expect(nextBtn.exists()).toBe(true);
      await nextBtn.trigger("click");

      const secondPageRows = wrapper.findAll(".document-row");
      expect(secondPageRows.length).toBe(20);
      expect(wrapper.find(".pagination-info").text()).toContain("Showing 21 to 40 of 45 documents");

      // Click next again for final page
      await wrapper.find(".next-btn").trigger("click");
      const lastPageRows = wrapper.findAll(".document-row");
      expect(lastPageRows.length).toBe(5);
      expect(wrapper.find(".pagination-info").text()).toContain("Showing 41 to 45 of 45 documents");

      // Verify page jump buttons (Go to Page 1)
      const pageBtn = wrapper.findAll(".page-num-btn");
      expect(pageBtn.length).toBe(3); // 45 items / 20 = 3 pages
      await pageBtn[0].trigger("click"); // jump back to page 1
      expect(wrapper.find(".pagination-info").text()).toContain("Showing 1 to 20 of 45 documents");
    });

    it("resets current page to 1 when active collection (documents list prop) changes", async () => {
      const store = useEtmfStore(pinia);
      await store.fetchBinderTree();

      const wrapper = mount(DocumentGrid, {
        props: {
          documents: mockDocuments,
        },
        global: {
          plugins: [pinia],
        },
      });

      // Go to page 2
      await wrapper.find(".next-btn").trigger("click");
      expect(wrapper.find(".pagination-info").text()).toContain("Showing 21 to 40 of 45 documents");

      // Change prop (e.g., active collection switches)
      const newDocs = mockDocuments.slice(0, 10);
      await wrapper.setProps({ documents: newDocs });

      // currentPage should reset to 1
      expect(wrapper.find(".pagination-info").text()).toContain("Showing 1 to 10 of 10 documents");
    });
  });
});
