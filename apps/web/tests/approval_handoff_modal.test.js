import { describe, it, expect, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import ApprovalHandoffModal from "../src/components/crf/ApprovalHandoffModal.vue";

describe("ApprovalHandoffModal", () => {
  let pinia;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
  });

  it("does not render when isOpen is false", () => {
    const wrapper = mount(ApprovalHandoffModal, {
      props: {
        isOpen: false,
        unresolvedCount: 0,
        editChecksVerified: true,
      },
      global: {
        plugins: [pinia],
      },
    });

    expect(wrapper.find("#approval-handoff-modal").exists()).toBe(false);
  });

  it("renders when isOpen is true", () => {
    const wrapper = mount(ApprovalHandoffModal, {
      props: {
        isOpen: true,
        unresolvedCount: 0,
        editChecksVerified: true,
      },
      global: {
        plugins: [pinia],
      },
    });

    expect(wrapper.find("#approval-handoff-modal").exists()).toBe(true);
    expect(wrapper.find("#approval-role").exists()).toBe(true);
    expect(wrapper.find("#approval-password").exists()).toBe(true);
    expect(wrapper.find("#approval-reason").exists()).toBe(true);
    expect(wrapper.find("#btn-cancel-approval").exists()).toBe(true);
    expect(wrapper.find("#btn-confirm-approval").exists()).toBe(true);
  });

  it("disables the confirm button when the pre-approval checklist is not complete", () => {
    const wrapper = mount(ApprovalHandoffModal, {
      props: {
        isOpen: true,
        unresolvedCount: 2,
        editChecksVerified: false,
      },
      global: {
        plugins: [pinia],
      },
    });

    const confirmBtn = wrapper.find("#btn-confirm-approval");
    expect(confirmBtn.element.disabled).toBe(true);
  });

  it("enables the confirm button when the pre-approval checklist is complete", () => {
    const wrapper = mount(ApprovalHandoffModal, {
      props: {
        isOpen: true,
        unresolvedCount: 0,
        editChecksVerified: true,
      },
      global: {
        plugins: [pinia],
      },
    });

    const confirmBtn = wrapper.find("#btn-confirm-approval");
    expect(confirmBtn.element.disabled).toBe(false);
  });

  it("emits cancel when cancel button is clicked", async () => {
    const wrapper = mount(ApprovalHandoffModal, {
      props: {
        isOpen: true,
        unresolvedCount: 0,
        editChecksVerified: true,
      },
      global: {
        plugins: [pinia],
      },
    });

    await wrapper.find("#btn-cancel-approval").trigger("click");
    expect(wrapper.emitted("cancel")).toBeTruthy();
  });

  it("emits approve with role, password, and reason on success", async () => {
    const wrapper = mount(ApprovalHandoffModal, {
      props: {
        isOpen: true,
        unresolvedCount: 0,
        editChecksVerified: true,
      },
      global: {
        plugins: [pinia],
      },
    });

    await wrapper.find("#approval-role").setValue("Lead Data Manager");
    await wrapper.find("#approval-password").setValue("mySecretPassword"); // pragma: allowlist secret
    await wrapper.find("#approval-reason").setValue("Protocol amendment complete");

    await wrapper.find("#btn-confirm-approval").trigger("click");

    expect(wrapper.emitted("approve")).toBeTruthy();
    expect(wrapper.emitted("approve")[0][0]).toEqual({
      role: "Lead Data Manager",
      password: "mySecretPassword", // pragma: allowlist secret
      reason: "Protocol amendment complete",
    });

    // Password must be wiped
    expect(wrapper.vm.password).toBe("");
  });

  it("passes WCAG 2.1 AA accessibility audit with zero violations", async () => {
    const wrapper = mount(ApprovalHandoffModal, {
      props: {
        isOpen: true,
        unresolvedCount: 0,
        editChecksVerified: true,
      },
      global: {
        plugins: [pinia],
      },
    });

    await expect(wrapper).toBeAccessible();
  });
});
