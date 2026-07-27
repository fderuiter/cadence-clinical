import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import EcrfView from "../src/views/EcrfView.vue";
import MdrView from "../src/views/MdrView.vue";
import { terminologyClient } from "../src/api/terminologyClient.js";
import { validateField } from "../index.js";

// Mock the terminology client
vi.mock("../src/api/terminologyClient.js", () => {
  return {
    terminologyClient: {
      validateSingleCode: vi.fn(),
      searchTerminology: vi.fn(),
    },
  };
});

describe("Controlled Terminology (CT) Live Validation Unit & UI Tests", () => {
  beforeEach(() => {
    const pinia = createPinia();
    setActivePinia(pinia);
    vi.resetAllMocks();
    vi.useFakeTimers();
  });

  describe("eCRF Concept Code Lookup & Race Conditions", () => {
    it("renders the concept_code field with concept_code input type and lookup container", () => {
      const wrapper = mount(EcrfView);
      const container = wrapper.find("#field-container-concept_code");
      expect(container.exists()).toBe(true);
      expect(container.classes()).toContain("clinical-lookup-container");

      const input = wrapper.find("input#concept_code");
      expect(input.exists()).toBe(true);
    });

    it("triggers debounced asynchronous validation as the user types", async () => {
      terminologyClient.validateSingleCode.mockResolvedValue({
        concept_code: "C4872",
        state: "VALID",
        decode: "Adverse Event",
        system: "NCI_Thesaurus",
      });

      const wrapper = mount(EcrfView);
      const input = wrapper.find("input#concept_code");

      // Set input value and trigger input event
      await input.setValue("C4872");

      // Initially, it shouldn't have been called (due to debounce)
      expect(terminologyClient.validateSingleCode).not.toHaveBeenCalled();

      // Fast-forward debounce timer (300ms)
      await vi.advanceTimersByTimeAsync(300);

      expect(terminologyClient.validateSingleCode).toHaveBeenCalledTimes(1);
      expect(terminologyClient.validateSingleCode).toHaveBeenCalledWith(
        "C4872",
        expect.any(Object)
      );

      // Check visual success feedback in DOM
      const indicator = wrapper.find("#lookup-status-concept_code");
      expect(indicator.exists()).toBe(true);
      expect(indicator.classes()).toContain("lookup-valid");
      expect(indicator.text()).toContain('Code is valid: "Adverse Event"');
    });

    it("displays correct visual state for INVALID codes", async () => {
      terminologyClient.validateSingleCode.mockResolvedValue({
        concept_code: "INVALID_CODE",
        state: "INVALID",
        error_message:
          'Invalid code "INVALID_CODE". Not found in NCI Thesaurus.',
      });

      const wrapper = mount(EcrfView);
      const input = wrapper.find("input#concept_code");

      await input.setValue("INVALID_CODE");
      await vi.advanceTimersByTimeAsync(300);

      const indicator = wrapper.find("#lookup-status-concept_code");
      expect(indicator.exists()).toBe(true);
      expect(indicator.classes()).toContain("lookup-invalid");
      expect(indicator.text()).toContain("Invalid code");
    });

    it("displays correct visual state for DEGRADED/offline terminology service", async () => {
      terminologyClient.validateSingleCode.mockResolvedValue({
        concept_code: "C123",
        state: "DEGRADED",
        error_message: "Terminology service degraded. Validation offline.",
      });

      const wrapper = mount(EcrfView);
      const input = wrapper.find("input#concept_code");

      await input.setValue("C123");
      await vi.advanceTimersByTimeAsync(300);

      const indicator = wrapper.find("#lookup-status-concept_code");
      expect(indicator.exists()).toBe(true);
      expect(indicator.classes()).toContain("lookup-degraded");
      expect(indicator.text()).toContain("service degraded");
    });

    it("discards stale asynchronous responses (race conditions guard)", async () => {
      let resolveFirst, resolveSecond;
      const firstPromise = new Promise((resolve) => {
        resolveFirst = resolve;
      });
      const secondPromise = new Promise((resolve) => {
        resolveSecond = resolve;
      });

      terminologyClient.validateSingleCode
        .mockReturnValueOnce(firstPromise)
        .mockReturnValueOnce(secondPromise);

      const wrapper = mount(EcrfView);
      const input = wrapper.find("input#concept_code");

      // Type first value
      await input.setValue("C1");
      await vi.advanceTimersByTimeAsync(300);

      // Type second value (before first response resolves)
      await input.setValue("C12");
      await vi.advanceTimersByTimeAsync(300);

      expect(terminologyClient.validateSingleCode).toHaveBeenCalledTimes(2);

      // Resolve second response first (e.g. faster network response for second call)
      resolveSecond({
        concept_code: "C12",
        state: "VALID",
        decode: "Faster Response",
        system: "NCI_Thesaurus",
      });
      await vi.runAllTimersAsync();

      let indicator = wrapper.find("#lookup-status-concept_code");
      expect(indicator.text()).toContain("Faster Response");

      // Resolve first response now (which is slower)
      resolveFirst({
        concept_code: "C1",
        state: "VALID",
        decode: "Slower Stale Response",
        system: "NCI_Thesaurus",
      });
      await vi.runAllTimersAsync();

      // Indicator must STILL show "Faster Response" and MUST NOT have been overwritten by stale response!
      indicator = wrapper.find("#lookup-status-concept_code");
      expect(indicator.text()).toContain("Faster Response");
      expect(indicator.text()).not.toContain("Slower Stale Response");
    });
  });

  describe("MDR/Protocol Visualizer Lookups & Autocomplete", () => {
    it("renders concept code inputs for Arm Type and Visit Type in builder mode", async () => {
      const wrapper = mount(MdrView);

      // Open builder mode
      const toggleBtn = wrapper.find("button.btn");
      await toggleBtn.trigger("click");

      expect(wrapper.find("input#new-arm-concept").exists()).toBe(true);
      expect(wrapper.find("input#new-enc-concept").exists()).toBe(true);
    });

    it("triggers debounced search/autocomplete on typing in Arm Type concept code field", async () => {
      terminologyClient.searchTerminology.mockResolvedValue({
        results: [
          { concept_code: "C123", preferred_name: "Active Arm Concept" },
        ],
      });

      const wrapper = mount(MdrView);
      const toggleBtn = wrapper.find("button.btn");
      await toggleBtn.trigger("click");

      const armInput = wrapper.find("input#new-arm-concept");
      await armInput.setValue("Act");

      // Debounce search
      await vi.advanceTimersByTimeAsync(300);

      expect(terminologyClient.searchTerminology).toHaveBeenCalledTimes(1);

      // Verify dropdown is visible and shows the suggestion
      const dropdown = wrapper.find(".autocomplete-dropdown");
      expect(dropdown.exists()).toBe(true);
      expect(dropdown.text()).toContain("C123");
      expect(dropdown.text()).toContain("Active Arm Concept");
    });
  });

  describe("Synchronous Validation Behavior", () => {
    it("preserves correct validateField synchronous behavior for unrelated fields", () => {
      const numericField = {
        id: "vssbp",
        label: "Systolic BP",
        validation: {
          min: 50,
          max: 250,
          message: "Must be between 50 and 250",
        },
      };

      // Valid value
      const res1 = validateField(numericField, "120");
      expect(res1).toEqual({ valid: true });

      // Out of range value
      const res2 = validateField(numericField, "300");
      expect(res2.valid).toBe(false);
      expect(res2.message).toBe("Must be between 50 and 250");

      // Non-numeric value
      const res3 = validateField(numericField, "not-a-number");
      expect(res3.valid).toBe(false);
      expect(res3.message).toBe("Value must be a number.");
    });
  });
});
