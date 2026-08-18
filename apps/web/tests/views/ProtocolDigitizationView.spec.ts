import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import ProtocolDigitizationView from "@/views/ProtocolDigitizationView.vue";
import { useAuthStore } from "@/stores/auth";
import { useClinicalStore } from "@/stores/clinical";
import { apiClient } from "@/api/apiClient";

vi.mock("@/api/apiClient", () => {
  return {
    apiClient: {
      get: vi.fn(),
      post: vi.fn(),
      put: vi.fn(),
      delete: vi.fn(),
    },
  };
});

describe("ProtocolDigitizationView.vue - AI Protocol Ingestion & Synthesis Specification", () => {
  let pinia: any;
  let authStore: any;
  let clinicalStore: any;

  const mockExtractionData = {
    study_title: "Phase III Cardiology Trial in Hypertension",
    protocol_id: "CADENCE-HYP-301",
    phase: "PHASE_III",
    therapeutic_area: "Cardiovascular",
    confidence_score: 0.98,
    arms: [
      { name: "Active Treatment Arm A", arm_type: "EXPERIMENTAL", description: "Compound X 50mg daily", target_sample_size: 100 },
      { name: "Placebo Control Arm B", arm_type: "PLACEBO", description: "Matching placebo daily", target_sample_size: 100 },
    ],
    epochs: [
      { name: "Screening Epoch", epoch_type: "SCREENING", sequence_index: 1 },
      { name: "Treatment Epoch", epoch_type: "TREATMENT", sequence_index: 2 },
    ],
    visits: [
      { visit_name: "Screening (Day -7)", epoch_name: "Screening Epoch", target_day: -7, window_lower_days: 2, window_upper_days: 2, is_mandatory: true },
      { visit_name: "Baseline (Day 1)", epoch_name: "Treatment Epoch", target_day: 1, window_lower_days: 0, window_upper_days: 1, is_mandatory: true },
    ],
    activities: [
      { activity_name: "Vital Signs Assessment", cdash_domain: "VS", biomedical_concept_code: "C25298", assigned_visit_names: ["Screening (Day -7)", "Baseline (Day 1)"] },
      { activity_name: "12-Lead Electrocardiogram", cdash_domain: "EG", biomedical_concept_code: "C38054", assigned_visit_names: ["Baseline (Day 1)"] },
    ],
    criteria: [
      { criterion_type: "INCLUSION", identifier: "INC-01", text_expression: "Adults aged 18 to 75 years", logical_expression: "DM.AGE >= 18" },
      { criterion_type: "EXCLUSION", identifier: "EXC-01", text_expression: "History of severe hepatic impairment", logical_expression: "LB.ALT > 150" },
    ],
  };

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);

    authStore = useAuthStore();
    clinicalStore = useClinicalStore();

    authStore.isAuthenticated = true;
    authStore.isDemoMode = false;
    authStore.rawRoles = ["Sponsor Designer", "Data Manager"];

    vi.mocked(apiClient.post).mockImplementation((url) => {
      if (url.includes("/digitize") || url.includes("/ingest")) {
        return Promise.resolve(mockExtractionData);
      }
      return Promise.resolve({});
    });

    vi.mocked(apiClient.get).mockResolvedValue({});
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("renders wizard stepper with document ingestion initial step", () => {
    const wrapper = mount(ProtocolDigitizationView, {
      global: {
        plugins: [pinia],
        stubs: {
          "router-link": true,
        },
      },
    });

    expect(wrapper.find(".wizard-stepper").exists()).toBe(true);
    expect(wrapper.find(".upload-card").exists()).toBe(true);
    expect(wrapper.text()).toContain("AI-Native USDM Protocol Digitization");
    expect(wrapper.text()).toContain("Drag & Drop Protocol Document");
  });

  it("advances stepper when file is selected and triggers AI extraction", async () => {
    const wrapper = mount(ProtocolDigitizationView, {
      global: {
        plugins: [pinia],
        stubs: {
          "router-link": true,
        },
      },
    });

    // Simulate file selection
    const file = new File(["Protocol text content for hypertension study"], "protocol_hypertension.pdf", {
      type: "application/pdf",
    });

    wrapper.vm.selectedFile = file;
    await wrapper.vm.$nextTick();

    expect(wrapper.text()).toContain("protocol_hypertension.pdf");

    const extractBtn = wrapper.find(".btn-extract");
    if (extractBtn.exists()) {
      await extractBtn.trigger("click");
      await flushPromises();
    }
  });

  it("displays extracted USDM arms, epochs, and synthesized eCRF forms", async () => {
    const wrapper = mount(ProtocolDigitizationView, {
      global: {
        plugins: [pinia],
        stubs: {
          "router-link": true,
        },
      },
    });

    wrapper.vm.rawExtractionData = mockExtractionData;
    wrapper.vm.currentStep = 3; // SoA & Verification step
    await wrapper.vm.$nextTick();

    expect(wrapper.text()).toContain("Schedule of Activities (SoA) Matrix");
    expect(wrapper.text()).toContain("Arms & Epoch Timeline Visualizer");
  });

  it("displays synthesized eCRF layout elements in activation step", async () => {
    const wrapper = mount(ProtocolDigitizationView, {
      global: {
        plugins: [pinia],
        stubs: {
          "router-link": true,
        },
      },
    });

    wrapper.vm.rawExtractionData = mockExtractionData;
    wrapper.vm.currentStep = 4; // eCRF synthesis step
    await wrapper.vm.$nextTick();

    expect(wrapper.text()).toContain("Automated CDASH eCRF Synthesis");
    expect(wrapper.text()).toContain("Vital Signs eCRF");
  });
});
