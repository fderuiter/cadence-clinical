import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import MedicalCodingView from "../../src/views/MedicalCodingView.vue";
import CodingQueueTable from "../../src/components/CodingQueueTable.vue";
import { useCodingStore } from "../../src/stores/coding.js";
import { useAuthStore } from "../../src/stores/auth.js";

vi.mock("../../src/api/apiClient.js", () => ({
  apiClient: {
    get: vi.fn().mockResolvedValue([]),
    post: vi.fn().mockResolvedValue({ status: "SUCCESS" }),
  },
}));

describe("Verbatim Cluster Auto-Coding Workbench UI Components", () => {
  let pinia;
  let codingStore;
  let authStore;

  const sampleAssignments = [
    {
      id: "ASSIGN-101",
      verbatim_text: "SEVERE HEADACHE",
      dictionary_type: "MEDDRA",
      dictionary_version: "26.0",
      status: "SUGGESTED",
      score: 0.95,
      suggestions: [
        { code: "10019211", pt_name: "Headache", llt_name: "Severe headache", score: 0.95 },
      ],
    },
    {
      id: "ASSIGN-102",
      verbatim_text: "Severe Headache",
      dictionary_type: "MEDDRA",
      dictionary_version: "26.0",
      status: "SUGGESTED",
      score: 0.95,
      suggestions: [
        { code: "10019211", pt_name: "Headache", llt_name: "Severe headache", score: 0.95 },
      ],
    },
    {
      id: "ASSIGN-103",
      verbatim_text: "Abdominal Pain",
      dictionary_type: "MEDDRA",
      dictionary_version: "26.0",
      status: "SUGGESTED",
      score: 0.78,
      suggestions: [
        { code: "10000081", pt_name: "Abdominal pain", score: 0.78 },
      ],
    },
  ];

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    codingStore = useCodingStore(pinia);
    authStore = useAuthStore(pinia);

    authStore.rawRoles = ["data_manager"];
    codingStore.assignments = [...sampleAssignments];
  });

  it("renders MedicalCodingView with queue view mode toggle and confidence filter", async () => {
    const wrapper = mount(MedicalCodingView, {
      global: { plugins: [pinia] },
    });

    // View mode toggle exists
    const toggleButtons = wrapper.findAll(".view-mode-toggle button");
    expect(toggleButtons.length).toBe(2);
    expect(toggleButtons[0].text()).toContain("Individual Items");
    expect(toggleButtons[1].text()).toContain("Verbatim Clusters");

    // Confidence select filter exists
    const confidenceSelect = wrapper.find(".confidence-select");
    expect(confidenceSelect.exists()).toBe(true);
    expect(confidenceSelect.findAll("option").length).toBe(4);
  });

  it("switches to Verbatim Clusters view and displays cluster metrics cards", async () => {
    const wrapper = mount(MedicalCodingView, {
      global: { plugins: [pinia] },
    });

    codingStore.setViewMode("CLUSTERS");
    await wrapper.vm.$nextTick();

    // Cluster stats grid is displayed
    const clusterGrid = wrapper.find(".cluster-stats-grid");
    expect(clusterGrid.exists()).toBe(true);

    const clusterStatCards = wrapper.findAll(".cluster-stats-grid .stat-card");
    expect(clusterStatCards.length).toBe(5);
    expect(wrapper.text()).toContain("Verbatim Clusters");
    expect(wrapper.text()).toContain("Subject Instances");
    expect(wrapper.text()).toContain("Auto-Coded Progress");
  });

  it("renders verbatim cluster rows with instance count, dictionary, and top suggestion in CodingQueueTable", async () => {
    codingStore.setViewMode("CLUSTERS");

    const wrapper = mount(CodingQueueTable, {
      global: { plugins: [pinia] },
    });

    await wrapper.vm.$nextTick();

    const clusterRows = wrapper.findAll(".cluster-row");
    expect(clusterRows.length).toBe(2); // "SEVERE HEADACHE" and "Abdominal Pain"

    const headacheRow = clusterRows[0];
    expect(headacheRow.text()).toContain("SEVERE HEADACHE");
    expect(headacheRow.text()).toContain("2 subject instances");
    expect(headacheRow.text().toUpperCase()).toContain("MEDDRA");
    expect(headacheRow.text()).toContain("Headache");
    expect(headacheRow.text()).toContain("10019211");
    expect(headacheRow.text()).toContain("95%");
  });

  it("filters cluster view by confidence threshold", async () => {
    codingStore.setViewMode("CLUSTERS");
    codingStore.filters.confidenceThreshold = "HIGH"; // >= 90%

    const wrapper = mount(CodingQueueTable, {
      global: { plugins: [pinia] },
    });

    await wrapper.vm.$nextTick();

    const clusterRows = wrapper.findAll(".cluster-row");
    expect(clusterRows.length).toBe(1);
    expect(clusterRows[0].text()).toContain("SEVERE HEADACHE");
  });

  it("opens Batch Approve Cluster modal and executes batch approval with GxP reason", async () => {
    codingStore.setViewMode("CLUSTERS");
    vi.spyOn(codingStore, "batchApproveClusters").mockResolvedValue({ status: "SUCCESS" });

    const wrapper = mount(CodingQueueTable, {
      global: { plugins: [pinia] },
    });

    await wrapper.vm.$nextTick();

    // Click "✓ Approve Cluster" button on first cluster
    const approveBtn = wrapper.find(".cluster-row .btn-primary");
    await approveBtn.trigger("click");

    // Modal opens
    const modal = wrapper.find(".batch-dialog-card");
    expect(modal.exists()).toBe(true);
    expect(modal.text()).toContain("Batch Approve Verbatim Cluster(s)");

    // Fill GxP change reason
    const reasonInput = modal.find("#cluster-approve-reason");
    await reasonInput.setValue("High confidence verbatim batch approval");

    // Click approve submit button
    const submitBtn = modal.findAll("button").find((b) => b.text().includes("Batch Approve Cluster(s)"));
    await submitBtn.trigger("click");

    expect(codingStore.batchApproveClusters).toHaveBeenCalledWith(
      expect.objectContaining({
        reason: "High confidence verbatim batch approval",
      })
    );
  });
});
