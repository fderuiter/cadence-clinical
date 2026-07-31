import { describe, it, expect } from "vitest";
import { mount } from "@vue/test-utils";
import { defineComponent } from "vue";

const DummyComponent = defineComponent({
  template: "<div>Foundational Vue SPA Scaffold Active</div>",
});

describe("Trivial Component Smoke Test", () => {
  it("mounts a basic component in the JSDOM environment", () => {
    const wrapper = mount(DummyComponent);
    expect(wrapper.text()).toBe("Foundational Vue SPA Scaffold Active");
  });
});
