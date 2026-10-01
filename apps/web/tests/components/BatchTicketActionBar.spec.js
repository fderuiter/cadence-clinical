import { describe, it, expect } from "vitest";
import { mount } from "@vue/test-utils";
import BatchTicketActionBar from "../../src/components/tickets/BatchTicketActionBar.vue";

describe("BatchTicketActionBar.vue", () => {
  it("does not render when selectedCount is 0", () => {
    const wrapper = mount(BatchTicketActionBar, {
      props: { selectedCount: 0 },
    });
    expect(wrapper.find("#batch-ticket-action-bar").exists()).toBe(false);
  });

  it("renders selection count and action buttons when selectedCount > 0", () => {
    const wrapper = mount(BatchTicketActionBar, {
      props: { selectedCount: 3 },
    });

    expect(wrapper.find("#batch-ticket-action-bar").exists()).toBe(true);
    expect(wrapper.find(".selection-badge").text()).toBe("3");
    expect(wrapper.find(".selection-text").text()).toContain("3 issues selected");

    expect(wrapper.find(".btn-action-resolve").exists()).toBe(true);
    expect(wrapper.find(".btn-action-escalate").exists()).toBe(true);
    expect(wrapper.find(".btn-action-pause-sla").exists()).toBe(true);
    expect(wrapper.find(".btn-action-clear").exists()).toBe(true);
  });

  it("emits resolve, escalate, pause-sla, and clear events when buttons are clicked", async () => {
    const wrapper = mount(BatchTicketActionBar, {
      props: { selectedCount: 2 },
    });

    await wrapper.find(".btn-action-resolve").trigger("click");
    expect(wrapper.emitted("resolve")).toBeTruthy();

    await wrapper.find(".btn-action-escalate").trigger("click");
    expect(wrapper.emitted("escalate")).toBeTruthy();

    await wrapper.find(".btn-action-pause-sla").trigger("click");
    expect(wrapper.emitted("pause-sla")).toBeTruthy();

    await wrapper.find(".btn-action-clear").trigger("click");
    expect(wrapper.emitted("clear")).toBeTruthy();
  });
});
