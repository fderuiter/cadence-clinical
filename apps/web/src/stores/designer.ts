/**
 * useDesignerStore Pinia Store
 *
 * Manages the client-side state for the eCRF designer canvas, including
 * the active form schema, selected field identifier, viewport layout simulation mode,
 * and whether layout warnings have been explicitly bypassed by the designer.
 *
 * Conforms to clinical metadata-driven layout validation guidelines.
 */
import { defineStore } from "pinia";

export interface DesignerField {
  id: string;
  label: string;
  type: string;
  cdash?: string;
  gridSpan: number;
  required?: boolean;
  [key: string]: any; // Index signature for flexible fields
}

export interface DesignerSection {
  id: string;
  name: string;
  isCollapsed?: boolean;
  items: DesignerField[];
  [key: string]: any;
}

export interface DesignerForm {
  id: string;
  name: string;
  sections: DesignerSection[];
  layoutJustification?: string;
  [key: string]: any;
}

export interface DesignerState {
  activeForm: DesignerForm | null;
  selectedFieldId: string | null;
  viewport: string;
  dismissedWarnings: boolean;
}

export const useDesignerStore = defineStore("designer", {
  state: (): DesignerState => ({
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
      layoutJustification: "",
    },
    selectedFieldId: null,
    // Manage simulated eCRF designer canvas viewports (desktop, tablet, mobile)
    viewport: "desktop",
    // Track user explicit confirmation to override dense grid layouts during compilation
    dismissedWarnings: false,
  }),
  actions: {
    setViewport(viewport: string) {
      this.viewport = viewport;
    },
    setDismissedWarnings(dismissed: boolean) {
      this.dismissedWarnings = dismissed;
    },
    setLayoutJustification(justification: string) {
      if (this.activeForm) {
        this.activeForm.layoutJustification = justification;
      }
    },
    setSelectedFieldId(id: string | null) {
      this.selectedFieldId = id;
    },
    updateActiveForm(form: DesignerForm) {
      this.activeForm = form;
    },
    updateSections(sections: DesignerSection[]) {
      if (this.activeForm) {
        this.activeForm.sections = sections;
      }
    },
    addFieldToSection(sectionId: string, field: DesignerField) {
      if (!this.activeForm) return;
      const section = this.activeForm.sections.find((s) => s.id === sectionId);
      if (section) {
        if (!section.items) {
          section.items = [];
        }
        section.items.push(field);
      }
    },
    deleteField(fieldId: string) {
      if (!this.activeForm) return;
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
    duplicateField(fieldId: string) {
      if (!this.activeForm) return;
      for (const section of this.activeForm.sections) {
        const idx = section.items.findIndex((item) => item.id === fieldId);
        if (idx !== -1) {
          const original = section.items[idx];
          const newId = `${original.id}-copy-${Date.now()}`;
          const copy: DesignerField = {
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
    moveFieldToSection(
      fieldId: string,
      targetSectionId: string,
      targetIndex?: number
    ): boolean {
      if (!this.activeForm || !Array.isArray(this.activeForm.sections)) {
        return false;
      }
      const targetSection = this.activeForm.sections.find(
        (s) => s.id === targetSectionId
      );
      if (!targetSection) return false;

      let sourceSection: DesignerSection | undefined;
      let sourceIdx = -1;
      for (const section of this.activeForm.sections) {
        const idx = (section.items || []).findIndex(
          (item) => item.id === fieldId
        );
        if (idx !== -1) {
          sourceSection = section;
          sourceIdx = idx;
          break;
        }
      }

      if (!sourceSection || sourceIdx === -1) return false;
      if (sourceSection.id === targetSection.id && targetIndex === undefined) {
        return false;
      }

      const [movedField] = sourceSection.items.splice(sourceIdx, 1);
      if (!targetSection.items) {
        targetSection.items = [];
      }
      const boundedIndex =
        typeof targetIndex === "number"
          ? Math.max(0, Math.min(targetSection.items.length, targetIndex))
          : targetSection.items.length;
      targetSection.items.splice(boundedIndex, 0, movedField);
      targetSection.isCollapsed = false;
      this.selectedFieldId = movedField.id;
      return true;
    },
    moveFieldAcrossSections(
      fieldId: string,
      direction: "up" | "down"
    ): boolean {
      if (!this.activeForm || !Array.isArray(this.activeForm.sections)) {
        return false;
      }
      const sections = this.activeForm.sections;
      for (let sIdx = 0; sIdx < sections.length; sIdx++) {
        const section = sections[sIdx];
        const items = section.items || [];
        const fIdx = items.findIndex((item) => item.id === fieldId);
        if (fIdx === -1) continue;

        if (direction === "up") {
          if (fIdx > 0) {
            const temp = items[fIdx];
            items[fIdx] = items[fIdx - 1];
            items[fIdx - 1] = temp;
            this.selectedFieldId = fieldId;
            return true;
          }
          if (sIdx > 0) {
            const prevSection = sections[sIdx - 1];
            return this.moveFieldToSection(
              fieldId,
              prevSection.id,
              (prevSection.items || []).length
            );
          }
          return false;
        }

        if (direction === "down") {
          if (fIdx < items.length - 1) {
            const temp = items[fIdx];
            items[fIdx] = items[fIdx + 1];
            items[fIdx + 1] = temp;
            this.selectedFieldId = fieldId;
            return true;
          }
          if (sIdx < sections.length - 1) {
            const nextSection = sections[sIdx + 1];
            return this.moveFieldToSection(fieldId, nextSection.id, 0);
          }
          return false;
        }
      }
      return false;
    },
  },
});
