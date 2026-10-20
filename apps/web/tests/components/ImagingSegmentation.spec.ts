import { describe, it, expect, vi } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import OncologyCalipers from "../../src/components/imaging/OncologyCalipers.vue";
import DicomMprViewer from "../../src/components/imaging/DicomMprViewer.vue";
import ImagingView from "../../src/views/ImagingView.vue";

describe("Lesion calipers and AI-labelled annotations", () => {
  it("offers no AI Auto-Segment tool while no segmentation model reads the image (#5426)", () => {
    const wrapper = mount(OncologyCalipers, {
      props: {
        annotations: [],
        criteria: "RECIST_1_1",
      },
    });

    expect(wrapper.find(".btn-ai-segment").exists()).toBe(false);
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

  it("never draws or emits a measurement of its own when a seed is clicked (#5426)", async () => {
    const fetchSpy = vi.fn();
    vi.stubGlobal("fetch", fetchSpy);

    const wrapper = mount(DicomMprViewer, {
      props: {
        activeTool: "AI_AUTO_SEGMENT",
        timepoint: "BASELINE",
        seriesUid: "series-under-test",
      },
    });

    await wrapper
      .find("canvas.mpr-canvas")
      .trigger("mousedown", { clientX: 180, clientY: 150 });
    await flushPromises();

    expect(wrapper.emitted("caliperCreated")).toBeFalsy();
    expect(fetchSpy).not.toHaveBeenCalled();
    vi.unstubAllGlobals();
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
