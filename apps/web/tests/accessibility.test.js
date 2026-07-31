import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { ref, defineComponent, nextTick } from "vue";
import { useFocusTrap } from "../src/composables/useFocusTrap";
import { useEscapeClose } from "../src/composables/useEscapeClose";
import ClinicalQueryPanel from "../src/components/clinical/ClinicalQueryPanel.vue";
import ClinicalInput from "../src/components/clinical/ClinicalInput.vue";

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
      const event = new KeyboardEvent("keydown", { key: "Tab", shiftKey: true, bubbles: true });
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

      const event = new KeyboardEvent("keydown", { key: "Escape", bubbles: true });
      document.dispatchEvent(event);

      expect(onClose).toHaveBeenCalledTimes(1);
    });

    it("does not call close handler after being unmounted", () => {
      const onClose = vi.fn();
      const wrapper = mount(TestEscapeComponent, { props: { onClose }, attachTo: container });

      wrapper.unmount();

      const event = new KeyboardEvent("keydown", { key: "Escape", bubbles: true });
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
      const escapeEvent = new KeyboardEvent("keydown", { key: "Escape", bubbles: true });
      document.dispatchEvent(escapeEvent);
      await nextTick();

      expect(wrapper.findComponent(ClinicalQueryPanel).exists()).toBe(false);
      expect(document.activeElement).toBe(flagButton);
    });
  });
});
