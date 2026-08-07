import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { nextTick, defineComponent } from "vue";
import { vKeyboardClick } from "../../src/directives/keyboardClick.js";
import CanvasFieldWidget from "../../src/components/crf/CanvasFieldWidget.vue";
import FormSectionContainer from "../../src/components/crf/FormSectionContainer.vue";
import LanguageTranslationTabs from "../../src/components/econsent/LanguageTranslationTabs.vue";
import { useDesignerStore } from "../../src/stores/designer.js";
import { useEconsentStore } from "../../src/stores/econsent.js";

// Dummy wrapper component to test directive
const DirectiveTestComponent = defineComponent({
  directives: {
    "keyboard-click": vKeyboardClick,
  },
  props: ["callback"],
  template: `
    <div>
      <div id="test-div" v-keyboard-click="callback">Clickable Div</div>
      <input id="test-input" v-keyboard-click="callback" type="text" />
    </div>
  `,
});

describe("Keyboard Navigation & Focus Accessibility Engine", () => {
  let pinia;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
  });

  describe("Directive: v-keyboard-click", () => {
    it("applies tabindex and role to a standard div element", () => {
      const callback = vi.fn();
      const wrapper = mount(DirectiveTestComponent, {
        props: { callback },
      });

      const div = wrapper.find("#test-div");
      expect(div.attributes("tabindex")).toBe("0");
      expect(div.attributes("role")).toBe("button");
    });

    it("does not apply role='button' to native input elements", () => {
      const callback = vi.fn();
      const wrapper = mount(DirectiveTestComponent, {
        props: { callback },
      });

      const input = wrapper.find("#test-input");
      expect(input.attributes("role")).toBeUndefined();
    });

    it("triggers callback when Enter or Space is pressed on div", async () => {
      const callback = vi.fn();
      const wrapper = mount(DirectiveTestComponent, {
        props: { callback },
      });

      const div = wrapper.find("#test-div");

      // Press Enter
      await div.trigger("keydown", { key: "Enter" });
      expect(callback).toHaveBeenCalledTimes(1);

      // Press Space
      await div.trigger("keydown", { key: " " });
      expect(callback).toHaveBeenCalledTimes(2);

      // Press Other key
      await div.trigger("keydown", { key: "ArrowRight" });
      expect(callback).toHaveBeenCalledTimes(2); // no change
    });

    it("does not trigger callback on inputs when Enter or Space is pressed", async () => {
      const callback = vi.fn();
      const wrapper = mount(DirectiveTestComponent, {
        props: { callback },
      });

      const input = wrapper.find("#test-input");
      await input.trigger("keydown", { key: "Enter" });
      expect(callback).not.toHaveBeenCalled();

      await input.trigger("keydown", { key: " " });
      expect(callback).not.toHaveBeenCalled();
    });
  });

  describe("Component: CanvasFieldWidget.vue", () => {
    it("supports keyboard focus and handles selection via Enter or Space", async () => {
      const field = {
        id: "field-test-1",
        label: "Blood Pressure",
        type: "numeric",
        required: true,
      };

      const wrapper = mount(CanvasFieldWidget, {
        props: {
          field,
          selectedFieldId: null,
        },
        global: {
          plugins: [pinia],
        },
      });

      // Assert focus classes and accessibility roles are set
      expect(wrapper.attributes("tabindex")).toBe("0");
      expect(wrapper.attributes("role")).toBe("button");
      expect(wrapper.classes()).toContain("focus-visible:ring-indigo-600");

      // Verify that pressing Enter triggers standard select field emit
      await wrapper.trigger("keydown", { key: "Enter" });
      expect(wrapper.emitted("select-field")).toBeTruthy();
      expect(wrapper.emitted("select-field")[0]).toEqual(["field-test-1"]);

      // Verify that pressing Space triggers selection
      await wrapper.trigger("keydown", { key: " " });
      expect(wrapper.emitted("select-field")[1]).toEqual(["field-test-1"]);
    });

    it("shows configuration toolbar when selected", async () => {
      const field = {
        id: "field-test-1",
        label: "Blood Pressure",
        type: "numeric",
      };

      const wrapper = mount(CanvasFieldWidget, {
        props: {
          field,
          selectedFieldId: "field-test-1", // selected
        },
        global: {
          plugins: [pinia],
        },
      });

      const toolbar = wrapper.find(".widget-actions");
      expect(toolbar.exists()).toBe(true);

      const duplicateBtn = toolbar.find('button[title="Duplicate Field"]');
      const deleteBtn = toolbar.find('button[title="Delete Field"]');

      expect(duplicateBtn.exists()).toBe(true);
      expect(deleteBtn.exists()).toBe(true);
    });
  });

  describe("Component: FormSectionContainer.vue", () => {
    it("handles section toggle, tabindex, and aria-expanded correctly", async () => {
      const designerStore = useDesignerStore(pinia);

      const section = {
        id: "section-1",
        name: "Vitals Section",
        isCollapsed: false,
        items: [],
      };

      designerStore.activeForm = {
        id: "form-1",
        sections: [section],
      };

      const wrapper = mount(FormSectionContainer, {
        props: {
          section,
          selectedFieldId: null,
        },
        global: {
          plugins: [pinia],
        },
      });

      const header = wrapper.find(".section-header");
      expect(header.attributes("tabindex")).toBe("0");
      expect(header.attributes("role")).toBe("button");
      expect(header.attributes("aria-expanded")).toBe("true");

      // Verify toggling collapse via keyboard Enter key
      await header.trigger("keydown", { key: "Enter" });
      expect(designerStore.activeForm.sections[0].isCollapsed).toBe(true);

      // Verify aria-expanded updates properly on the DOM
      await wrapper.setProps({
        section: { ...section, isCollapsed: true },
      });
      expect(header.attributes("aria-expanded")).toBe("false");
    });
  });

  describe("Component: LanguageTranslationTabs.vue", () => {
    it("implements roving tabindex and horizontal arrow-key tab navigation", async () => {
      const econsentStore = useEconsentStore(pinia);
      econsentStore.activeLanguage = "en";
      econsentStore.sections = [];

      const wrapper = mount(LanguageTranslationTabs, {
        global: {
          plugins: [pinia],
        },
        attachTo: document.body, // required to test focus movement
      });

      const tabsList = wrapper.find(".tabs-list");
      const tabs = tabsList.findAll('[role="tab"]');

      // English is active, spanish, french, german are inactive
      expect(tabs[0].attributes("aria-selected")).toBe("true");
      expect(tabs[0].attributes("tabindex")).toBe("0");
      expect(tabs[1].attributes("aria-selected")).toBe("false");
      expect(tabs[1].attributes("tabindex")).toBe("-1");

      // Focus first tab
      tabs[0].element.focus();
      expect(document.activeElement).toBe(tabs[0].element);

      // Dispatch ArrowRight KeyboardEvent natively to bubble and be caught by addEventListener
      const arrowRightEvent = new KeyboardEvent("keydown", {
        key: "ArrowRight",
        bubbles: true,
      });
      tabsList.element.dispatchEvent(arrowRightEvent);

      expect(econsentStore.activeLanguage).toBe("es");

      // Spanish is active now
      await nextTick();
      expect(tabs[1].attributes("aria-selected")).toBe("true");
      expect(tabs[1].attributes("tabindex")).toBe("0");

      // Dispatch ArrowLeft KeyboardEvent natively
      const arrowLeftEvent = new KeyboardEvent("keydown", {
        key: "ArrowLeft",
        bubbles: true,
      });
      tabsList.element.dispatchEvent(arrowLeftEvent);

      expect(econsentStore.activeLanguage).toBe("en");

      wrapper.unmount();
    });
  });
});
