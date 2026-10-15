/**
 * Component tests for ReconsentChannelPicker.vue, the re-consent notification channel checkboxes.
 */

import { describe, it, expect } from "vitest";
import { mount } from "@vue/test-utils";
import ReconsentChannelPicker from "../../src/components/econsent/ReconsentChannelPicker.vue";

describe("ReconsentChannelPicker", () => {
  it("checks the channels in the model and emits the new selection", async () => {
    const wrapper = mount(ReconsentChannelPicker, {
      props: {
        modelValue: ["SMS"],
        "onUpdate:modelValue": (value: string[]) =>
          wrapper.setProps({ modelValue: value }),
      },
    });

    const boxes = wrapper.findAll<HTMLInputElement>("input[type=checkbox]");
    expect(boxes.map((b) => b.element.checked)).toEqual([true, false, false]);

    await boxes[2].setValue(true);

    expect(wrapper.emitted("update:modelValue")?.at(-1)).toEqual([
      ["SMS", "Email"],
    ]);
  });

  it("renders custom channel labels", () => {
    const wrapper = mount(ReconsentChannelPicker, {
      props: {
        modelValue: [],
        channels: [{ value: "SMS", label: "SMS Alerts" }],
      },
    });

    expect(wrapper.text()).toContain("SMS Alerts");
  });
});
