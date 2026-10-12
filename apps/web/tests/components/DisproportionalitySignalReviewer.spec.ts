/**
 * Unit & Component tests for DisproportionalitySignalReviewer.vue
 *
 * Requirements Traceability: PRD-PVG-003 | PRD-PVG-008 | 21 CFR Part 11 Regulated
 */

import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import DisproportionalitySignalReviewer from "../../src/components/clinical/DisproportionalitySignalReviewer.vue";
import { safetyApi } from "../../src/api/safety";
import { useToastStore } from "../../src/stores/toasts.js";

describe("DisproportionalitySignalReviewer.vue", () => {
  let pinia: any;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("renders header metadata and switches to alerts tab", async () => {
    /**
     * @req: PRD-PVG-003
     */
    const wrapper = mount(DisproportionalitySignalReviewer, {
      props: {
        targetDrug: "CADENCE-101",
        studyId: "STUDY-CAD-2026",
      },
      global: {
        plugins: [pinia],
      },
    });

    expect(wrapper.text()).toContain("Safety Signal Detection Engine");
    expect(wrapper.text()).toContain("CADENCE-101");

    // Click Alerts Tab
    const tabButtons = wrapper.findAll("button[role='tab']");
    const alertsTab = tabButtons.find((btn) => btn.text().includes("Signal Review Alerts"));
    expect(alertsTab).toBeDefined();

    await alertsTab!.trigger("click");
    expect(wrapper.text()).toContain("Triage Summary");
    expect(wrapper.find("table[aria-label='Signal Review Alerts Queue']").exists()).toBe(true);
  });

  it("supports multi-select checkbox toggles and floating action bar display", async () => {
    /**
     * @req: PRD-PVG-003
     */
    const wrapper = mount(DisproportionalitySignalReviewer, {
      global: {
        plugins: [pinia],
      },
    });

    // Switch to alerts tab
    const tabButtons = wrapper.findAll("button[role='tab']");
    const alertsTab = tabButtons.find((btn) => btn.text().includes("Signal Review Alerts"));
    await alertsTab!.trigger("click");

    // Initially floating bar does not exist
    expect(wrapper.find(".floating-bulk-bar").exists()).toBe(false);

    // Toggle master header checkbox to select all active alerts
    const headerCheckbox = wrapper.find("th.checkbox-col input[type='checkbox']");
    expect(headerCheckbox.exists()).toBe(true);

    await headerCheckbox.setValue(true);
    await wrapper.vm.$nextTick();

    // Floating bar should now be visible
    expect(wrapper.find(".floating-bulk-bar").exists()).toBe(true);
    expect(wrapper.find(".bulk-count").text()).toContain("2");

    // Click 'Deselect All'
    const deselectBtn = wrapper.find(".floating-bulk-bar .btn-secondary");
    await deselectBtn.trigger("click");
    await wrapper.vm.$nextTick();

    expect(wrapper.find(".floating-bulk-bar").exists()).toBe(false);
  });

  it("executes bulk signature successfully and updates alert statuses in place", async () => {
    /**
     * @req: PRD-PVG-008
     */
    const mockBulkSignResponse = {
      updated_count: 2,
      status: "CONFIRMED_SIGNAL",
      esignature_manifest_id: "SIG-PVG-12345678",
      results: [
        {
          alert_id: "alt-001",
          status: "CONFIRMED_SIGNAL",
          reviewed_by: "safety_physician_01",
          reviewed_at: "2026-10-12T12:00:00Z",
          esignature_manifest_id: "SIG-PVG-12345678",
          message: "Successfully signed",
        },
        {
          alert_id: "alt-002",
          status: "CONFIRMED_SIGNAL",
          reviewed_by: "safety_physician_01",
          reviewed_at: "2026-10-12T12:00:00Z",
          esignature_manifest_id: "SIG-PVG-12345678",
          message: "Successfully signed",
        },
      ],
      alerts: [
        {
          id: "alt-001",
          target_drug: "CADENCE-101",
          meddra_term: "Hepatic failure",
          term_level: "PT",
          case_count_a: 15,
          prr: 15.0,
          ror: 17.47,
          eb05: 5.2,
          severity: "CRITICAL",
          status: "CONFIRMED_SIGNAL",
          triage_summary: "Triage complete",
          assigned_safety_officer: "Dr. Sarah Jenkins",
          review_decision: "CONFIRM_SIGNAL",
          created_at: "2026-10-12T00:00:00Z",
          created_by: "system",
          reason_for_change: "Bulk disposition",
          version_index: 2,
        },
        {
          id: "alt-002",
          target_drug: "CADENCE-101",
          meddra_term: "Acute kidney injury",
          term_level: "PT",
          case_count_a: 8,
          prr: 3.64,
          ror: 3.82,
          eb05: 1.62,
          severity: "HIGH",
          status: "CONFIRMED_SIGNAL",
          triage_summary: "Triage complete",
          assigned_safety_officer: "Dr. Michael Chang",
          review_decision: "CONFIRM_SIGNAL",
          created_at: "2026-10-12T00:00:00Z",
          created_by: "system",
          reason_for_change: "Bulk disposition",
          version_index: 2,
        },
      ],
      message: "Bulk signed 2 alerts.",
    };

    const spyBulkSign = vi
      .spyOn(safetyApi, "bulkSignAlerts")
      .mockResolvedValue(mockBulkSignResponse);

    const wrapper = mount(DisproportionalitySignalReviewer, {
      global: {
        plugins: [pinia],
      },
    });

    // Switch to alerts tab
    const tabButtons = wrapper.findAll("button[role='tab']");
    const alertsTab = tabButtons.find((btn) => btn.text().includes("Signal Review Alerts"));
    await alertsTab!.trigger("click");

    // Select master checkbox
    const headerCheckbox = wrapper.find("th.checkbox-col input[type='checkbox']");
    await headerCheckbox.setValue(true);

    // Open Bulk Sign Modal
    const bulkSignBtn = wrapper.find(".floating-bulk-bar .btn-primary");
    await bulkSignBtn.trigger("click");

    expect(wrapper.find("#bulk-sign-modal-title").exists()).toBe(true);

    // Fill bulk sign modal fields
    await wrapper.find("#bulk-review-justification").setValue("Confirmed signal based on statistical thresholds.");
    await wrapper.find("#bulk-sign-secret").setValue("valid-physician-secret"); // pragma: allowlist secret

    // Submit bulk sign
    const submitBtn = wrapper.find(".modal-footer .btn-primary");
    await submitBtn.trigger("click");

    await wrapper.vm.$nextTick();

    expect(spyBulkSign).toHaveBeenCalledWith({
      alert_ids: ["alt-001", "alt-002"],
      user_id: "safety_physician_01",
      signature_secret: "valid-physician-secret", // pragma: allowlist secret
      decision: "CONFIRM_SIGNAL",
      justification: "Confirmed signal based on statistical thresholds.",
    });

    // Selection should be cleared and modal closed
    expect(wrapper.find(".floating-bulk-bar").exists()).toBe(false);
    expect(wrapper.find("#bulk-sign-modal-title").exists()).toBe(false);

    // Alert rows should show approved/signed status
    expect(wrapper.text()).toContain("Approved (CONFIRM_SIGNAL)");
  });

  it("triggers toast store apiError on 401 or 409 API failures without modifying state", async () => {
    /**
     * @req: PRD-PVG-008
     */
    const spyBulkSign = vi
      .spyOn(safetyApi, "bulkSignAlerts")
      .mockRejectedValue(new Error("Invalid signing secret"));

    const toastStore = useToastStore();
    const spyToastApiError = vi.spyOn(toastStore, "apiError");

    const wrapper = mount(DisproportionalitySignalReviewer, {
      global: {
        plugins: [pinia],
      },
    });

    // Switch to alerts tab
    const tabButtons = wrapper.findAll("button[role='tab']");
    const alertsTab = tabButtons.find((btn) => btn.text().includes("Signal Review Alerts"));
    await alertsTab!.trigger("click");

    // Select row 1
    const rowCheckboxes = wrapper.findAll("td.checkbox-col input[type='checkbox']");
    await rowCheckboxes[0].setValue(true);

    // Open Bulk Sign Modal
    const bulkSignBtn = wrapper.find(".floating-bulk-bar .btn-primary");
    await bulkSignBtn.trigger("click");

    // Fill invalid credentials and submit
    await wrapper.find("#bulk-review-justification").setValue("Test rationale");
    await wrapper.find("#bulk-sign-secret").setValue("invalid-secret"); // pragma: allowlist secret

    const submitBtn = wrapper.find(".modal-footer .btn-primary");
    await submitBtn.trigger("click");

    await wrapper.vm.$nextTick();
    await new Promise((resolve) => setTimeout(resolve, 10));

    expect(spyBulkSign).toHaveBeenCalled();
    expect(spyToastApiError).toHaveBeenCalledWith(
      "Bulk alert signature failed",
      expect.any(Error)
    );

    // Modal remains open or state is intact, alerts remain in TRIGGERED / UNDER_REVIEW
    expect(wrapper.text()).toContain("TRIGGERED");
  });
});
