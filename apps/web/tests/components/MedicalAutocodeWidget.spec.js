import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount } from "@vue/test-utils";
import MedicalAutocodeWidget from "../../src/components/crf/widgets/MedicalAutocodeWidget.vue";

describe("MedicalAutocodeWidget.vue", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it("renders correctly with label and input", () => {
    const wrapper = mount(MedicalAutocodeWidget, {
      props: {
        label: "Adverse Event",
        modelValue: "Headache",
        required: true,
      },
    });

    expect(wrapper.find(".widget-label").text()).toContain("Adverse Event");
    expect(wrapper.find(".required-star").exists()).toBe(true);
    expect(wrapper.find("input").element.value).toBe("Headache");
  });

  it("triggers debounced API call on input and displays suggestions", async () => {
    const mockSuggestions = [
      {
        name: "Headache",
        code: "10019211",
        pt: "HEADACHE",
        soc: "Nervous system disorders",
        dictionary: "MedDRA",
        confidence: 1.0,
      },
    ];

    global.fetch = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => mockSuggestions,
    });

    const wrapper = mount(MedicalAutocodeWidget, {
      props: {
        modelValue: "",
        dictionary: "MedDRA",
      },
    });

    const input = wrapper.find("input");
    await input.setValue("Headache");

    // Advance 250ms debounce
    await vi.advanceTimersByTimeAsync(250);
    await wrapper.vm.$nextTick();

    expect(global.fetch).toHaveBeenCalledWith(
      expect.stringContaining("/api/v1/execution/coding/autosuggest?query=Headache&dictionary=MedDRA")
    );
  });

  it("emits code-selected when a suggestion is clicked", async () => {
    const wrapper = mount(MedicalAutocodeWidget, {
      props: {
        modelValue: "Headache",
      },
    });

    const term = {
      name: "Headache",
      code: "10019211",
      pt: "HEADACHE",
      soc: "Nervous system disorders",
      dictionary: "MedDRA",
    };

    await wrapper.vm.selectTerm(term);

    expect(wrapper.emitted("update:modelValue")[0]).toEqual(["Headache"]);
    expect(wrapper.emitted("code-selected")[0]).toEqual([term]);
  });
});
