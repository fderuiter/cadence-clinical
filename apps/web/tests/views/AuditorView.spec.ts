import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import AuditorView from "../../src/views/AuditorView.vue";
import { useAuditorStore } from "../../src/stores/auditor";

// Mock auditorService
vi.mock("../../src/api/auditor", () => ({
  auditorService: {
    getAuditLogs: vi.fn().mockResolvedValue({
      items: [
        {
          id: "log-1",
          timestamp: "2026-08-20T10:00:00Z",
          user_id: "auditor_user",
          action: "SIGN_OFF",
          details: "Verified clinical trial form signatures.",
          reason_for_change: "Annual Compliance Audit Check",
          version_index: 2,
        },
      ],
      total_count: 1,
    }),
  },
}));

describe("AuditorView.vue Component Unit Tests", () => {
  let pinia: any;
  let auditorStore: any;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    auditorStore = useAuditorStore();
    vi.spyOn(auditorStore, "fetchAuditLogs");
  });

  it("renders the auditor workspace correctly displaying read-only banner and hiding edit/delete controls", async () => {
    const wrapper = mount(AuditorView, {
      global: {
        plugins: [pinia],
        stubs: {
          // Stub children if necessary, but here we can just mount them normally or with stub
          AuditorExportModal: true,
        },
      },
    });

    // Check presence of inspection banner
    const banner = wrapper.find(".inspection-banner");
    expect(banner.exists()).toBe(true);
    expect(banner.text()).toContain("REGULATORY INSPECTION MODE");
    expect(banner.text()).toContain("2026-12-31T23:59:59Z"); // Expiry date from store

    // Ensure header title is present
    expect(wrapper.text()).toContain("Regulatory Audit & Inspection Trail");

    // Ensure edit and delete buttons are NOT present (hides edit/delete buttons)
    // We can explicitly look for any button containing "edit", "delete", "create", "update" or "remove" (excluding standard buttons)
    const buttons = wrapper.findAll("button");
    const editOrDeleteButtons = buttons.filter((btn) => {
      const text = btn.text().toLowerCase();
      return text.includes("edit") || text.includes("delete") || text.includes("remove") || text.includes("create");
    });
    expect(editOrDeleteButtons.length).toBe(0);
  });

  it("changing the date filter dispatches the fetch action to the auditor store", async () => {
    const wrapper = mount(AuditorView, {
      global: {
        plugins: [pinia],
        stubs: {
          AuditorExportModal: true,
        },
      },
    });

    // Wait for onMounted fetch
    expect(auditorStore.fetchAuditLogs).toHaveBeenCalledTimes(1);

    // Find date range inputs in AuditTrailViewer
    const startDateInput = wrapper.find(".filter-start-date");
    expect(startDateInput.exists()).toBe(true);

    // Trigger changes on start date
    await startDateInput.setValue("2026-08-01");
    // Vue fires 'change' for date input, triggering onDateChange which dispatches fetchAuditLogs
    await startDateInput.trigger("change");

    expect(auditorStore.filters.dateRange.start).toBe("2026-08-01");
    expect(auditorStore.fetchAuditLogs).toHaveBeenCalled();
  });
});
