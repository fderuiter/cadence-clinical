import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import TicketsView from "../../src/views/TicketsView.vue";
import * as unbackedModule from "../../src/utils/unbackedActions.js";

describe("TicketsView - Batch Ticket Selection & Multi-Item Updates", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.stubEnv("VITE_DEMO_MODE", "true");
  });

  afterEach(() => {
    vi.unstubAllEnvs();
    vi.restoreAllMocks();
  });

  async function mountTicketsView() {
    const wrapper = mount(TicketsView);
    await flushPromises();
    return wrapper;
  }

  it("renders table view with select-all header checkbox and row checkboxes", async () => {
    const wrapper = await mountTicketsView();

    // Switch to table view
    const tableToggleBtn = wrapper
      .findAll(".view-toggle-btn")
      .find((btn) => btn.text().includes("Table"));
    await tableToggleBtn.trigger("click");
    await flushPromises();

    // Verify table header checkbox exists
    const headerCheckbox = wrapper.find(
      "th.checkbox-cell input[type='checkbox']"
    );
    expect(headerCheckbox.exists()).toBe(true);

    // Verify row checkboxes exist
    const rowCheckboxes = wrapper.findAll(
      "td.checkbox-cell input[type='checkbox']"
    );
    expect(rowCheckboxes.length).toBeGreaterThan(0);

    // Bulk action toolbar should be hidden initially when no tickets selected
    expect(wrapper.find(".bulk-action-bar").exists()).toBe(false);
  });

  it("toggles single ticket selection and shows bulk action bar", async () => {
    const wrapper = await mountTicketsView();

    // Switch to table view
    const tableToggleBtn = wrapper
      .findAll(".view-toggle-btn")
      .find((btn) => btn.text().includes("Table"));
    await tableToggleBtn.trigger("click");
    await flushPromises();

    const rowCheckboxes = wrapper.findAll(
      "td.checkbox-cell input[type='checkbox']"
    );
    expect(rowCheckboxes.length).toBeGreaterThan(0);

    // Check first ticket
    await rowCheckboxes[0].setValue(true);
    await flushPromises();

    // Bulk action bar should appear
    const bulkBar = wrapper.find(".bulk-action-bar");
    expect(bulkBar.exists()).toBe(true);
    expect(bulkBar.text()).toContain("1 ticket(s) selected");

    // Uncheck first ticket
    await rowCheckboxes[0].setValue(false);
    await flushPromises();

    // Bulk action bar should hide
    expect(wrapper.find(".bulk-action-bar").exists()).toBe(false);
  });

  it("toggles select-all across visible table rows and respects active filters", async () => {
    const wrapper = await mountTicketsView();

    // Switch to table view
    const tableToggleBtn = wrapper
      .findAll(".view-toggle-btn")
      .find((btn) => btn.text().includes("Table"));
    await tableToggleBtn.trigger("click");
    await flushPromises();

    // Apply severity filter = MAJOR
    const severitySelect = wrapper.findAll(".filter-select")[1];
    await severitySelect.setValue("MAJOR");
    await flushPromises();

    const visibleRows = wrapper.findAll(".table-row");
    const visibleCount = visibleRows.length;
    expect(visibleCount).toBeGreaterThan(0);

    // Click select-all header checkbox
    const headerCheckbox = wrapper.find(
      "th.checkbox-cell input[type='checkbox']"
    );
    await headerCheckbox.setValue(true);
    await flushPromises();

    const bulkBar = wrapper.find(".bulk-action-bar");
    expect(bulkBar.exists()).toBe(true);
    expect(bulkBar.text()).toContain(`${visibleCount} ticket(s) selected`);

    // Toggle select-all off
    await headerCheckbox.setValue(false);
    await flushPromises();
    expect(wrapper.find(".bulk-action-bar").exists()).toBe(false);
  });

  it("performs batch status transitions, verifies allowUnbackedAction call, and resets selection", async () => {
    const allowSpy = vi.spyOn(unbackedModule, "allowUnbackedAction");
    const wrapper = await mountTicketsView();

    // Switch to table view
    const tableToggleBtn = wrapper
      .findAll(".view-toggle-btn")
      .find((btn) => btn.text().includes("Table"));
    await tableToggleBtn.trigger("click");
    await flushPromises();

    // Select all visible tickets
    const headerCheckbox = wrapper.find(
      "th.checkbox-cell input[type='checkbox']"
    );
    await headerCheckbox.setValue(true);
    await flushPromises();

    // Select target status 'IN_PROGRESS'
    const statusSelect = wrapper.find(".batch-select");
    await statusSelect.setValue("IN_PROGRESS");
    await flushPromises();

    // Click Apply Status
    const applyStatusBtn = wrapper
      .findAll(".bulk-actions .btn-primary")
      .find((btn) => btn.text().includes("Apply Status"));
    await applyStatusBtn.trigger("click");
    await flushPromises();

    // Verify allowUnbackedAction was called
    expect(allowSpy).toHaveBeenCalledWith("Batch updating ticket status");

    // Selection should be cleared and bulk action bar hidden
    expect(wrapper.find(".bulk-action-bar").exists()).toBe(false);
  });

  it("performs batch severity update, verifies allowUnbackedAction call, and resets selection", async () => {
    const allowSpy = vi.spyOn(unbackedModule, "allowUnbackedAction");
    const wrapper = await mountTicketsView();

    // Switch to table view
    const tableToggleBtn = wrapper
      .findAll(".view-toggle-btn")
      .find((btn) => btn.text().includes("Table"));
    await tableToggleBtn.trigger("click");
    await flushPromises();

    // Select first ticket
    const rowCheckboxes = wrapper.findAll(
      "td.checkbox-cell input[type='checkbox']"
    );
    await rowCheckboxes[0].setValue(true);
    await flushPromises();

    // Select target severity 'CRITICAL'
    const severitySelect = wrapper.findAll(".batch-select")[1];
    await severitySelect.setValue("CRITICAL");
    await flushPromises();

    // Click Apply Severity
    const applySeverityBtn = wrapper
      .findAll(".bulk-actions .btn-primary")
      .find((btn) => btn.text().includes("Apply Severity"));
    await applySeverityBtn.trigger("click");
    await flushPromises();

    // Verify allowUnbackedAction was called
    expect(allowSpy).toHaveBeenCalledWith("Batch updating ticket severity");

    // Selection should be reset
    expect(wrapper.find(".bulk-action-bar").exists()).toBe(false);
  });

  it("handles batch transitions on closed or eSigned tickets gracefully without mutating them", async () => {
    const wrapper = await mountTicketsView();

    // Inject a closed ticket and an eSigned ticket into component state
    const vm = wrapper.vm;
    vm.tickets.push(
      {
        id: "tk-closed-test",
        reference: "DEV-CLOSED-01",
        title: "Closed Deviation Ticket",
        category: "SITE_OPERATIONS",
        gxp_severity: "MINOR",
        status: "CLOSED",
        version_index: 1,
      },
      {
        id: "tk-esigned-test",
        reference: "DEV-ESIGNED-01",
        title: "eSigned Ticket",
        category: "PROTOCOL_DEVIATION",
        gxp_severity: "MAJOR",
        status: "RESOLVED",
        signature_token: "sig_token_sample",
        version_index: 1,
      }
    );
    await flushPromises();

    // Select the closed ticket and eSigned ticket
    vm.toggleSelectTicket("tk-closed-test");
    vm.toggleSelectTicket("tk-esigned-test");
    await flushPromises();

    expect(vm.selectedTicketCount).toBe(2);

    // Attempt batch transition to IN_PROGRESS
    vm.handleBatchTransition("IN_PROGRESS");
    await flushPromises();

    // Closed and eSigned tickets must remain CLOSED / RESOLVED
    const closedTicket = vm.tickets.find((t) => t.id === "tk-closed-test");
    const esignedTicket = vm.tickets.find((t) => t.id === "tk-esigned-test");

    expect(closedTicket.status).toBe("CLOSED");
    expect(esignedTicket.status).toBe("RESOLVED");
    expect(vm.selectedTicketCount).toBe(0);
  });
});
