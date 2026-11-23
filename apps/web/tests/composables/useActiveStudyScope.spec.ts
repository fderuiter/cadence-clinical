import { DOMWrapper, flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import AppShell from "../../src/components/AppShell.vue";
import CommandPaletteOverlay from "../../src/components/CommandPaletteOverlay.vue";
import {
  clearActiveStudyScope,
  syncActiveStudyScope,
  useActiveStudyScope,
} from "../../src/composables/useActiveStudyScope";
import { useAuthStore } from "../../src/stores/auth";
import { useClinicalStore } from "../../src/stores/clinical";
import { useClinicalScopeStore } from "../../src/stores/clinicalScope";
import { useEtmfStore } from "../../src/stores/etmf";
import { useStudyCanvasStore } from "../../src/stores/studyCanvas";
import { signInAs } from "../helpers/signIn.js";

const ScopeProbe = {
  template: `<div data-testid="probe-study">{{ activeStudyCode }}</div>`,
  setup() {
    const { activeStudyCode } = useActiveStudyScope();
    return { activeStudyCode };
  },
};

function createTestRouter() {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: "/studies/:studyCode?", name: "study", component: ScopeProbe },
      { path: "/ctms", name: "ctms", component: ScopeProbe },
      { path: "/ssu", name: "ssu", component: ScopeProbe },
      { path: "/ecrf", name: "ecrf", component: ScopeProbe },
      { path: "/etmf", name: "etmf", component: ScopeProbe },
      { path: "/training", name: "training", component: ScopeProbe },
      { path: "/tickets", name: "tickets", component: ScopeProbe },
      { path: "/knowledge", name: "knowledge", component: ScopeProbe },
      { path: "/overview", name: "overview", component: ScopeProbe },
      { path: "/plans", name: "plans", component: ScopeProbe },
      { path: "/login", name: "login", component: ScopeProbe },
    ],
  });
}

