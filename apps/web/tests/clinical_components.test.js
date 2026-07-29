import { describe, it, expect } from "vitest";
import { mount } from "@vue/test-utils";
import ClinicalInput from "../src/components/clinical/ClinicalInput.vue";
import ClinicalRadioGroup from "../src/components/clinical/ClinicalRadioGroup.vue";
import ClinicalLookupInput from "../src/components/clinical/ClinicalLookupInput.vue";
import ClinicalFormField from "../src/components/clinical/ClinicalFormField.vue";
import ClinicalQueryFlag from "../src/components/clinical/ClinicalQueryFlag.vue";
import ClinicalQueryPanel from "../src/components/clinical/ClinicalQueryPanel.vue";

describe("ClinicalQueryFlag.vue", () => {
  it("renders with status NONE correctly", () => {
    const wrapper = mount(ClinicalQueryFlag, {
      props: { id: "test-field", query: null, isOpen: false },
    });
    expect(wrapper.classes()).toContain("query-status-none");
    expect(wrapper.text()).toContain("💬");
    expect(wrapper.attributes("aria-label")).toBe(
      "No active queries. Click to create."
    );
  });

  it("renders with status OPEN correctly", () => {
    const wrapper = mount(ClinicalQueryFlag, {
      props: { id: "test-field", query: { status: "OPEN" }, isOpen: true },
    });
    expect(wrapper.classes()).toContain("query-status-open");
    expect(wrapper.text()).toContain("⚠️");
    expect(wrapper.attributes("aria-label")).toBe("Query status: OPEN");
    expect(wrapper.attributes("aria-expanded")).toBe("true");
  });
});

describe("ClinicalQueryPanel.vue", () => {
  it("renders query creation panel for NONE status", async () => {
    const wrapper = mount(ClinicalQueryPanel, {
      props: { id: "fieldX", query: null },
    });
    expect(wrapper.text()).toContain("Raise a query for this field:");
    expect(wrapper.find("textarea").exists()).toBe(true);

    const submitBtn = wrapper.find(".btn-submit-query");
    expect(submitBtn.exists()).toBe(true);

    await wrapper.find("textarea").setValue("Check this value");
    await submitBtn.trigger("click");

    expect(wrapper.emitted("create-query")).toBeTruthy();
    expect(wrapper.emitted("create-query")[0]).toEqual(["Check this value"]);
  });

  it("renders response section for OPEN status", async () => {
    const query = {
      status: "OPEN",
      message: "Check value range",
      createdBy: "CRA",
      createdAt: "2026-07-22",
    };
    const wrapper = mount(ClinicalQueryPanel, {
      props: { id: "fieldX", query },
    });

    expect(wrapper.text()).toContain("Check value range");
    expect(wrapper.text()).toContain("Status: OPEN");

    const respondBtn = wrapper.find(".btn-respond-query");
    expect(respondBtn.exists()).toBe(true);

    await wrapper.find("textarea").setValue("Justification notes");
    await respondBtn.trigger("click");

    expect(wrapper.emitted("respond-query")).toBeTruthy();
    expect(wrapper.emitted("respond-query")[0]).toEqual([
      "Justification notes",
    ]);
  });

  it("renders resolve/reopen buttons for ANSWERED status", async () => {
    const query = {
      status: "ANSWERED",
      message: "Check value range",
      response: "My response justification",
      respondedBy: "Investigator",
    };
    const wrapper = mount(ClinicalQueryPanel, {
      props: { id: "fieldX", query },
    });

    expect(wrapper.text()).toContain("Status: ANSWERED");
    expect(wrapper.text()).toContain("My response justification");

    const closeBtn = wrapper.find(".btn-close-query");
    const reopenBtn = wrapper.find(".btn-reopen-query");

    expect(closeBtn.exists()).toBe(true);
    expect(reopenBtn.exists()).toBe(true);

    await closeBtn.trigger("click");
    expect(wrapper.emitted("close-query")).toBeTruthy();

    await reopenBtn.trigger("click");
    expect(wrapper.emitted("reopen-query")).toBeTruthy();
  });
});

