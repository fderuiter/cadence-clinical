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
});
