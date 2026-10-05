import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import CellGeneTherapyCocConsole from "@/components/clinical/CellGeneTherapyCocConsole.vue";

describe("CellGeneTherapyCocConsole.vue - Domain Formatting Specification", () => {
  it("renders human-readable clinical labels instead of raw database keys or UUID labels", () => {
    const wrapper = mount(CellGeneTherapyCocConsole);

    expect(wrapper.text()).toContain("Subject Identifier");
    expect(wrapper.text()).not.toContain("Subject ID / UUID");

    expect(wrapper.text()).toContain("Enter a valid Study Protocol and Subject Identifier above");
    expect(wrapper.text()).not.toContain("Subject UUID");
  });

  it("displays human readable labels for facility codes when ledger data is loaded", async () => {
    const wrapper = mount(CellGeneTherapyCocConsole);
    wrapper.vm.cocData = {
      id: "coc-1",
      study_id: "STUDY-801",
      subject_id: "SUBJ-001",
      din: "DIN-12345",
      batch_id: "BATCH-A1",
      current_stage: "HARVEST",
      status: "ACTIVE",
      tamper_evident_barcode: "BARCODE",
      barcode_signature: "SIG",
      collection_site_id: "SITE-ALPHA",
      collection_timestamp: "2026-10-01",
      manufacturing_facility_id: "CMO-BETA",
      cryo_temp_excursion_count: 0,
      lowest_temp_celsius: -196.0,
      infusion_unlocked: false,
    };
    await wrapper.vm.$nextTick();

    expect(wrapper.text()).toContain("Collection Site: SITE-ALPHA");
    expect(wrapper.text()).toContain("Manufacturing Facility: CMO-BETA");
  });
});
