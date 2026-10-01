import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import TicketsView from "../../src/views/TicketsView.vue";
import apiClient from "../../src/services/api";

vi.mock("../../src/services/api", () => ({
  default: {
    get: vi.fn().mockResolvedValue({
      data: [
        {
          id: "tk-8801",
          reference: "DEV-2026-0041",
          title: "Investigational Product Cold-Chain Excursion at Site 101",
          category: "SUPPLY_EXCURSION",
          gxp_severity: "CRITICAL",
          priority: "CRITICAL",
          status: "OPEN",
          study_id: "STUDY-ONC-202",
        },
        {
          id: "tk-8802",
          reference: "DEV-2026-0042",
          title: "Missed Scheduled Blood Draw at Cycle 2 Day 1",
          category: "PROTOCOL_DEVIATION",
          gxp_severity: "MAJOR",
          priority: "HIGH",
          status: "IN_PROGRESS",
          study_id: "STUDY-ONC-202",
        },
      ],
    }),
    post: vi.fn().mockResolvedValue({ data: { success_count: 2, failure_count: 0 } }),
  },
}));

describe("TicketsView.vue Batch Operations", () => {
  let pinia;

  const mockTickets = [
    {
      id: "tk-8801",
      reference: "DEV-2026-0041",
      title: "Investigational Product Cold-Chain Excursion at Site 101",
      category: "SUPPLY_EXCURSION",
      gxp_severity: "CRITICAL",
      priority: "CRITICAL",
      status: "OPEN",
      study_id: "STUDY-ONC-202",
    },
    {
      id: "tk-8802",
      reference: "DEV-2026-0042",
      title: "Missed Scheduled Blood Draw at Cycle 2 Day 1",
      category: "PROTOCOL_DEVIATION",
      gxp_severity: "MAJOR",
      priority: "HIGH",
      status: "IN_PROGRESS",
      study_id: "STUDY-ONC-202",
    },
  ];

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    vi.clearAllMocks();
  });

  it("switches to table mode and renders multi-select checkboxes", async () => {
    const wrapper = mount(TicketsView, {
      global: { plugins: [pinia] },
    });

    wrapper.vm.tickets = [...mockTickets];
    wrapper.vm.viewMode = "table";
    await wrapper.vm.$nextTick();

    const headerCheckbox = wrapper.find(".header-checkbox");
    expect(headerCheckbox.exists()).toBe(true);

    const rowCheckboxes = wrapper.findAll(".row-checkbox");
    expect(rowCheckboxes.length).toBeGreaterThan(0);
  });

  it("selects all tickets when header checkbox is toggled", async () => {
    const wrapper = mount(TicketsView, {
      global: { plugins: [pinia] },
    });

    wrapper.vm.tickets = [...mockTickets];
    wrapper.vm.viewMode = "table";
    await wrapper.vm.$nextTick();

    wrapper.vm.toggleSelectAll();
    await wrapper.vm.$nextTick();

    expect(wrapper.vm.selectedTicketIds.length).toBe(wrapper.vm.filteredTickets.length);
    expect(wrapper.vm.isAllSelected).toBe(true);
  });

  it("renders BatchTicketActionBar when tickets are selected", async () => {
    const wrapper = mount(TicketsView, {
      global: { plugins: [pinia] },
    });

    wrapper.vm.tickets = [...mockTickets];
    wrapper.vm.viewMode = "table";
    wrapper.vm.selectedTicketIds = ["tk-8801", "tk-8802"];
    await wrapper.vm.$nextTick();

    expect(wrapper.find("#batch-ticket-action-bar").exists()).toBe(true);
    expect(wrapper.find(".selection-badge").text()).toBe("2");
  });

  it("opens BatchTicketSignModal when Resolve is clicked in action bar", async () => {
    const wrapper = mount(TicketsView, {
      global: { plugins: [pinia] },
    });

    wrapper.vm.tickets = [...mockTickets];
    wrapper.vm.viewMode = "table";
    wrapper.vm.selectedTicketIds = ["tk-8801"];
    await wrapper.vm.$nextTick();

    wrapper.findComponent({ name: "BatchTicketActionBar" }).vm.$emit("resolve");
    await wrapper.vm.$nextTick();

    expect(wrapper.vm.isBatchSignModalOpen).toBe(true);
  });

  it("executes batch resolution API call and updates tickets reactively", async () => {
    const wrapper = mount(TicketsView, {
      global: { plugins: [pinia] },
    });

    wrapper.vm.tickets = [...mockTickets];
    wrapper.vm.viewMode = "table";
    wrapper.vm.selectedTicketIds = ["tk-8801", "tk-8802"];
    await wrapper.vm.$nextTick();

    await wrapper.vm.handleExecuteBatchResolve({
      rationale: "Batch resolution test rationale",
      root_cause_category: "DATA_ENTRY_CORRECTION",
      signature_token: "sig_token_test_123",
      signature_meaning: "I authoritatively verify and close these clinical deviations/queries.",
    });

    expect(apiClient.post).toHaveBeenCalledWith(
      "/api/v1/tickets/batch",
      expect.objectContaining({
        ticket_ids: ["tk-8801", "tk-8802"],
        action: "RESOLVE",
      })
    );

    const resolvedTicket1 = wrapper.vm.tickets.find((t) => t.id === "tk-8801");
    expect(resolvedTicket1.status).toBe("RESOLVED");
    expect(resolvedTicket1.signature_token).toBe("sig_token_test_123");

    expect(wrapper.vm.selectedTicketIds.length).toBe(0);
  });
});
