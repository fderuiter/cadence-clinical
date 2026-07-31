import { describe, it, expect } from "vitest";
import { mount } from "@vue/test-utils";
import ReasonForChangeModal from "../../src/components/ReasonForChangeModal.vue";

describe("ReasonForChangeModal.vue - Wrapper Component", () => {
  it("renders when show is true and passes properties to ReasonModal", () => {
    const wrapper = mount(ReasonForChangeModal, {
      props: {
        show: true,
        title: "Test Reason Title",
        description: "Test description for Part 11 requirements",
        idPrefix: "wrap-",
      },
    });

    expect(wrapper.find(".modal-header").text()).toBe("Test Reason Title");
    expect(wrapper.find(".modal-body p").text()).toBe(
      "Test description for Part 11 requirements"
    );
  });

  it("bubbles cancel and confirm events correctly", async () => {
    const wrapper = mount(ReasonForChangeModal, {
      props: {
        show: true,
        idPrefix: "evt-",
      },
    });

    // Verify cancel event propagation
    await wrapper.find("#evt-btn-cancel-change").trigger("click");
    expect(wrapper.emitted("cancel")).toBeTruthy();

    // Verify confirm event propagation
    await wrapper
      .find("textarea")
      .setValue("Adding custom verification details");
    await wrapper.find("#evt-btn-save-change").trigger("click");
    expect(wrapper.emitted("confirm")).toBeTruthy();
    expect(wrapper.emitted("confirm")[0]).toEqual([
      "Initial Entry: Adding custom verification details",
    ]);
  });
});
