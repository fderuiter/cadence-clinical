import { beforeEach, describe, expect, it, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import {
  notifySessionCacheCleared,
  runLogoutCleanups,
} from "../../src/auth/sessionCoordinator.js";
import {
  buildScopedWorkflowPath,
  resolveActiveWorkspaceScope,
} from "../../src/canvas/palette";
import {
  clearSynchronizedWorkspaceScope,
  resolveHydratedWorkspaceScope,
  syncWorkspaceScope,
} from "../../src/stores/workspaceScope";
import { useClinicalScopeStore } from "../../src/stores/clinicalScope";
import { useStudyCanvasStore } from "../../src/stores/studyCanvas";
import { resolveAuthorizedTaskRoute } from "../../src/stores/onboarding.js";
import { router } from "../../src/router/index.js";

describe("workspaceScope cross-view synchronization", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    notifySessionCacheCleared();
  });

  it("returns empty scope fields rather than synthetic study or site defaults when unset", () => {
    const scope = resolveActiveWorkspaceScope(null, useStudyCanvasStore());
    expect(scope).toMatchObject({
      studyCode: "",
      siteId: "",
      explicitSiteId: "",
      subjectCode: "",
      enrollmentId: "",
    });
    expect(resolveHydratedWorkspaceScope(null)).toEqual({
      studyId: "",
      siteId: "",
    });
    expect(buildScopedWorkflowPath("/ecrf", scope)).toBe("/ecrf");
  });

  it("synchronizes studyId and siteId across studyCanvas, clinicalScope, and router query", () => {
    const clinicalScope = useClinicalScopeStore();
    const studyCanvas = useStudyCanvasStore();
    const replace = vi.fn().mockResolvedValue(undefined);
    const mockRoute = { query: { tab: "greenlight" }, params: {} };

    syncWorkspaceScope({
      studyId: "CADENCE-ONC-204",
      siteId: "SITE-044",
      router: { replace } as never,
      route: mockRoute,
      syncRouteQuery: true,
    });

    expect(clinicalScope.activeStudyId).toBe("CADENCE-ONC-204");
    expect(clinicalScope.activeSiteId).toBe("SITE-044");
    expect(studyCanvas.studyCode).toBe("CADENCE-ONC-204");
    expect(replace).toHaveBeenCalledWith({
      query: {
        tab: "greenlight",
        studyId: "CADENCE-ONC-204",
        siteId: "SITE-044",
      },
    });
    expect(resolveActiveWorkspaceScope(null, studyCanvas)).toMatchObject({
      studyCode: "CADENCE-ONC-204",
      siteId: "SITE-044",
    });
    expect(resolveHydratedWorkspaceScope(null)).toEqual({
      studyId: "CADENCE-ONC-204",
      siteId: "SITE-044",
    });
  });

  it("clears stale activeSiteId when switching to a different study without an explicit site", () => {
    syncWorkspaceScope({
      studyId: "CADENCE-101",
      siteId: "SITE-101",
    });
    syncWorkspaceScope({
      studyId: "CADENCE-202",
    });

    const clinicalScope = useClinicalScopeStore();
    expect(clinicalScope.activeStudyId).toBe("CADENCE-202");
    expect(clinicalScope.activeSiteId).toBe("");
    expect(resolveHydratedWorkspaceScope(null)).toEqual({
      studyId: "CADENCE-202",
      siteId: "",
    });
  });

  it("forwards active studyCode and site in /data-lock and /exports route redirects", () => {
    syncWorkspaceScope({
      studyId: "CADENCE-LOCK-01",
      siteId: "SITE-09",
    });

    const routes = router.options.routes;
    const dataLockRoute = routes.find((r) => r.path === "/data-lock");
    const exportsRoute = routes.find((r) => r.path === "/exports");

    expect(dataLockRoute?.beforeEnter).toBeTypeOf("function");
    expect(exportsRoute?.beforeEnter).toBeTypeOf("function");

    const redirectFnLock = dataLockRoute!.beforeEnter as (to: {
      query: Record<string, string>;
    }) => unknown;
    const redirectFnExports = exportsRoute!.beforeEnter as (to: {
      query: Record<string, string>;
    }) => unknown;

    expect(redirectFnLock({ query: {} })).toEqual({
      name: "study",
      params: { studyCode: "CADENCE-LOCK-01" },
      query: {
        stage: "data",
        section: "locks",
        site: "SITE-09",
      },
    });

    expect(redirectFnExports({ query: {} })).toEqual({
      name: "study",
      params: { studyCode: "CADENCE-LOCK-01" },
      query: {
        stage: "submit",
        section: "exports",
        site: "SITE-09",
      },
    });
  });

  it("enriches onboarding task routes with active study and site scope", () => {
    syncWorkspaceScope({
      studyId: "CADENCE-DM-301",
      siteId: "SITE-301",
    });

    expect(
      resolveAuthorizedTaskRoute({ route: "/ecrf" }, ["data_manager"])
    ).toBe(
      "/studies/CADENCE-DM-301?stage=conduct&site=SITE-301&section=participants"
    );
    expect(
      resolveAuthorizedTaskRoute({ route: "/studies" }, ["data_manager"])
    ).toBe("/studies/CADENCE-DM-301?site=SITE-301");
  });

  it("clears synchronized study, site, and canvas scope on session cache clear and logout", () => {
    const clinicalScope = useClinicalScopeStore();
    const studyCanvas = useStudyCanvasStore();

    syncWorkspaceScope({
      studyId: "CADENCE-101",
      siteId: "SITE-001",
    });
    expect(clinicalScope.activeStudyId).toBe("CADENCE-101");
    expect(studyCanvas.studyCode).toBe("CADENCE-101");

    notifySessionCacheCleared();

    expect(clinicalScope.activeStudyId).toBe("");
    expect(clinicalScope.activeSiteId).toBe("");
    expect(studyCanvas.studyCode).toBe("");
    expect(resolveActiveWorkspaceScope(null, studyCanvas)).toMatchObject({
      studyCode: "",
      siteId: "",
    });

    syncWorkspaceScope({
      studyId: "CADENCE-202",
      siteId: "SITE-002",
    });
    runLogoutCleanups();
    clearSynchronizedWorkspaceScope();
    expect(clinicalScope.activeStudyId).toBe("");
    expect(clinicalScope.activeSiteId).toBe("");
    expect(studyCanvas.studyCode).toBe("");
  });

  it("propagates active study and site across StudyTrainingView, TicketsView + TicketCreateModal, OverviewView, and /data-lock redirects", async () => {
    const { flushPromises, mount } = await import("@vue/test-utils");
    const { getActivePinia } = await import("pinia");
    const { createMemoryHistory, createRouter } = await import("vue-router");
    const { trainingApi } = await import("../../src/api/training");
    const { ticketsApi } = await import("../../src/api/ticketsApi");
    const { useAuthStore } = await import("../../src/stores/auth");
    const { useClinicalStore } = await import("../../src/stores/clinical");
    const StudyTrainingView = (
      await import("../../src/views/StudyTrainingView.vue")
    ).default;
    const TicketsView = (await import("../../src/views/TicketsView.vue"))
      .default;
    const OverviewView = (await import("../../src/views/OverviewView.vue"))
      .default;

    const pinia = getActivePinia()!;
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Data Manager"];
    authStore.currentPersona = "data_manager";

    vi.spyOn(trainingApi, "listCurricula").mockResolvedValue([]);
    vi.spyOn(trainingApi, "listAssignments").mockResolvedValue([]);
    vi.spyOn(trainingApi, "listDelegations").mockResolvedValue([]);
    vi.spyOn(ticketsApi, "listTickets").mockResolvedValue([]);

    const localRouter = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: "/training",
          name: "training",
          component: StudyTrainingView,
        },
        {
          path: "/tickets",
          name: "tickets",
          component: TicketsView,
        },
        {
          path: "/overview",
          name: "overview",
          component: OverviewView,
        },
        {
          path: "/studies/:studyCode?",
          name: "study",
          component: { template: "<div />" },
        },
        {
          path: "/ecrf",
          name: "ecrf",
          component: { template: "<div />" },
        },
        {
          path: "/ctms",
          name: "ctms",
          component: { template: "<div />" },
        },
      ],
    });

    const clinicalStore = useClinicalStore();
    const studyCanvas = useStudyCanvasStore();

    // 1. Switch study and site in /training -> updates studyCanvas, clinical, and URL studyId/siteId
    await localRouter.push("/training");
    await localRouter.isReady();
    const trainingWrapper = mount(StudyTrainingView, {
      global: { plugins: [pinia, localRouter] },
    });
    await flushPromises();

    await trainingWrapper
      .find("#training-study-filter")
      .setValue("CADENCE-TRN-01");
    await trainingWrapper.find("#training-site-filter").setValue("SITE-077");
    await trainingWrapper.find("#training-study-filter").trigger("change");
    await flushPromises();

    expect(studyCanvas.studyCode).toBe("CADENCE-TRN-01");
    expect(clinicalStore.activeStudyId).toBe("CADENCE-TRN-01");
    expect(clinicalStore.activeSiteId).toBe("SITE-077");
    expect(localRouter.currentRoute.value.query).toMatchObject({
      studyId: "CADENCE-TRN-01",
      siteId: "SITE-077",
    });
    trainingWrapper.unmount();

    // 2. Navigate to /tickets without query -> hydrates active study & site and pre-populates TicketCreateModal
    await localRouter.push("/tickets");
    const ticketsWrapper = mount(TicketsView, {
      global: { plugins: [pinia, localRouter] },
    });
    await flushPromises();

    expect(
      (ticketsWrapper.find("#tickets-study-filter").element as HTMLInputElement)
        .value
    ).toBe("CADENCE-TRN-01");
    expect(
      (ticketsWrapper.find("#tickets-site-filter").element as HTMLInputElement)
        .value
    ).toBe("SITE-077");

    // Open TicketCreateModal and verify default study/site hydration
    const createBtn = ticketsWrapper
      .findAll("button")
      .find((b) => b.text().includes("Log Issue"));
    expect(createBtn).toBeDefined();
    await createBtn!.trigger("click");
    await flushPromises();
    expect(
      (ticketsWrapper.find("#ticket-study").element as HTMLInputElement).value
    ).toBe("CADENCE-TRN-01");
    expect(
      (ticketsWrapper.find("#ticket-site").element as HTMLInputElement).value
    ).toBe("SITE-077");

    // Switch study in /tickets -> updates studyCanvas, clinical, and URL studyId
    await ticketsWrapper
      .find("#tickets-study-filter")
      .setValue("CADENCE-TKT-02");
    await ticketsWrapper.find("#tickets-site-filter").setValue("SITE-088");
    await ticketsWrapper.find("#tickets-study-filter").trigger("change");
    await flushPromises();

    expect(studyCanvas.studyCode).toBe("CADENCE-TKT-02");
    expect(clinicalStore.activeStudyId).toBe("CADENCE-TKT-02");
    expect(clinicalStore.activeSiteId).toBe("SITE-088");
    expect(localRouter.currentRoute.value.query).toMatchObject({
      studyId: "CADENCE-TKT-02",
      siteId: "SITE-088",
    });
    ticketsWrapper.unmount();

    // 3. OverviewView forwards active study and site parameters on workflow launchers
    await localRouter.push("/overview");
    const pushSpy = vi.spyOn(localRouter, "push");
    const overviewWrapper = mount(OverviewView, {
      global: { plugins: [pinia, localRouter] },
    });
    await flushPromises();

    const launcherBtns = overviewWrapper.findAll(".btn-launcher-action");
    expect(launcherBtns.length).toBeGreaterThanOrEqual(3);
    await launcherBtns[0].trigger("click");
    expect(pushSpy).toHaveBeenCalledWith(
      "/studies/CADENCE-TKT-02?site=SITE-088"
    );
    overviewWrapper.unmount();
  });
});
