import { describe, it, expect, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import SoAMatrixEditor from "../src/components/clinical/SoAMatrixEditor.vue";
import SoaMatrixGrid from "../src/components/clinical/SoaMatrixGrid.vue";

describe("SoAMatrixEditor.vue & SoaMatrixGrid.vue - Bulk Matrix & Range Selection Suite", () => {
  const initialVisits = [
    { visit_name: "Visit 1 (Baseline)", target_day: 1 },
    { visit_name: "Visit 2 (Week 2)", target_day: 14 },
    { visit_name: "Visit 3 (Week 4)", target_day: 28 },
  ];

  const initialActivities = [
    {
      activity_name: "Vital Signs Assessment",
      cdash_domain: "VS",
      biomedical_concept_code: "C25298",
      assigned_visit_names: ["Visit 1 (Baseline)"],
    },
    {
      activity_name: "12-Lead Electrocardiogram",
      cdash_domain: "EG",
      biomedical_concept_code: "C38054",
      assigned_visit_names: [],
    },
    {
      activity_name: "Safety Laboratory Panel",
      cdash_domain: "LB",
      biomedical_concept_code: "C49286",
      assigned_visit_names: ["Visit 1 (Baseline)", "Visit 2 (Week 2)"],
    },
  ];

  it("renders SoaMatrixGrid wrapper component with master checkboxes and matrix table", () => {
    const wrapper = mount(SoaMatrixGrid, {
      props: {
        activities: initialActivities,
        visits: initialVisits,
      },
    });

    expect(wrapper.find(".soa-interactive-table").exists()).toBe(true);
    expect(wrapper.findAll(".master-col-checkbox").length).toBe(3);
    expect(wrapper.findAll(".master-row-checkbox").length).toBe(3);
    expect(wrapper.find(".master-grand-checkbox").exists()).toBe(true);
  });

  it("toggles all visits for a procedure when row master checkbox is clicked", async () => {
    const wrapper = mount(SoAMatrixEditor, {
      props: {
        activities: initialActivities,
        visits: initialVisits,
      },
    });

    const rowCheckboxes = wrapper.findAll(".master-row-checkbox");
    expect(rowCheckboxes.length).toBe(3);

    // Click master row checkbox for Vital Signs (currently assigned to Visit 1 only)
    await rowCheckboxes[0].setValue(true);
    await wrapper.vm.$nextTick();

    const emitted = wrapper.emitted("update:activities");
    expect(emitted).toBeTruthy();
    const updatedActivities = emitted[emitted.length - 1][0];

    // Vital signs should now be assigned to all 3 visits
    expect(updatedActivities[0].assigned_visit_names).toEqual([
      "Visit 1 (Baseline)",
      "Visit 2 (Week 2)",
      "Visit 3 (Week 4)",
    ]);
  });

  it("toggles all procedures for a visit when column master checkbox is clicked", async () => {
    const wrapper = mount(SoAMatrixEditor, {
      props: {
        activities: initialActivities,
        visits: initialVisits,
      },
    });

    const colCheckboxes = wrapper.findAll(".master-col-checkbox");
    expect(colCheckboxes.length).toBe(3);

    // Click master column checkbox for Visit 3 (currently 0 procedures assigned)
    await colCheckboxes[2].setValue(true);
    await wrapper.vm.$nextTick();

    const emitted = wrapper.emitted("update:activities");
    expect(emitted).toBeTruthy();
    const updatedActivities = emitted[emitted.length - 1][0];

    // Visit 3 should now be assigned in all 3 procedures
    expect(updatedActivities[0].assigned_visit_names).toContain(
      "Visit 3 (Week 4)"
    );
    expect(updatedActivities[1].assigned_visit_names).toContain(
      "Visit 3 (Week 4)"
    );
    expect(updatedActivities[2].assigned_visit_names).toContain(
      "Visit 3 (Week 4)"
    );
  });

  it("toggles the entire matrix when grand master checkbox is clicked", async () => {
    const wrapper = mount(SoAMatrixEditor, {
      props: {
        activities: initialActivities,
        visits: initialVisits,
      },
    });

    const grandMaster = wrapper.find(".master-grand-checkbox");
    expect(grandMaster.exists()).toBe(true);

    // Click grand master to assign all cells across matrix
    await grandMaster.trigger("click");
    await wrapper.vm.$nextTick();

    let emitted = wrapper.emitted("update:activities");
    expect(emitted).toBeTruthy();
    let updatedActivities = emitted[emitted.length - 1][0];

    for (const act of updatedActivities) {
      expect(act.assigned_visit_names.length).toBe(3);
    }
  });

  it("supports mouse drag range selection and bulk range assign/unassign", async () => {
    const wrapper = mount(SoAMatrixEditor, {
      props: {
        activities: initialActivities,
        visits: initialVisits,
      },
    });

    const cells = wrapper.findAll(".cell-toggle");
    expect(cells.length).toBe(9); // 3 rows x 3 cols

    // Pointer down on (0, 0)
    await cells[0].trigger("pointerdown");
    // Pointer move on (1, 1)
    await cells[4].trigger("pointermove");

    expect(wrapper.vm.isDragging).toBe(true);
    expect(wrapper.vm.selectedRangeCellCount).toBe(4); // 2x2 grid = 4 cells

    window.dispatchEvent(new Event("pointerup"));
    await wrapper.vm.$nextTick();

    expect(wrapper.find(".range-action-banner").exists()).toBe(true);

    // Click "Assign Range"
    await wrapper.find(".btn-range-assign").trigger("click");
    await wrapper.vm.$nextTick();

    const emitted = wrapper.emitted("update:activities");
    expect(emitted).toBeTruthy();
    const updated = emitted[emitted.length - 1][0];

    // Check cells in range (0,0), (0,1), (1,0), (1,1) are assigned
    expect(updated[0].assigned_visit_names).toContain("Visit 1 (Baseline)");
    expect(updated[0].assigned_visit_names).toContain("Visit 2 (Week 2)");
    expect(updated[1].assigned_visit_names).toContain("Visit 1 (Baseline)");
    expect(updated[1].assigned_visit_names).toContain("Visit 2 (Week 2)");
  });

  it("executes procedure group batch cloning preserving Biomedical Concept bindings", async () => {
    const wrapper = mount(SoAMatrixEditor, {
      props: {
        activities: initialActivities,
        visits: initialVisits,
      },
    });

    // Open procedure group clone form
    await wrapper.find(".btn-clone-group").trigger("click");
    await wrapper.vm.$nextTick();

    expect(wrapper.find(".clone-group-box").exists()).toBe(true);

    wrapper.vm.cloneSourceDomain = "VS";
    wrapper.vm.cloneNameSuffix = "(Cohort B)";

    await wrapper.find(".btn-execute-clone").trigger("click");
    await wrapper.vm.$nextTick();

    const emitted = wrapper.emitted("update:activities");
    expect(emitted).toBeTruthy();
    const updated = emitted[emitted.length - 1][0];

    expect(updated.length).toBe(4); // original 3 + 1 cloned
    const cloned = updated.find((a) =>
      a.activity_name.includes("Vital Signs Assessment (Cohort B)")
    );
    expect(cloned).toBeTruthy();
    expect(cloned.cdash_domain).toBe("VS");
    expect(cloned.biomedical_concept_code).toBe("C25298"); // Preserved concept binding!
  });
});
