import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import fs from "fs";
import path from "path";
import EhrReconciliationDrawer from "../src/components/clinical/EhrReconciliationDrawer.vue";
import ShareDrawer from "../src/components/fileshare/ShareDrawer.vue";
import ReadabilityDrawer from "../src/components/econsent/ReadabilityDrawer.vue";
import { interopService } from "../src/api/interop";
import { econsentService } from "../src/api/econsent";

vi.mock("../src/api/interop", () => ({
  interopService: {
    listEhrCandidates: vi.fn(() =>
      Promise.resolve({
        candidates: [
          {
            id: "cand-1",
            study_id: "STUDY-101",
            site_id: "SITE-01",
            subject_id: "SUBJ-001",
            subject_pseudonym: "PAT-001",
            ehr_system_id: "EHR-EPIC",
            fhir_resource_type: "Observation",
            fhir_resource_id: "obs-123",
            source_timestamp: "2026-09-30T10:00:00Z",
            target_domain: "VS",
            target_variable: "SYSBP",
            cdash_test: "Systolic Blood Pressure",
            candidate_value: 120,
            candidate_unit: "mmHg",
            current_ecrf_value: 118,
            discrepancy_type: "VALUE_MISMATCH",
            confidence_score: 0.98,
            mapping_provenance: "LOINC 8480-6 mapping",
            raw_fhir_resource: { resourceType: "Observation" },
            status: "PENDING_REVIEW",
            version_index: 1,
          },
        ],
      })
    ),
    acceptEhrCandidate: vi.fn(),
    rejectEhrCandidate: vi.fn(),
    raiseEhrClinicalQuery: vi.fn(),
  },
}));

vi.mock("../src/api/econsent", () => ({
  econsentService: {
    analyzeReadability: vi.fn(() =>
      Promise.resolve({
        metrics: {
          flesch_kincaid_grade_level: 7.2,
          is_target_grade_level: true,
          interpretation: "Plain language",
          dale_chall_score: 6.1,
          dale_chall_grade_level: "5th-6th Grade",
          difficult_word_count: 3,
          word_count: 50,
        },
      })
    ),
    harmonizeReadability: vi.fn(() =>
      Promise.resolve({
        harmonized_metrics: {
          flesch_kincaid_grade_level: 6.5,
          is_target_grade_level: true,
          interpretation: "Target grade level",
          dale_chall_score: 5.5,
          dale_chall_grade_level: "5th-6th Grade",
          difficult_word_count: 1,
          word_count: 50,
        },
        substitutions: [
          {
            original_term: "myocardial infarction",
            suggested_term: "heart attack",
            confidence_score: 0.98,
            rationale: "Common term for patient comprehension",
            category: "Cardiology",
          },
        ],
      })
    ),
  },
}));

