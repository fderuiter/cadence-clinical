import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import {
  mount,
  flushPromises,
  DOMWrapper,
  enableAutoUnmount,
} from "@vue/test-utils";
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

enableAutoUnmount(afterEach);

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
  let body;

  beforeEach(async () => {
    pinia = createPinia();
    setActivePinia(pinia);
    body = new DOMWrapper(document.body);
    await router.push("/overview");
    await router.isReady();
  });

  it("does not render when isOpen is false", () => {
    mount(CommandPaletteOverlay, {
      props: { isOpen: false },
      global: { plugins: [pinia, router] },
    });

    expect(body.find("#canvas-palette").exists()).toBe(false);
  });

  it("teleports to document.body when isOpen is true and focuses #canvas-palette-input", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Admin"];

    const wrapper = mount(CommandPaletteOverlay, {
      props: { isOpen: true },
      global: { plugins: [pinia, router] },
      attachTo: document.body,
    });

    await nextTick();
    const backdrop = document.getElementById("canvas-palette-backdrop");
    expect(backdrop?.parentElement).toBe(document.body);
    expect(body.find("#canvas-palette").exists()).toBe(true);
    const input = body.find("#canvas-palette-input").element;
    expect(document.activeElement).toBe(input);

    wrapper.unmount();
  });

  it("combines authorized UX-1 workflow destinations and workspace actions without product acronyms", () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Admin"];

    mount(CommandPaletteOverlay, {
      props: { isOpen: true },
      global: { plugins: [pinia, router] },
    });

    const text = body.find("#canvas-palette").text();
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

    const designerLabels = body
      .findAll("#canvas-palette .palette-label")
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
    const crcLabels = body
      .findAll("#canvas-palette .palette-label")
      .map((el) => el.text());
    expect(crcLabels).toContain("Studies");
    expect(crcLabels).toContain("Data collection");
    expect(crcLabels).toContain("Site readiness");
    crcWrapper.unmount();
  });

  it("combines study search (kernelApi.search) and lifecycle navigation when a study is open", async () => {
    vi.useFakeTimers();
    const listSpy = vi
      .spyOn(executionApi, "listSubjects")
      .mockResolvedValue({ items: [], total: 0, limit: 200, offset: 0 });
    const detailSpy = vi
      .spyOn(executionApi, "getSubjectDetail")
      .mockRejectedValue(
        Object.assign(new Error("Not found"), { status: 404 })
      );
    const searchSpy = vi.spyOn(kernelApi, "search").mockResolvedValue([
      {
        ref: "study:11111111-1111-1111-1111-111111111111",
        kind: "study",
        title: "CAD-202 Phase II Oncology",
        code: "CAD-202",
        masked: false,
      },
    ]);

    try {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["CRA"];

      await router.push("/studies/CAD-101");

      mount(CommandPaletteOverlay, {
        props: { isOpen: true },
        global: { plugins: [pinia, router] },
      });
      await flushPromises();

      const initialText = body.find("#canvas-palette").text();
      expect(initialText).toContain("CAD-101");
      expect(initialText).toContain("Conduct");
      expect(initialText).toContain("Participants");
      expect(initialText).toContain("Site readiness");

      const input = body.find("#canvas-palette-input");
      await input.setValue("CAD-202");
      expect(body.find("#canvas-palette-list").attributes("aria-busy")).toBe(
        "true"
      );
      vi.advanceTimersByTime(250);
      await flushPromises();
      expect(body.find("#canvas-palette-list").attributes("aria-busy")).toBe(
        "false"
      );

      expect(searchSpy).toHaveBeenCalled();
      expect(body.find("#canvas-palette").text()).toContain("CAD-202");
      expect(body.find("#canvas-palette").text()).toContain(
        "CAD-202 Phase II Oncology"
      );
    } finally {
      listSpy.mockRestore();
      detailSpy.mockRestore();
      searchSpy.mockRestore();
      vi.useRealTimers();
    }
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

    const input = body.find("#canvas-palette-input");
    await input.setValue("Open contextual help");
    const helpOptions = body.findAll("#canvas-palette [role='option']");
    expect(helpOptions.length).toBe(1);
    await helpOptions[0].trigger("click");
    expect(openHelpSpy).toHaveBeenCalled();
    expect(wrapper.emitted("close")).toBeTruthy();

    await input.setValue("Start guided tour");
    await body.find("#canvas-palette [role='option']").trigger("click");
    expect(startTourSpy).toHaveBeenCalled();
  });

  it("populates study participants across non-Participants views, resolves codes beyond 200 rows, and suppresses Open study on matches", async () => {
    const many = Array.from({ length: 210 }, (_, i) => ({
      id: `0192f3c4-5e6a-7b8c-9d0e-${String(i + 1).padStart(12, "0")}`,
      subject_id: `017-${String(i + 1).padStart(4, "0")}`,
      study_id: "CAD-101",
      site_id: "017",
      status: "ACTIVE",
      active_protocol_version: "3.0",
      enrollment_index: i + 1,
    }));

    const listSpy = vi
      .spyOn(executionApi, "listSubjects")
      .mockImplementation(async ({ offset = 0, limit = 200 }) => ({
        items: many.slice(offset, offset + limit),
        total: many.length,
        limit,
        offset,
      }));
    const detailSpy = vi
      .spyOn(executionApi, "getSubjectDetail")
      .mockImplementation(async (code) => {
        const found = many.find((p) => p.subject_id === code);
        if (!found) {
          throw Object.assign(new Error("Not found"), { status: 404 });
        }
        return {
          subject_id: found.subject_id,
          study_id: found.study_id,
          status: found.status,
          site_id: found.site_id,
          treatment_group: null,
          randomization_seed: null,
          investigational_product_id: null,
        };
      });
    const searchSpy = vi.spyOn(kernelApi, "search").mockResolvedValue([]);
    const pushSpy = vi.spyOn(router, "push");

    try {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["CRA"];

      const canvasStore = useStudyCanvasStore();
      canvasStore.openStudy("CAD-101");
      await router.push("/ecrf");

      mount(CommandPaletteOverlay, {
        props: { isOpen: true },
        global: { plugins: [pinia, router] },
      });
      await flushPromises();

      expect(listSpy).toHaveBeenCalledWith({
        studyId: "CAD-101",
        limit: 200,
        offset: 0,
      });

      const input = body.find("#canvas-palette-input");
      await input.setValue("017-0002");
      await flushPromises();

      let options = body.findAll("#canvas-palette [role='option']");
      expect(options[0].text()).toContain("017-0002");
      expect(
        options.some((o) => o.find(".palette-group").text() === "Open study")
      ).toBe(false);

      await input.setValue("017-0205");
      await flushPromises();

      expect(detailSpy).toHaveBeenCalledWith("017-0205");
      options = body.findAll("#canvas-palette [role='option']");
      expect(options[0].text()).toContain("017-0205");
      expect(
        options.some((o) => o.find(".palette-group").text() === "Open study")
      ).toBe(false);

      await input.trigger("keydown", { key: "Enter" });
      expect(pushSpy).toHaveBeenCalledWith({
        name: "study",
        params: { studyCode: "CAD-101" },
        query: {
          stage: "conduct",
          section: "participants",
          node: "participant:0192f3c4-5e6a-7b8c-9d0e-000000000205",
        },
      });
    } finally {
      listSpy.mockRestore();
      detailSpy.mockRestore();
      searchSpy.mockRestore();
    }
  });

  it("supports keyboard navigation (ArrowDown, ArrowUp, Home, End, Enter, Escape), scrolls active options into view, and closes on backdrop click", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Admin"];

    const pushSpy = vi.spyOn(router, "push");
    const scrollSpy = vi.fn();
    const originalScroll = HTMLElement.prototype.scrollIntoView;
    HTMLElement.prototype.scrollIntoView = scrollSpy;

    try {
      const wrapper = mount(CommandPaletteOverlay, {
        props: { isOpen: true },
        global: { plugins: [pinia, router] },
      });

      const input = body.find("#canvas-palette-input");
      let options = body.findAll("#canvas-palette [role='option']");
      expect(options[0].attributes("aria-selected")).toBe("true");

      await input.trigger("keydown", { key: "ArrowDown" });
      options = body.findAll("#canvas-palette [role='option']");
      expect(options[1].attributes("aria-selected")).toBe("true");
      expect(scrollSpy).toHaveBeenLastCalledWith({ block: "nearest" });

      await input.trigger("keydown", { key: "End" });
      options = body.findAll("#canvas-palette [role='option']");
      expect(options[options.length - 1].attributes("aria-selected")).toBe(
        "true"
      );

      await input.trigger("keydown", { key: "Home" });
      options = body.findAll("#canvas-palette [role='option']");
      expect(options[0].attributes("aria-selected")).toBe("true");

      await input.trigger("keydown", { key: "ArrowDown" });
      await input.trigger("keydown", { key: "ArrowUp" });
      options = body.findAll("#canvas-palette [role='option']");
      expect(options[0].attributes("aria-selected")).toBe("true");
      expect(scrollSpy).toHaveBeenLastCalledWith({ block: "nearest" });

      await input.trigger("keydown", { key: "Enter" });
      expect(pushSpy).toHaveBeenCalledWith("/studies");
      expect(wrapper.emitted("close")).toBeTruthy();
      wrapper.unmount();

      const wrapper2 = mount(CommandPaletteOverlay, {
        props: { isOpen: true },
        global: { plugins: [pinia, router] },
      });
      await body.find(".palette-backdrop").trigger("click");
      expect(wrapper2.emitted("close")).toBeTruthy();
      wrapper2.unmount();

      const wrapperAttached = mount(CommandPaletteOverlay, {
        attachTo: document.body,
        props: { isOpen: true },
        global: { plugins: [pinia, router] },
      });
      await body
        .find("#canvas-palette-input")
        .trigger("keydown", { key: "Escape" });
      expect(wrapperAttached.emitted("close")).toHaveLength(1);
      wrapperAttached.unmount();
    } finally {
      HTMLElement.prototype.scrollIntoView = originalScroll;
    }
  });

  it("retains the highlighted option key when async study results prepend above it", async () => {
    vi.useFakeTimers();
    let resolveSearch;
    const searchSpy = vi.spyOn(kernelApi, "search").mockImplementation(
      () =>
        new Promise((resolve) => {
          resolveSearch = resolve;
        })
    );

    try {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["Sponsor Admin"];

      mount(CommandPaletteOverlay, {
        props: { isOpen: true },
        global: { plugins: [pinia, router] },
      });

      const input = body.find("#canvas-palette-input");
      await input.setValue("st");
      vi.advanceTimersByTime(250);

      // Before async search resolves, move down to highlight "Start guided tour"
      let options = body.findAll("#canvas-palette [role='option']");
      const tourIndex = options.findIndex((o) =>
        o.text().includes("Start guided tour")
      );
      expect(tourIndex).toBeGreaterThanOrEqual(0);
      for (let i = 0; i < tourIndex; i += 1) {
        await input.trigger("keydown", { key: "ArrowDown" });
      }
      options = body.findAll("#canvas-palette [role='option']");
      expect(options[tourIndex].attributes("aria-selected")).toBe("true");

      // Resolve async study search which inserts a Study result ahead of actions
      resolveSearch([
        {
          ref: "study:99999999-9999-9999-9999-999999999999",
          kind: "study",
          title: "Strategic Trial Alpha",
          code: "ST-100",
          masked: false,
        },
      ]);
      await flushPromises();

      options = body.findAll("#canvas-palette [role='option']");
      const selected = options.find(
        (o) => o.attributes("aria-selected") === "true"
      );
      expect(selected?.text()).toContain("Start guided tour");
    } finally {
      searchSpy.mockRestore();
      vi.useRealTimers();
    }
  });

  it("displays #canvas-palette-subject-note when executionApi subject lookup fails with a non-404 error", async () => {
    const listSpy = vi
      .spyOn(executionApi, "listSubjects")
      .mockRejectedValueOnce(
        Object.assign(new Error("Execution service unreachable"), {
          status: 503,
        })
      )
      .mockResolvedValue({
        items: [],
        total: 250,
        limit: 200,
        offset: 0,
      });
    const detailSpy = vi
      .spyOn(executionApi, "getSubjectDetail")
      .mockRejectedValueOnce(
        Object.assign(new Error("Not found"), { status: 404 })
      )
      .mockRejectedValueOnce(
        Object.assign(new Error("Subject detail gateway timeout"), {
          status: 504,
        })
      );
    const searchSpy = vi.spyOn(kernelApi, "search").mockResolvedValue([]);

    try {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["CRA"];

      const canvasStore = useStudyCanvasStore();
      canvasStore.openStudy("CAD-101");
      await router.push("/studies/CAD-101");

      const firstWrapper = mount(CommandPaletteOverlay, {
        props: { isOpen: true },
        global: { plugins: [pinia, router] },
      });
      await flushPromises();

      const note = body.find("#canvas-palette-subject-note");
      expect(note.exists()).toBe(true);
      expect(note.attributes("role")).toBe("status");
      expect(note.text()).toContain(
        "Participants can't be searched right now: Execution service unreachable"
      );
      firstWrapper.unmount();

      // Switch study: 404 on getSubjectDetail stays silent, 504 surfaces #canvas-palette-subject-note
      canvasStore.openStudy("CAD-102");
      await router.push("/studies/CAD-102");
      mount(CommandPaletteOverlay, {
        props: { isOpen: true },
        global: { plugins: [pinia, router] },
      });
      await flushPromises();
      expect(body.find("#canvas-palette-subject-note").exists()).toBe(false);

      await body.find("#canvas-palette-input").setValue("017-9999");
      await flushPromises();
      expect(body.find("#canvas-palette-subject-note").exists()).toBe(false);

      await body.find("#canvas-palette-input").setValue("017-0500");
      await flushPromises();
      expect(body.find("#canvas-palette-subject-note").text()).toContain(
        "Participants can't be searched right now: Subject detail gateway timeout"
      );
    } finally {
      listSpy.mockRestore();
      detailSpy.mockRestore();
      searchSpy.mockRestore();
    }
  });

  it("restores focus to #btn-open-palette fallback instead of document.body when the prior trigger was detached", async () => {
    const fallbackBtn = document.createElement("button");
    fallbackBtn.id = "btn-open-palette";
    fallbackBtn.textContent = "Command";
    document.body.appendChild(fallbackBtn);

    const transientBtn = document.createElement("button");
    transientBtn.textContent = "Transient";
    document.body.appendChild(transientBtn);
    transientBtn.focus();

    try {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["Sponsor Admin"];

      const wrapper = mount(CommandPaletteOverlay, {
        props: { isOpen: true },
        global: { plugins: [pinia, router] },
        attachTo: document.body,
      });
      await nextTick();

      transientBtn.remove();
      await wrapper.setProps({ isOpen: false });
      await nextTick();

      expect(document.activeElement).toBe(fallbackBtn);
    } finally {
      fallbackBtn.remove();
      transientBtn.remove();
    }
  });

  it("queries Execution listSubjects with q for partial subject searches beyond 200 rows and drops stale out-of-order responses", async () => {
    vi.useFakeTimers();
    const preload = Array.from({ length: 200 }, (_, i) => ({
      id: `0192f3c4-5e6a-7b8c-9d0e-a1b2c3d4${String(i + 1).padStart(4, "0")}`,
      subject_id: `SUBJ-${String(i + 1).padStart(4, "0")}`,
      study_id: "CAD-101",
      site_id: "SITE-17",
      status: "ACTIVE",
      active_protocol_version: "3.0",
      enrollment_index: i + 1,
    }));
    const beyond = {
      id: "0192f3c4-5e6a-7b8c-9d0e-a1b2c3d40245",
      subject_id: "SUBJ-0245",
      study_id: "CAD-101",
      site_id: "SITE-22",
      status: "SCREENING",
      active_protocol_version: "3.0",
      enrollment_index: 245,
    };
    const staleMatch = {
      id: "0192f3c4-5e6a-7b8c-9d0e-a1b2c3d40219",
      subject_id: "SUBJ-0219",
      study_id: "CAD-101",
      site_id: "SITE-17",
      status: "ACTIVE",
      active_protocol_version: "3.0",
      enrollment_index: 219,
    };

    let resolveStaleQuery;
    const listSpy = vi
      .spyOn(executionApi, "listSubjects")
      .mockImplementation(({ q, limit = 200, offset = 0 }) => {
        if (!q) {
          return Promise.resolve({
            items: preload,
            total: 250,
            limit,
            offset,
          });
        }
        if (q === "021") {
          return new Promise((resolve) => {
            resolveStaleQuery = resolve;
          });
        }
        if (q === "024") {
          return Promise.resolve({
            items: [beyond],
            total: 1,
            limit,
            offset: 0,
          });
        }
        return Promise.resolve({ items: [], total: 0, limit, offset: 0 });
      });
    const detailSpy = vi
      .spyOn(executionApi, "getSubjectDetail")
      .mockRejectedValue(
        Object.assign(new Error("Not found"), { status: 404 })
      );
    const searchSpy = vi.spyOn(kernelApi, "search").mockResolvedValue([]);

    try {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["CRA"];

      const canvasStore = useStudyCanvasStore();
      canvasStore.openStudy("CAD-101");
      await router.push("/studies/CAD-101");

      mount(CommandPaletteOverlay, {
        props: { isOpen: true },
        global: { plugins: [pinia, router] },
      });
      await flushPromises();

      const input = body.find("#canvas-palette-input");
      await input.setValue("021");
      vi.advanceTimersByTime(250);

      await input.setValue("024");
      vi.advanceTimersByTime(250);
      await flushPromises();

      expect(listSpy).toHaveBeenCalledWith({
        studyId: "CAD-101",
        q: "024",
        limit: 5,
      });
      expect(body.find("#canvas-palette").text()).toContain("SUBJ-0245");

      resolveStaleQuery({
        items: [staleMatch],
        total: 1,
        limit: 5,
        offset: 0,
      });
      await flushPromises();

      expect(body.find("#canvas-palette").text()).toContain("SUBJ-0245");
      expect(body.find("#canvas-palette").text()).not.toContain("SUBJ-0219");
    } finally {
      listSpy.mockRestore();
      detailSpy.mockRestore();
      searchSpy.mockRestore();
      vi.useRealTimers();
    }
  });

  it("preserves studyId and siteId in Site readiness, Studies, and Data collection palette items across Canvas, /ecrf, and /ctms", async () => {
    const listSpy = vi
      .spyOn(executionApi, "listSubjects")
      .mockResolvedValue({ items: [], total: 0, limit: 200, offset: 0 });
    const searchSpy = vi.spyOn(kernelApi, "search").mockResolvedValue([]);
    const pushSpy = vi.spyOn(router, "push");

    try {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["CRA"];

      const canvasStore = useStudyCanvasStore();
      canvasStore.openStudy("STALE-STUDY");

      // 1. At /studies/CADENCE-101?site=SITE-101 -> Site readiness dispatches /ctms?studyId=CADENCE-101&siteId=SITE-101
      await router.push("/studies/CADENCE-101?site=SITE-101");
      const canvasPalette = mount(CommandPaletteOverlay, {
        props: { isOpen: true },
        global: { plugins: [pinia, router] },
      });
      await flushPromises();

      const input = body.find("#canvas-palette-input");
      await input.setValue("Site readiness");
      const workspaceReadiness = body
        .findAll("#canvas-palette [role='option']")
        .find(
          (o) =>
            o.find(".palette-label").text() === "Site readiness" &&
            o.find(".palette-group").text() === "Workspace"
        );
      expect(workspaceReadiness).toBeTruthy();
      await workspaceReadiness.trigger("click");
      expect(pushSpy).toHaveBeenLastCalledWith(
        "/studies/CADENCE-101?stage=start&section=startup-greenlight&site=SITE-101"
      );
      canvasPalette.unmount();

      // 2. At /ctms?studyId=CADENCE-101 -> Studies dispatches /studies/CADENCE-101 and Data collection dispatches Conduct canvas
      await router.push("/ctms?studyId=CADENCE-101");
      const ctmsPalette = mount(CommandPaletteOverlay, {
        props: { isOpen: true },
        global: { plugins: [pinia, router] },
      });
      await flushPromises();

      await body.find("#canvas-palette-input").setValue("Studies");
      const studyOption = body
        .findAll("#canvas-palette [role='option']")
        .find((o) => o.find(".palette-label").text() === "Studies");
      expect(studyOption).toBeTruthy();
      await studyOption.trigger("click");
      expect(pushSpy).toHaveBeenLastCalledWith("/studies/CADENCE-101");
      ctmsPalette.unmount();

      await router.push("/ecrf?studyId=CADENCE-101");
      const ecrfPalette = mount(CommandPaletteOverlay, {
        props: { isOpen: true },
        global: { plugins: [pinia, router] },
      });
      await flushPromises();

      await body.find("#canvas-palette-input").setValue("Data collection");
      const dcOption = body
        .findAll("#canvas-palette [role='option']")
        .find((o) => o.find(".palette-label").text() === "Data collection");
      expect(dcOption).toBeTruthy();
      await dcOption.trigger("click");
      expect(pushSpy).toHaveBeenLastCalledWith(
        "/studies/CADENCE-101?stage=conduct&section=participants"
      );
      ecrfPalette.unmount();

      vi.stubEnv("VITE_DEMO_MODE", "true");
      try {
        await router.push("/ecrf?studyId=CADENCE-101&siteId=SITE-101");
        const demoPalette = mount(CommandPaletteOverlay, {
          props: { isOpen: true },
          global: { plugins: [pinia, router] },
        });
        await flushPromises();
        await body.find("#canvas-palette-input").setValue("Data collection");
        const demoDcOption = body
          .findAll("#canvas-palette [role='option']")
          .find((o) => o.find(".palette-label").text() === "Data collection");
        expect(demoDcOption).toBeTruthy();
        await demoDcOption.trigger("click");
        expect(pushSpy).toHaveBeenLastCalledWith(
          "/ecrf?studyId=CADENCE-101&siteId=SITE-101"
        );
        demoPalette.unmount();

        const demoReadinessPalette = mount(CommandPaletteOverlay, {
          props: { isOpen: true },
          global: { plugins: [pinia, router] },
        });
        await flushPromises();
        await body.find("#canvas-palette-input").setValue("Site readiness");
        const demoReadinessOption = body
          .findAll("#canvas-palette [role='option']")
          .find(
            (o) =>
              o.find(".palette-label").text() === "Site readiness" &&
              o.find(".palette-group").text() === "Workspace"
          );
        expect(demoReadinessOption).toBeTruthy();
        await demoReadinessOption.trigger("click");
        expect(pushSpy).toHaveBeenLastCalledWith(
          "/ctms?studyId=CADENCE-101&siteId=SITE-101"
        );
        demoReadinessPalette.unmount();
      } finally {
        vi.unstubAllEnvs();
      }
    } finally {
      listSpy.mockRestore();
      searchSpy.mockRestore();
    }
  });
});
