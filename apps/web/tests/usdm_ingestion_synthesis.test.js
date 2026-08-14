import { describe, it, expect, beforeEach, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { useClinicalStore } from "../src/stores/clinical";
import { useDesignerStore } from "../src/stores/designer";
import { useAuthStore } from "../src/stores/auth.js";
import { mount } from "@vue/test-utils";
import MdrView from "../src/views/MdrView.vue";
import { stateTrackingPlugin } from "../src/stores/plugins.js";

// Mock router
vi.mock("vue-router", () => ({
  useRoute: () => ({
    query: { tab: "soa" },
  }),
  useRouter: () => ({
    push: vi.fn(),
  }),
}));

// Mock terminologyClient
vi.mock("../src/api/terminologyClient.js", () => ({
  terminologyClient: {
    searchTerminology: vi.fn().mockResolvedValue({ results: [] }),
  },
}));

describe("M5: USDM Ingestion & Synthesis Workspace Experience (MdrView.vue)", () => {
  let pinia;

  beforeEach(() => {
    pinia = createPinia();
    pinia.use(stateTrackingPlugin);
    setActivePinia(pinia);

    const authStore = useAuthStore();
    authStore.accessToken = "mock-keycloak-jwt-token";
    authStore.isAuthenticated = true;
    authStore.isDemoMode = false;

    if (typeof window !== "undefined" && window.localStorage) {
      window.localStorage.clear();
    }
    vi.resetAllMocks();
  });

  it("renders the #btn-open-usdm-modal button in the SoA header toolbar", () => {
    const wrapper = mount(MdrView, {
      global: {
        plugins: [pinia],
        stubs: {
          ClinicalSoAMatrix: true,
          ClinicalGanttVisualizer: true,
          CrfAuthoringCanvas: true,
          OnboardingTour: true,
          ReasonModal: true,
        },
      },
    });

    const openBtn = wrapper.find("#btn-open-usdm-modal");
    expect(openBtn.exists()).toBe(true);
    expect(openBtn.text()).toContain("Ingest & Synthesize USDM Protocol");
  });

  it("opens USDM Ingestion Modal upon clicking #btn-open-usdm-modal and loads sample protocol", async () => {
    const wrapper = mount(MdrView, {
      global: {
        plugins: [pinia],
        stubs: {
          ClinicalSoAMatrix: true,
          ClinicalGanttVisualizer: true,
          CrfAuthoringCanvas: true,
          OnboardingTour: true,
          ReasonModal: true,
        },
      },
    });

    // Click open button
    await wrapper.find("#btn-open-usdm-modal").trigger("click");

    const modal = wrapper.find("#usdm-ingestion-modal");
    expect(modal.exists()).toBe(true);
    expect(modal.text()).toContain("Zero-Click USDM Study Ingestion & Synthesis");

    // Dropzone exists
    const dropzone = modal.find(".dropzone-area");
    expect(dropzone.exists()).toBe(true);

    // Textarea exists
    const textarea = modal.find("#usdm-payload-input");
    expect(textarea.exists()).toBe(true);

    // Load sample button
    const loadSampleBtn = modal.find("#btn-load-sample-usdm");
    expect(loadSampleBtn.exists()).toBe(true);
    await loadSampleBtn.trigger("click");

    // Expect valid badge to show up
    expect(modal.html()).toContain("Valid USDM Schema (v4.0 Compliant)");
    expect(modal.html()).toContain("CDNC-2026-001");
  });

  it("performs client-side schema validation and disables synthesize button on invalid payload", async () => {
    const wrapper = mount(MdrView, {
      global: {
        plugins: [pinia],
        stubs: {
          ClinicalSoAMatrix: true,
          ClinicalGanttVisualizer: true,
          CrfAuthoringCanvas: true,
          OnboardingTour: true,
          ReasonModal: true,
        },
      },
    });

    await wrapper.find("#btn-open-usdm-modal").trigger("click");
    const textarea = wrapper.find("#usdm-payload-input");

    // Enter invalid JSON
    await textarea.setValue("{ invalid_json: ");
    await textarea.trigger("input");

    const modal = wrapper.find("#usdm-ingestion-modal");
    expect(modal.html()).toContain("Schema Validation Issue");

    const synthesizeBtn = wrapper.find("#btn-synthesize-usdm");
    expect(synthesizeBtn.attributes("disabled")).toBeDefined();
  });

  it("executes zero-click synthesis and renders real-time metrics dashboard card", async () => {
    const wrapper = mount(MdrView, {
      global: {
        plugins: [pinia],
        stubs: {
          ClinicalSoAMatrix: true,
          ClinicalGanttVisualizer: true,
          CrfAuthoringCanvas: true,
          OnboardingTour: true,
          ReasonModal: true,
        },
      },
    });

    // Open modal and load sample
    await wrapper.find("#btn-open-usdm-modal").trigger("click");
    await wrapper.find("#btn-load-sample-usdm").trigger("click");

    // Click synthesize button
    const synthesizeBtn = wrapper.find("#btn-synthesize-usdm");
    expect(synthesizeBtn.attributes("disabled")).toBeUndefined();
    await synthesizeBtn.trigger("click");

    // Modal closes and synthesis metrics card appears
    expect(wrapper.find("#usdm-ingestion-modal").exists()).toBe(false);

    const metricsCard = wrapper.find(".synthesis-metrics-card");
    expect(metricsCard.exists()).toBe(true);

    const cardHtml = metricsCard.html();

    // Assert protocol identity
    expect(cardHtml).toContain("CDNC-2026-001");
    expect(cardHtml).toContain("A Phase II Randomized Study of Novel Therapeutic vs Control in Advanced Solid Tumors");
    expect(cardHtml).toContain("Phase: Phase II");
    expect(cardHtml).toContain("TA: Oncology");

    // Assert graph entity counts
    const cardText = metricsCard.text();
    expect(cardText).toContain("Neo4j Graph Entities");
    expect(cardText).toContain("Arms: 2");
    expect(cardText).toContain("Epochs: 3");
    expect(cardText).toContain("Visits/Encounters: 4");
    expect(cardText).toContain("Activities: 7");
    expect(cardText).toContain("Criteria: 4");

    // Assert synthesized eCRFs and variables
    expect(cardText).toContain("Synthesized CDASH eCRFs");
    expect(cardText).toContain("7 forms");
    expect(cardText).toContain("CDASH Variables: 34");
    expect(cardText).toContain("VAS Slider, 74-Zone Body Map");

    // Assert automated validation rules
    expect(cardText).toContain("Automated Validation Rules");
    expect(cardText).toContain("6 edit checks");
    expect(cardText).toContain("CHK_VS_BP_SANITY");
    expect(cardText).toContain("CHK_EG_QTC_ALERT");

    // Assert DIA TMF EDL seeding
    expect(cardText).toContain("Seeded DIA TMF EDL");
    expect(cardText).toContain("14 documents");
    expect(cardText).toContain("Pre-Seeded Zones: 1, 2, 4, 5 (of 1–11)");

    // Assert latency benchmark badge (< 3.0s SLA)
    expect(cardText).toContain("Latency:");
    expect(cardText).toContain("< 3.0s SLA Compliant");

    // Assert action buttons exist
    expect(wrapper.find("#btn-promote-to-edc").exists()).toBe(true);
    expect(wrapper.find("#btn-inspect-crf").exists()).toBe(true);
    expect(wrapper.find("#btn-view-soa").exists()).toBe(true);
  });

  it("promotes synthesized study build to active EDC via Part 11 electronic signature", async () => {
    const clinicalStore = useClinicalStore();
    const designerStore = useDesignerStore();

    const wrapper = mount(MdrView, {
      global: {
        plugins: [pinia],
        stubs: {
          ClinicalSoAMatrix: true,
          ClinicalGanttVisualizer: true,
          CrfAuthoringCanvas: true,
          OnboardingTour: true,
          ReasonModal: {
            props: ["show", "title", "options", "defaultOption"],
            emits: ["confirm", "cancel"],
            template: `
              <div v-if="show" class="mock-reason-modal">
                <span>{{ title }}</span>
                <button id="mock-confirm-reason" @click="$emit('confirm', 'Initial Protocol Synthesis Build Activation')">
                  Confirm
                </button>
              </div>
            `,
          },
        },
      },
    });

    // Execute synthesis first
    await wrapper.find("#btn-open-usdm-modal").trigger("click");
    await wrapper.find("#btn-load-sample-usdm").trigger("click");
    await wrapper.find("#btn-synthesize-usdm").trigger("click");

    // Click promote button
    await wrapper.find("#btn-promote-to-edc").trigger("click");

    // Reason modal opens
    expect(wrapper.find(".mock-reason-modal").exists()).toBe(true);

    // Confirm electronic signature
    await wrapper.find("#mock-confirm-reason").trigger("click");
    await new Promise((resolve) => setTimeout(resolve, 50));
    await wrapper.vm.$nextTick();

    // Assert Pinia clinical store active study build updated
    expect(clinicalStore.activeStudyId).toBe("CDNC-2026-001");
    expect(clinicalStore.activeStudyVersionId).toBe("CDNC-2026-001_v1");
    expect(clinicalStore.currentUsdm.studyId).toBe("CDNC-2026-001");
    expect(clinicalStore.currentUsdm.arms.length).toBe(2);
    expect(clinicalStore.currentUsdm.epochs.length).toBe(3);
    expect(clinicalStore.currentUsdm.encounters.length).toBe(4);
    expect(clinicalStore.currentUsdm.rows.length).toBe(7);

    // Assert Part 11 audit ledger entry recorded
    const ledger = clinicalStore.ledgerBlocks;
    const promotionBlock = ledger.find((b) => b.action === "USDM_SYNTHESIS_PROMOTION");
    expect(promotionBlock).toBeDefined();
    expect(promotionBlock.reason).toBe("Initial Protocol Synthesis Build Activation");
    expect(promotionBlock.details.studyId).toBe("CDNC-2026-001");

    // Assert designer store active form updated
    expect(designerStore.activeForm.id).toBe("form-synthesized-CDNC-2026-001");
    expect(designerStore.activeForm.sections.length).toBe(5);
    expect(designerStore.activeForm.layoutJustification).toBe(
      "Initial Protocol Synthesis Build Activation"
    );

    // Assert success feedback banner and badge
    expect(wrapper.html()).toContain("Active EDC Study Build");
    expect(wrapper.html()).toContain("successfully promoted to Active EDC!");

    // Assert navigation buttons work
    await wrapper.find("#btn-inspect-crf").trigger("click");
    // Tab changes to canvas
    expect(wrapper.find(".tab-btn-canvas").classes()).toContain("btn-primary");
  });
});
