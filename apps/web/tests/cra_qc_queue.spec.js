import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import CraQcQueue from "../src/components/etmf/CraQcQueue.vue";
import { useEtmfStore } from "../src/stores/etmf.js";

describe("CraQcQueue.vue", () => {
  let pinia;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
  });

  it("renders empty state when no items in QC queue", () => {
    const wrapper = mount(CraQcQueue, {
      global: {
        plugins: [pinia],
      },
    });

    expect(wrapper.text()).toContain("CRA Quality Control Review Queue");
    expect(wrapper.text()).toContain(
      "No staged documents pending Quality Control review"
    );
  });

  it("displays staged items in queue table", async () => {
    const store = useEtmfStore();
    store.qcQueue = [
      {
        document_id: "DOC-STAGED-101",
        study_id: "STUDY-USDM-001",
        site_id: "SITE-101",
        filename: "Investigator_CV.pdf",
        status: "TECHNICAL_QC",
        staged_at: "2026-10-01T10:00:00Z",
        staged_by: "crc.user",
        intelligence_report: {
          confidence_tier: "HIGH",
          primary_classification: {
            zone: 5,
            section: "05.02",
            artifact_code: "05.02.03",
            artifact_type: "Investigator CV",
            confidence: 0.95,
          },
        },
      },
    ];

    const wrapper = mount(CraQcQueue, {
      global: {
        plugins: [pinia],
      },
    });

    expect(wrapper.text()).toContain("Investigator_CV.pdf");
    expect(wrapper.text()).toContain("05.02.03");
    expect(wrapper.text()).toContain("Investigator CV");
    expect(wrapper.text()).toContain("95% HIGH");
  });

  it("executes batch accept for selected items with Part 11 reason", async () => {
    const store = useEtmfStore();
    store.qcQueue = [
      {
        document_id: "DOC-STAGED-101",
        study_id: "STUDY-USDM-001",
        filename: "Investigator_CV.pdf",
        status: "TECHNICAL_QC",
        intelligence_report: {
          confidence_tier: "HIGH",
          primary_classification: {
            zone: 5,
            section: "05.02",
            artifact_code: "05.02.03",
            artifact_type: "Investigator CV",
            confidence: 0.95,
          },
        },
      },
    ];

    vi.spyOn(store, "batchAcceptClassifications").mockResolvedValue([
      { success: true },
    ]);

    const wrapper = mount(CraQcQueue, {
      global: {
        plugins: [pinia],
      },
    });

    // Select item
    const checkbox = wrapper.find('tbody input[type="checkbox"]');
    await checkbox.setValue(true);

    const batchAcceptBtn = wrapper.find("button.btn-batch-accept");
    expect(batchAcceptBtn.text()).toContain("Batch Accept Classifications (1)");
    await batchAcceptBtn.trigger("click");

    expect(wrapper.find(".qc-modal").exists()).toBe(true);

    const confirmBtn = wrapper.find("button.btn-confirm-batch-accept");
    await confirmBtn.trigger("click");

    expect(store.batchAcceptClassifications).toHaveBeenCalledWith(
      ["DOC-STAGED-101"],
      expect.stringMatching(/.{10,}/)
    );
  });
});
