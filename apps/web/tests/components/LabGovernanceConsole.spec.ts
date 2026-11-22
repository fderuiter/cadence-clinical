import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import LabGovernanceConsole from "../../src/components/clinical/LabGovernanceConsole.vue";

describe("LabGovernanceConsole.vue", () => {
  beforeEach(() => {
    vi.clearAllMocks();

    // Mock global fetch
    global.fetch = vi.fn((input: RequestInfo | URL, options?: any) => {
      const url = String(input);
      if (url.includes("/api/v1/execution/lab-ranges")) {
        return Promise.resolve({
          ok: true,
          json: () =>
            Promise.resolve([
              {
                id: "range-1",
                study_id: "STUDY-101",
                site_id: null,
                source: "CENTRAL",
                test_code: "ALT",
                test_name: "Alanine Aminotransferase",
                unit: "U/L",
                low_bound: 7,
                high_bound: 56,
                sex_applicability: "ALL",
                version: 1,
              },
            ]),
        } as Response);
      }

      if (url.includes("/api/v1/execution/labs/import/dry-run")) {
        return Promise.resolve({
          ok: true,
          json: () =>
            Promise.resolve({
              dry_run_id: "dry-run-123",
              file_name: "lab_ranges.csv",
              total_rows_parsed: 2,
              valid_rows_count: 2,
              invalid_rows_count: 0,
              is_valid: true,
              rows: [
                {
                  row_number: 1,
                  test_code: "ALT",
                  test_name: "Alanine Aminotransferase",
                  unit: "U/L",
                  low_bound: 7,
                  high_bound: 56,
                  sex_applicability: "ALL",
                  loinc_code: "1742-6",
                  is_valid: true,
                },
                {
                  row_number: 2,
                  test_code: "CREAT",
                  test_name: "Serum Creatinine",
                  unit: "mg/dL",
                  low_bound: 0.6,
                  high_bound: 1.2,
                  sex_applicability: "M",
                  loinc_code: "2160-0",
                  is_valid: true,
                },
              ],
              validation_errors: [],
            }),
        } as Response);
      }

      if (url.includes("/api/v1/execution/labs/import/commit")) {
        return Promise.resolve({
          ok: true,
          json: () =>
            Promise.resolve({
              id: "job-456",
              study_id: "STUDY-101",
              site_id: null,
              lab_source: "CENTRAL",
              file_name: "import.csv",
              status: "COMPLETED",
              total_rows: 2,
              imported_count: 2,
              error_count: 0,
              created_at: new Date().toISOString(),
            }),
        } as Response);
      }

      if (url.includes("/api/v1/execution/labs/convert-unit")) {
        return Promise.resolve({
          ok: true,
          json: () =>
            Promise.resolve({
              test_code: "CREAT",
              original_value: 2.5,
              from_unit: "mg/dL",
              converted_value: 221,
              to_unit: "umol/L",
              factor: 88.4,
              loinc_code: "2160-0",
            }),
        } as Response);
      }

      if (url.includes("/api/v1/execution/labs/loinc-catalog")) {
        return Promise.resolve({
          ok: true,
          json: () =>
            Promise.resolve({
              items: [
                {
                  test_code: "ALT",
                  test_name: "Alanine Aminotransferase",
                  loinc_code: "1742-6",
                  standard_unit: "U/L",
                  category: "Hepatic Safety",
                  molecular_weight: 584.66,
                },
              ],
            }),
        } as Response);
      }

      if (url.includes("/api/v1/execution/labs/toxicity/review-queue")) {
        return Promise.resolve({
          ok: true,
          json: () => Promise.resolve([]),
        } as Response);
      }

      if (url.includes("/api/v1/execution/labs/certifications")) {
        return Promise.resolve({
          ok: true,
          json: () => Promise.resolve([]),
        } as Response);
      }

      if (url.includes("/api/v1/execution/labs/batch-status")) {
        return Promise.resolve({
          ok: true,
          json: () => Promise.resolve([]),
        } as Response);
      }

      if (url.includes("/api/v1/execution/labs/toxicity/evaluate")) {
        return Promise.resolve({
          ok: true,
          json: () =>
            Promise.resolve({
              ctcae_grade: 3,
              toxicity_description: "Severe Adverse Event Trigger",
              is_critical_ae_trigger: true,
              is_hys_law_warning: false,
            }),
        } as Response);
      }

      return Promise.resolve({
        ok: true,
        json: () => Promise.resolve({}),
      } as Response);
    });
  });

  it("renders console header and initial ranges correctly", async () => {
    const wrapper = mount(LabGovernanceConsole);
    expect(wrapper.text()).toContain(
      "Lab Reference Range Governance & Toxicity Grading Console"
    );
    expect(wrapper.text()).toContain("Normal Reference Ranges");
  });

  it("opens bulk import modal and executes dry run preflight validation", async () => {
    window.alert = vi.fn();
    const wrapper = mount(LabGovernanceConsole);

    // Click Bulk Import button
    const bulkBtn = wrapper.find(".header-actions button");
    expect(bulkBtn.exists()).toBe(true);
    await bulkBtn.trigger("click");

    // Check modal exists
    expect(wrapper.text()).toContain(
      "Bulk Laboratory Reference Range Import (CSV / XLSX)"
    );

    // Enter raw CSV content
    const textarea = wrapper.find("#bulk-raw");
    expect(textarea.exists()).toBe(true);
    await textarea.setValue(
      "TestCode,TestName,Unit,LowBound,HighBound,Sex,LoincCode\nALT,Alanine Aminotransferase,U/L,7,56,ALL,1742-6\nCREAT,Serum Creatinine,mg/dL,0.6,1.2,M,2160-0"
    );

    // Click Pre-Flight Dry Run button
    const buttons = wrapper.findAll(".modal-footer button");
    const dryRunBtn = buttons.find((b) =>
      b.text().includes("Pre-Flight Dry Run")
    );
    expect(dryRunBtn?.exists()).toBe(true);
    await dryRunBtn?.trigger("click");

    // Verify dry-run API call
    expect(global.fetch).toHaveBeenCalledWith(
      "/api/v1/execution/labs/import/dry-run",
      expect.objectContaining({
        method: "POST",
      })
    );

    // Verify preview results render
    expect(wrapper.text()).toContain("Pre-Flight Validation Preview");
    expect(wrapper.text()).toContain("VALIDATED CLEANLY");
    expect(wrapper.text()).toContain("Total Rows: 2");
  });

  it("commits bulk import with GxP reason for change header", async () => {
    window.alert = vi.fn();
    const wrapper = mount(LabGovernanceConsole);

    // Open modal
    await wrapper.find(".header-actions button").trigger("click");

    // Set raw CSV content & reason
    await wrapper
      .find("#bulk-raw")
      .setValue("ALT,Alanine Aminotransferase,U/L,7,56,ALL,1742-6");
    await wrapper
      .find("#bulk-reason")
      .setValue("Updating clinical trial protocol normal reference bounds");

    // Dry run
    const buttons = wrapper.findAll(".modal-footer button");
    const dryRunBtn = buttons.find((b) =>
      b.text().includes("Pre-Flight Dry Run")
    );
    await dryRunBtn?.trigger("click");

    // Commit import
    const commitBtn = wrapper
      .findAll(".modal-footer button")
      .find((b) => b.text().includes("Commit Bulk Import"));
    expect(commitBtn?.attributes("disabled")).toBeUndefined();
    await commitBtn?.trigger("click");

    // Verify commit API call
    expect(global.fetch).toHaveBeenCalledWith(
      "/api/v1/execution/labs/import/commit",
      expect.objectContaining({
        method: "POST",
        headers: expect.objectContaining({
          "X-Change-Reason":
            "Updating clinical trial protocol normal reference bounds",
        }),
      })
    );
  });
});
