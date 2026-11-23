import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import BatchUploadDrawer from "../src/components/etmf/BatchUploadDrawer.vue";
import { useEtmfStore } from "../src/stores/etmf.js";

describe("BatchUploadDrawer.vue", () => {
  let pinia;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
  });

  it("renders overlay and drawer when isOpen is true", () => {
    const wrapper = mount(BatchUploadDrawer, {
      props: {
        isOpen: true,
        studyId: "STUDY-USDM-001",
      },
      global: {
        plugins: [pinia],
      },
    });

    expect(wrapper.find(".batch-drawer-overlay").exists()).toBe(true);
    expect(wrapper.text()).toContain(
      "Batch Ingestion & AI Auto-Classification"
    );
    expect(wrapper.find("input#batch-reason-input").exists()).toBe(true);
  });

  it("does not render when isOpen is false", () => {
    const wrapper = mount(BatchUploadDrawer, {
      props: {
        isOpen: false,
      },
      global: {
        plugins: [pinia],
      },
    });

    expect(wrapper.find(".batch-drawer-overlay").exists()).toBe(false);
  });

  it("enforces 50 file limit per batch transaction", async () => {
    const wrapper = mount(BatchUploadDrawer, {
      props: {
        isOpen: true,
      },
      global: {
        plugins: [pinia],
      },
    });

    const dummyFiles = Array.from(
      { length: 51 },
      (_, i) => new File(["test"], `file_${i}.pdf`, { type: "application/pdf" })
    );

    const input = wrapper.find('input[type="file"]');
    Object.defineProperty(input.element, "files", {
      value: dummyFiles,
    });
    await input.trigger("change");

    expect(wrapper.text()).toContain(
      "Maximum limit of 50 documents per batch transaction exceeded."
    );
  });

  it("triggers batch auto-classification staging on button click", async () => {
    const wrapper = mount(BatchUploadDrawer, {
      props: {
        isOpen: true,
        studyId: "STUDY-USDM-001",
      },
      global: {
        plugins: [pinia],
      },
    });

    const store = useEtmfStore();
    const spy = vi.spyOn(store, "stageQcDocument").mockResolvedValue({
      document_id: "DOC-STAGED-1",
      status: "TECHNICAL_QC",
      intelligence_report: {
        filename: "FDA_1572.pdf",
        confidence_tier: "HIGH",
        primary_classification: {
          zone: 5,
          section: "05.02",
          artifact_code: "05.02.01",
          artifact_type: "FDA Form 1572",
          confidence: 0.98,
        },
      },
    });

    const file = new File(["FDA 1572 content"], "FDA_1572.pdf", {
      type: "application/pdf",
    });
    const input = wrapper.find('input[type="file"]');
    Object.defineProperty(input.element, "files", {
      value: [file],
    });
    await input.trigger("change");

    expect(wrapper.text()).toContain("Upload Queue (1 / 50 Files)");

    const startBtn = wrapper.find("button.btn-submit-batch");
    expect(startBtn.attributes("disabled")).toBeUndefined();
    await startBtn.trigger("click");
    await new Promise((resolve) => setTimeout(resolve, 100));
    await flushPromises();

    expect(spy).toHaveBeenCalled();
  });

  it("renders formatted clinical labels and structured AI provenance with a collapsed technical record disclosure", async () => {
    const wrapper = mount(BatchUploadDrawer, {
      props: {
        isOpen: true,
        studyId: "STUDY-USDM-001",
      },
      global: {
        plugins: [pinia],
      },
    });

    const store = useEtmfStore();
    vi.spyOn(store, "stageQcDocument").mockResolvedValue({
      document_id: "DOC-STAGED-2",
      status: "TECHNICAL_QC",
      intelligence_report: {
        filename: "Investigator_CV.pdf",
        confidence_tier: "HIGH",
        qc_recommendation: "CRA_QC_REQUIRED",
        ai_generation_manifest: {
          classifier_model: "tmf-v3.2",
          extraction_confidence: 0.94,
        },
        primary_classification: {
          zone: 5,
          section: "05.02",
          artifact_code: "05.02.03",
          artifact_type: "Investigator CV",
          confidence: 0.94,
        },
      },
    });

    const file = new File(["CV content"], "Investigator_CV.pdf", {
      type: "application/pdf",
    });
    const input = wrapper.find('input[type="file"]');
    Object.defineProperty(input.element, "files", { value: [file] });
    await input.trigger("change");

    expect(wrapper.find(".queue-row .badge").text()).toBe("Pending");

    await wrapper.find("button.btn-submit-batch").trigger("click");
    await new Promise((resolve) => setTimeout(resolve, 50));
    await flushPromises();

    expect(wrapper.find(".queue-row .badge").text()).toBe("Staged");
    await wrapper.find("button.btn-inspect-item").trigger("click");
    await flushPromises();

    const inspectionText = wrapper.find(".item-inspection-card").text();
    expect(inspectionText).toContain("CRA QC Required");
    expect(inspectionText).toContain("Classifier Model");
    expect(inspectionText).toContain("tmf-v3.2");
    expect(inspectionText).toContain("Extraction Confidence");
    expect(inspectionText).toContain("94%");

    const disclosure = wrapper.find(
      ".item-inspection-card details.support-details"
    );
    expect(disclosure.exists()).toBe(true);
    expect(disclosure.attributes("open")).toBeUndefined();
  });
});
