import fs from "node:fs";
import path from "node:path";
import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { nextTick } from "vue";
import { ClinicalDropzone } from "ui";
import BodyMapWidget from "../../src/components/crf/widgets/BodyMapWidget.vue";
import DicomMprViewer from "../../src/components/imaging/DicomMprViewer.vue";
import OncologyCalipers from "../../src/components/imaging/OncologyCalipers.vue";
import ImagingView from "../../src/views/ImagingView.vue";
import CanvasFieldWidget from "../../src/components/crf/CanvasFieldWidget.vue";
import FormSectionContainer from "../../src/components/crf/FormSectionContainer.vue";
import CrfAuthoringCanvas from "../../src/components/crf/CrfAuthoringCanvas.vue";
import SoAMatrixEditor from "../../src/components/clinical/SoAMatrixEditor.vue";
import ClinicalGanttVisualizer from "../../src/components/clinical/ClinicalGanttVisualizer.vue";
import QuizTemplateDrawer from "../../src/components/econsent/QuizTemplateDrawer.vue";
import SynonymImportDrawer from "../../src/components/SynonymImportDrawer.vue";
import CellGeneTherapyCocConsole from "../../src/components/clinical/CellGeneTherapyCocConsole.vue";
import { useDesignerStore } from "../../src/stores/designer.js";
import { useClinicalStore } from "../../src/stores/clinical.js";
import { useCodingStore } from "../../src/stores/coding.js";
import { useAuthStore } from "../../src/stores/auth.js";

vi.mock("vuedraggable", () => ({
  default: {
    name: "draggable",
    props: [
      "modelValue",
      "itemKey",
      "forceFallback",
      "fallbackTolerance",
      "scrollSensitivity",
      "handle",
      "group",
    ],
    emits: ["update:modelValue", "change"],
    template: `
      <div class="mock-draggable">
        <div v-for="(item, index) in modelValue" :key="item[itemKey]">
          <slot name="item" :element="item" :index="index"></slot>
        </div>
      </div>
    `,
  },
}));

function assertZeroNestedControlsInRoleButton(rootEl: Element) {
  const roleButtons = Array.from(rootEl.querySelectorAll('[role="button"]'));
  for (const rb of roleButtons) {
    const nested = rb.querySelector("button, input, select, textarea, a[href]");
    expect(nested).toBeNull();
  }
}

