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

describe("TicketsView - Live API Mode (VITE_DEMO_MODE=false)", () => {
  const sampleLiveTicket = {
    id: "tkt-live-01",
    reference: "TKT-2026-0042",
    title: "Site 101 eCRF Vital Signs Lock Escalation",
    description: "Investigating locked systolic field on SUBJ-8812.",
    category: "PROTOCOL_DEVIATION",
    gxp_severity: "MAJOR",
    priority: "HIGH",
    status: "IN_PROGRESS",
    assigned_queue: "DATA_CAPTURE_ECRF",
    reporter: "usr-crc-01",
    created_by: "usr-crc-01",
    study_id: "STUDY-ONC-202",
    site_id: "SITE-101",
    subject_id: "SUBJ-8812",
    related_entity_type: "CRF_FORM",
    related_entity_id: "FORM-VS-01",
    created_at: "2026-11-20T10:00:00Z",
    updated_at: "2026-11-20T12:00:00Z",
    sla_target_at: "2099-11-21T10:00:00Z",
    sla_total_paused_seconds: 0,
    sla_breached: false,
    sla_amber_warned: false,
    reason_for_change: "Initial escalation",
    version_index: 2,
  };

  beforeEach(() => {
    setActivePinia(createPinia());
    vi.stubEnv("VITE_DEMO_MODE", "false");
  });

  afterEach(() => {
    vi.unstubAllEnvs();
    vi.restoreAllMocks();
  });

  it("strips demo mock tickets, static KPIs, bulk checkboxes, and client-side exports outside demo mode", async () => {
    const { ticketsApi } = await import("../../src/api/ticketsApi");
    vi.spyOn(ticketsApi, "listTickets").mockResolvedValue([]);

    const wrapper = mount(TicketsView);
    await flushPromises();

    // No demo mock tickets or static 96.4% / 4.2h KPIs
    expect(wrapper.vm.tickets).toHaveLength(0);
    expect(wrapper.find("#kpi-sla-compliance").text()).toBe("—");
    expect(wrapper.find("#kpi-mttr").text()).toBe("—");
    expect(wrapper.text()).not.toContain("Export CSV");
    expect(wrapper.text()).not.toContain("Export JSON");

    // Table view hides bulk selection checkboxes
    const tableToggleBtn = wrapper
      .findAll(".view-toggle-btn")
      .find((btn) => btn.text().includes("Table"));
    await tableToggleBtn.trigger("click");
    await flushPromises();
    expect(wrapper.find("th.checkbox-cell").exists()).toBe(false);
    expect(wrapper.find(".bulk-action-bar").exists()).toBe(false);
  });

  it("auto-opens ?ref=TKT-... deep link, strips fake comments/attachments, hides INTERNAL_SPONSOR for CRC, and wires live comment, transition, and Part 11 sign", async () => {
    const { ticketsApi } = await import("../../src/api/ticketsApi");
    const { useAuthStore } = await import("../../src/stores/auth");
    const authStore = useAuthStore();
    authStore.rawRoles = ["CRC"];
    authStore.currentPersona = "site_crc";

    vi.spyOn(ticketsApi, "listTickets").mockResolvedValue([sampleLiveTicket]);
    vi.spyOn(ticketsApi, "getTicket").mockResolvedValue(sampleLiveTicket);
    vi.spyOn(ticketsApi, "listComments").mockResolvedValue([
      {
        id: "cmt-live-1",
        ticket_id: "tkt-live-01",
        body: "Live public note from clinical data manager.",
        visibility: "PUBLIC",
        created_at: "2026-11-20T10:30:00Z",
        created_by: "dm.lead",
        reason_for_change: "Initial triage note",
        version_index: 1,
      },
    ]);
    vi.spyOn(ticketsApi, "getAuditTrail").mockResolvedValue([
      {
        id: "11111111-1111-1111-1111-111111111111",
        entity_name: "Ticket",
        entity_id: "tkt-live-01",
        action: "TICKET_CREATED",
        timestamp: "2026-11-20T10:00:00Z",
        user_id: "usr-crc-01",
        reason_for_change: "Initial escalation",
        version_index: 1,
        changes_json: "{}",
        block_hash: "mock-merkle-block-hash-0001",
        predecessor_hash: "",
      },
    ]);

    const createCommentSpy = vi
      .spyOn(ticketsApi, "createComment")
      .mockResolvedValue({
        id: "cmt-live-2",
        ticket_id: "tkt-live-01",
        body: "Site confirmed cuff recalibration for SUBJ-8812.",
        visibility: "PUBLIC",
        created_at: "2026-11-20T11:00:00Z",
        created_by: "usr-crc-01",
        reason_for_change: "Coordinator response",
        version_index: 1,
      });

    const resolvedTicket = {
      ...sampleLiveTicket,
      status: "RESOLVED",
      root_cause_category: "PROCESS_WORKFLOW_DEFICIT",
      root_cause_summary: "Updated site cuff calibration log.",
      resolution_code: "CORRECTIVE_ACTION_IMPLEMENTED",
      version_index: 3,
    };
    const transitionSpy = vi
      .spyOn(ticketsApi, "transitionTicket")
      .mockResolvedValue(resolvedTicket);

    vi.spyOn(ticketsApi, "verifyTicketSignature").mockResolvedValue({
      signature_token: "live-jwt-sig-token",
    });
    const signedTicket = {
      ...resolvedTicket,
      signature_token: "jti-part11-001",
      signature_user: "dm.lead",
      signature_meaning:
        "I approve the clinical assessment and corrective medical action.",
      signature_timestamp: "2026-11-20T12:30:00Z",
      version_index: 4,
    };
    const signSpy = vi
      .spyOn(ticketsApi, "signTicket")
      .mockResolvedValue(signedTicket);

    const wrapper = mount(TicketsView, {
      global: {
        mocks: {
          $route: {
            query: { ref: "TKT-2026-0042" },
          },
        },
      },
    });
    await flushPromises();

    // Auto-opened drawer for TKT-2026-0042 without fake attachments tab or fake comments
    expect(wrapper.vm.isDrawerOpen).toBe(true);
    expect(wrapper.text()).toContain("TKT-2026-0042");
    expect(wrapper.text()).not.toContain("Evidence (");
    expect(wrapper.text()).not.toContain("logger_raw_export.csv");

    // Switch to Discussion tab and verify CRC cannot see INTERNAL_SPONSOR option
    const discussionTab = wrapper
      .findAll(".drawer-tabs .tab-item")
      .find((t) => t.text().includes("Discussion"));
    await discussionTab.trigger("click");
    await flushPromises();

    expect(wrapper.text()).toContain(
      "Live public note from clinical data manager."
    );
    expect(wrapper.text()).not.toContain("Internal Sponsor Only");
    expect(wrapper.text()).not.toContain("Internal Sponsor Note");

    // Post a live comment
    await wrapper
      .find("textarea[aria-label='Clinical discussion note']")
      .setValue("Site confirmed cuff recalibration for SUBJ-8812.");
    await wrapper
      .find("input[aria-label='Reason for adding note']")
      .setValue("Coordinator response");
    await wrapper.find("#btn-post-ticket-comment").trigger("click");
    await flushPromises();

    expect(createCommentSpy).toHaveBeenCalledWith(
      "tkt-live-01",
      {
        body: "Site confirmed cuff recalibration for SUBJ-8812.",
        visibility: "PUBLIC",
      },
      "Coordinator response"
    );

    // Switch to sponsor Data Manager and resolve + Part 11 sign the ticket
    authStore.rawRoles = ["Data Manager"];
    authStore.currentPersona = "data_manager";
    authStore.user = { username: "dm.lead" };
    await flushPromises();

    await wrapper.find("#btn-open-resolve").trigger("click");
    await wrapper
      .find("#resolve-rca-summary")
      .setValue("Updated site cuff calibration log.");
    await wrapper
      .find("#resolve-change-reason")
      .setValue("Resolved after source verification");
    await wrapper.find("#btn-confirm-resolve").trigger("click");
    await flushPromises();

    expect(transitionSpy).toHaveBeenCalledWith(
      "tkt-live-01",
      {
        target_status: "RESOLVED",
        root_cause_category: "PROCESS_WORKFLOW_DEFICIT",
        root_cause_summary: "Updated site cuff calibration log.",
        resolution_code: "CORRECTIVE_ACTION_IMPLEMENTED",
      },
      "Resolved after source verification"
    );

    // Open Part 11 eSign modal and sign
    await wrapper.find("#btn-open-part11-sign").trigger("click");
    await flushPromises();
    await wrapper.find("#signature-password").setValue("ValidPass!123");
    await wrapper.find(".ticket-sign-modal .btn-sign-action").trigger("click");
    await flushPromises();

    expect(signSpy).toHaveBeenCalledWith(
      "tkt-live-01",
      expect.objectContaining({
        signature_action: "SIGN_CLOSURE",
      }),
      "live-jwt-sig-token",
      expect.any(String)
    );
    expect(wrapper.text()).toContain("Locked under 21 CFR Part 11");
  });
});

