import { describe, it, expect, vi } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import OncologyCalipers from "../../src/components/imaging/OncologyCalipers.vue";
import DicomMprViewer from "../../src/components/imaging/DicomMprViewer.vue";
import ImagingView from "../../src/views/ImagingView.vue";

describe("Interactive AI Lesion Auto-Segmentation & Caliper Fitting", () => {
  it("selects AI Auto-Segment tool in OncologyCalipers and emits toolSelected", async () => {
    const wrapper = mount(OncologyCalipers, {
      props: {
        annotations: [],
        criteria: "RECIST_1_1",
      },
    });

    const aiBtn = wrapper.find(".btn-ai-segment");
    expect(aiBtn.exists()).toBe(true);

    await aiBtn.trigger("click");
    expect(wrapper.emitted("toolSelected")).toBeTruthy();
    expect(wrapper.emitted("toolSelected")![0]).toEqual(["AI_AUTO_SEGMENT"]);
  });

  it("displays AI_AUTO method badge and HU stats for AI-generated annotations", () => {
    const aiAnnotation = {
      id: "ai-annot-001",
      lesion_id: "TL1",
      organ_site: "LIVER",
      lesion_type: "TARGET",
      measurement_type: "AI_AUTO_CONTOUR",
      is_ai_assisted: true,
      longest_diameter_mm: 37.5,
      perpendicular_diameter_mm: 27.0,
      hounsfield_stats: { meanHu: 48.5, stdDevHu: 11.4 },
      timepoint: "BASELINE",
    };

    const wrapper = mount(OncologyCalipers, {
      props: {
        annotations: [aiAnnotation],
        criteria: "RECIST_1_1",
      },
    });

    const aiBadge = wrapper.find(".ai-badge");
    expect(aiBadge.exists()).toBe(true);
    expect(aiBadge.text()).toContain("AI");
  });

  it("converts AI-generated contour to editable manual caliper when Convert button is clicked", async () => {
    const aiAnnotation = {
      id: "ai-annot-002",
      lesion_id: "TL2",
      organ_site: "LUNG_RIGHT",
      lesion_type: "TARGET",
      measurement_type: "AI_AUTO_CONTOUR",
      is_ai_assisted: true,
      longest_diameter_mm: 22.0,
      perpendicular_diameter_mm: 16.0,
      timepoint: "CYCLE_2_DAY_1",
    };

    const wrapper = mount(OncologyCalipers, {
      props: {
        annotations: [aiAnnotation],
        criteria: "RECIST_1_1",
      },
    });

    const convertBtn = wrapper.find(".btn-convert-manual");
    expect(convertBtn.exists()).toBe(true);

    await convertBtn.trigger("click");
    expect(wrapper.emitted("annotationUpdated")).toBeTruthy();
    const updated = wrapper.emitted("annotationUpdated")![0][0] as any;
    expect(updated.measurement_type).toBe("MANUAL_EDITED");
    expect(updated.is_ai_assisted).toBe(true);
  });

  it("sends ROI seed request and emits caliperCreated event in DicomMprViewer when activeTool is AI_AUTO_SEGMENT", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new Error("Network error")));

    const wrapper = mount(DicomMprViewer, {
      props: {
        activeTool: "AI_AUTO_SEGMENT",
        timepoint: "BASELINE",
        seriesUid: "1.2.840.113619.2.55.3.200.1",
      },
    });

    const canvas = wrapper.find("canvas.mpr-canvas");
    expect(canvas.exists()).toBe(true);

    // Simulate clicking on the canvas viewport
    await canvas.trigger("mousedown", { clientX: 180, clientY: 150 });
    await flushPromises();

    expect(wrapper.emitted("caliperCreated")).toBeTruthy();
    const emittedEvent = wrapper.emitted("caliperCreated")![0][0] as any;
    expect(emittedEvent.tool).toBe("AI_AUTO_SEGMENT");
    expect(emittedEvent.measurementType).toBe("AI_AUTO_CONTOUR");
    expect(emittedEvent.isAiAssisted).toBe(true);
    expect(emittedEvent.longestDiameterMm).toBeGreaterThan(0);
    expect(emittedEvent.points.length).toBeGreaterThan(2);
    expect(emittedEvent.modelVersion).toBe("UNet-ResNet34-2D-v2.1");
  });

  it("includes AI assistance metadata when electronic signature assessment is submitted", async () => {
    const aiAnnotation = {
      id: "ai-annot-003",
      lesion_id: "TL1",
      organ_site: "LIVER",
      lesion_type: "TARGET",
      measurement_type: "AI_AUTO_CONTOUR",
      is_ai_assisted: true,
      longest_diameter_mm: 30.0,
      timepoint: "CYCLE_2_DAY_1",
    };

    const wrapper = mount(OncologyCalipers, {
      props: {
        annotations: [aiAnnotation],
        criteria: "RECIST_1_1",
      },
    });

    await wrapper.find(".btn-primary-signoff").trigger("click");
    expect(wrapper.find(".modal-overlay").exists()).toBe(true);

    await wrapper.find("#signature-input").setValue("dr_central_reader_123");
    await wrapper.find(".btn-confirm-sign").trigger("click");

    expect(wrapper.emitted("assessmentSigned")).toBeTruthy();
    const signedEvent = wrapper.emitted("assessmentSigned")![0][0] as any;
    expect(signedEvent.aiAssisted).toBe(true);
    expect(signedEvent.aiAnnotationsCount).toBe(1);
  });
});
