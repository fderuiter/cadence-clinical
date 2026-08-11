import { describe, it, expect } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { useAuthStore } from "../src/stores/auth";
import {
  ClinicalInput,
  ClinicalRadioGroup,
  ClinicalLookupInput,
  ClinicalFormField,
  ClinicalQueryFlag,
  ClinicalQueryPanel,
  createClinicalLookupInput,
} from "ui";

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

  it("displays role-aware labels and fallback statuses correctly", async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const authStore = useAuthStore();

    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Site Investigator"];

    const queryOpen = {
      status: "OPEN",
      message: "Value out of bounds",
    };
    const wrapperOpenSite = mount(ClinicalQueryPanel, {
      props: { id: "fieldX", query: queryOpen },
      global: { plugins: [pinia] },
    });
    expect(wrapperOpenSite.text()).toContain("Status: Awaiting Site Action");

    const queryAnswered = {
      status: "ANSWERED",
      message: "Value out of bounds",
      response: "Resolved",
    };
    const wrapperAnsweredSite = mount(ClinicalQueryPanel, {
      props: { id: "fieldX", query: queryAnswered },
      global: { plugins: [pinia] },
    });
    expect(wrapperAnsweredSite.text()).toContain("Status: Submitted to CRA");

    const queryClosed = {
      status: "CLOSED",
      message: "Value out of bounds",
      response: "Resolved",
    };
    const wrapperClosedSite = mount(ClinicalQueryPanel, {
      props: { id: "fieldX", query: queryClosed },
      global: { plugins: [pinia] },
    });
    expect(wrapperClosedSite.text()).toContain("Status: CLOSED");

    authStore.rawRoles = ["CRA"];

    const wrapperOpenMonitor = mount(ClinicalQueryPanel, {
      props: { id: "fieldX", query: queryOpen },
      global: { plugins: [pinia] },
    });
    expect(wrapperOpenMonitor.text()).toContain(
      "Status: Awaiting Site Response"
    );

    const wrapperAnsweredMonitor = mount(ClinicalQueryPanel, {
      props: { id: "fieldX", query: queryAnswered },
      global: { plugins: [pinia] },
    });
    expect(wrapperAnsweredMonitor.text()).toContain(
      "Status: Awaiting CRA Review"
    );

    authStore.isAuthenticated = false;
    authStore.rawRoles = [];
    const wrapperOpenFallback = mount(ClinicalQueryPanel, {
      props: { id: "fieldX", query: queryOpen },
      global: { plugins: [pinia] },
    });
    expect(wrapperOpenFallback.text()).toContain("Status: OPEN");
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

  it("attaches aria-describedby and aria-invalid dynamically and validates accessibility", async () => {
    const id = "systolic";
    const wrapper = mount(ClinicalInput, {
      props: {
        id,
        label: "Systolic BP",
        modelValue: "280",
        error: "Systolic Blood Pressure must be between 50 and 250 mmHg",
      },
    });

    const input = wrapper.find("input");
    expect(input.attributes("aria-describedby")).toBe(`validation-error-${id}`);
    expect(input.attributes("aria-invalid")).toBe("true");

    const errorMsg = wrapper.find(".validation-error-msg");
    expect(errorMsg.attributes("id")).toBe(`validation-error-${id}`);
    expect(errorMsg.attributes("role")).toBe("status");
    expect(errorMsg.attributes("aria-live")).toBe("polite");

    // Verify it passes accessibility audit
    await expect(wrapper).toBeAccessible();
  });

  it("does not attach aria-describedby or aria-invalid when there is no error", async () => {
    const wrapper = mount(ClinicalInput, {
      props: {
        id: "systolic",
        label: "Systolic BP",
        modelValue: "120",
        error: null,
      },
    });

    const input = wrapper.find("input");
    expect(input.attributes("aria-describedby")).toBeUndefined();
    expect(input.attributes("aria-invalid")).toBeUndefined();

    // Verify it passes accessibility audit
    await expect(wrapper).toBeAccessible();
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

  it("renders validation errors below choices and passes accessibility audit", async () => {
    const options = [
      { value: "M", label: "Male" },
      { value: "F", label: "Female" },
    ];
    const id = "gender";
    const wrapper = mount(ClinicalRadioGroup, {
      props: {
        id,
        label: "Gender Selection",
        options,
        modelValue: "",
        error: "Gender selection is required",
      },
    });

    expect(wrapper.classes()).toContain("has-error");
    const errorMsg = wrapper.find(".validation-error-msg");
    expect(errorMsg.exists()).toBe(true);
    expect(errorMsg.text()).toContain("Gender selection is required");
    expect(errorMsg.attributes("id")).toBe(`validation-error-${id}`);
    expect(errorMsg.attributes("role")).toBe("status");
    expect(errorMsg.attributes("aria-live")).toBe("polite");

    await expect(wrapper).toBeAccessible();
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

  it("satisfies exact markup and accessibility attribute contract parity with vanilla implementation", async () => {
    const states = ["none", "loading", "valid", "invalid", "degraded"];
    const testAttributes = {
      placeholder: "Search clinical codes...",
      disabled: "disabled",
      title: "Search concept dictionary",
    };

    for (const state of states) {
      const id = `concept-${state}`;
      const label = `Concept Code (${state})`;
      const value = "C99";
      const statusMessage =
        state === "none" ? "" : `Status: ${state} explanation`;

      // 1. Generate vanilla HTML
      const vanillaHtml = createClinicalLookupInput(
        id,
        label,
        value,
        state,
        statusMessage
      );
      const vanillaContainer = document.createElement("div");
      vanillaContainer.innerHTML = vanillaHtml.trim();
      const vanillaRoot = vanillaContainer.firstElementChild;

      // 2. Mount Vue component
      const wrapper = mount(ClinicalLookupInput, {
        props: {
          id,
          label,
          modelValue: value,
          status: state,
          statusMessage,
          attributes: testAttributes,
        },
      });
      const vueRoot = wrapper.element;

      // 3. Compare Outer Container Core Properties
      expect(vueRoot.id).toBe(vanillaRoot.id);
      expect(vueRoot.className).toContain("clinical-lookup-container");
      expect(vueRoot.className).toContain("clinical-input");
      expect(vueRoot.className).toContain("grid-span-12");

      // 4. Compare Label Element
      const vanillaLabel = vanillaRoot.querySelector("label");
      const vueLabel = vueRoot.querySelector("label");
      expect(vueLabel.getAttribute("for")).toBe(
        vanillaLabel.getAttribute("for")
      );
      expect(vueLabel.textContent.trim()).toBe(vanillaLabel.textContent.trim());

      // 5. Compare Input Element and its forwarded/interactive/ARIA attributes
      const vanillaInput = vanillaRoot.querySelector("input");
      const vueInput = vueRoot.querySelector("input");
      expect(vueInput.id).toBe(vanillaInput.id);
      expect(vueInput.getAttribute("type")).toBe(
        vanillaInput.getAttribute("type")
      );
      expect(vueInput.getAttribute("name")).toBe(
        vanillaInput.getAttribute("name")
      );
      expect(vueInput.value).toBe(vanillaInput.value);
      expect(vueInput.getAttribute("autocomplete")).toBe(
        vanillaInput.getAttribute("autocomplete")
      );

      // Interactive attributes forwarded to input
      expect(vueInput.getAttribute("placeholder")).toBe(
        testAttributes.placeholder
      );
      expect(vueInput.getAttribute("disabled")).toBe("");
      expect(vueInput.getAttribute("title")).toBe(testAttributes.title);

      // ARIA describedby and invalid state parity
      if (state !== "none") {
        expect(vueInput.getAttribute("aria-describedby")).toBe(
          vanillaInput.getAttribute("aria-describedby")
        );
      } else {
        expect(vueInput.hasAttribute("aria-describedby")).toBe(false);
      }

      if (state === "invalid") {
        expect(vueInput.getAttribute("aria-invalid")).toBe("true");
      } else {
        expect(vueInput.hasAttribute("aria-invalid")).toBe(false);
      }

      // 6. Compare Terminology Status Indicator Parity
      const vanillaIndicator = vanillaRoot.querySelector(
        ".lookup-status-indicator"
      );
      const vueIndicator = vueRoot.querySelector(".lookup-status-indicator");
      expect(vueIndicator.id).toBe(vanillaIndicator.id);
      expect(vueIndicator.getAttribute("role")).toBe(
        vanillaIndicator.getAttribute("role")
      );
      expect(vueIndicator.getAttribute("aria-live")).toBe(
        vanillaIndicator.getAttribute("aria-live")
      );

      // Check inline styles parity for none state hidden indicator
      if (state === "none") {
        expect(vueIndicator.style.display).toBe("none");
      } else {
        expect(vueIndicator.style.display).not.toBe("none");
        // State-specific styles
        const stateClass = `lookup-${state}`;
        expect(vueIndicator.className).toContain(stateClass);
        // Icon and text parity
        const vanillaIcon = vanillaIndicator
          .querySelector(".lookup-status-icon")
          .textContent.trim();
        const vueIcon = vueIndicator
          .querySelector(".lookup-status-icon")
          .textContent.trim();
        expect(vueIcon).toBe(vanillaIcon);

        const vanillaText = vanillaIndicator
          .querySelector(".lookup-status-text")
          .textContent.trim();
        const vueText = vueIndicator
          .querySelector(".lookup-status-text")
          .textContent.trim();
        expect(vueText).toBe(vanillaText);
      }
    }
  });

  it("combines lookup status and validation error in aria-describedby", async () => {
    const id = "conceptCode";
    const wrapper = mount(ClinicalLookupInput, {
      props: {
        id,
        label: "Concept Code",
        modelValue: "C123",
        status: "loading",
        error: "Invalid concept code range",
      },
    });

    const input = wrapper.find("input");
    const describedBy = input.attributes("aria-describedby");
    expect(describedBy).toContain(`lookup-status-${id}`);
    expect(describedBy).toContain(`validation-error-${id}`);
    expect(input.attributes("aria-invalid")).toBe("true");

    const errorMsg = wrapper.find(".validation-error-msg");
    expect(errorMsg.exists()).toBe(true);
    expect(errorMsg.attributes("role")).toBe("status");
    expect(errorMsg.attributes("aria-live")).toBe("polite");

    await expect(wrapper).toBeAccessible();
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
