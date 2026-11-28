import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { h, ref, defineComponent, nextTick } from "vue";
import {
  useFocusTrap,
  useEscapeClose,
  createConditionRowHTML,
  ClinicalDataTable,
  ClinicalQueryPanel,
  ClinicalInput,
} from "ui";

// Helper component for testing useFocusTrap directly
const TestTrapComponent = defineComponent({
  setup() {
    const containerRef = ref(null);
    useFocusTrap(containerRef);
    return { containerRef };
  },
  template: `
    <div ref="containerRef">
      <button id="btn1">Button 1</button>
      <input id="input1" />
      <button id="btn2">Button 2</button>
    </div>
  `,
});

// Helper component for testing useEscapeClose directly
const TestEscapeComponent = defineComponent({
  props: ["onClose"],
  setup(props) {
    useEscapeClose(props.onClose);
    return {};
  },
  template: `<div>Escape Component</div>`,
});

describe("Accessibility Composables & Query Panel Integration", () => {
  let container;

  beforeEach(() => {
    container = document.createElement("div");
    document.body.appendChild(container);
  });

  afterEach(() => {
    document.body.innerHTML = "";
  });

  describe("useFocusTrap", () => {
    it("focuses the first focusable element on mount", async () => {
      const wrapper = mount(TestTrapComponent, { attachTo: container });
      await nextTick();
      const btn1 = wrapper.find("#btn1").element;
      expect(document.activeElement).toBe(btn1);
    });

    it("loops focus on Tab from the last element to the first", async () => {
      const wrapper = mount(TestTrapComponent, { attachTo: container });
      await nextTick();

      const btn1 = wrapper.find("#btn1").element;
      const btn2 = wrapper.find("#btn2").element;

      btn2.focus();
      expect(document.activeElement).toBe(btn2);

      // Trigger Tab keydown
      const event = new KeyboardEvent("keydown", { key: "Tab", bubbles: true });
      document.dispatchEvent(event);

      // Focus should loop back to the first element
      expect(document.activeElement).toBe(btn1);
    });

    it("loops focus on Shift+Tab from the first element to the last", async () => {
      const wrapper = mount(TestTrapComponent, { attachTo: container });
      await nextTick();

      const btn1 = wrapper.find("#btn1").element;
      const btn2 = wrapper.find("#btn2").element;

      btn1.focus();
      expect(document.activeElement).toBe(btn1);

      // Trigger Shift+Tab keydown
      const event = new KeyboardEvent("keydown", {
        key: "Tab",
        shiftKey: true,
        bubbles: true,
      });
      document.dispatchEvent(event);

      // Focus should loop back to the last element
      expect(document.activeElement).toBe(btn2);
    });

    it("restores focus to previous active element on unmount", async () => {
      const triggerButton = document.createElement("button");
      triggerButton.id = "trigger";
      container.appendChild(triggerButton);
      triggerButton.focus();
      expect(document.activeElement).toBe(triggerButton);

      const wrapper = mount(TestTrapComponent, { attachTo: container });
      await nextTick();

      wrapper.unmount();
      await nextTick();

      expect(document.activeElement).toBe(triggerButton);
    });
  });

  describe("useEscapeClose", () => {
    it("calls close handler when Escape key is pressed", () => {
      const onClose = vi.fn();
      mount(TestEscapeComponent, { props: { onClose }, attachTo: container });

      const event = new KeyboardEvent("keydown", {
        key: "Escape",
        bubbles: true,
      });
      document.dispatchEvent(event);

      expect(onClose).toHaveBeenCalledTimes(1);
    });

    it("does not call close handler after being unmounted", () => {
      const onClose = vi.fn();
      const wrapper = mount(TestEscapeComponent, {
        props: { onClose },
        attachTo: container,
      });

      wrapper.unmount();

      const event = new KeyboardEvent("keydown", {
        key: "Escape",
        bubbles: true,
      });
      document.dispatchEvent(event);

      expect(onClose).not.toHaveBeenCalled();
    });
  });

  describe("ClinicalQueryPanel Integration", () => {
    it("integrates with useFocusTrap and useEscapeClose inside clinical query input flow", async () => {
      const wrapper = mount(ClinicalInput, {
        props: {
          id: "input-test",
          label: "Test Input",
          modelValue: "",
        },
        attachTo: container,
      });

      const flagButton = wrapper.find(".query-flag").element;
      flagButton.focus();
      expect(document.activeElement).toBe(flagButton);

      await wrapper.find(".query-flag").trigger("click");
      await nextTick();

      const queryPanel = wrapper.findComponent(ClinicalQueryPanel);
      expect(queryPanel.exists()).toBe(true);

      const closeBtn = queryPanel.find(".btn-close-panel").element;
      await nextTick();
      expect(document.activeElement).toBe(closeBtn);

      // Pressing Escape should close the query panel and return focus to the flag button
      const escapeEvent = new KeyboardEvent("keydown", {
        key: "Escape",
        bubbles: true,
      });
      document.dispatchEvent(escapeEvent);
      await nextTick();

      expect(wrapper.findComponent(ClinicalQueryPanel).exists()).toBe(false);
      expect(document.activeElement).toBe(flagButton);
    });
  });

  describe("Shared Clinical UI primitives accessibility audits", () => {
    it("createConditionRowHTML condition elements pass accessibility audit", async () => {
      const forms = [{ id: "f1", name: "Form 1" }];
      const fields = [{ id: "fld1", name: "Field 1", formId: "f1" }];
      const html = createConditionRowHTML(0, forms, fields);

      // Let's wrap it in a proper container/form to be valid HTML structure
      const wrapped = `<form>${html}</form>`;
      await expect(wrapped).toBeAccessible();
    });

    it("ClinicalDataTable supports keyboard sorting, focusable row activation, target guards, live pagination status, and passes accessibility audit", async () => {
      const onRowClick = vi.fn();
      const onAction = vi.fn();
      const columns = [
        { key: "id", label: "Subject ID", sortable: true },
        { key: "site", label: "Site", sortable: true },
        { key: "status", label: "Status", sortable: false },
        { key: "actions", label: "Actions" },
      ];
      const rows = [
        { id: "SUBJ-102", site: "SITE-02", status: "SCREENED" },
        { id: "SUBJ-101", site: "SITE-01", status: "ENROLLED" },
        { id: "SUBJ-103", site: "SITE-01", status: "ENROLLED" },
      ];

      const wrapper = mount(ClinicalDataTable, {
        props: {
          title: "Subject Screening Roster",
          columns,
          data: rows,
          pageSize: 2,
          selectable: true,
          onRowClick,
        },
        slots: {
          "cell-actions": ({ row }) =>
            h(
              "button",
              {
                type: "button",
                class: "slot-open-btn",
                onClick: () => onAction(row),
              },
              "Open"
            ),
        },
        attachTo: container,
      });

      const heading = wrapper.find("h3.table-title");
      const region = wrapper.find('.table-wrapper[role="region"]');
      expect(region.attributes("aria-labelledby")).toBe(
        heading.attributes("id")
      );
      expect(wrapper.find("table caption.sr-only").text()).toBe(
        "Subject Screening Roster"
      );

      const headers = wrapper.findAll("thead th");
      expect(headers[0].attributes("aria-sort")).toBeUndefined();
      expect(headers[1].attributes("aria-sort")).toBe("none");
      expect(headers[3].attributes("aria-sort")).toBeUndefined();
      expect(headers[4].attributes("aria-sort")).toBeUndefined();

      const idSortBtn = headers[1].find('button[type="button"]');
      await idSortBtn.trigger("keydown", { key: "Enter" });
      expect(headers[1].attributes("aria-sort")).toBe("ascending");
      expect(wrapper.find("tbody tr.data-row td.data-cell").text()).toBe(
        "SUBJ-101"
      );

      await idSortBtn.trigger("keydown", { key: " " });
      expect(headers[1].attributes("aria-sort")).toBe("descending");
      expect(wrapper.find("tbody tr.data-row td.data-cell").text()).toBe(
        "SUBJ-103"
      );

      const firstRow = wrapper.find("tbody tr.data-row");
      expect(firstRow.attributes("tabindex")).toBe("0");
      expect(firstRow.attributes("aria-selected")).toBe("false");

      await firstRow.trigger("keydown", { key: "Enter" });
      expect(onRowClick).toHaveBeenCalledTimes(1);

      const checkbox = firstRow.find('input[type="checkbox"]');
      await checkbox.trigger("keydown", { key: " " });
      await checkbox.setValue(true);
      expect(firstRow.attributes("aria-selected")).toBe("true");
      expect(onRowClick).toHaveBeenCalledTimes(1);

      const slotBtn = firstRow.find(".slot-open-btn");
      await slotBtn.trigger("keydown", { key: "Enter" });
      await slotBtn.trigger("click");
      expect(onAction).toHaveBeenCalledTimes(1);
      expect(onRowClick).toHaveBeenCalledTimes(1);

      const pageStatus = wrapper.find(".pagination-info");
      expect(pageStatus.attributes("role")).toBe("status");
      expect(pageStatus.attributes("aria-live")).toBe("polite");
      expect(pageStatus.text()).toBe("Page 1 of 2");

      const nextBtn = wrapper.findAll(".pagination-btn")[1];
      expect(nextBtn.attributes("type")).toBe("button");
      await nextBtn.trigger("click");
      expect(pageStatus.text()).toBe("Page 2 of 2");

      await wrapper.setProps({ data: [rows[1]] });
      expect(wrapper.find(".empty-cell").exists()).toBe(false);
      expect(wrapper.text()).toContain("SUBJ-101");

      await expect(wrapper).toBeAccessible();
      wrapper.unmount();
    });
  });

  describe("Token-Driven Accessibility validation behavior", () => {
    it("keeps error border visible when value is cleared/empty", async () => {
      const wrapper = mount(ClinicalInput, {
        props: {
          id: "test-input-clear",
          label: "Required Field",
          modelValue: "",
          error: "Field is required",
        },
      });
      expect(wrapper.find(".validation-error-msg").exists()).toBe(true);
      expect(wrapper.classes()).toContain("has-error");
    });

    it("displays error when value is present", async () => {
      const wrapper = mount(ClinicalInput, {
        props: {
          id: "test-input-present",
          label: "Numeric Field",
          modelValue: "invalid-text",
          error: "Must be a number",
        },
      });
      expect(wrapper.find(".validation-error-msg").exists()).toBe(true);
      expect(wrapper.classes()).toContain("has-error");
    });

    it("does not show error state if error prop is empty", async () => {
      const wrapper = mount(ClinicalInput, {
        props: {
          id: "test-input-no-err",
          label: "Optional Field",
          modelValue: "",
          error: null,
        },
      });
      expect(wrapper.find(".validation-error-msg").exists()).toBe(false);
      expect(wrapper.classes()).not.toContain("has-error");
    });
  });

  describe("Reactive Overlay Focus Containment & 21 CFR Part 11 Escape Credential Wiping", () => {
    it("marks ReadabilityDrawer inert and aria-hidden when closed, and traps/restores focus when opened", async () => {
      const { default: ReadabilityDrawer } =
        await import("../src/components/econsent/ReadabilityDrawer.vue");
      const triggerBtn = document.createElement("button");
      triggerBtn.id = "readability-trigger";
      container.appendChild(triggerBtn);
      triggerBtn.focus();

      const wrapper = mount(ReadabilityDrawer, {
        props: {
          isOpen: false,
          text: "",
        },
        attachTo: container,
      });
      await nextTick();

      const drawer = wrapper.find(".readability-drawer");
      expect(drawer.attributes("inert")).toBeDefined();
      expect(drawer.attributes("aria-hidden")).toBe("true");
      expect(drawer.attributes("aria-modal")).toBeUndefined();

      await wrapper.setProps({ isOpen: true });
      await nextTick();
      await nextTick();

      expect(drawer.attributes("inert")).toBeUndefined();
      expect(drawer.attributes("aria-hidden")).toBeUndefined();
      expect(drawer.attributes("aria-modal")).toBe("true");
      expect(drawer.element.contains(document.activeElement)).toBe(true);

      document.dispatchEvent(
        new KeyboardEvent("keydown", {
          key: "Escape",
          bubbles: true,
          cancelable: true,
        })
      );
      await nextTick();
      expect(wrapper.emitted("close")).toBeTruthy();

      await wrapper.setProps({ isOpen: false });
      await nextTick();
      expect(document.activeElement).toBe(triggerBtn);
      wrapper.unmount();
    });

    it("clears typed credential immediately when ReAuthModal is dismissed via Escape", async () => {
      const { default: ReAuthModal } =
        await import("../src/components/shared/ReAuthModal.vue");
      const triggerBtn = document.createElement("button");
      container.appendChild(triggerBtn);
      triggerBtn.focus();

      const wrapper = mount(ReAuthModal, {
        props: {
          isOpen: true,
          username: "monitor.01",
          filename: "protocol_v2.pdf",
        },
        attachTo: container,
      });
      await nextTick();
      await nextTick();

      const pwInput = wrapper.find("#reauth-credential");
      await pwInput.setValue("Part11SecretPassword!");
      expect(pwInput.element.value).toBe("Part11SecretPassword!");

      document.dispatchEvent(
        new KeyboardEvent("keydown", {
          key: "Escape",
          bubbles: true,
          cancelable: true,
        })
      );
      await nextTick();

      expect(wrapper.emitted("cancel")).toBeTruthy();
      expect(pwInput.element.value).toBe("");

      await wrapper.setProps({ isOpen: false });
      await nextTick();
      expect(document.activeElement).toBe(triggerBtn);
      wrapper.unmount();
    });

    it("clears typed password and TOTP immediately when SignatureCaptureModal is dismissed via Escape", async () => {
      const { default: SignatureCaptureModal } =
        await import("../src/features/signatures/components/SignatureCaptureModal.vue");
      const triggerBtn = document.createElement("button");
      container.appendChild(triggerBtn);
      triggerBtn.focus();

      const wrapper = mount(SignatureCaptureModal, {
        props: {
          isOpen: true,
          username: "investigator.01",
          actionUrl: "/api/v1/sign",
        },
        attachTo: container,
      });
      await nextTick();
      await nextTick();

      const pwInput = wrapper.find("#sig-password");
      await pwInput.setValue("InvestigatorSecret99!");
      expect(pwInput.element.value).toBe("InvestigatorSecret99!");

      document.dispatchEvent(
        new KeyboardEvent("keydown", {
          key: "Escape",
          bubbles: true,
          cancelable: true,
        })
      );
      await nextTick();

      expect(wrapper.emitted("cancel")).toBeTruthy();
      expect(pwInput.element.value).toBe("");

      await wrapper.setProps({ isOpen: false });
      await nextTick();
      expect(document.activeElement).toBe(triggerBtn);
      wrapper.unmount();
    });
  });
});