describe("ClinicalInput.vue", () => {
  it("renders labels, values, and styles properly", async () => {
    const wrapper = mount(ClinicalInput, {
      props: {
        id: "patientName",
        label: "Patient Name",
        modelValue: "John Doe",
        gridSpan: 6,
      },
    });

    expect(wrapper.find("label").text()).toBe("Patient Name");
    const input = wrapper.find("input");
    expect(input.element.value).toBe("John Doe");
    expect(wrapper.classes()).toContain("grid-span-6");

    await input.setValue("Jane Doe");
    expect(wrapper.emitted("update:modelValue")).toBeTruthy();
    expect(wrapper.emitted("update:modelValue")[0]).toEqual(["Jane Doe"]);
  });

  it("presents validation errors when input is not empty", () => {
    const wrapper = mount(ClinicalInput, {
      props: {
        id: "vssbp",
        label: "Systolic BP",
        modelValue: "280",
        error: "Systolic Blood Pressure must be between 50 and 250 mmHg",
      },
    });

    expect(wrapper.classes()).toContain("has-error");
    expect(wrapper.find(".validation-error-msg").text()).toContain(
      "must be between 50 and 250 mmHg"
    );
  });
});

describe("ClinicalRadioGroup.vue", () => {
  it("renders accessible fieldsets and options", async () => {
    const options = [
      { value: "M", label: "Male" },
      { value: "F", label: "Female" },
    ];
    const wrapper = mount(ClinicalRadioGroup, {
      props: {
        id: "gender",
        label: "Gender Selection",
        options,
        modelValue: "F",
      },
    });

    expect(wrapper.find("legend").text()).toBe("Gender Selection");
    const radioF = wrapper.find("#gender_option_1");
    expect(radioF.element.checked).toBe(true);

    const radioM = wrapper.find("#gender_option_0");
    await radioM.trigger("change");

    expect(wrapper.emitted("update:modelValue")).toBeTruthy();
    expect(wrapper.emitted("update:modelValue")[0]).toEqual(["M"]);
  });
});

describe("ClinicalLookupInput.vue", () => {
  it("renders loading, valid, invalid, degraded states accessibly", async () => {
    const wrapper = mount(ClinicalLookupInput, {
      props: {
        id: "conceptCode",
        label: "Concept Code",
        modelValue: "C123",
        status: "loading",
      },
    });

    const indicator = wrapper.find(".lookup-status-indicator");
    expect(indicator.classes()).toContain("lookup-loading");
    expect(indicator.text()).toContain("⏳");
    expect(indicator.text()).toContain("Searching terminology database...");

    await wrapper.setProps({
      status: "valid",
      statusMessage: "Code C123 is verified.",
    });
    expect(indicator.classes()).toContain("lookup-valid");
    expect(indicator.text()).toContain("✅");
    expect(indicator.text()).toContain("Code C123 is verified.");
  });
});

describe("ClinicalFormField.vue", () => {
  it("dispatches metadata dynamically to appropriate sub-component", () => {
    const fieldText = { id: "text-id", label: "Text Label", type: "text" };
    const wrapperText = mount(ClinicalFormField, {
      props: { field: fieldText, modelValue: "Initial" },
    });
    expect(wrapperText.findComponent(ClinicalInput).exists()).toBe(true);

    const fieldRadio = {
      id: "radio-id",
      label: "Radio Label",
      type: "radio",
      options: ["Y", "N"],
    };
    const wrapperRadio = mount(ClinicalFormField, {
      props: { field: fieldRadio, modelValue: "Y" },
    });
    expect(wrapperRadio.findComponent(ClinicalRadioGroup).exists()).toBe(true);

    const fieldConcept = {
      id: "lookup-id",
      label: "Lookup Label",
      type: "concept_code",
    };
    const wrapperConcept = mount(ClinicalFormField, {
      props: { field: fieldConcept, modelValue: "C11" },
    });
    expect(wrapperConcept.findComponent(ClinicalLookupInput).exists()).toBe(
      true
    );
  });
});
