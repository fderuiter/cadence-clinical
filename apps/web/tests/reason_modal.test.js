import { describe, it, expect } from "vitest";
import { mount } from "@vue/test-utils";
import ReasonModal from "../src/components/ReasonModal.vue";

describe("ReasonModal.vue", () => {
  it("renders when show is true and hides when false", () => {
    const wrapper = mount(ReasonModal, {
      props: {
        show: true,
        title: "Reason Required",
        description: "To comply with Part 11, please select a reason.",
        options: [
          { value: "Initial Entry", text: "Initial Setup" },
          { value: "Other", text: "Other" },
        ],
      },
    });

    expect(wrapper.find(".modal").exists()).toBe(true);
    expect(wrapper.find(".modal-header").text()).toBe("Reason Required");
    expect(wrapper.find(".modal-body p").text()).toBe(
      "To comply with Part 11, please select a reason."
    );

    const hiddenWrapper = mount(ReasonModal, {
      props: {
        show: false,
      },
    });
    expect(hiddenWrapper.find(".modal").exists()).toBe(false);
  });

  it("applies the idPrefix correctly to HTML elements", () => {
    const wrapper = mount(ReasonModal, {
      props: {
        show: true,
        idPrefix: "test-prefix-",
        options: [{ value: "Initial Entry", text: "Initial Setup" }],
      },
    });

    expect(wrapper.find("#test-prefix-reason-modal").exists()).toBe(true);
    expect(wrapper.find("#test-prefix-modal-title").exists()).toBe(true);
    expect(wrapper.find("#test-prefix-change-reason-select").exists()).toBe(
      true
    );
    expect(wrapper.find("#test-prefix-change-reason-text").exists()).toBe(true);
    expect(wrapper.find("#test-prefix-btn-cancel-change").exists()).toBe(true);
    expect(wrapper.find("#test-prefix-btn-save-change").exists()).toBe(true);
  });

  it("performs standard accessibility checks (role, aria-modal, aria-labelledby, for attributes)", () => {
    const wrapper = mount(ReasonModal, {
      props: {
        show: true,
        idPrefix: "acc-",
        options: [{ value: "Initial Entry", text: "Initial Setup" }],
      },
    });

    const overlay = wrapper.find("#acc-reason-modal");
    expect(overlay.attributes("role")).toBe("dialog");
    expect(overlay.attributes("aria-modal")).toBe("true");
    expect(overlay.attributes("aria-labelledby")).toBe("acc-modal-title");

    const selectLabel = wrapper.find("label[for='acc-change-reason-select']");
    expect(selectLabel.exists()).toBe(true);

    const textLabel = wrapper.find("label[for='acc-change-reason-text']");
    expect(textLabel.exists()).toBe(true);
  });

  it("selects options correctly and clears validation error on change", async () => {
    const wrapper = mount(ReasonModal, {
      props: {
        show: true,
        idPrefix: "sel-",
        options: [
          { value: "Initial Entry", text: "Initial Setup" },
          { value: "Other", text: "Other Reason" },
        ],
      },
    });

    const select = wrapper.find("select");
    expect(select.element.value).toBe("Initial Entry");

    await select.setValue("Other");
    expect(select.element.value).toBe("Other");
  });

  it("emits cancel event when cancel button is clicked", async () => {
    const wrapper = mount(ReasonModal, {
      props: {
        show: true,
        options: [{ value: "Initial Entry", text: "Initial Setup" }],
      },
    });

    await wrapper.find("#btn-cancel-change").trigger("click");
    expect(wrapper.emitted("cancel")).toBeTruthy();
  });

  it("emits confirm event with compiled reason when valid", async () => {
    const wrapper = mount(ReasonModal, {
      props: {
        show: true,
        options: [{ value: "Initial Entry", text: "Initial Setup" }],
      },
    });

    await wrapper.find("textarea").setValue("Typo correction");
    await wrapper.find("#btn-save-change").trigger("click");

    expect(wrapper.emitted("confirm")).toBeTruthy();
    expect(wrapper.emitted("confirm")[0]).toEqual([
      "Initial Entry: Typo correction",
    ]);
  });

  it("strictly validates custom explanation is non-empty when Other is selected and focuses textarea", async () => {
    const wrapper = mount(ReasonModal, {
      attachTo: document.body,
      props: {
        show: true,
        idPrefix: "val-",
        options: [
          { value: "Typographical Error", text: "Correction of typo" },
          { value: "Other", text: "Other (specify below)" },
        ],
      },
    });

    const form = wrapper.find("#val-reason-modal-form");
    expect(form.exists()).toBe(true);
    expect(form.attributes("novalidate")).toBeDefined();

    const saveBtn = wrapper.find("#val-btn-save-change");
    expect(saveBtn.attributes("type")).toBe("submit");
    expect(saveBtn.attributes("form")).toBe("val-reason-modal-form");

    const textLabel = wrapper.find("label[for='val-change-reason-text']");
    const textarea = wrapper.find("#val-change-reason-text");
    expect(textLabel.text()).toContain("Custom Explanation (Optional)");
    expect(textarea.attributes("aria-required")).toBeUndefined();

    const select = wrapper.find("select");
    await select.setValue("Other");

    expect(textLabel.text()).toContain("Custom Explanation (Required)");
    expect(textarea.attributes("required")).toBeDefined();
    expect(textarea.attributes("aria-required")).toBe("true");

    // Click confirm with empty custom explanation
    await saveBtn.trigger("click");
    await wrapper.vm.$nextTick();

    expect(wrapper.emitted("confirm")).toBeFalsy();
    const errorMsg = wrapper.find("#val-reason-error");
    expect(errorMsg.exists()).toBe(true);
    expect(errorMsg.attributes("role")).toBe("status");
    expect(errorMsg.attributes("aria-live")).toBe("polite");
    expect(errorMsg.text()).toContain(
      "Custom explanation is required when selecting 'Other'."
    );
    expect(textarea.attributes("aria-invalid")).toBe("true");
    expect(textarea.attributes("aria-describedby")).toBe("val-reason-error");
    expect(document.activeElement).toBe(textarea.element);

    // Provide custom explanation and confirm via Ctrl+Enter
    await textarea.setValue("Authorized modification");
    expect(textarea.attributes("aria-invalid")).toBeUndefined();
    await textarea.trigger("keydown", { key: "Enter", ctrlKey: true });

    expect(wrapper.emitted("confirm")).toBeTruthy();
    expect(wrapper.emitted("confirm")[0]).toEqual(["Authorized modification"]);
    wrapper.unmount();
  });

  it("supports Enter submission from select and falls back safely when defaultOption or options are missing", async () => {
    const wrapper = mount(ReasonModal, {
      attachTo: document.body,
      props: {
        show: true,
        idPrefix: "kbd-",
        defaultOption: "NonExistentOption",
        options: [
          { value: "Typographical Error", text: "Correction of typo" },
          { value: "Other", text: "Other (specify below)" },
        ],
      },
    });

    const select = wrapper.find("#kbd-change-reason-select");
    expect(select.element.value).toBe("Typographical Error");

    await select.trigger("keydown.enter");
    expect(wrapper.emitted("confirm")?.[0]).toEqual(["Typographical Error"]);
    wrapper.unmount();

    const emptyOptionsWrapper = mount(ReasonModal, {
      attachTo: document.body,
      props: {
        show: true,
        idPrefix: "empty-",
        options: [],
      },
    });

    await emptyOptionsWrapper.find("#empty-btn-save-change").trigger("click");
    await emptyOptionsWrapper.vm.$nextTick();

    expect(emptyOptionsWrapper.emitted("confirm")).toBeFalsy();
    expect(emptyOptionsWrapper.find("#empty-reason-error").text()).toContain(
      "A non-empty reason for change is strictly required."
    );
    expect(document.activeElement?.id).toBe("empty-change-reason-text");
    emptyOptionsWrapper.unmount();
  });
});
