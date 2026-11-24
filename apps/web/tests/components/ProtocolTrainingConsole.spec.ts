import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { trainingApi } from "../../src/api/training";
import ProtocolTrainingConsole from "../../src/components/clinical/ProtocolTrainingConsole.vue";
import { useAuthStore } from "../../src/stores/auth";
import { signInAs } from "../helpers/signIn.js";

describe("ProtocolTrainingConsole.vue", () => {
  const mockCurriculum = {
    id: "curr-1",
    study_id: "STUDY-2026",
    protocol_version: "2.0",
    title: "Protocol Version 2.0 Delta Training",
    description: "Detailed procedural updates for Version 2.0...",
    is_active: true,
    created_at: "2026-09-01T10:00:00Z",
  };

  beforeEach(() => {
    setActivePinia(createPinia());
    signInAs(useAuthStore(), ["Principal Investigator"], {
      username: "site_investigator_01",
    });

    vi.spyOn(trainingApi, "listAssignments").mockResolvedValue([]);
    vi.spyOn(trainingApi, "listCurricula").mockResolvedValue([mockCurriculum]);
    vi.spyOn(trainingApi, "getCurriculum").mockResolvedValue(mockCurriculum);
    vi.spyOn(trainingApi, "listDelegations").mockResolvedValue([]);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("loads no assignments, curricula, or delegations when no one is signed in (#5400)", async () => {
    const auth = useAuthStore();
    auth.user = null;
    auth.rawRoles = [];
    auth.isAuthenticated = false;

    const wrapper = mount(ProtocolTrainingConsole, {
      props: { studyId: "STUDY-2026" },
    });
    await flushPromises();

    expect(trainingApi.listAssignments).not.toHaveBeenCalled();
    expect(trainingApi.listCurricula).not.toHaveBeenCalled();
    expect(trainingApi.listDelegations).not.toHaveBeenCalled();
    expect(wrapper.find(".training-workspace").exists()).toBe(false);
  });

  it("passes protocol_version to listCurricula and renders live curriculum without unmounted quiz/progress/sign-off tabs", async () => {
    const wrapper = mount(ProtocolTrainingConsole, {
      props: {
        studyId: "STUDY-2026",
        protocolVersion: "2.0",
      },
    });
    await flushPromises();

    expect(trainingApi.listCurricula).toHaveBeenCalledWith({
      study_id: "STUDY-2026",
      protocol_version: "2.0",
    });
    const callArg = vi.mocked(trainingApi.listCurricula).mock
      .calls[0][0] as Record<string, unknown>;
    expect(callArg).not.toHaveProperty("protocol_version_id");

    expect(wrapper.find(".console-title").text()).toContain(
      "Protocol Amendment Training & Qualification Console"
    );
    expect(wrapper.text()).toContain(
      "Detailed procedural updates for Version 2.0..."
    );
    expect(wrapper.text()).not.toContain("Take Qualification Quiz");

    expect("recordProgress" in trainingApi).toBe(false);
    expect("submitQuiz" in trainingApi).toBe(false);
    expect("signOffTraining" in trainingApi).toBe(false);
    expect("getCertificate" in trainingApi).toBe(false);
  });

  it("allows Data Manager to author a protocol curriculum with procedural modules and emits curriculum-created", async () => {
    signInAs(useAuthStore(), ["Data Manager"], { username: "sponsor_dm" });
    const createdCurriculum = {
      id: "curr-new-3",
      study_id: "STUDY-2026",
      protocol_version: "3.0",
      title: "Amendment 3.0 Delta",
      description: "Dosing window update",
      is_active: true,
      modules: [
        {
          id: "mod-1",
          title: "Visit Window Tolerance",
          content_type: "PROCEDURE_DELTA",
          duration_minutes: 15,
          body_markdown: "Verify +/- 2 day window.",
          sort_order: 1,
        },
      ],
    };
    vi.spyOn(trainingApi, "createCurriculum").mockResolvedValue(
      createdCurriculum
    );

    const wrapper = mount(ProtocolTrainingConsole, {
      props: { studyId: "STUDY-2026" },
    });
    await flushPromises();

    const openBtn = wrapper.find("#btn-open-create-curriculum");
    expect(openBtn.exists()).toBe(true);
    await openBtn.trigger("click");
    await flushPromises();

    await wrapper.find("#create-curr-title").setValue("Amendment 3.0 Delta");
    await wrapper.find("#create-curr-version").setValue("3.0");
    await wrapper.find("#create-curr-desc").setValue("Dosing window update");
    await wrapper
      .find("#create-mod-title-0")
      .setValue("Visit Window Tolerance");
    await wrapper
      .find("#create-mod-body-0")
      .setValue("Verify +/- 2 day window.");
    await wrapper
      .find("#create-curr-reason")
      .setValue("Publish Amendment 3.0 curriculum");
    await wrapper.find("#btn-submit-create-curriculum").trigger("click");
    await flushPromises();

    expect(trainingApi.createCurriculum).toHaveBeenCalledWith(
      expect.objectContaining({
        study_id: "STUDY-2026",
        protocol_version: "3.0",
        title: "Amendment 3.0 Delta",
        modules: [
          expect.objectContaining({
            title: "Visit Window Tolerance",
            content_type: "PROCEDURE_DELTA",
            duration_minutes: 15,
            body_markdown: "Verify +/- 2 day window.",
            sort_order: 1,
          }),
        ],
      })
    );
    expect(wrapper.emitted("curriculum-created")).toBeTruthy();
  });

  it("allows assigned trainee to complete pending qualification assignment and emits assignment-completed", async () => {
    const pendingAssignment = {
      id: "asn-101",
      curriculum_id: "curr-1",
      user_id: "site_investigator_01",
      user_role: "principal_investigator",
      study_id: "STUDY-2026",
      site_id: "SITE-101",
      status: "PENDING",
      curriculum: {
        ...mockCurriculum,
        modules: [
          {
            id: "mod-1",
            title: "Informed Consent Updates",
            content_type: "PROCEDURE_DELTA",
            duration_minutes: 10,
            body_markdown: "Review updated ICF signature block.",
            sort_order: 1,
          },
        ],
      },
    };
    vi.spyOn(trainingApi, "listAssignments").mockResolvedValue([
      pendingAssignment,
    ]);
    vi.spyOn(trainingApi, "getAssignmentDetail").mockResolvedValue(
      pendingAssignment
    );
    vi.spyOn(trainingApi, "completeAssignment").mockResolvedValue({
      ...pendingAssignment,
      status: "COMPLETED",
      completed_at: "2026-10-15T12:00:00Z",
    });

    const wrapper = mount(ProtocolTrainingConsole, {
      props: { studyId: "STUDY-2026", siteId: "SITE-101" },
    });
    await flushPromises();

    expect(wrapper.text()).toContain("Review updated ICF signature block.");
    const completeBtn = wrapper.find("#btn-complete-training-assignment");
    expect(completeBtn.exists()).toBe(true);
    await completeBtn.trigger("click");
    await flushPromises();

    expect(trainingApi.completeAssignment).toHaveBeenCalledWith(
      "asn-101",
      expect.objectContaining({
        reason_for_change: expect.any(String),
      })
    );
    expect(wrapper.emitted("assignment-completed")).toBeTruthy();
  });
});
