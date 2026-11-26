import { describe, it, expect } from "vitest";
import { mount } from "@vue/test-utils";
import { EmptyState } from "ui";

describe("EmptyState Component", () => {
  it("renders with title and message props", () => {
    const wrapper = mount(EmptyState, {
      props: {
        icon: "stethoscope",
        title: "No Data Found",
        message: "Try adjusting your filters.",
      },
    });

    const svg = wrapper.find("svg.clinical-icon");
    expect(svg.exists()).toBe(true);
    expect(svg.attributes("stroke-width")).toBe("1.75");
    expect(wrapper.text()).toContain("No Data Found");
    expect(wrapper.text()).toContain("Try adjusting your filters.");
    expect(wrapper.attributes("role")).toBe("region");
  });

  it("renders action button and emits action event when clicked", async () => {
    const wrapper = mount(EmptyState, {
      props: {
        title: "No Items",
        actionText: "Create Item",
      },
    });

    const button = wrapper.find(".empty-state-btn");
    expect(button.exists()).toBe(true);
    expect(button.text()).toBe("Create Item");

    await button.trigger("click");
    expect(wrapper.emitted("action")).toBeTruthy();
    expect(wrapper.emitted("action").length).toBe(1);
  });

  it("renders custom action slot content", () => {
    const wrapper = mount(EmptyState, {
      props: {
        title: "Empty Folder",
      },
      slots: {
        action: '<button class="custom-btn">Custom Upload</button>',
      },
    });

    const customBtn = wrapper.find(".custom-btn");
    expect(customBtn.exists()).toBe(true);
    expect(customBtn.text()).toBe("Custom Upload");
  });
});
