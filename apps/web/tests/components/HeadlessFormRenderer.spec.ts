import { describe, it, expect, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import HeadlessFormRenderer from "../../src/components/crf/HeadlessFormRenderer.vue";

describe("HeadlessFormRenderer.vue Skip Logic & Submission Payload Filtering", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  const formDefWithHideSkipLogic = {
    form_id: "vital_signs",
    version_tag: "1.0",
    data_schema: {
      name: "Vital Signs",
      variables: [
        { id: "pulse", name: "Pulse Rate", data_type: "number" },
        { id: "pulse_details", name: "Pulse Notes", data_type: "string" },
        { id: "vssbp", name: "Systolic BP", data_type: "number" },
      ],
    },
    ui_schema: {
      sections: [
        {
          id: "sec1",
          title: "Vitals",
          columns: 2,
          widgets: [
            { variable_id: "pulse", widget: "number_input" },
            { variable_id: "pulse_details", widget: "text_input" },
            { variable_id: "vssbp", widget: "number_input" },
          ],
        },
      ],
    },
    logic_schema: {
      skip_logic: [
        {
          type: "skip_logic",
          target_field: "pulse_details",
          action: "hide",
          condition: {
            type: "comparison",
            operator: "<=",
            operands: [
              { type: "field_ref", field_ref: { field_id: "pulse" } },
              { type: "constant", value: 100 },
            ],
          },
        },
      ],
    },
  };

  it("evaluates hide action skip logic rules and purges hidden fields in submission payload", async () => {
    const wrapper = mount(HeadlessFormRenderer, {
      props: {
        formDefinition: formDefWithHideSkipLogic,
        initialValues: {
          pulse: 80,
          pulse_details: "Should be hidden and purged",
          vssbp: 120,
        },
      },
    });

    // When pulse is 80 (<= 100), hide condition triggers (relevant = false)
    expect(wrapper.vm.visibleFields["pulse_details"]).toBe(false);
    expect(wrapper.vm.formData["pulse_details"]).toBe("");

    // Submit form
    await wrapper.find("form").trigger("submit");

    const emitted = wrapper.emitted("form-submitted");
    expect(emitted).toBeTruthy();
    expect(emitted.length).toBe(1);

    const submissionPayload = emitted[0][0];
    expect(submissionPayload.data).toEqual({
      pulse: 80,
      vssbp: 120,
    });
    expect(submissionPayload.data).not.toHaveProperty("pulse_details");
  });

  it("keeps fields visible when hide condition is not met", async () => {
    const wrapper = mount(HeadlessFormRenderer, {
      props: {
        formDefinition: formDefWithHideSkipLogic,
        initialValues: {
          pulse: 110,
          pulse_details: "Tachycardic notes",
          vssbp: 120,
        },
      },
    });

    // When pulse is 110 (> 100), hide condition is false -> relevant = true
    expect(wrapper.vm.visibleFields["pulse_details"]).toBe(true);
    expect(wrapper.vm.formData["pulse_details"]).toBe("Tachycardic notes");

    await wrapper.find("form").trigger("submit");

    const emitted = wrapper.emitted("form-submitted");
    expect(emitted).toBeTruthy();
    const submissionPayload = emitted[0][0];
    expect(submissionPayload.data).toEqual({
      pulse: 110,
      pulse_details: "Tachycardic notes",
      vssbp: 120,
    });
  });
});