describe("TicketsView - Auditor Read-Only Guards & Clinical Scope Pre-Fill", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.stubEnv("VITE_DEMO_MODE", "false");
  });

  afterEach(() => {
    vi.unstubAllEnvs();
    vi.restoreAllMocks();
  });

  it("disables #btn-create-ticket with a plain-language explanation for auditor roles and triggers zero HTTP 403 mutations", async () => {
    const { ticketsApi } = await import("../../src/api/ticketsApi");
    const { useAuthStore } = await import("../../src/stores/auth");
    const authStore = useAuthStore();
    authStore.currentPersona = "auditor";
    authStore.rawRoles = ["Auditor"];

    vi.spyOn(ticketsApi, "listTickets").mockResolvedValue([]);
    const createSpy = vi.spyOn(ticketsApi, "createTicket");

    const wrapper = mount(TicketsView);
    await flushPromises();

    const createBtn = wrapper.find("#btn-create-ticket");
    expect(createBtn.exists()).toBe(true);
    expect(createBtn.attributes("disabled")).toBeDefined();
    expect(createBtn.attributes("aria-disabled")).toBe("true");
    expect(createBtn.attributes("aria-describedby")).toBe(
      "tickets-auditor-readonly-note"
    );

    const readonlyNote = wrapper.find("#tickets-auditor-readonly-note");
    expect(readonlyNote.exists()).toBe(true);
    expect(readonlyNote.text()).toContain("Read-only auditor access");

    await createBtn.trigger("click");
    await flushPromises();

    expect(wrapper.find(".ticket-create-modal").exists()).toBe(false);
    expect(createSpy).not.toHaveBeenCalled();
  });

  it("binds active studyId, siteId, and pseudonymous subjectId onto TicketCreateModal when opened from /tickets", async () => {
    const { ticketsApi } = await import("../../src/api/ticketsApi");
    const { useAuthStore } = await import("../../src/stores/auth");
    const { useClinicalScopeStore } =
      await import("../../src/stores/clinicalScope");
    const authStore = useAuthStore();
    authStore.currentPersona = "site_crc";
    authStore.rawRoles = ["CRC"];

    const scopeStore = useClinicalScopeStore();
    scopeStore.$patch({
      activeStudyId: "STUDY-ONC-202",
      activeSiteId: "SITE-101",
      activeSubjectId: "SUBJ-8812",
    });

    vi.spyOn(ticketsApi, "listTickets").mockResolvedValue([]);

    const wrapper = mount(TicketsView, {
      global: {
        mocks: {
          $route: {
            query: {
              studyId: "STUDY-ONC-202",
              siteId: "SITE-101",
              subjectId: "SUBJ-8812",
            },
          },
        },
      },
    });
    await flushPromises();

    const createBtn = wrapper.find("#btn-create-ticket");
    expect(createBtn.attributes("disabled")).toBeUndefined();
    await createBtn.trigger("click");
    await flushPromises();

    expect(wrapper.find("#ticket-study").element.value).toBe("STUDY-ONC-202");
    expect(wrapper.find("#ticket-site").element.value).toBe("SITE-101");
    expect(wrapper.find("#ticket-subject").element.value).toBe("SUBJ-8812");
  });
});
