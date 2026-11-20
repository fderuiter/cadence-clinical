import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createRouter, createWebHistory } from "vue-router";
import { nextTick } from "vue";
import CommandPaletteOverlay from "../../src/components/CommandPaletteOverlay.vue";
import { useAuthStore } from "../../src/stores/auth";
import { useKnowledgeStore } from "../../src/stores/knowledge";
import { useOnboardingStore } from "../../src/stores/onboarding";
import { useStudyCanvasStore } from "../../src/stores/studyCanvas";
import { executionApi } from "../../src/api/executionApi";
import { kernelApi } from "../../src/api/kernelApi";

const DummyView = { template: "<div>Dummy</div>" };

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: "/", name: "home", component: DummyView },
    { path: "/overview", name: "overview", component: DummyView },
    { path: "/studies/:studyCode?", name: "study", component: DummyView },
    { path: "/ecrf", name: "ecrf", component: DummyView },
    { path: "/ctms", name: "ctms", component: DummyView },
    { path: "/ssu", name: "ssu", component: DummyView },
  ],
});

describe("CommandPaletteOverlay.vue - Unified Global Command Palette", () => {
  let pinia;

  beforeEach(async () => {
    pinia = createPinia();
    setActivePinia(pinia);
    await router.push("/overview");
    await router.isReady();
  });

  it("does not render when isOpen is false", () => {
    const wrapper = mount(CommandPaletteOverlay, {
      props: { isOpen: false },
      global: { plugins: [pinia, router] },
    });

    expect(wrapper.find("#canvas-palette").exists()).toBe(false);
  });

  it("renders when isOpen is true and focuses #canvas-palette-input", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Admin"];

    const wrapper = mount(CommandPaletteOverlay, {
      props: { isOpen: true },
      global: { plugins: [pinia, router] },
      attachTo: document.body,
    });

    await nextTick();
    expect(wrapper.find("#canvas-palette").exists()).toBe(true);
    const input = wrapper.find("#canvas-palette-input").element;
    expect(document.activeElement).toBe(input);

    wrapper.unmount();
  });

  it("combines authorized UX-1 workflow destinations and workspace actions without product acronyms", () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Admin"];

    const wrapper = mount(CommandPaletteOverlay, {
      props: { isOpen: true },
      global: { plugins: [pinia, router] },
    });

    const text = wrapper.text();
    expect(text).toContain("Studies");
    expect(text).toContain("Data collection");
    expect(text).toContain("Site readiness");
    expect(text).toContain("Orientation");
    expect(text).toContain("Open contextual help");
    expect(text).toContain("Open setup checklist");
    expect(text).toContain("Start guided tour");

    expect(text).not.toContain("EDC");
    expect(text).not.toContain("CTMS");
    expect(text).not.toContain("eTMF");
    expect(text).not.toContain("MDR Protocol Designer");
  });

  it("dynamically filters workflow destinations by role across personas", () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Designer"];

    const designerWrapper = mount(CommandPaletteOverlay, {
      props: { isOpen: true },
      global: { plugins: [pinia, router] },
    });

    const designerLabels = designerWrapper
      .findAll(".palette-label")
      .map((el) => el.text());
    expect(designerLabels).toContain("Studies");
    expect(designerLabels).toContain("Orientation");
    expect(designerLabels).not.toContain("Data collection");
    expect(designerLabels).not.toContain("Site readiness");
    designerWrapper.unmount();

    authStore.rawRoles = ["CRC"];
    const crcWrapper = mount(CommandPaletteOverlay, {
      props: { isOpen: true },
      global: { plugins: [pinia, router] },
    });
    const crcLabels = crcWrapper
      .findAll(".palette-label")
      .map((el) => el.text());
    expect(crcLabels).toContain("Studies");
    expect(crcLabels).toContain("Data collection");
    expect(crcLabels).toContain("Site readiness");
    crcWrapper.unmount();
  });

  it("combines study search (kernelApi.search) and lifecycle navigation when a study is open", async () => {
    vi.useFakeTimers();
    const searchSpy = vi.spyOn(kernelApi, "search").mockResolvedValue([
      {
        ref: "study:11111111-1111-1111-1111-111111111111",
        kind: "study",
        title: "CAD-202 Phase II Oncology",
        code: "CAD-202",
        masked: false,
      },
    ]);

    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["CRA"];

    await router.push("/studies/CAD-101");

    const wrapper = mount(CommandPaletteOverlay, {
      props: { isOpen: true },
      global: { plugins: [pinia, router] },
    });

    const initialText = wrapper.text();
    expect(initialText).toContain("CAD-101");
    expect(initialText).toContain("Conduct");
    expect(initialText).toContain("Participants");
    expect(initialText).toContain("Site readiness");

    const input = wrapper.find("#canvas-palette-input");
    await input.setValue("CAD-202");
    vi.advanceTimersByTime(250);
    await flushPromises();

    expect(searchSpy).toHaveBeenCalled();
    expect(wrapper.text()).toContain("CAD-202");
    expect(wrapper.text()).toContain("CAD-202 Phase II Oncology");

    searchSpy.mockRestore();
    vi.useRealTimers();
  });

  it("dispatches contextual help, checklist, guided tour, and persona view switches", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Admin"];

    const knowledgeStore = useKnowledgeStore();
    const onboardingStore = useOnboardingStore();
    const openHelpSpy = vi.spyOn(knowledgeStore, "openDrawer");
    const startTourSpy = vi.spyOn(onboardingStore, "startTour");

    const wrapper = mount(CommandPaletteOverlay, {
      props: { isOpen: true },
      global: { plugins: [pinia, router] },
    });

    const input = wrapper.find("#canvas-palette-input");
    await input.setValue("Open contextual help");
    const helpOptions = wrapper.findAll("[role='option']");
    expect(helpOptions.length).toBe(1);
    await helpOptions[0].trigger("click");
    expect(openHelpSpy).toHaveBeenCalled();
    expect(wrapper.emitted("close")).toBeTruthy();

    await input.setValue("Start guided tour");
    await wrapper.find("[role='option']").trigger("click");
    expect(startTourSpy).toHaveBeenCalled();
  });

  it("supports keyboard navigation (ArrowDown, ArrowUp, Enter, Escape) and backdrop close", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Admin"];

    const pushSpy = vi.spyOn(router, "push");

    const wrapper = mount(CommandPaletteOverlay, {
      props: { isOpen: true },
      global: { plugins: [pinia, router] },
    });

    const input = wrapper.find("#canvas-palette-input");
    let options = wrapper.findAll("[role='option']");
    expect(options[0].attributes("aria-selected")).toBe("true");

    await input.trigger("keydown", { key: "ArrowDown" });
    options = wrapper.findAll("[role='option']");
    expect(options[1].attributes("aria-selected")).toBe("true");

    await input.trigger("keydown", { key: "ArrowUp" });
    options = wrapper.findAll("[role='option']");
    expect(options[0].attributes("aria-selected")).toBe("true");

    await input.trigger("keydown", { key: "Enter" });
    expect(pushSpy).toHaveBeenCalledWith("/studies");
    expect(wrapper.emitted("close")).toBeTruthy();

    const wrapper2 = mount(CommandPaletteOverlay, {
      props: { isOpen: true },
      global: { plugins: [pinia, router] },
    });
    await wrapper2.find(".palette-backdrop").trigger("click");
    expect(wrapper2.emitted("close")).toBeTruthy();

    const wrapperAttached = mount(CommandPaletteOverlay, {
      attachTo: document.body,
      props: { isOpen: true },
      global: { plugins: [pinia, router] },
    });
    await wrapperAttached
      .find("#canvas-palette-input")
      .trigger("keydown", { key: "Escape" });
    expect(wrapperAttached.emitted("close")).toHaveLength(1);
    wrapperAttached.unmount();
  });

  it("queries executionApi.listSubjects for the active study from any screen, suppresses Open study for matched subject codes, and opens the participant", async () => {
    vi.useFakeTimers();
    const subjectUuid = "0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d02";
    const remoteParticipant = {
      id: subjectUuid,
      subject_id: "SUBJ-02",
      study_id: "CAD-101",
      site_id: "022",
      status: "ACTIVE",
      active_protocol_version: "3.0",
      enrollment_index: 52,
    };
    const searchSpy = vi.spyOn(kernelApi, "search").mockResolvedValue([]);
    const listSpy = vi.spyOn(executionApi, "listSubjects").mockResolvedValue({
      items: [remoteParticipant],
      total: 1,
      limit: 5,
      offset: 0,
    });

    try {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["CRC"];

      const studyCanvas = useStudyCanvasStore();
      studyCanvas.openStudy("CAD-101");
      await router.push("/ecrf");

      const wrapper = mount(CommandPaletteOverlay, {
        props: { isOpen: true },
        global: { plugins: [pinia, router] },
      });

      const input = wrapper.find("#canvas-palette-input");
      await input.setValue("SUBJ-02");
      await vi.advanceTimersByTimeAsync(300);
      await flushPromises();

      expect(listSpy).toHaveBeenCalledWith({
        studyId: "CAD-101",
        q: "SUBJ-02",
        limit: 5,
      });

      const options = wrapper.findAll("[role='option']");
      const groups = options.map((opt) => opt.find(".palette-group").text());
      expect(groups).toContain("Participant");
      expect(groups).not.toContain("Open study");

      const participantOption = options.find(
        (opt) => opt.find(".palette-label").text() === "SUBJ-02"
      );
      await participantOption.trigger("click");
      await flushPromises();

      expect(studyCanvas.knownParticipants[subjectUuid]).toEqual(
        remoteParticipant
      );
      expect(router.currentRoute.value.path).toBe("/studies/CAD-101");
      expect(router.currentRoute.value.query).toEqual({
        stage: "conduct",
        section: "participants",
        node: `participant:${subjectUuid}`,
      });
    } finally {
      searchSpy.mockRestore();
      listSpy.mockRestore();
      vi.useRealTimers();
    }
  });

  it("drops stale out-of-order subject search responses when the query changes", async () => {
    vi.useFakeTimers();
    let resolveFirst;
    const firstPromise = new Promise((resolve) => {
      resolveFirst = resolve;
    });
    const staleUuid = "0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d03";
    const freshUuid = "0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d04";
    const searchSpy = vi.spyOn(kernelApi, "search").mockResolvedValue([]);
    const listSpy = vi
      .spyOn(executionApi, "listSubjects")
      .mockImplementation(({ q }) => {
        if (q === "SUBJ-OLD") return firstPromise;
        return Promise.resolve({
          items: [
            {
              id: freshUuid,
              subject_id: "SUBJ-NEW",
              study_id: "CAD-101",
              site_id: "017",
              status: "ACTIVE",
              active_protocol_version: "3.0",
              enrollment_index: 9,
            },
          ],
          total: 1,
          limit: 5,
          offset: 0,
        });
      });

    try {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["CRC"];
      await router.push("/studies/CAD-101");

      const wrapper = mount(CommandPaletteOverlay, {
        props: { isOpen: true },
        global: { plugins: [pinia, router] },
      });

      const input = wrapper.find("#canvas-palette-input");
      await input.setValue("SUBJ-OLD");
      await vi.advanceTimersByTimeAsync(300);

      await input.setValue("SUBJ-NEW");
      await vi.advanceTimersByTimeAsync(300);
      await flushPromises();

      resolveFirst({
        items: [
          {
            id: staleUuid,
            subject_id: "SUBJ-NEW-STALE",
            study_id: "CAD-101",
            site_id: "017",
            status: "SCREENING",
            active_protocol_version: "3.0",
            enrollment_index: 8,
          },
        ],
        total: 1,
        limit: 5,
        offset: 0,
      });
      await flushPromises();

      const labels = wrapper.findAll(".palette-label").map((el) => el.text());
      expect(labels).toContain("SUBJ-NEW");
      expect(labels).not.toContain("SUBJ-NEW-STALE");
    } finally {
      searchSpy.mockRestore();
      listSpy.mockRestore();
      vi.useRealTimers();
    }
  });
});
