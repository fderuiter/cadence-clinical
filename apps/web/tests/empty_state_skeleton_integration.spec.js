import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import DocumentGrid from "../src/components/etmf/DocumentGrid.vue";
import CodingQueueTable from "../src/components/CodingQueueTable.vue";
import TicketDetailDrawer from "../src/components/tickets/TicketDetailDrawer.vue";
import EhrReconciliationDrawer from "../src/components/clinical/EhrReconciliationDrawer.vue";
import { useCodingStore } from "../src/stores/coding";

vi.mock("../src/api/etmf", () => ({
  etmfService: {
    getDocuments: vi.fn(() => Promise.resolve([])),
    ingestDocument: vi.fn(() => Promise.resolve({ id: "DOC-1" })),
  },
}));

vi.mock("../src/api/interop", () => ({
  interopService: {
    getEhrCandidates: vi.fn(() => Promise.resolve([])),
    listEhrCandidates: vi.fn(() => Promise.resolve([])),
  },
}));

describe("EmptyState and SkeletonLoader Component Integration", () => {
  let pinia;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
  });

  describe("DocumentGrid Integration", () => {
    it("renders SkeletonLoader when isLoading prop is true", () => {
      const wrapper = mount(DocumentGrid, {
        props: {
          documents: [],
          isLoading: true,
        },
        global: { plugins: [pinia] },
      });

      const skeleton = wrapper.findComponent({ name: "SkeletonLoader" });
      expect(skeleton.exists()).toBe(true);
      expect(skeleton.props("type")).toBe("row");
    });

    it("renders EmptyState with upload actions when documents list is empty", () => {
      const wrapper = mount(DocumentGrid, {
        props: {
          documents: [],
          isLoading: false,
        },
        global: { plugins: [pinia] },
      });

      const emptyState = wrapper.findComponent({ name: "EmptyState" });
      expect(emptyState.exists()).toBe(true);
      expect(emptyState.text()).toContain("No Regulated Documents Found");

      const uploadBtn = wrapper.find(".upload-empty-btn");
      const batchBtn = wrapper.find(".batch-empty-btn");
      expect(uploadBtn.exists()).toBe(true);
      expect(batchBtn.exists()).toBe(true);
    });
  });

  describe("CodingQueueTable Integration", () => {
    it("renders SkeletonLoader when codingStore.isLoading is true", () => {
      const codingStore = useCodingStore();
      codingStore.isLoading = true;

      const wrapper = mount(CodingQueueTable, {
        global: { plugins: [pinia] },
      });

      const skeleton = wrapper.findComponent({ name: "SkeletonLoader" });
      expect(skeleton.exists()).toBe(true);
      expect(skeleton.props("type")).toBe("row");
    });

    it("renders EmptyState and resets filters on action button click", async () => {
      const codingStore = useCodingStore();
      codingStore.isLoading = false;
      codingStore.assignments = [];
      codingStore.filters.status = "CODED";

      const wrapper = mount(CodingQueueTable, {
        global: { plugins: [pinia] },
      });

      const emptyState = wrapper.findComponent({ name: "EmptyState" });
      expect(emptyState.exists()).toBe(true);
      expect(emptyState.text()).toContain("No Coding Assignments Found");

      const resetBtn = wrapper.find(".btn-reset-filters");
      expect(resetBtn.exists()).toBe(true);

      await resetBtn.trigger("click");
      expect(codingStore.filters.status).toBe("ALL");
    });
  });

  describe("TicketDetailDrawer Integration", () => {
    it("renders timeline SkeletonLoader when audit tab is active and isAuditLoading is true", async () => {
      const wrapper = mount(TicketDetailDrawer, {
        props: {
          isOpen: true,
          ticket: { id: "TICK-101", title: "Missing Protocol Signoff" },
          auditLogs: [],
          isAuditLoading: true,
        },
      });

      wrapper.vm.activeTab = "audit";
      await wrapper.vm.$nextTick();

      const skeleton = wrapper.findComponent({ name: "SkeletonLoader" });
      expect(skeleton.exists()).toBe(true);
      expect(skeleton.props("type")).toBe("timeline");
    });

    it("renders EmptyState when auditLogs list is empty", async () => {
      const wrapper = mount(TicketDetailDrawer, {
        props: {
          isOpen: true,
          ticket: { id: "TICK-101", title: "Missing Protocol Signoff" },
          auditLogs: [],
          isAuditLoading: false,
        },
      });

      wrapper.vm.activeTab = "audit";
      await wrapper.vm.$nextTick();

      const emptyState = wrapper.findComponent({ name: "EmptyState" });
      expect(emptyState.exists()).toBe(true);
      expect(emptyState.text()).toContain("No Audit Entries Found");
    });
  });

  describe("EhrReconciliationDrawer Integration", () => {
    it("renders card SkeletonLoader when isLoading is true", async () => {
      const wrapper = mount(EhrReconciliationDrawer, {
        props: {
          isOpen: true,
          studyId: "STUDY-101",
        },
      });

      wrapper.vm.isLoading = true;
      await wrapper.vm.$nextTick();

      const skeleton = wrapper.findComponent({ name: "SkeletonLoader" });
      expect(skeleton.exists()).toBe(true);
      expect(skeleton.props("type")).toBe("card");
    });

    it("renders EmptyState with refresh trigger when candidates are empty", async () => {
      const wrapper = mount(EhrReconciliationDrawer, {
        props: {
          isOpen: true,
          studyId: "STUDY-101",
        },
      });

      wrapper.vm.isLoading = false;
      wrapper.vm.candidates = [];
      await wrapper.vm.$nextTick();

      const emptyState = wrapper.findComponent({ name: "EmptyState" });
      expect(emptyState.exists()).toBe(true);
      expect(emptyState.text()).toContain("No Candidate Records Found");

      const refreshBtn = wrapper.find(".btn-refresh-candidates");
      expect(refreshBtn.exists()).toBe(true);
    });
  });
});
