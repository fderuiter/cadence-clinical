import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createRouter, createWebHistory } from "vue-router";
import AuditView from "../../src/views/AuditView.vue";
import { auditorService } from "../../src/api/auditor";
import { etmfService } from "../../src/api/etmf";

// Mock router for component mount
const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: "/audit", name: "audit", component: AuditView },
    { path: "/login", name: "login", component: { template: "<div>Login</div>" } },
  ],
});

// Mock Auditor and eTMF APIs
vi.mock("../../src/api/auditor", () => ({
  auditorService: {
    getAuditLogs: vi.fn(),
    getExecutionIntegrity: vi.fn(),
    getWatermarkedDownloadUrl: vi.fn(),
    getBinderExportUrl: vi.fn(),
  },
}));

vi.mock("../../src/api/etmf", () => ({
  etmfService: {
    getDocuments: vi.fn(),
    getCompleteness: vi.fn(),
  },
}));

describe("AuditView.vue - Audit Trail Explorer & Part 11 Inspection (Issue #4080)", () => {
  let pinia: ReturnType<typeof createPinia>;

  const mockLogs = [
    {
      id: "AUDIT-LOG-101",
      timestamp: "2026-08-21T10:00:00Z",
      user_id: "crc.user",
      user_role: "site_crc",
      action: "UPDATE",
      entity_type: "Observation",
      record_id: "OBS-1042",
      details: "Observation VS.VSSBP changed from 140 to 120 mmHg",
      old_value: { observation_id: "OBS-1042", value: "140", unit: "mmHg" },
      new_value: { observation_id: "OBS-1042", value: "120", unit: "mmHg" },
      reason_for_change: "Typographical error corrected against paper source chart",
      sha256_hash: "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
    },
    {
      id: "AUDIT-LOG-102",
      timestamp: "2026-08-21T11:30:00Z",
      user_id: "dm.user",
      user_role: "data_manager",
      action: "SIGN",
      entity_type: "Query",
      record_id: "QRY-208",
      details: "Electronic sign-off for discrepancy query QRY-208",
      old_value: { query_id: "QRY-208", status: "OPEN" },
      new_value: { query_id: "QRY-208", status: "CLOSED" },
      reason_for_change: "Investigator confirmation received and verified",
      sha256_hash: "3a7bd3e2360a3d29eea436fcfb7e44c735d117c42d1c1835420b6b9942dd4f1b",
    },
  ];

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    vi.clearAllMocks();

    vi.mocked(auditorService.getAuditLogs).mockResolvedValue({
      items: mockLogs,
      total_count: 2,
      limit: 20,
      offset: 0,
    });

    vi.mocked(auditorService.getExecutionIntegrity).mockResolvedValue({
      status: "VERIFIED",
      ledger_entries: 2,
      last_sealed_at: "2026-08-21T11:30:00Z",
    });

    vi.mocked(etmfService.getDocuments).mockResolvedValue([]);
    vi.mocked(etmfService.getCompleteness).mockResolvedValue({
      expected_count: 10,
      present_count: 10,
      missing_count: 0,
      completeness_percentage: 100,
    });
  });

  it("renders audit trail logs and initializes with default query params", async () => {
    const wrapper = mount(AuditView, {
      global: {
        plugins: [pinia, router],
      },
    });

    // Wait for onMounted fetch
    await new Promise((r) => setTimeout(r, 20));
    await wrapper.vm.$nextTick();

    expect(auditorService.getAuditLogs).toHaveBeenCalled();
    expect(wrapper.text()).toContain("crc.user");
    expect(wrapper.text()).toContain("UPDATE");
    expect(wrapper.text()).toContain("Observation VS.VSSBP changed");
  });

  it("supports filtering by user ID, action, entity type, and date range", async () => {
    const wrapper = mount(AuditView, {
      global: {
        plugins: [pinia, router],
      },
    });

    await new Promise((r) => setTimeout(r, 20));
    await wrapper.vm.$nextTick();

    const userInput = wrapper.find<HTMLInputElement>(".filter-user-id");
    await userInput.setValue("crc.user");

    const actionSelect = wrapper.find<HTMLSelectElement>(".filter-action");
    await actionSelect.setValue("UPDATE");

    const entitySelect = wrapper.find<HTMLSelectElement>(".filter-entity-type");
    await entitySelect.setValue("Observation");

    const startDateInput = wrapper.find<HTMLInputElement>(".filter-start-date");
    await startDateInput.setValue("2026-08-20");

    const endDateInput = wrapper.find<HTMLInputElement>(".filter-end-date");
    await endDateInput.setValue("2026-08-22");

    const applyBtn = wrapper.find(".btn-apply-filters");
    await applyBtn.trigger("click");

    expect(auditorService.getAuditLogs).toHaveBeenCalledWith(
      expect.objectContaining({
        user_id: "crc.user",
        action: "UPDATE",
        entity_type: "Observation",
        start_time: "2026-08-20",
        end_time: "2026-08-22",
      })
    );
  });

  it("expands inspection row to render old_value vs new_value, reason_for_change, and sha256_hash", async () => {
    const wrapper = mount(AuditView, {
      global: {
        plugins: [pinia, router],
      },
    });

    await new Promise((r) => setTimeout(r, 20));
    await wrapper.vm.$nextTick();

    // Inspection row is initially hidden
    expect(wrapper.find(".audit-details-row").exists()).toBe(false);

    // Click the Inspect button on the first row
    const inspectBtn = wrapper.find(".btn-inspect-row");
    expect(inspectBtn.exists()).toBe(true);
    await inspectBtn.trigger("click");
    await wrapper.vm.$nextTick();

    // Verify inspection row expanded
    const detailsRow = wrapper.find(".audit-details-row");
    expect(detailsRow.exists()).toBe(true);

    // Verify diff blocks
    const oldValDiff = detailsRow.find(".old-value-diff");
    expect(oldValDiff.exists()).toBe(true);
    expect(oldValDiff.text()).toContain("140");

    const newValDiff = detailsRow.find(".new-value-diff");
    expect(newValDiff.exists()).toBe(true);
    expect(newValDiff.text()).toContain("120");

    // Verify mandatory reason for change
    const reasonBlock = detailsRow.find(".reason-for-change");
    expect(reasonBlock.exists()).toBe(true);
    expect(reasonBlock.text()).toContain("Typographical error corrected");

    // Verify cryptographic SHA-256 hash
    const sha256Block = detailsRow.find(".sha256-hash");
    expect(sha256Block.exists()).toBe(true);
    expect(sha256Block.text()).toContain("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    expect(sha256Block.text()).toContain("SEAL VERIFIED");
  });

  it("resets all filters when clear button is clicked", async () => {
    const wrapper = mount(AuditView, {
      global: {
        plugins: [pinia, router],
      },
    });

    await new Promise((r) => setTimeout(r, 20));
    await wrapper.vm.$nextTick();

    const userInput = wrapper.find<HTMLInputElement>(".filter-user-id");
    await userInput.setValue("test_user");

    const clearBtn = wrapper.find(".btn-clear-filters");
    await clearBtn.trigger("click");

    expect(auditorService.getAuditLogs).toHaveBeenCalledWith(
      expect.objectContaining({
        user_id: undefined,
        action: undefined,
        entity_type: undefined,
      })
    );
  });
});