describe("Spatial Composables & Sibling Buttons Accessibility Suite", () => {
  let pinia: ReturnType<typeof createPinia>;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
  });

  describe("Acceptance 1: Sibling Buttons & Zero Nested Interactive Controls inside role='button'", () => {
    it("triggers only focused nested actions on Enter and Space in CanvasFieldWidget and FormSectionContainer", async () => {
      const store = useDesignerStore(pinia);
      const section = {
        id: "sec-1",
        name: "Vital Signs",
        isCollapsed: false,
        items: [
          {
            id: "field-hr",
            label: "Heart Rate",
            type: "numeric",
            gridSpan: 6,
            required: true,
          },
          {
            id: "field-bp",
            label: "Systolic BP",
            type: "numeric",
            gridSpan: 6,
            required: false,
          },
        ],
      };
      store.activeForm = {
        id: "form-1",
        name: "Vitals Form",
        sections: [section],
      };

      const widgetWrapper = mount(CanvasFieldWidget, {
        props: {
          field: section.items[0],
          selectedFieldId: "field-hr",
          isFirst: true,
          isLast: false,
        },
        global: { plugins: [pinia] },
      });

      await expect(widgetWrapper).toBeAccessible();
      assertZeroNestedControlsInRoleButton(widgetWrapper.element);

      const duplicateBtn = widgetWrapper.find(
        'button[title="Duplicate Field"]'
      );
      await duplicateBtn.trigger("keydown", { key: "Enter" });
      await duplicateBtn.trigger("keydown", { key: " " });
      await duplicateBtn.trigger("click");

      expect(widgetWrapper.emitted("duplicate-field")).toHaveLength(1);
      expect(widgetWrapper.emitted("select-field")).toBeUndefined();

      const sectionWrapper = mount(FormSectionContainer, {
        props: {
          section,
          selectedFieldId: "field-hr",
          isFirst: true,
          isLast: true,
        },
        global: { plugins: [pinia] },
      });

      await expect(sectionWrapper).toBeAccessible();
      assertZeroNestedControlsInRoleButton(sectionWrapper.element);

      const addBtn = sectionWrapper.find("button.btn-add-item");
      await addBtn.trigger("keydown", { key: "Enter" });
      await addBtn.trigger("keydown", { key: " " });
      await addBtn.trigger("click");

      expect(store.activeForm.sections[0].isCollapsed).toBe(false);
      expect(store.activeForm.sections[0].items).toHaveLength(3);
    });

    it("eliminates role='button' card wrappers in QuizTemplateDrawer so Enter and Space operate only the checkbox", async () => {
      const wrapper = mount(QuizTemplateDrawer, {
        props: { isOpen: true },
        global: { plugins: [pinia] },
      });

      await nextTick();
      await expect(wrapper).toBeAccessible();
      assertZeroNestedControlsInRoleButton(wrapper.element);

      const firstCheckbox = wrapper.find("input.checkbox-template-select");
      expect(firstCheckbox.exists()).toBe(true);

      // Enter toggles only the focused checkbox without a wrapper double-toggle
      await firstCheckbox.trigger("keydown", { key: "Enter" });
      expect(wrapper.vm.selectedTemplateIds).toHaveLength(1);

      // Space / change toggles back cleanly
      await firstCheckbox.trigger("change");
      expect(wrapper.vm.selectedTemplateIds).toHaveLength(0);
    });

    it("keeps SynonymImportDrawer and CellGeneTherapyCocConsole dropzones free of role='button' wrappers around Remove File", async () => {
      const authStore = useAuthStore(pinia);
      authStore.isDemoMode = true;
      const codingStore = useCodingStore(pinia);
      codingStore.isSynonymDrawerOpen = true;

      const synonymWrapper = mount(SynonymImportDrawer, {
        global: { plugins: [pinia] },
      });
      await nextTick();
      assertZeroNestedControlsInRoleButton(synonymWrapper.element);

      const dropzone = synonymWrapper.findComponent(ClinicalDropzone);
      const mockCsv = new File(
        ["verbatim_text,dictionary_type\nheadache,MEDDRA"],
        "synonyms.csv",
        { type: "text/csv" }
      );
      dropzone.vm.$emit("file-selected", mockCsv);
      await nextTick();

      const removeBtn = synonymWrapper
        .findAll("button")
        .find((b) => b.text().includes("Remove File"));
      expect(removeBtn).toBeTruthy();
      assertZeroNestedControlsInRoleButton(synonymWrapper.element);

      await removeBtn!.trigger("keydown", { key: "Enter" });
      await removeBtn!.trigger("keydown", { key: " " });
      await removeBtn!.trigger("click");
      await nextTick();

      expect(synonymWrapper.text()).not.toContain("synonyms.csv");

      const cgtWrapper = mount(CellGeneTherapyCocConsole, {
        global: { plugins: [pinia] },
      });
      await nextTick();
      (cgtWrapper.vm as any).cocData = {
        id: "coc-1",
        study_id: "STUDY-CGT-801",
        subject_id: "SUBJ-101-0042",
        din: "W000026000001",
        batch_id: "BATCH-01",
        current_stage: "COURIER_TRANSIT_OUTBOUND",
        status: "IN_TRANSIT",
        tamper_evident_barcode: "PAYLOAD",
        barcode_signature: "SIG-01",
        collection_site_id: "SITE-01",
        collection_timestamp: "2026-08-01T10:00:00Z",
        manufacturing_facility_id: "MFG-01",
        cryo_temp_excursion_count: 0,
        lowest_temp_celsius: -192.4,
        highest_temp_celsius: -185.1,
        infusion_unlocked: false,
        infused_at: null,
        reconciled_at: null,
      };
      (cgtWrapper.vm as any).showMilestoneModal = true;
      (cgtWrapper.vm as any).parsedTelemetry = {
        fileName: "logger.csv",
        vendor: "Sensitech",
        isDownsampled: false,
        keypoints: [],
        totalSamples: 12,
        excursionCount: 0,
        minTemperature: -190.0,
        maxTemperature: -185.0,
        excursionEvents: [],
        sampleReadings: [],
      };
      await nextTick();

      assertZeroNestedControlsInRoleButton(cgtWrapper.element);
      const cgtRemoveBtn = cgtWrapper
        .findAll("button")
        .find((b) => b.text().includes("Remove File"));
      expect(cgtRemoveBtn).toBeTruthy();
      await cgtRemoveBtn!.trigger("keydown", { key: "Enter" });
      await cgtRemoveBtn!.trigger("click");
      await nextTick();
      expect((cgtWrapper.vm as any).parsedTelemetry).toBeNull();
    });
  });

  describe("Acceptance 2: eCRF Cross-Section Field Movement & Inspector Controls", () => {
    it("moves fields across section boundaries via single-pointer clicks and the Inspector Section selector", async () => {
      const store = useDesignerStore(pinia);
      store.viewport = "mobile"; // triggers warning items for narrow gridSpan
      store.activeForm = {
        id: "form-multi",
        name: "Multi-Section eCRF",
        sections: [
          {
            id: "sec-demographics",
            name: "Demographics",
            isCollapsed: false,
            items: [
              {
                id: "f-subj",
                label: "Subject Initials",
                type: "text",
                gridSpan: 3,
              },
            ],
          },
          {
            id: "sec-vitals",
            name: "Vital Signs",
            isCollapsed: false,
            items: [
              {
                id: "f-pulse",
                label: "Pulse Rate",
                type: "numeric",
                gridSpan: 6,
              },
            ],
          },
          {
            id: "sec-labs",
            name: "Central Labs",
            isCollapsed: true,
            items: [],
          },
        ],
      };

      const wrapper = mount(CrfAuthoringCanvas, {
        props: {
          formSchema: store.activeForm,
          selectedFieldId: "f-subj",
        },
        global: { plugins: [pinia] },
      });

      await nextTick();
      await expect(wrapper).toBeAccessible();

      // Warning items are native <button> elements
      const warningBtn = wrapper.find("button.warning-item");
      expect(warningBtn.exists()).toBe(true);
      await warningBtn.trigger("click");
      expect(store.selectedFieldId).toBe("f-subj");

      // Move f-subj across section boundary from Demographics (last item) into Vital Signs (index 0)
      const sec1 = wrapper.findAllComponents(FormSectionContainer)[0];
      const moveDownBtn = sec1.find(".btn-field-move-down");
      expect(moveDownBtn.attributes("disabled")).toBeUndefined();
      await moveDownBtn.trigger("click");
      await nextTick();

      expect(store.activeForm.sections[0].items).toHaveLength(0);
      expect(store.activeForm.sections[1].items[0].id).toBe("f-subj");

      // Move f-subj back up across section boundary from Vital Signs (index 0) into Demographics
      const sec2 = wrapper.findAllComponents(FormSectionContainer)[1];
      const moveUpBtn = sec2
        .findAllComponents(CanvasFieldWidget)[0]
        .find(".btn-field-move-up");
      expect(moveUpBtn.attributes("disabled")).toBeUndefined();
      await moveUpBtn.trigger("click");
      await nextTick();

      expect(store.activeForm.sections[0].items[0].id).toBe("f-subj");

      // Move f-subj directly to Central Labs via the Inspector Section selector
      const sectionSelect = wrapper.find("select#inspect-field-section");
      expect(sectionSelect.exists()).toBe(true);
      await sectionSelect.setValue("sec-labs");
      await nextTick();

      expect(store.activeForm.sections[0].items).toHaveLength(0);
      expect(store.activeForm.sections[2].items[0].id).toBe("f-subj");
      expect(store.activeForm.sections[2].isCollapsed).toBe(false);
    });
  });

  describe("Acceptance 3: BodyMapWidget, DicomMprViewer, OncologyCalipers & ImagingView", () => {
    it("exposes accessible label on BodyMapWidget and records valid 0%-100% reticle pins without NaN%", async () => {
      const wrapper = mount(BodyMapWidget, {
        props: {
          label: "Dermatology Lesion Body Map",
          modelValue: [],
        },
      });

      await expect(wrapper).toBeAccessible();

      const region = wrapper.find(".canvas-wrapper");
      expect(region.attributes("aria-label")).toBe(
        "Dermatology Lesion Body Map"
      );

      // Even with 0x0 bounding rect in JSDOM, Enter & Space emit valid finite percentages (0..100, never NaN)
      await region.trigger("keydown", { key: "ArrowRight" });
      await region.trigger("keydown", { key: "ArrowDown" });
      await region.trigger("keydown", { key: "Enter" });

      const emitted = wrapper.emitted("update:modelValue");
      expect(emitted).toBeTruthy();
      const firstPins = emitted![0][0] as Array<{ x: number; y: number }>;
      expect(firstPins).toHaveLength(1);
      expect(firstPins[0].x).toBe(55);
      expect(firstPins[0].y).toBe(55);
      expect(Number.isNaN(firstPins[0].x)).toBe(false);
      expect(Number.isNaN(firstPins[0].y)).toBe(false);
      expect(wrapper.html()).not.toContain("NaN%");

      await wrapper.setProps({ modelValue: firstPins });
      await region.trigger("keydown", { key: "ArrowLeft" });
      await region.trigger("keydown", { key: " " });

      const secondPins = wrapper.emitted("update:modelValue")![1][0] as Array<{
        x: number;
        y: number;
      }>;
      expect(secondPins).toHaveLength(2);
      expect(secondPins[1].x).toBe(50);
      expect(secondPins[1].y).toBe(55);
      expect(wrapper.html()).not.toContain("NaN%");
    });

    it("creates valid calipers in DicomMprViewer via two single-pointer clicks AND keyboard reticle Enter presses", async () => {
      const wrapper = mount(DicomMprViewer, {
        props: {
          timepoint: "BASELINE",
          viewportLabel: "Viewport A",
          activeTool: "CALIPER_1D",
        },
      });

      await nextTick();
      await expect(wrapper).toBeAccessible();

      const axialCanvas = wrapper.find('canvas[aria-label*="Axial"]');
      expect(axialCanvas.exists()).toBe(true);
      vi.spyOn(axialCanvas.element, "getBoundingClientRect").mockReturnValue({
        left: 0,
        top: 0,
        width: 380,
        height: 380,
        right: 380,
        bottom: 380,
        x: 0,
        y: 0,
        toJSON: () => ({}),
      });

      // 1) Two single-pointer clicks without dragging
      axialCanvas.element.dispatchEvent(
        new MouseEvent("click", { clientX: 120, clientY: 160, bubbles: true })
      );
      axialCanvas.element.dispatchEvent(
        new MouseEvent("click", { clientX: 180, clientY: 160, bubbles: true })
      );
      await nextTick();

      const emitted = wrapper.emitted("caliperCreated");
      expect(emitted).toBeTruthy();
      expect(emitted!).toHaveLength(1);
      expect((emitted![0][0] as any).lengthMm).toBeCloseTo(45, 1);

      // 2) Keyboard reticle stepping + two Enter presses without pointer input
      axialCanvas.element.dispatchEvent(
        new KeyboardEvent("keydown", { key: "Enter", bubbles: true })
      );
      axialCanvas.element.dispatchEvent(
        new KeyboardEvent("keydown", {
          key: "ArrowRight",
          shiftKey: true,
          bubbles: true,
        })
      );
      axialCanvas.element.dispatchEvent(
        new KeyboardEvent("keydown", { key: "Enter", bubbles: true })
      );
      await nextTick();

      expect(wrapper.emitted("caliperCreated")).toHaveLength(2);
      expect((wrapper.emitted("caliperCreated")![1][0] as any).lengthMm).toBe(
        27
      );
    });

    it("supports keyboard form submission in OncologyCalipers and native series buttons in ImagingView", async () => {
      const calipersWrapper = mount(OncologyCalipers, {
        props: {
          annotations: [
            {
              id: "annot-1",
              lesion_id: "TL1",
              organ_site: "LIVER",
              lesion_type: "TARGET",
              longest_diameter_mm: 24.0,
              measurement_type: "1D_RECIST",
            },
          ],
          criteria: "RECIST_1_1",
        },
      });

      await expect(calipersWrapper).toBeAccessible();

      // Keyboard submit for manual caliper entry
      await calipersWrapper.find("#manual-diameter-input").setValue(32.5);
      await calipersWrapper
        .find("form.lesion-config-section")
        .trigger("submit");
      expect(calipersWrapper.emitted("caliperCreated")).toHaveLength(1);
      expect(
        (calipersWrapper.emitted("caliperCreated")![0][0] as any).lengthMm
      ).toBe(32.5);

      // Keyboard submit for 21 CFR Part 11 e-signature modal preserves exact payload schema
      await calipersWrapper.find("button.btn-primary-signoff").trigger("click");
      await nextTick();
      await calipersWrapper
        .find("#change-reason-input")
        .setValue("Primary Reader Official Timepoint Evaluation");
      await calipersWrapper.find("#signature-input").setValue("123456");
      await calipersWrapper.find("form.modal-card").trigger("submit");

      const signed = calipersWrapper.emitted("assessmentSigned");
      expect(signed).toHaveLength(1);
      expect(signed![0][0]).toEqual({
        reason: "Primary Reader Official Timepoint Evaluation",
        signature: "123456",
        aiAssisted: false,
        aiAnnotationsCount: 0,
        totalAnnotationsCount: 1,
      });

      // ImagingView series buttons
      const imagingWrapper = mount(ImagingView, {
        global: {
          plugins: [pinia],
          stubs: {
            DualReaderAdjudicationPanel: true,
            BatchDicomDropzoneModal: true,
          },
        },
      });
      await nextTick();

      const seriesButtons = imagingWrapper.findAll("button.series-item");
      expect(seriesButtons.length).toBeGreaterThanOrEqual(2);
      expect(seriesButtons[0].attributes("aria-pressed")).toBe("true");
      expect(seriesButtons[1].attributes("aria-pressed")).toBe("false");

      await seriesButtons[1].trigger("click");
      expect(seriesButtons[1].attributes("aria-pressed")).toBe("true");
    });
  });

  describe("Acceptance 5: SoAMatrixEditor role='grid' Navigation & Single-Pointer + Shift+Arrow Range Selection", () => {
    const visits = [
      { visit_name: "Screening", target_day: -7 },
      { visit_name: "Day 1", target_day: 1 },
      { visit_name: "Week 4", target_day: 28 },
    ];
    const activities = [
      {
        activity_name: "Vital Signs",
        cdash_domain: "VS",
        biomedical_concept_code: "C25298",
        assigned_visit_names: [],
      },
      {
        activity_name: "12-Lead ECG",
        cdash_domain: "EG",
        biomedical_concept_code: "C38054",
        assigned_visit_names: [],
      },
    ];

    it("exposes role='grid' navigation and completes multi-cell range selection via unmodified clicks and Shift+Arrow keys", async () => {
      const wrapper = mount(SoAMatrixEditor, {
        props: { activities, visits },
        attachTo: document.body,
      });

      try {
        await nextTick();
        await expect(wrapper).toBeAccessible();

        const gridTable = wrapper.find('table[role="grid"]');
        expect(gridTable.exists()).toBe(true);
        expect(gridTable.attributes("aria-label")).toBe(
          "Schedule of Activities Matrix"
        );

        const cells = wrapper.findAll('td[role="gridcell"].cell-toggle');
        expect(cells).toHaveLength(6);
        expect(cells[0].attributes("tabindex")).toBe("0");
        expect(cells[1].attributes("tabindex")).toBe("-1");

        // 1) Unmodified two-click single-pointer range selection (no Shift, no drag)
        const rangeModeBtn = wrapper.find("button.btn-range-mode");
        await rangeModeBtn.trigger("click");
        expect(rangeModeBtn.attributes("aria-pressed")).toBe("true");

        await cells[0].trigger("click"); // (0, 0)
        await cells[4].trigger("click"); // (1, 1) -> 2x2 = 4 cells
        expect((wrapper.vm as any).selectedRangeCellCount).toBe(4);

        await wrapper.find(".btn-range-assign").trigger("click");
        const emitted = wrapper.emitted("update:activities");
        expect(emitted).toBeTruthy();

        // Exit range mode & clear selection
        await wrapper.find(".btn-range-clear").trigger("click");
        await rangeModeBtn.trigger("click");
        expect(rangeModeBtn.attributes("aria-pressed")).toBe("false");

        // 2) Shift + Arrow key range selection
        await cells[0].trigger("keydown", {
          key: "ArrowRight",
          shiftKey: true,
        });
        await cells[1].trigger("keydown", {
          key: "ArrowDown",
          shiftKey: true,
        });
        expect((wrapper.vm as any).selectedRangeCellCount).toBe(4);
      } finally {
        wrapper.unmount();
      }
    });
  });

  describe("Acceptance 6: ClinicalGanttVisualizer Accessible Diagram, Focusable Visit Buttons & Zero Lint Suppressions", () => {
    it("exposes accessible SVG label, opens visit modal on Enter and Space, and leaves zero vuejs-accessibility suppressions", async () => {
      const clinicalStore = useClinicalStore(pinia);
      clinicalStore.currentUsdm = {
        arms: [{ arm_id: "arm-a", arm_name: "Experimental Arm A" }],
        epochs: [
          {
            epoch_id: "ep-1",
            epoch_name: "Screening Phase",
            sequence: 1,
            arm_id: "arm-a",
          },
        ],
        encounters: [
          {
            encounter_id: "enc-1",
            encounter_name: "Visit 1 Screening",
            epoch_id: "ep-1",
            sequence: 1,
            planned_day: 1,
            min_offset: 1,
            max_offset: 3,
          },
        ],
      } as any;

      const wrapper = mount(ClinicalGanttVisualizer, {
        global: { plugins: [pinia] },
      });

      await nextTick();
      await expect(wrapper).toBeAccessible();

      const svg = wrapper.find("svg");
      expect(svg.attributes("aria-label")).toBe(
        "Interactive Arm-Aware Gantt Pathway Visualizer"
      );

      const visitBtn = wrapper.find('g.gantt-visit-node[role="button"]');
      expect(visitBtn.exists()).toBe(true);
      expect(visitBtn.attributes("tabindex")).toBe("0");

      // Activate with Enter
      await visitBtn.trigger("keydown", { key: "Enter" });
      await nextTick();
      expect(wrapper.find("#gantt-planned-day").exists()).toBe(true);

      // Close and re-open with Space
      await wrapper
        .findAll("button")
        .find((b) => b.text() === "Cancel")!
        .trigger("click");
      await nextTick();
      expect(wrapper.find("#gantt-planned-day").exists()).toBe(false);

      await visitBtn.trigger("keydown", { key: " " });
      await nextTick();
      expect(wrapper.find("#gantt-planned-day").exists()).toBe(true);

      // Verify zero vuejs-accessibility suppressions across all target files
      const repoRoot = path.resolve(__dirname, "../../../..");
      const guardedFiles = [
        "packages/ui/src/components/MediaPlayer.vue",
        "packages/ui/src/components/clinical/ViewportPanPad.vue",
        "apps/web/src/components/crf/widgets/BodyMapWidget.vue",
        "apps/web/src/components/imaging/DicomMprViewer.vue",
        "apps/web/src/components/imaging/OncologyCalipers.vue",
        "apps/web/src/views/ImagingView.vue",
        "apps/web/src/components/crf/CanvasFieldWidget.vue",
        "apps/web/src/components/crf/FormSectionContainer.vue",
        "apps/web/src/components/crf/CrfAuthoringCanvas.vue",
        "apps/web/src/components/clinical/SoAMatrixEditor.vue",
        "apps/web/src/components/clinical/ClinicalGanttVisualizer.vue",
        "apps/web/src/components/econsent/QuizTemplateDrawer.vue",
      ];

      for (const relPath of guardedFiles) {
        const content = fs.readFileSync(path.join(repoRoot, relPath), "utf8");
        expect(content).not.toMatch(/eslint-disable[^\n]*vuejs-accessibility/);
      }
    });
  });
});