describe("Drawer Responsiveness & Viewport Tokens", () => {
  const webSrcDir = path.resolve(__dirname, "../src");
  const uiDir = path.resolve(__dirname, "../../../packages/ui");

  beforeEach(() => {
    setActivePinia(createPinia());
  });

  describe("Requirement 1: CSS Design Tokens for Drawer Widths", () => {
    it("defines viewport-relative drawer tokens in tokens.css", () => {
      const tokensContent = fs.readFileSync(
        path.join(uiDir, "tokens.css"),
        "utf-8"
      );
      expect(tokensContent).toContain("--drawer-width-sm: min(100vw, 440px)");
      expect(tokensContent).toContain("--drawer-width-md: min(100vw, 480px)");
      expect(tokensContent).toContain("--drawer-width-lg: min(100vw, 640px)");
      expect(tokensContent).toContain("--drawer-width-xl: min(100vw, 920px)");
      expect(tokensContent).toContain(
        "--semantic-drawer-width-xl: var(--drawer-width-xl)"
      );
    });

    it("uses drawer width tokens in EhrReconciliationDrawer.vue, ShareDrawer.vue, and ReadabilityDrawer.vue", () => {
      const ehrContent = fs.readFileSync(
        path.join(webSrcDir, "components/clinical/EhrReconciliationDrawer.vue"),
        "utf-8"
      );
      const shareContent = fs.readFileSync(
        path.join(webSrcDir, "components/fileshare/ShareDrawer.vue"),
        "utf-8"
      );
      const readabilityContent = fs.readFileSync(
        path.join(webSrcDir, "components/econsent/ReadabilityDrawer.vue"),
        "utf-8"
      );

      expect(ehrContent).toContain("--semantic-drawer-width-xl");
      expect(shareContent).toContain("--semantic-drawer-width-md");
      expect(readabilityContent).toContain("--semantic-drawer-width-sm");
    });
  });

  describe("Requirement 2 & 3: Mobile Breakpoints and Grid Refactoring", () => {
    it("includes @media (max-width: 640px) mobile overrides in EhrReconciliationDrawer.vue", () => {
      const content = fs.readFileSync(
        path.join(webSrcDir, "components/clinical/EhrReconciliationDrawer.vue"),
        "utf-8"
      );
      expect(content).toContain("@media (max-width: 640px)");
      expect(content).toContain("width: 100vw");
      expect(content).toContain("grid-template-columns: 1fr"); // split-screen-grid vertical stack
      expect(content).toContain("grid-template-columns: repeat(2, 1fr)"); // metrics-grid 2 columns
    });

    it("includes @media (max-width: 640px) mobile overrides in ReadabilityDrawer.vue", () => {
      const content = fs.readFileSync(
        path.join(webSrcDir, "components/econsent/ReadabilityDrawer.vue"),
        "utf-8"
      );
      expect(content).toContain("@media (max-width: 640px)");
      expect(content).toContain("width: 100vw");
      expect(content).toContain("grid-template-columns: 1fr"); // metrics-grid 1 column
    });

    it("includes @media (max-width: 640px) mobile overrides in ShareDrawer.vue", () => {
      const content = fs.readFileSync(
        path.join(webSrcDir, "components/fileshare/ShareDrawer.vue"),
        "utf-8"
      );
      expect(content).toContain("@media (max-width: 640px)");
      expect(content).toContain("width: 100vw");
    });
  });

  describe("Requirement 4 & 5: Action Button Stacking and Form Input Constraints", () => {
    it("stacks action buttons vertically and constrains inputs in mobile styles", () => {
      const ehrContent = fs.readFileSync(
        path.join(webSrcDir, "components/clinical/EhrReconciliationDrawer.vue"),
        "utf-8"
      );
      const shareContent = fs.readFileSync(
        path.join(webSrcDir, "components/fileshare/ShareDrawer.vue"),
        "utf-8"
      );
      const readabilityContent = fs.readFileSync(
        path.join(webSrcDir, "components/econsent/ReadabilityDrawer.vue"),
        "utf-8"
      );

      // Check max-width: 100% on inputs
      expect(ehrContent).toContain("max-width: 100%");
      expect(shareContent).toContain("max-width: 100%");
      expect(readabilityContent).toContain("max-width: 100%");

      // Check vertical flex stacking in action-btn-group / drawer-actions / action-bar
      expect(ehrContent).toContain("flex-direction: column");
      expect(shareContent).toContain("flex-direction: column");
      expect(readabilityContent).toContain("flex-direction: column");

      // Check 48px touch target compliance on mobile
      expect(ehrContent).toContain(
        "min-height: var(--semantic-touch-target-min, 48px)"
      );
      expect(shareContent).toContain(
        "min-height: var(--semantic-touch-target-min, 48px)"
      );
      expect(readabilityContent).toContain(
        "min-height: var(--semantic-touch-target-min, 48px)"
      );
    });
  });

  describe("Component Rendering & Mobile Viewport Compatibility (375px, 414px, 768px, 1280px)", () => {
    const viewports = [375, 414, 768, 1280];

    viewports.forEach((viewportWidth) => {
      it(`renders EhrReconciliationDrawer cleanly at ${viewportWidth}px width`, async () => {
        const wrapper = mount(EhrReconciliationDrawer, {
          props: {
            isOpen: true,
            studyId: "STUDY-TEST",
            subjectId: "SUBJ-TEST",
          },
        });
        await flushPromises();
        expect(wrapper.find(".ehr-reconciliation-drawer").exists()).toBe(true);
        expect(wrapper.find(".split-screen-grid").exists()).toBe(true);
        expect(wrapper.find(".metrics-grid").exists()).toBe(true);
      });

      it(`renders ShareDrawer cleanly at ${viewportWidth}px width`, async () => {
        const wrapper = mount(ShareDrawer, {
          props: {
            isOpen: true,
            files: [{ id: "f1", filename: "Protocol_v1.pdf" }],
          },
        });
        await wrapper.vm.$nextTick();
        expect(wrapper.find(".share-drawer").exists()).toBe(true);
        expect(wrapper.find(".drawer-header").exists()).toBe(true);
      });

      it(`renders ReadabilityDrawer cleanly at ${viewportWidth}px width`, async () => {
        const wrapper = mount(ReadabilityDrawer, {
          props: {
            isOpen: true,
            text: "Sample informed consent text for testing.",
          },
        });
        await wrapper.vm.$nextTick();
        expect(wrapper.find(".readability-drawer").exists()).toBe(true);
        expect(wrapper.find(".metrics-grid").exists()).toBe(true);
      });
    });
  });

  describe("Requirement 6 & ADR-2657: Bounded ClinicalDrawer Adoption Across All 13 Web Drawers", () => {
    it("renders all 13 *Drawer.vue components in apps/web/src/components through ClinicalDrawer with bounded body scroll", () => {
      const drawerPaths = [
        "components/ContextualHelpDrawer.vue",
        "components/OnboardingChecklistDrawer.vue",
        "components/SynonymImportDrawer.vue",
        "components/UpversioningImpactDrawer.vue",
        "components/clinical/BulkIpKitReconciliationDrawer.vue",
        "components/clinical/EhrReconciliationDrawer.vue",
        "components/clinical/UsdmConceptStagingDrawer.vue",
        "components/econsent/QuizTemplateDrawer.vue",
        "components/econsent/ReadabilityDrawer.vue",
        "components/etmf/BatchUploadDrawer.vue",
        "components/etmf/ExportJobDrawer.vue",
        "components/fileshare/ShareDrawer.vue",
        "components/tickets/TicketDetailDrawer.vue",
      ];

      for (const relPath of drawerPaths) {
        const src = fs.readFileSync(path.join(webSrcDir, relPath), "utf-8");
        expect(src, `${relPath} must use <ClinicalDrawer`).toContain(
          "<ClinicalDrawer"
        );
        expect(src, `${relPath} must not use #subheader slot`).not.toContain(
          "#subheader"
        );
      }

      const bulkSrc = fs.readFileSync(
        path.join(
          webSrcDir,
          "components/clinical/BulkIpKitReconciliationDrawer.vue"
        ),
        "utf-8"
      );
      expect(bulkSrc).toContain("@media (max-width: 480px)");
      expect(bulkSrc).toContain("attr(data-label)");

      const synonymSrc = fs.readFileSync(
        path.join(webSrcDir, "components/SynonymImportDrawer.vue"),
        "utf-8"
      );
      expect(synonymSrc).not.toContain("fixed inset-0");
    });
  });
});
