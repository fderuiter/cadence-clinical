import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { trainingApi } from "../../src/api/training";
import { syncActiveStudyScope } from "../../src/composables/useActiveStudyScope";
import { useAuthStore } from "../../src/stores/auth";
import StudyTrainingView from "../../src/views/StudyTrainingView.vue";
import { signInAs } from "../helpers/signIn.js";

describe("StudyTrainingView.vue", () => {
  const mockCurriculum = {
    id: "curr-2026-v2",
    study_id: "CAD-101",
    protocol_version: "2.0",
    title: "CAD-101 Amendment 2 Curriculum",
    description: "Updated inclusion criteria.",
    is_active: true,
    created_at: "2026-10-01T12:00:00Z",
  };

  beforeEach(() => {
    setActivePinia(createPinia());
    signInAs(useAuthStore(), ["Data Manager"], { username: "sponsor_dm" });
    vi.spyOn(trainingApi, "listCurricula").mockResolvedValue([mockCurriculum]);
    vi.spyOn(trainingApi, "getCurriculum").mockResolvedValue(mockCurriculum);
    vi.spyOn(trainingApi, "listAssignments").mockResolvedValue([]);
    vi.spyOn(trainingApi, "listDelegations").mockResolvedValue([]);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("loads curricula with protocol_version (not protocol_version_id) and renders live Knowledge training tabs", async () => {
    const wrapper = mount(StudyTrainingView, {
      props: { studyId: "CAD-101" },
    });
    await flushPromises();

    const protocolFilter = wrapper.find("#training-protocol-filter");
    expect(protocolFilter.exists()).toBe(true);
    await protocolFilter.setValue("2.0");
    await protocolFilter.trigger("change");
    await flushPromises();

    expect(trainingApi.listCurricula).toHaveBeenCalledWith({
      study_id: "CAD-101",
      protocol_version: "2.0",
    });
    for (const [query] of vi.mocked(trainingApi.listCurricula).mock.calls) {
      expect(query).not.toHaveProperty("protocol_version_id");
    }

    expect(wrapper.text()).toContain("CAD-101 Amendment 2 Curriculum");
    expect(wrapper.find("#tab-my-training").exists()).toBe(false);
  });

  it("gates curriculum assignment to Sponsor Data Manager / System Administrator roles", async () => {
    signInAs(useAuthStore(), ["CRA"], { username: "cra_user" });
    const wrapper = mount(StudyTrainingView, {
      props: { studyId: "CAD-101" },
    });
    await flushPromises();

    const batchTab = wrapper
      .findAll(".tab-btn")
      .find((btn) => btn.text().includes("Batch Assignments"));
    expect(batchTab).toBeDefined();
    await batchTab!.trigger("click");
    await flushPromises();

    expect(wrapper.find("#training-assign-role-note").text()).toContain(
      "training_curriculum:assign"
    );
  });

  it("passes siteId prop to training assignments and delegations when embedded in Clinical Canvas", async () => {
    const wrapper = mount(StudyTrainingView, {
      props: { studyId: "CAD-101", siteId: "SITE-101" },
    });
    await flushPromises();

    expect(trainingApi.listAssignments).toHaveBeenCalledWith(
      expect.objectContaining({
        study_id: "CAD-101",
        site_id: "SITE-101",
      })
    );
    expect(trainingApi.listDelegations).toHaveBeenCalledWith({
      study_id: "CAD-101",
      site_id: "SITE-101",
    });
    expect(wrapper.find("#training-site-filter").exists()).toBe(false);
    wrapper.unmount();
  });

  it("passes synchronized study_id to listCurricula, listAssignments, and listDelegations when opening /training without a query parameter", async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const authStore = useAuthStore();
    signInAs(authStore, ["CRA"]);
    syncActiveStudyScope("CAD-101");

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: "/training", name: "training", component: StudyTrainingView },
      ],
    });
    await router.push("/training");
    await router.isReady();

    const wrapper = mount(
      { template: "<router-view />" },
      { global: { plugins: [pinia, router] } }
    );
    await flushPromises();

    expect(trainingApi.listCurricula).toHaveBeenCalledWith(
      expect.objectContaining({ study_id: "CAD-101" })
    );
    expect(trainingApi.listAssignments).toHaveBeenCalledWith(
      expect.objectContaining({ study_id: "CAD-101" })
    );
    expect(trainingApi.listDelegations).toHaveBeenCalledWith(
      expect.objectContaining({ study_id: "CAD-101" })
    );
    wrapper.unmount();
  });
});
