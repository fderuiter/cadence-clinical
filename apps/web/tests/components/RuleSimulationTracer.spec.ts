import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import CrfAuthoringCanvas from "../../src/components/crf/CrfAuthoringCanvas.vue";
import RuleTracerPanel from "../../src/components/crf/RuleTracerPanel.vue";
import HeadlessFormRenderer from "../../src/components/crf/HeadlessFormRenderer.vue";
import {
  evaluateAST,
  renderFormFromJSON,
  traceASTEvaluation,
} from "../../src/evaluator.js";

vi.mock("vuedraggable", () => ({
  default: {
    name: "draggable",
    props: ["modelValue", "itemKey"],
    template: `<div class="mock-draggable"><slot name="item" v-for="(item, index) in modelValue" :element="item" :index="index"></slot></div>`,
  },
}));

describe("Client-Side Embedded Rule Simulation Canvas & Live Execution Tracer", () => {
  let pinia: any;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
  });

  const mockFormSchema = {
    id: "vitals-simulation-form",
    name: "Vitals & Safety Form",
    sections: [
      {
        id: "sec-vitals",
        name: "Vital Signs",
        items: [
          {
            id: "VS_SYSBP",
            label: "Systolic Blood Pressure",
            type: "numeric",
            gridSpan: 6,
            required: true,
            constraints: {
              condition: {
                type: "comparison",
                operator: "<=",
                operands: [
                  { type: "field_ref", field_ref: { field_id: "VS_SYSBP" } },
                  { type: "constant", value: 180 },
                ],
              },
              message: "Systolic BP exceeds safety alert limit (180 mmHg).",
            },
          },
          {
            id: "VS_DIABP",
            label: "Diastolic Blood Pressure",
            type: "numeric",
            gridSpan: 6,
            required: true,
          },
          {
            id: "HYPERTENSION_FOLLOWUP",
            label: "Hypertension Follow-Up Details",
            type: "text",
            gridSpan: 12,
            relevant: {
              type: "comparison",
              operator: ">",
              operands: [
                { type: "field_ref", field_ref: { field_id: "VS_SYSBP" } },
                { type: "constant", value: 140 },
              ],
            },
          },
        ],
      },
    ],
  };

  it("renders 'Simulation Mode' toggle button on CrfAuthoringCanvas control bar", () => {
    const wrapper = mount(CrfAuthoringCanvas, {
      props: {
        formSchema: mockFormSchema,
        selectedFieldId: null,
      },
    });

    const simBtn = wrapper.find(".btn-simulation-mode");
    expect(simBtn.exists()).toBe(true);
    expect(simBtn.text()).toContain("Simulation Mode");
  });

  it("toggles Simulation Mode view and embeds HeadlessFormRenderer and RuleTracerPanel", async () => {
    const wrapper = mount(CrfAuthoringCanvas, {
      props: {
        formSchema: mockFormSchema,
        selectedFieldId: null,
      },
    });

    expect(wrapper.findComponent(HeadlessFormRenderer).exists()).toBe(false);
    expect(wrapper.findComponent(RuleTracerPanel).exists()).toBe(false);

    const simBtn = wrapper.find(".btn-simulation-mode");
    await simBtn.trigger("click");

    expect(wrapper.findComponent(HeadlessFormRenderer).exists()).toBe(true);
    expect(wrapper.findComponent(RuleTracerPanel).exists()).toBe(true);
    expect(wrapper.text()).toContain("Rule Simulation Mode Active");
  });

  it("updates field visibility using renderFormFromJSON when input changes in Simulation Mode", async () => {
    const fieldsMeta = [
      { id: "VS_SYSBP", relevant: null },
      { id: "VS_DIABP", relevant: null },
      {
        id: "HYPERTENSION_FOLLOWUP",
        relevant: {
          type: "comparison",
          operator: ">",
          operands: [
            { type: "field_ref", field_ref: { field_id: "VS_SYSBP" } },
            { type: "constant", value: 140 },
          ],
        },
      },
    ];

    // Baseline SYSBP = 120 (<= 140) -> HYPERTENSION_FOLLOWUP is irrelevant
    let res = renderFormFromJSON(fieldsMeta, { VS_SYSBP: 120 });
    expect(res.visibleFields.HYPERTENSION_FOLLOWUP).toBe(false);

    // Elevated SYSBP = 150 (> 140) -> HYPERTENSION_FOLLOWUP becomes relevant
    res = renderFormFromJSON(fieldsMeta, { VS_SYSBP: 150 });
    expect(res.visibleFields.HYPERTENSION_FOLLOWUP).toBe(true);
  });

  it("displays inline query message badge when a ConstraintRule is violated", async () => {
    const wrapper = mount(CrfAuthoringCanvas, {
      props: {
        formSchema: mockFormSchema,
        selectedFieldId: null,
      },
    });

    await wrapper.find(".btn-simulation-mode").trigger("click");

    const sysbpInput = wrapper.find("#widget-VS_SYSBP");
    expect(sysbpInput.exists()).toBe(true);

    // Enter invalid value exceeding 180 mmHg
    await sysbpInput.setValue(190);

    const errorMessage = wrapper.find(".field-error-message");
    expect(errorMessage.exists()).toBe(true);
    expect(errorMessage.text()).toContain(
      "Systolic BP exceeds safety alert limit (180 mmHg)."
    );
  });

  it("displays step-by-step AST evaluation results and dependencies in RuleTracerPanel", () => {
    const trace = traceASTEvaluation(
      {
        type: "comparison",
        operator: ">",
        operands: [
          { type: "field_ref", field_ref: { field_id: "VS_SYSBP" } },
          { type: "constant", value: 140 },
        ],
      },
      { VS_SYSBP: 150 }
    );

    expect(trace.val).toBe(true);
    expect(trace.operator).toBe(">");
    expect(trace.referencedFields).toContain("VS_SYSBP");
    expect(trace.children.length).toBe(2);
    expect(trace.children[0].val).toBe(150);
    expect(trace.children[1].val).toBe(140);
  });

  it("pre-fills standard mock subject profiles in simulation mode", async () => {
    const wrapper = mount(CrfAuthoringCanvas, {
      props: {
        formSchema: mockFormSchema,
        selectedFieldId: null,
      },
    });

    await wrapper.find(".btn-simulation-mode").trigger("click");

    const profileSelect = wrapper.find("#mock-profile-select");
    expect(profileSelect.exists()).toBe(true);

    // Apply Hypertensive Alert profile
    await profileSelect.setValue("hypertensive");
    await profileSelect.trigger("change");
    await wrapper.vm.$nextTick();

    const sysbpInput = wrapper.find("#widget-VS_SYSBP") as any;
    expect(sysbpInput.element.value).toBe("165");
  });

  it("resets simulation state and restores default authoring view", async () => {
    const wrapper = mount(CrfAuthoringCanvas, {
      props: {
        formSchema: mockFormSchema,
        selectedFieldId: null,
      },
    });

    const simBtn = wrapper.find(".btn-simulation-mode");
    await simBtn.trigger("click");

    expect(wrapper.findComponent(HeadlessFormRenderer).exists()).toBe(true);

    // Reset simulation values
    const resetBtn = wrapper.find(".btn-reset-simulation");
    await resetBtn.trigger("click");

    // Exit simulation mode
    await simBtn.trigger("click");

    expect(wrapper.findComponent(HeadlessFormRenderer).exists()).toBe(false);
    expect(wrapper.find(".authoring-canvas-stage").exists()).toBe(true);
  });
});
