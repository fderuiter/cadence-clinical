import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import CanvasInspector from "../../src/components/canvas/CanvasInspector.vue";
import { executionApi } from "../../src/api/executionApi";

vi.mock("../../src/api/executionApi", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("../../src/api/executionApi")>();
  return {
    ...actual,
    executionApi: {
      ...actual.executionApi,
      issueSubjectActivationCode: vi.fn(),
    },
  };
});

const SAMPLE_PARTICIPANT = {
  id: "0192f3c4-5e6a-7b8c-9d0e-000000000101",
  subject_id: "SUBJ-101-001",
  study_id: "CADENCE-101",
  site_id: "SITE-101",
  status: "ENROLLED",
  active_protocol_version: "1.0.0",
  enrollment_index: 1,
};

describe("Study Canvas ParticipantActivationCode", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.mocked(executionApi.issueSubjectActivationCode).mockReset();
  });

  it("hides activation code controls when canManageParticipants is false", () => {
    const wrapper = mount(CanvasInspector, {
      props: {
        studyCode: "CADENCE-101",
        participant: SAMPLE_PARTICIPANT,
        nodeRef: "enrollment:0192f3c4-5e6a-7b8c-9d0e-000000000101",
        missing: false,
        canManageParticipants: false,
      },
      global: {
        stubs: { "router-link": true },
      },
    });

    expect(wrapper.find("#inspector-activation-code").exists()).toBe(false);
  });

  it("issues a 30-day one-time activation code with mandatory audit reason when canManageParticipants is true", async () => {
    vi.mocked(executionApi.issueSubjectActivationCode).mockResolvedValue({
      subject_id: "SUBJ-101-001",
      study_id: "CADENCE-101",
      site_id: "SITE-101",
      activation_code: "CAD-7K9P-3M2X-8Q4W",
      issued_at: "2026-11-20T12:00:00Z",
      expires_at: "2026-12-20T12:00:00Z",
      expires_in_days: 30,
    });

    const wrapper = mount(CanvasInspector, {
      props: {
        studyCode: "CADENCE-101",
        participant: SAMPLE_PARTICIPANT,
        nodeRef: "enrollment:0192f3c4-5e6a-7b8c-9d0e-000000000101",
        missing: false,
        canManageParticipants: true,
      },
      global: {
        stubs: { "router-link": true },
      },
    });

    const section = wrapper.find("#inspector-activation-code");
    expect(section.exists()).toBe(true);

    const issueButton = wrapper.find("#btn-issue-activation-code");
    expect((issueButton.element as HTMLButtonElement).disabled).toBe(true);

    await wrapper
      .find("#activation-code-reason")
      .setValue("Participant onboarding at Visit 1");
    expect((issueButton.element as HTMLButtonElement).disabled).toBe(false);

    await issueButton.trigger("click");
    await flushPromises();

    expect(executionApi.issueSubjectActivationCode).toHaveBeenCalledWith(
      "SUBJ-101-001",
      "Participant onboarding at Visit 1"
    );
    expect(wrapper.find("#activation-code-value").text()).toBe(
      "CAD-7K9P-3M2X-8Q4W"
    );
    expect(wrapper.find("#activation-code-issued").text()).toContain(
      "2026-12-20"
    );
  });
});
