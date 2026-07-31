import { describe, it, expect } from "vitest";
import { mount } from "@vue/test-utils";
import TrivialComponent from "../../src/components/TrivialComponent.vue";

describe("TrivialComponent.vue Smoke Test", () => {
  it("renders the default message", () => {
    const wrapper = mount(TrivialComponent);
    expect(wrapper.text()).toContain("Hello Cadence!");
  });
});