describe("useActiveStudyScope bidirectional navigation", () => {
  let pinia: ReturnType<typeof createPinia>;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    clearActiveStudyScope();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("synchronizes study codes across studyCanvas, clinicalScope, clinical, and etmf stores without default study IDs outside demo mode", async () => {
    const canvasStore = useStudyCanvasStore();
    const clinicalScopeStore = useClinicalScopeStore();
    const clinicalStore = useClinicalStore();
    const etmfStore = useEtmfStore();

    expect(canvasStore.studyCode).toBe("");
    expect(clinicalScopeStore.activeStudyId).toBe("");
    expect(clinicalStore.activeStudyId).toBe("");
    expect(etmfStore.currentStudyId).toBe("");

    syncActiveStudyScope("CADENCE-101");
    expect(canvasStore.studyCode).toBe("CADENCE-101");
    expect(clinicalScopeStore.activeStudyId).toBe("CADENCE-101");
    expect(clinicalStore.activeStudyId).toBe("CADENCE-101");
    expect(etmfStore.currentStudyId).toBe("CADENCE-101");

    canvasStore.openStudy("CADENCE-202");
    expect(clinicalScopeStore.activeStudyId).toBe("CADENCE-202");
    expect(clinicalStore.activeStudyId).toBe("CADENCE-202");
    expect(etmfStore.currentStudyId).toBe("CADENCE-202");

    const authStore = useAuthStore();
    signInAs(authStore, ["CRA"]);
    await authStore.logout();
    expect(canvasStore.studyCode).toBe("");
    expect(clinicalScopeStore.activeStudyId).toBe("");
    expect(clinicalStore.activeStudyId).toBe("");
    expect(etmfStore.currentStudyId).toBe("");
  });

  it("preserves study scope in both directions across /studies/:studyCode, /ctms, /ssu, /ecrf, /etmf, /training, and /overview via AppShell and CommandPaletteOverlay", async () => {
    const authStore = useAuthStore();
    signInAs(authStore, ["CRA", "Data Manager"], { username: "cra_lead" });
    const canvasStore = useStudyCanvasStore();
    vi.spyOn(canvasStore, "ensurePaletteParticipants").mockResolvedValue();

    const router = createTestRouter();
    await router.push("/studies/CADENCE-101");
    await router.isReady();

    const shell = mount(
      {
        components: { AppShell },
        template: "<AppShell><router-view /></AppShell>",
      },
      {
        global: { plugins: [pinia, router] },
        attachTo: document.body,
      }
    );
    await flushPromises();

    // AppShell primary navigation forwards active study to unified Conduct canvas and /ctms
    expect(shell.find("#nav-studies").attributes("href")).toBe(
      "/studies/CADENCE-101"
    );
    expect(shell.find("#nav-data-collection").attributes("href")).toBe(
      "/studies/CADENCE-101?stage=conduct&section=participants"
    );
    expect(shell.find("#nav-site-readiness").attributes("href")).toBe(
      "/ctms?studyId=CADENCE-101"
    );

    // Navigate via CommandPaletteOverlay to /etmf, /training, /overview, /ctms, Conduct canvas, and back to /studies/CADENCE-101
    const palette = mount(CommandPaletteOverlay, {
      props: { isOpen: true },
      global: { plugins: [pinia, router] },
      attachTo: document.body,
    });
    await flushPromises();

    const body = new DOMWrapper(document.body);
    const optionsText = body.find("#canvas-palette").text();
    expect(optionsText).toContain("Trial documents");
    expect(optionsText).toContain("Study training");
    expect(optionsText).toContain("Orientation");
    expect(optionsText).not.toMatch(/\b(EDC|CTMS|eTMF|RTSM)\b/);

    for (const [labelText, expectedPath, expectedQueryStudy] of [
      ["Trial documents", "/etmf", "CADENCE-101"],
      ["Study training", "/training", "CADENCE-101"],
      ["Orientation", "/overview", "CADENCE-101"],
      ["Site readiness", "/ctms", "CADENCE-101"],
    ] as const) {
      await palette.setProps({ isOpen: true });
      await flushPromises();
      const btn = body
        .findAll("#canvas-palette button, #canvas-palette [role='option']")
        .find((el) => el.text().includes(labelText));
      expect(btn).toBeDefined();
      await btn!.trigger("click");
      await flushPromises();

      expect(router.currentRoute.value.path).toBe(expectedPath);
      expect(router.currentRoute.value.query.studyId).toBe(expectedQueryStudy);
      expect(shell.find("[data-testid='probe-study']").text()).toBe(
        "CADENCE-101"
      );
    }

    await palette.setProps({ isOpen: true });
    await flushPromises();
    const dataCollectionBtn = body
      .findAll("#canvas-palette button, #canvas-palette [role='option']")
      .find((el) => el.text().includes("Data collection"));
    expect(dataCollectionBtn).toBeDefined();
    await dataCollectionBtn!.trigger("click");
    await flushPromises();

    expect(router.currentRoute.value.path).toBe("/studies/CADENCE-101");
    expect(router.currentRoute.value.query.stage).toBe("conduct");
    expect(shell.find("[data-testid='probe-study']").text()).toBe(
      "CADENCE-101"
    );

    // Reverse direction: entering via /ssu?studyId=CADENCE-303 as a site CRC updates AppShell + palette back to /studies/CADENCE-303 and /training (even without query param)
    signInAs(authStore, ["CRC"], { username: "site_crc" });
    await router.push("/ssu?studyId=CADENCE-303");
    await flushPromises();

    expect(shell.find("#nav-site-readiness").attributes("href")).toBe(
      "/ssu?studyId=CADENCE-303"
    );
    expect(shell.find("#nav-studies").attributes("href")).toBe(
      "/studies/CADENCE-303"
    );

    // Navigating to /training without a query param still preserves CADENCE-303
    await router.push("/training");
    await flushPromises();
    expect(shell.find("[data-testid='probe-study']").text()).toBe(
      "CADENCE-303"
    );

    // Navigating back to Studies via CommandPaletteOverlay opens /studies/CADENCE-303
    await palette.setProps({ isOpen: true });
    await flushPromises();
    const studiesOption = body
      .findAll("#canvas-palette button, #canvas-palette [role='option']")
      .find((el) => el.text().includes("Studies"));
    expect(studiesOption).toBeDefined();
    await studiesOption!.trigger("click");
    await flushPromises();
    expect(router.currentRoute.value.fullPath).toBe("/studies/CADENCE-303");

    palette.unmount();
    shell.unmount();
  });
});
