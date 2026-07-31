import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import CrfAuthoringCanvas from "../../src/components/crf/CrfAuthoringCanvas.vue";
import FormSectionContainer from "../../src/components/crf/FormSectionContainer.vue";
import CanvasFieldWidget from "../../src/components/crf/CanvasFieldWidget.vue";
import { useDesignerStore } from "../../src/stores/designer.js";

// Mock draggable to make tests synchronous and easier to test without full sortablejs drag interactions
vi.mock("vuedraggable", () => {
  return {
    default: {
      name: "draggable",
      props: ["modelValue", "itemKey"],
      emits: ["update:modelValue", "change"],
      template: `
        <div class="mock-draggable">
          <div v-for="(item, index) in modelValue" :key="item[itemKey]">
            <slot name="item" :element="item" :index="index"></slot>
          </div>
        </div>
      `,
    },
  };
});

describe("CrfAuthoringCanvas.vue & Drag-and-Drop Authoring Component Suite", () => {
  let pinia: any;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
  });

  it("renders form sections and nested field widgets correctly", () => {
    const formSchema = {
      id: "form-test-1",
      name: "Subject Visit Log",
      sections: [
        {
          id: "section-1",
          name: "Vitals Section",
          isCollapsed: false,
          items: [
            {
              id: "field-vssbp",
              label: "Systolic Blood Pressure",
              type: "numeric",
              cdash: "VS.SYSBP",
              gridSpan: 6,
              required: true,
            },
            {
              id: "field-vsdpb",
              label: "Diastolic Blood Pressure",
              type: "numeric",
              cdash: "VS.DIABP",
              gridSpan: 6,
              required: true,
            },
          ],
        },
      ],
    };

    const wrapper = mount(CrfAuthoringCanvas, {
      props: {
        formSchema,
        selectedFieldId: "field-vssbp",
      },
    });

    // Check section header is rendered
    expect(wrapper.text()).toContain("Vitals Section");
    expect(wrapper.text()).toContain("Systolic Blood Pressure");
    expect(wrapper.text()).toContain("[VS.SYSBP]");
    expect(wrapper.text()).toContain("Diastolic Blood Pressure");
    expect(wrapper.text()).toContain("[VS.DIABP]");

    // Check we have the highlighted primary border on selected item
    const selectedWidget = wrapper.find(".border-indigo-600");
    expect(selectedWidget.exists()).toBe(true);
    expect(selectedWidget.text()).toContain("Systolic Blood Pressure");
  });

  it("emits select-field event and updates Pinia selectedFieldId on clicking field widget", async () => {
    const formSchema = {
      id: "form-test-1",
      name: "Subject Visit Log",
      sections: [
        {
          id: "section-1",
          name: "Vitals Section",
          isCollapsed: false,
          items: [
            {
              id: "field-vssbp",
              label: "Systolic Blood Pressure",
              type: "numeric",
              cdash: "VS.SYSBP",
              gridSpan: 6,
              required: true,
            },
          ],
        },
      ],
    };

    const store = useDesignerStore();
    expect(store.selectedFieldId).toBeNull();

    const wrapper = mount(CrfAuthoringCanvas, {
      props: {
        formSchema,
        selectedFieldId: null,
      },
    });

    const widget = wrapper.findComponent(CanvasFieldWidget);
    await widget.trigger("click");

    // Verify select-field event is emitted
    const selectFieldEvents = wrapper.emitted("select-field");
    expect(selectFieldEvents).toBeTruthy();
    expect(selectFieldEvents?.[0]).toEqual(["field-vssbp"]);

    // Verify it updated the Pinia store selectedFieldId
    expect(store.selectedFieldId).toBe("field-vssbp");
  });

  it("supports section collapsing and expansion toggling", async () => {
    const section = {
      id: "section-1",
      name: "Demographics",
      isCollapsed: false,
      items: [],
    };

    const wrapper = mount(FormSectionContainer, {
      props: {
        section,
        selectedFieldId: null,
      },
    });

    const bodyDiv = wrapper.find(".section-body");
    expect(bodyDiv.isVisible()).toBe(true);

    // Toggle collapse
    const header = wrapper.find(".section-header");
    await header.trigger("click");

    expect(section.isCollapsed).toBe(true);
  });

  it("appends new item to section items array on click '+ Add Row'", async () => {
    const section = {
      id: "section-1",
      name: "Demographics",
      isCollapsed: false,
      items: [],
    };

    const store = useDesignerStore();
    // Populate activeForm in store so addFieldToSection works
    store.activeForm = {
      id: "form-1",
      name: "Draft",
      sections: [section],
    };

    const wrapper = mount(FormSectionContainer, {
      props: {
        section,
        selectedFieldId: null,
      },
    });

    expect(section.items.length).toBe(0);

    const btn = wrapper.find(".btn-add-item");
    await btn.trigger("click");

    // Verify item was appended to section items array
    expect(section.items.length).toBe(1);
    expect(section.items[0].label).toBe("New Field Entry");
    expect(section.items[0].type).toBe("text");
    expect(section.items[0].gridSpan).toBe(12);

    // Verify Pinia store got updated and selectedFieldId set
    expect(store.activeForm.sections[0].items.length).toBe(1);
    expect(store.selectedFieldId).toBe(section.items[0].id);
  });

  it("duplicates field widget successfully inside section list and Pinia", async () => {
    const section = {
      id: "section-1",
      name: "Demographics",
      isCollapsed: false,
      items: [
        {
          id: "field-dup",
          label: "Pulse Rate",
          type: "numeric",
          gridSpan: 4,
          required: false,
        },
      ],
    };

    const store = useDesignerStore();
    store.activeForm = {
      id: "form-1",
      name: "Draft",
      sections: [section],
    };

    const wrapper = mount(FormSectionContainer, {
      props: {
        section,
        selectedFieldId: "field-dup",
      },
    });

    expect(section.items.length).toBe(1);

    const dupBtn = wrapper.find("button[title='Duplicate Field']");
    expect(dupBtn.exists()).toBe(true);
    await dupBtn.trigger("click");

    // Verify item was duplicated
    expect(section.items.length).toBe(2);
    expect(section.items[0].id).toBe("field-dup");
    expect(section.items[1].label).toBe("Pulse Rate (Copy)");

    // Verify Pinia state reflects duplication
    expect(store.activeForm.sections[0].items.length).toBe(2);
    expect(store.activeForm.sections[0].items[1].label).toBe("Pulse Rate (Copy)");
    expect(store.selectedFieldId).toBe(section.items[1].id);
  });

  it("deletes field widget successfully from section list and Pinia", async () => {
    const section = {
      id: "section-1",
      name: "Demographics",
      isCollapsed: false,
      items: [
        {
          id: "field-del",
          label: "Pulse Rate",
          type: "numeric",
          gridSpan: 4,
          required: false,
        },
      ],
    };

    const store = useDesignerStore();
    store.activeForm = {
      id: "form-1",
      name: "Draft",
      sections: [section],
    };

    const wrapper = mount(FormSectionContainer, {
      props: {
        section,
        selectedFieldId: "field-del",
      },
    });

    expect(section.items.length).toBe(1);

    const delBtn = wrapper.find("button[title='Delete Field']");
    expect(delBtn.exists()).toBe(true);
    await delBtn.trigger("click");

    // Verify item was deleted
    expect(section.items.length).toBe(0);

    // Verify Pinia state reflects deletion
    expect(store.activeForm.sections[0].items.length).toBe(0);
    expect(store.selectedFieldId).toBeNull();
  });

  it("displays empty dropzone placeholder when section is empty", () => {
    const section = {
      id: "section-empty",
      name: "Empty Section",
      isCollapsed: false,
      items: [],
    };

    const wrapper = mount(FormSectionContainer, {
      props: {
        section,
        selectedFieldId: null,
      },
    });

    const placeholder = wrapper.find(".empty-dropzone-placeholder");
    expect(placeholder.exists()).toBe(true);
    expect(placeholder.text()).toContain("Drag and drop field widgets here");
  });
});
