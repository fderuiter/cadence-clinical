import { describe, it, expect, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import AuditorExportModal from "../../src/components/auditor/AuditorExportModal.vue";
import { useAuditorStore } from "../../src/stores/auditor";

describe("Responsive Clinical Modals - Accessibility and Mobile Adaptability", () => {
  let pinia;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
  });

  describe("AuditorExportModal.vue", () => {
    it("renders date range fields in a mobile-responsive grid container", () => {
      const auditorStore = useAuditorStore();
      auditorStore.filters.dateRange.start = "2026-08-01";
      auditorStore.filters.dateRange.end = "2026-08-10";

      const wrapper = mount(AuditorExportModal, {
        global: {
          plugins: [pinia],
        },
        props: {
          isOpen: true,
        },
      });

      // Assert that date layout has been responsive-ized
      const dateLayout = wrapper.find(".grid-2-responsive");
      expect(dateLayout.exists()).toBe(true);

      const styleAttr = dateLayout.attributes("style") || "";
      expect(styleAttr).not.toContain("grid-template-columns");
      expect(styleAttr).not.toContain("display: grid");
    });

    it("proves format selectors, date selectors, and buttons have large touch target classes", () => {
      const wrapper = mount(AuditorExportModal, {
        global: {
          plugins: [pinia],
        },
        props: {
          isOpen: true,
        },
      });

      // Assert radio button formats have touch targets
      const formatLabels = wrapper.findAll("label.touch-target-interactive");
      expect(formatLabels.length).toBeGreaterThanOrEqual(3);

      // Assert start and end date inputs are touch compliant
      const startDateInput = wrapper.find(".export-start-date");
      const endDateInput = wrapper.find(".export-end-date");
      expect(startDateInput.classes()).toContain("touch-target-interactive");
      expect(endDateInput.classes()).toContain("touch-target-interactive");

      // Assert action buttons
      const buttons = wrapper.findAll("footer button.touch-target-interactive");
      expect(buttons.length).toBe(2);
    });
  });
});
