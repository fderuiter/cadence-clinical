import { defineStore } from "pinia";

export const useDesignerStore = defineStore("designer", {
  state: () => ({
    activeForm: {
      id: "form-1",
      name: "eCRF Draft Form",
      sections: [
        {
          id: "section-1",
          name: "Demographics",
          isCollapsed: false,
          items: [
            {
              id: "field-1",
              label: "Subject Initials",
              type: "text",
              cdash: "DM.SUBJINIT",
              gridSpan: 6,
              required: true,
            },
            {
              id: "field-2",
              label: "Date of Birth",
              type: "date",
              cdash: "DM.BRTHDT",
              gridSpan: 6,
              required: true,
            },
          ],
        },
      ],
    },
    selectedFieldId: null,
  }),
  actions: {
    setSelectedFieldId(id) {
      this.selectedFieldId = id;
    },
    updateActiveForm(form) {
      this.activeForm = form;
    },
    updateSections(sections) {
      if (this.activeForm) {
        this.activeForm.sections = sections;
      }
    },
    addFieldToSection(sectionId, field) {
      const section = this.activeForm.sections.find((s) => s.id === sectionId);
      if (section) {
        if (!section.items) {
          section.items = [];
        }
        section.items.push(field);
      }
    },
    deleteField(fieldId) {
      for (const section of this.activeForm.sections) {
        const idx = section.items.findIndex((item) => item.id === fieldId);
        if (idx !== -1) {
          section.items.splice(idx, 1);
          if (this.selectedFieldId === fieldId) {
            this.selectedFieldId = null;
          }
          break;
        }
      }
    },
    duplicateField(fieldId) {
      for (const section of this.activeForm.sections) {
        const idx = section.items.findIndex((item) => item.id === fieldId);
        if (idx !== -1) {
          const original = section.items[idx];
          const newId = `${original.id}-copy-${Date.now()}`;
          const copy = {
            ...original,
            id: newId,
            label: `${original.label} (Copy)`,
          };
          section.items.splice(idx + 1, 0, copy);
          this.selectedFieldId = newId;
          break;
        }
      }
    },
  },
});
