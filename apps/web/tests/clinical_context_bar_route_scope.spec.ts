import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createMemoryHistory, createRouter } from "vue-router";
import {
  buildScopedStudyLocation,
  buildScopedWorkflowPath,
  buildStudiesWorkspacePath,
  buildWorkspacePaletteItems,
  executePaletteTarget,
  readInstantiatedClinicalScope,
  resolveActiveWorkspaceScope,
} from "../src/canvas/palette";
import { i18n } from "../src/i18n/index.js";
import { webDemoStore } from "../src/mocks/demoStore.js";
import { useAuthStore } from "../src/stores/auth";
import { useClinicalStore } from "../src/stores/clinical";
import { useClinicalScopeStore } from "../src/stores/clinicalScope";
import { useConfirmStore } from "../src/stores/confirm";
import { useKnowledgeStore } from "../src/stores/knowledge.js";
import {
  resolveAuthorizedTaskRoute,
  useOnboardingStore,
} from "../src/stores/onboarding.js";
import { useStudyCanvasStore } from "../src/stores/studyCanvas";
import CommandPaletteOverlay from "../src/components/CommandPaletteOverlay.vue";
import ContextualHelpDrawer from "../src/components/ContextualHelpDrawer.vue";
import OnboardingChecklistDrawer from "../src/components/OnboardingChecklistDrawer.vue";
import CanvasPalette from "../src/components/canvas/CanvasPalette.vue";
import StudySitesPanel from "../src/components/canvas/StudySitesPanel.vue";
import ValidationSection from "../src/components/canvas/ValidationSection.vue";
import { clearKernelStudyCache } from "../src/canvas/kernelStudy";
import CtmsView from "../src/views/CtmsView.vue";
import DocumentManagerView from "../src/views/DocumentManagerView.vue";
import EcrfView from "../src/views/EcrfView.vue";
import KnowledgeView from "../src/views/KnowledgeView.vue";
import ObservationView from "../src/views/ObservationView.vue";
import OverviewView from "../src/views/OverviewView.vue";
import StudyCanvasView from "../src/views/StudyCanvasView.vue";
import StudyTrainingView from "../src/views/StudyTrainingView.vue";
import TicketsView from "../src/views/TicketsView.vue";
import * as availabilityModule from "../src/router/availability";
import { trainingApi } from "../src/api/training";
import { kernelApi } from "../src/api/kernelApi";
import { knowledgeService } from "../src/api/knowledge.js";
import { ticketsApi } from "../src/api/ticketsApi";
import { etmfService } from "../src/api/etmf";
import { signInAs } from "./helpers/signIn.js";

const PARTICIPANT_UUID = "0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e";
const PARTICIPANT_NODE_REF = `participant:${PARTICIPANT_UUID}`;
const FORM_UUID = "0192f3c4-5e6a-7b8c-9d0e-00000000f001";
const OBSERVATION_UUID = "0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d70";

function buildTestRouter() {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: "/studies/:studyCode?",
        name: "study",
        component: StudyCanvasView,
      },
      { path: "/ctms", name: "ctms", component: CtmsView },
      { path: "/ssu", name: "ssu", component: CtmsView },
      { path: "/training", name: "training", component: StudyTrainingView },
      { path: "/tickets", name: "tickets", component: TicketsView },
      { path: "/knowledge", name: "knowledge", component: KnowledgeView },
      { path: "/overview", name: "overview", component: OverviewView },
      { path: "/ecrf", name: "ecrf", component: EcrfView },
      { path: "/etmf", name: "etmf", component: DocumentManagerView },
      {
        path: "/observations/:observationRef",
        name: "observation",
        component: ObservationView,
        props: true,
      },
    ],
  });
}

describe("Shared ClinicalContextBar & Canonical Route Scope Codec", () => {
  beforeEach(() => {
    vi.stubEnv("VITE_DEMO_MODE", "false");
    setActivePinia(createPinia());
    signInAs(useAuthStore(), ["Data Manager", "CRA", "Site Investigator"], {
      username: "clinical_lead",
      assignedStudies: ["CADENCE-101", "STUDY-ONC-202"],
      assignedSites: ["SITE-101", "SITE-102"],
    });
  });

  afterEach(() => {
    vi.unstubAllEnvs();
    vi.restoreAllMocks();
  });

  it("does not fabricate SUBJ-001 in non-demo clinicalScope store and reads clinicalScope in palette helpers (Req 5)", () => {
    const scopeStore = useClinicalScopeStore();
    expect(scopeStore.activeStudyId).toBe("");
    expect(scopeStore.activeSiteId).toBe("");
    expect(scopeStore.activeSubjectId).toBe("");

    scopeStore.activeStudyId = "CADENCE-101";
    scopeStore.activeSiteId = "SITE-101";
    scopeStore.activeSubjectId = "017-0001";

    expect(readInstantiatedClinicalScope()).toEqual({
      activeStudyId: "CADENCE-101",
      activeSiteId: "SITE-101",
      activeSubjectId: "017-0001",
    });

    const canvasStore = useStudyCanvasStore();
    canvasStore.studyCode = "CADENCE-101";

    const resolved = resolveActiveWorkspaceScope(
      { params: { studyCode: "CADENCE-101" }, query: {} },
      canvasStore
    );
    expect(resolved.studyCode).toBe("CADENCE-101");
    expect(resolved.siteId).toBe("SITE-101");
    expect(resolved.subjectCode).toBe("017-0001");
  });

  it("serializes study, site, and participant parameters across Studies, Support, Knowledge, Site readiness, and Data collection palette items (Req 6)", () => {
    const items = buildWorkspacePaletteItems({
      isAuthenticated: true,
      normalizedRoles: ["cra"],
      activeStudyCode: "CADENCE-101",
      activeSiteId: "SITE-101",
      activeSubjectCode: "017-0001",
      activeParticipantRef: PARTICIPANT_NODE_REF,
      activeEnrollmentId: "enr-001",
    });

    const byKey = Object.fromEntries(
      items.map((item) => [item.key, item.target.routePath])
    );
    expect(byKey["workspace:studies"]).toBe(
      `/studies/CADENCE-101?site=SITE-101&participant=${encodeURIComponent(PARTICIPANT_NODE_REF)}`
    );
    expect(byKey["workspace:site-readiness"]).toBe(
      "/ctms?studyId=CADENCE-101&siteId=SITE-101&subjectId=017-0001"
    );
    expect(byKey["workspace:data-collection"]).toBe(
      `/studies/CADENCE-101?stage=conduct&site=SITE-101&participant=${encodeURIComponent(PARTICIPANT_NODE_REF)}`
    );
    expect(byKey["workspace:support"]).toBe(
      "/tickets?studyId=CADENCE-101&siteId=SITE-101&subjectId=017-0001"
    );
    expect(byKey["workspace:knowledge"]).toBe(
      "/knowledge?studyId=CADENCE-101&siteId=SITE-101&subjectId=017-0001"
    );

    vi.stubEnv("VITE_DEMO_MODE", "true");
    try {
      const demoItems = buildWorkspacePaletteItems({
        isAuthenticated: true,
        normalizedRoles: ["cra"],
        activeStudyCode: "CADENCE-101",
        activeSiteId: "SITE-101",
        activeSubjectCode: "017-0001",
        activeParticipantRef: PARTICIPANT_NODE_REF,
        activeEnrollmentId: "enr-001",
      });
      const demoByKey = Object.fromEntries(
        demoItems.map((item) => [item.key, item.target.routePath])
      );
      expect(demoByKey["workspace:data-collection"]).toBe(
        "/ecrf?studyId=CADENCE-101&siteId=SITE-101&subjectId=017-0001&enrollmentId=enr-001"
      );
    } finally {
      vi.unstubAllEnvs();
    }

    expect(
      buildStudiesWorkspacePath({
        studyCode: "CADENCE-101",
        siteId: "SITE-101",
        subjectCode: "017-0001",
      })
    ).toBe("/studies/CADENCE-101?site=SITE-101&participant=017-0001");
    expect(
      buildScopedWorkflowPath("/ssu", {
        studyCode: "CADENCE-101",
        siteId: "SITE-101",
        subjectCode: "017-0001",
      })
    ).toBe("/ssu?studyId=CADENCE-101&siteId=SITE-101&subjectId=017-0001");
  });

  it("aligns demoStore getZoomPath study step labels with KernelNavigator study.Code (Req 7)", () => {
    const studyPath = webDemoStore.getZoomPath(
      "study:00000000-0000-0000-0000-000000000001" // deid-ignore
    );
    const studyStep = studyPath.find(
      (step: { level: string }) => step.level === "study"
    );
    expect(studyStep).toBeTruthy();
    expect(studyStep?.label).not.toContain(" — ");
    const allStudies = webDemoStore.listStudies();
    expect(allStudies.length).toBeGreaterThan(0);
    const firstStudy = allStudies[0];
    const pathFromRef = webDemoStore.getZoomPath(firstStudy.ref);
    const step = pathFromRef.find(
      (s: { level: string }) => s.level === "study"
    );
    expect(step?.label).toBe(firstStudy.code);
  });

  it("preserves active site and participant node across StudyCanvasView stage, section, and Study home links while keeping zoom-up resets intact (Req 3 & Req 4)", async () => {
    vi.spyOn(kernelApi, "listStudies").mockResolvedValue([]);
    vi.spyOn(kernelApi, "listStudySites").mockResolvedValue([
      {
        ref: "site:0192f3c4-5e6a-7b8c-9d0e-000000000201",
        code: "SITE-101",
        name: "Boston General",
        country: "US",
        status: "ACTIVE",
      },
    ]);
    vi.spyOn(kernelApi, "listProtocolVersions").mockResolvedValue([]);
    vi.spyOn(trainingApi, "listCurricula").mockResolvedValue([]);
    vi.spyOn(trainingApi, "listAssignments").mockResolvedValue([]);
    vi.spyOn(trainingApi, "listDelegations").mockResolvedValue([]);

    const canvasStore = useStudyCanvasStore();
    canvasStore.studyCode = "CADENCE-101";
    canvasStore.knownParticipants[PARTICIPANT_UUID] = {
      id: PARTICIPANT_UUID,
      subject_id: "017-0001",
      study_id: "CADENCE-101",
      site_id: "SITE-101",
      status: "ENROLLED",
    };

    const router = buildTestRouter();
    await router.push(
      `/studies/CADENCE-101?stage=conduct&section=participants&site=SITE-101&node=${encodeURIComponent(PARTICIPANT_NODE_REF)}`
    );
    await router.isReady();

    const wrapper = mount(StudyCanvasView, {
      global: { plugins: [router, i18n] },
    });
    await flushPromises();

    const startStageHref = wrapper.find("#stage-start").attributes("href");
    expect(startStageHref).toContain("stage=start");
    expect(startStageHref).toContain("site=SITE-101");
    expect(startStageHref).toContain(`node=${PARTICIPANT_NODE_REF}`);

    const studyHomeHref = wrapper.find("#nav-study-home").attributes("href");
    expect(studyHomeHref).toContain("/studies/CADENCE-101?");
    expect(studyHomeHref).toContain("site=SITE-101");
    expect(studyHomeHref).toContain(`node=${PARTICIPANT_NODE_REF}`);

    // Zoom-up links still clear deeper levels intentionally:
    expect(wrapper.find("#zoom-studies").attributes("href")).toBe("/studies");
    expect(wrapper.find("#zoom-study").attributes("href")).toBe(
      "/studies/CADENCE-101"
    );
    expect(wrapper.find("#zoom-site").attributes("href")).toBe(
      "/studies/CADENCE-101?stage=conduct&section=participants&site=SITE-101"
    );

    // Navigate to Start -> Training inside StudyCanvasView and verify :site-id="siteFilter"
    await router.push(
      "/studies/CADENCE-101?stage=start&section=training&site=SITE-101"
    );
    await flushPromises();

    expect(trainingApi.listAssignments).toHaveBeenCalledWith(
      expect.objectContaining({
        study_id: "CADENCE-101",
        site_id: "SITE-101",
      })
    );

    wrapper.unmount();
  });

  it("redirects standalone ObservationView with canonical participant NodeRef when known in studyCanvas (Req 7)", async () => {
    const obsRef = `observation:${OBSERVATION_UUID}`;
    const enrollmentRef = "enrollment:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d72";

    vi.spyOn(kernelApi, "getObservation").mockResolvedValue({
      ref: obsRef,
      version: 1,
      concept: "concept:systolic@1",
      enrollment_ref: enrollmentRef,
      value: { kind: "quantity", number: "120", unit: "mm[Hg]" },
      observed_at: "2026-10-01T10:00:00Z",
      locks: [],
      revisions: [],
      audit: [],
    } as never);
    vi.spyOn(kernelApi, "getZoomPath").mockResolvedValue([
      {
        level: "study",
        node: { kind: "study", id: "0192f3c4-5e6a-7b8c-9d0e-000000000101" },
        label: "CADENCE-101",
        mask: null,
      },
      {
        level: "site",
        node: { kind: "site", id: "0192f3c4-5e6a-7b8c-9d0e-000000000201" },
        label: "SITE-101 — Boston General",
        mask: null,
      },
      {
        level: "enrollment",
        node: {
          kind: "enrollment",
          id: "0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d72",
        },
        label: "017-0001",
        mask: null,
      },
    ] as never);

    const canvasStore = useStudyCanvasStore();
    canvasStore.studyCode = "CADENCE-101";
    canvasStore.knownParticipants[PARTICIPANT_UUID] = {
      id: PARTICIPANT_UUID,
      subject_id: "017-0001",
      study_id: "CADENCE-101",
      site_id: "SITE-101",
      status: "ENROLLED",
    };

    const router = buildTestRouter();
    await router.push(`/observations/${obsRef}`);
    await router.isReady();

    const wrapper = mount(
      { template: "<router-view />" },
      { global: { plugins: [router, i18n] } }
    );
    await flushPromises();

    expect(router.currentRoute.value.name).toBe("study");
    expect(router.currentRoute.value.params.studyCode).toBe("CADENCE-101");
    expect(router.currentRoute.value.query).toEqual({
      stage: "conduct",
      site: "SITE-101",
      participant: PARTICIPANT_NODE_REF,
      observation: obsRef,
    });
    wrapper.unmount();
  });

  it("syncs standalone scope changes to URL queries via router.replace across /ctms, /ssu, /training, /tickets, /knowledge, /ecrf, and /etmf without false draft prompts (Req 8)", async () => {
    vi.spyOn(trainingApi, "listCurricula").mockResolvedValue([]);
    vi.spyOn(trainingApi, "listAssignments").mockResolvedValue([]);
    vi.spyOn(trainingApi, "listDelegations").mockResolvedValue([]);
    vi.spyOn(ticketsApi, "listTickets").mockResolvedValue([]);
    vi.spyOn(knowledgeService, "listCategories").mockResolvedValue([]);
    vi.spyOn(knowledgeService, "listArticles").mockResolvedValue([]);
    vi.spyOn(etmfService, "getDocuments").mockResolvedValue([]);
    vi.spyOn(etmfService, "getQcQueue").mockResolvedValue([]);
    vi.spyOn(etmfService, "listExportJobs").mockResolvedValue([]);
    vi.spyOn(kernelApi, "listStudies").mockResolvedValue([]);

    const router = buildTestRouter();
    const replaceSpy = vi.spyOn(router, "replace");
    const scopeStore = useClinicalScopeStore();

    // 1. /ctms
    await router.push("/ctms?studyId=CADENCE-101");
    await router.isReady();
    let wrapper = mount(CtmsView, { global: { plugins: [router, i18n] } });
    await flushPromises();
    scopeStore.activeSiteId = "SITE-101";
    scopeStore.activeSubjectId = "017-0001";
    await flushPromises();
    expect(router.currentRoute.value.query).toMatchObject({
      studyId: "CADENCE-101",
      siteId: "SITE-101",
      subjectId: "017-0001",
    });
    wrapper.unmount();

    // 2. /ssu
    await router.push("/ssu?studyId=CADENCE-101&siteId=SITE-101");
    await router.isReady();
    wrapper = mount(CtmsView, { global: { plugins: [router, i18n] } });
    await flushPromises();
    scopeStore.activeSiteId = "SITE-102";
    await flushPromises();
    expect(router.currentRoute.value.query.siteId).toBe("SITE-102");
    wrapper.unmount();

    // 3. /training
    await router.push("/training?studyId=CADENCE-101");
    await router.isReady();
    wrapper = mount(StudyTrainingView, {
      global: { plugins: [router, i18n] },
    });
    await flushPromises();
    await wrapper.find("#training-site-filter").setValue("SITE-101");
    await wrapper.find("#training-site-filter").trigger("change");
    await flushPromises();
    expect(router.currentRoute.value.query).toMatchObject({
      studyId: "CADENCE-101",
      siteId: "SITE-101",
    });
    wrapper.unmount();

    // 4. /tickets
    await router.push("/tickets?studyId=CADENCE-101");
    await router.isReady();
    wrapper = mount(TicketsView, { global: { plugins: [router, i18n] } });
    await flushPromises();
    await wrapper.find("#tickets-site-filter").setValue("SITE-101");
    await wrapper.find("#tickets-subject-filter").setValue("017-0001");
    await flushPromises();
    expect(router.currentRoute.value.query).toMatchObject({
      studyId: "CADENCE-101",
      siteId: "SITE-101",
      subjectId: "017-0001",
    });
    wrapper.unmount();

    // 5. /knowledge
    await router.push("/knowledge?studyId=CADENCE-101");
    await router.isReady();
    wrapper = mount(KnowledgeView, { global: { plugins: [router, i18n] } });
    await flushPromises();
    await wrapper.find("#knowledge-site-filter").setValue("SITE-101");
    await wrapper.find("#knowledge-subject-filter").setValue("017-0001");
    await flushPromises();
    expect(router.currentRoute.value.query).toMatchObject({
      studyId: "CADENCE-101",
      siteId: "SITE-101",
      subjectId: "017-0001",
    });
    wrapper.unmount();

    // 6. /etmf
    await router.push("/etmf?studyId=CADENCE-101");
    await router.isReady();
    wrapper = mount(DocumentManagerView, {
      global: { plugins: [router, i18n] },
    });
    await flushPromises();
    await wrapper.find("#etmf-site-select").setValue("SITE-101");
    await wrapper.find("#etmf-site-select").trigger("change");
    await flushPromises();
    expect(router.currentRoute.value.query).toMatchObject({
      studyId: "CADENCE-101",
      siteId: "SITE-101",
    });
    wrapper.unmount();

    // 7. /ecrf (verify router.replace syncs scope WITHOUT firing false unsaved draft dialog when formId stays open)
    const confirmStore = useConfirmStore();
    const confirmOpenSpy = vi.spyOn(confirmStore, "open");
    useClinicalStore();
    await router.push(
      `/ecrf?studyId=CADENCE-101&siteId=SITE-101&formId=${FORM_UUID}`
    );
    await router.isReady();
    wrapper = mount(
      { template: "<router-view />" },
      {
        global: {
          plugins: [router, i18n],
          stubs: { EcrfLiveWorkspace: true },
        },
      }
    );
    await flushPromises();

    // Mark EcrfView's form panel as dirty via emitted event on stubbed workspace
    const ecrfViewVm = wrapper.findComponent(EcrfView);
    const liveStub = ecrfViewVm.findComponent({ name: "EcrfLiveWorkspace" });
    if (liveStub.exists()) {
      liveStub.vm.$emit("form-loaded", {
        form_id: FORM_UUID,
        collection_title: "Vital Signs",
      });
      liveStub.vm.$emit("dirty-change", true);
    }
    await flushPromises();

    // Update site/participant scope in-place while keeping the same formId
    scopeStore.activeSubjectId = "017-0001";
    await flushPromises();
    expect(router.currentRoute.value.query).toMatchObject({
      studyId: "CADENCE-101",
      siteId: "SITE-101",
      subjectId: "017-0001",
      formId: FORM_UUID,
    });
    expect(confirmOpenSpy).not.toHaveBeenCalled();
    expect(replaceSpy).toHaveBeenCalled();

    wrapper.unmount();
  });

  it("preserves same-study site, participant, and node scope across executePaletteTarget, CommandPaletteOverlay, and StudyCanvasView while clearing deeper scope on cross-study jumps", async () => {
    vi.spyOn(availabilityModule, "isScreenAvailable").mockReturnValue(true);
    const scopeStore = useClinicalScopeStore();
    scopeStore.activeStudyId = "CADENCE-101";
    scopeStore.activeSiteId = "SITE-101";
    scopeStore.activeSubjectId = "SUBJ-101-001";

    const canvasStore = useStudyCanvasStore();
    canvasStore.openStudy("CADENCE-101");
    canvasStore.rememberParticipants([
      {
        id: PARTICIPANT_UUID,
        subject_id: "SUBJ-101-001",
        study_id: "CADENCE-101",
        site_id: "SITE-101",
        status: "ACTIVE",
        active_protocol_version: "2.0",
        enrollment_index: 1,
        treatment_group: "ARM-A",
      },
    ]);

    const sameStudyLoc = buildScopedStudyLocation(
      "CADENCE-101",
      { stage: "start", section: "greenlight" },
      {
        studyCode: "CADENCE-101",
        siteId: "SITE-101",
        explicitSiteId: "SITE-101",
        recordParticipantParam: "",
        inspectorNodeParam: PARTICIPANT_NODE_REF,
      }
    );
    expect(sameStudyLoc).toEqual({
      name: "study",
      params: { studyCode: "CADENCE-101" },
      query: {
        stage: "start",
        section: "greenlight",
        site: "SITE-101",
        node: PARTICIPANT_NODE_REF,
      },
    });

    const crossStudyLoc = buildScopedStudyLocation(
      "STUDY-ONC-202",
      { stage: "conduct", section: "sites" },
      {
        studyCode: "CADENCE-101",
        siteId: "SITE-101",
        explicitSiteId: "SITE-101",
        recordParticipantParam: PARTICIPANT_NODE_REF,
        inspectorNodeParam: PARTICIPANT_NODE_REF,
      }
    );
    expect(crossStudyLoc).toEqual({
      name: "study",
      params: { studyCode: "STUDY-ONC-202" },
      query: {
        stage: "conduct",
        section: "sites",
      },
    });

    const router = buildTestRouter();
    await router.push({
      name: "study",
      params: { studyCode: "CADENCE-101" },
      query: {
        stage: "conduct",
        section: "participants",
        site: "SITE-101",
        node: PARTICIPANT_NODE_REF,
      },
    });
    await router.isReady();
    const pushSpy = vi.spyOn(router, "push");

    const overlayWrapper = mount(CommandPaletteOverlay, {
      props: { isOpen: true },
      global: { plugins: [router, i18n] },
      attachTo: document.body,
    });
    await flushPromises();

    const paletteComp = overlayWrapper.findComponent({ name: "CanvasPalette" });
    paletteComp.vm.$emit("go", {
      studyCode: "CADENCE-101",
      stage: "data",
      section: "validation",
    });
    await flushPromises();

    expect(pushSpy).toHaveBeenLastCalledWith({
      name: "study",
      params: { studyCode: "CADENCE-101" },
      query: {
        stage: "data",
        section: "validation",
        site: "SITE-101",
        node: PARTICIPANT_NODE_REF,
      },
    });

    // Cross-study jump clears deeper scope and resets clinicalScope site/subject
    executePaletteTarget(
      { studyCode: "STUDY-ONC-202" },
      {
        router,
        currentRouteName: "study",
        currentQuery: router.currentRoute.value.query,
        activeStudyCode: "CADENCE-101",
        workspaceScope: resolveActiveWorkspaceScope(
          router.currentRoute.value,
          canvasStore
        ),
      }
    );
    expect(pushSpy).toHaveBeenLastCalledWith({
      name: "study",
      params: { studyCode: "STUDY-ONC-202" },
      query: {},
    });
    expect(scopeStore.activeStudyId).toBe("STUDY-ONC-202");
    expect(scopeStore.activeSiteId).toBe("");
    expect(scopeStore.activeSubjectId).toBe("");

    overlayWrapper.unmount();
  });

  it("includes studyId, siteId, and subjectId on /etmf, /training, and /overview workspace palette items", () => {
    vi.spyOn(availabilityModule, "isScreenAvailable").mockReturnValue(true);
    const items = buildWorkspacePaletteItems({
      isAuthenticated: true,
      normalizedRoles: ["sponsor_admin", "cra", "data_manager"],
      activeStudyCode: "CADENCE-101",
      activeSiteId: "SITE-102",
      activeSubjectCode: "SUBJ-102-001",
    });

    const byKey = Object.fromEntries(
      items.map((item) => [item.key, item.target.routePath])
    );
    expect(byKey["workspace:trial-documents"]).toBe(
      "/etmf?studyId=CADENCE-101&siteId=SITE-102&subjectId=SUBJ-102-001"
    );
    expect(byKey["workspace:training"]).toBe(
      "/training?studyId=CADENCE-101&siteId=SITE-102&subjectId=SUBJ-102-001"
    );
    expect(byKey["workspace:orientation"]).toBe(
      "/overview?studyId=CADENCE-101&siteId=SITE-102&subjectId=SUBJ-102-001"
    );
  });

  it("forwards active study, site, and subject scope from ContextualHelpDrawer into /knowledge and /tickets filter inputs", async () => {
    vi.spyOn(availabilityModule, "isScreenAvailable").mockReturnValue(true);
    vi.spyOn(ticketsApi, "listTickets").mockResolvedValue([]);
    vi.spyOn(knowledgeService, "listCategories").mockResolvedValue([]);
    vi.spyOn(knowledgeService, "listArticles").mockResolvedValue([]);

    const router = buildTestRouter();
    await router.push(
      "/ctms?studyId=CADENCE-101&siteId=SITE-102&subjectId=SUBJ-102-001"
    );
    await router.isReady();

    const knowledgeStore = useKnowledgeStore();
    knowledgeStore.isOpen = true;

    const appWrapper = mount(
      {
        components: { ContextualHelpDrawer },
        template: "<div><router-view /><ContextualHelpDrawer /></div>",
      },
      {
        global: {
          plugins: [router, i18n],
          stubs: { CtmsLiveWorkspace: true },
        },
        attachTo: document.body,
      }
    );
    await flushPromises();

    const ticketsBtn = document.getElementById("btn-open-tickets-hub");
    expect(ticketsBtn).toBeTruthy();
    ticketsBtn?.click();
    await flushPromises();

    expect(router.currentRoute.value.path).toBe("/tickets");
    expect(router.currentRoute.value.query).toMatchObject({
      studyId: "CADENCE-101",
      siteId: "SITE-102",
      subjectId: "SUBJ-102-001",
    });
    const ticketsSiteInput = appWrapper.find("#tickets-site-filter")
      .element as HTMLInputElement;
    const ticketsSubjectInput = appWrapper.find("#tickets-subject-filter")
      .element as HTMLInputElement;
    expect(ticketsSiteInput.value).toBe("SITE-102");
    expect(ticketsSubjectInput.value).toBe("SUBJ-102-001");

    // Re-open drawer and navigate to Knowledge Hub
    knowledgeStore.isOpen = true;
    await flushPromises();
    const knowledgeBtn = document.getElementById("btn-open-knowledge-hub");
    expect(knowledgeBtn).toBeTruthy();
    knowledgeBtn?.click();
    await flushPromises();

    expect(router.currentRoute.value.path).toBe("/knowledge");
    expect(router.currentRoute.value.query).toMatchObject({
      studyId: "CADENCE-101",
      siteId: "SITE-102",
      subjectId: "SUBJ-102-001",
    });

    appWrapper.unmount();
  });

  it("preserves study, site, and participant scope when launching tasks and orientation from OnboardingChecklistDrawer and workflow cards from OverviewView in live and demo builds", async () => {
    vi.spyOn(availabilityModule, "isScreenAvailable").mockReturnValue(true);

    for (const demoMode of ["false", "true"]) {
      vi.stubEnv("VITE_DEMO_MODE", demoMode);
      const pinia = createPinia();
      setActivePinia(pinia);
      const authStore = useAuthStore();
      signInAs(authStore, ["Sponsor Admin", "CRA", "Data Manager", "CRC"], {
        username: "lead_operator",
        assignedStudies: ["CADENCE-101"],
        assignedSites: ["SITE-101"],
      });

      const scopeSnapshot = {
        studyCode: "CADENCE-101",
        siteId: "SITE-101",
        explicitSiteId: "SITE-101",
        subjectCode: "SUBJ-101-001",
        participantRef: PARTICIPANT_NODE_REF,
        enrollmentId: "04468158-43bd-80f8-92d0-27759ba5b15f", // deid-ignore: canonical demo fixture enrollment UUID
      };

      expect(
        resolveAuthorizedTaskRoute(
          { routeName: "ctms" },
          authStore.normalizedRoles,
          scopeSnapshot
        )
      ).toBe(
        "/ctms?studyId=CADENCE-101&siteId=SITE-101&subjectId=SUBJ-101-001"
      );
      expect(
        resolveAuthorizedTaskRoute(
          { routeName: "ecrf" },
          authStore.normalizedRoles,
          scopeSnapshot
        )
      ).toBe(
        demoMode === "true"
          ? "/ecrf?studyId=CADENCE-101&siteId=SITE-101&subjectId=SUBJ-101-001&enrollmentId=04468158-43bd-80f8-92d0-27759ba5b15f" // deid-ignore: canonical demo fixture enrollment UUID
          : `/studies/CADENCE-101?stage=conduct&site=SITE-101&participant=${encodeURIComponent(PARTICIPANT_NODE_REF)}`
      );

      const router = buildTestRouter();
      await router.push(
        "/overview?studyId=CADENCE-101&siteId=SITE-101&subjectId=SUBJ-101-001"
      );
      await router.isReady();
      const pushSpy = vi.spyOn(router, "push");

      const onboardingStore = useOnboardingStore();
      onboardingStore.isDrawerOpen = true;
      const drawerWrapper = mount(OnboardingChecklistDrawer, {
        global: { plugins: [pinia, router, i18n] },
        attachTo: document.body,
      });
      await flushPromises();

      const orientationBtn = document.getElementById(
        "btn-open-orientation-hub"
      );
      expect(orientationBtn).toBeTruthy();
      orientationBtn?.click();
      await flushPromises();
      expect(pushSpy).toHaveBeenLastCalledWith(
        "/overview?studyId=CADENCE-101&siteId=SITE-101&subjectId=SUBJ-101-001"
      );
      drawerWrapper.unmount();

      const overviewWrapper = mount(OverviewView, {
        global: { plugins: [pinia, router, i18n] },
      });
      await flushPromises();

      const launcherButtons = overviewWrapper.findAll(".launcher-card button");
      expect(launcherButtons.length).toBeGreaterThan(0);
      await launcherButtons[0].trigger("click");
      await flushPromises();
      const pushedTarget = String(pushSpy.mock.calls.at(-1)?.[0] || "");
      expect(pushedTarget).toContain("CADENCE-101");
      expect(pushedTarget).toContain("SITE-101");
      expect(pushedTarget).toContain("SUBJ-101-001");

      overviewWrapper.unmount();
    }
  });

  it("preserves active site, participant, and node scope in StudySitesPanel #btn-manage-study-sites and ValidationSection finding links", async () => {
    clearKernelStudyCache();
    vi.spyOn(kernelApi, "listStudySites").mockResolvedValue([]);
    vi.spyOn(kernelApi, "search").mockResolvedValue([
      {
        ref: "study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d80@1", // deid-ignore: synthetic test study ref
        kind: "study",
        code: "CADENCE-101",
        title: "CADENCE-101",
        exact_code: true,
      } as never,
    ]);
    vi.spyOn(kernelApi, "listStudies").mockResolvedValue([
      {
        ref: "study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d80", // deid-ignore: synthetic test study ref
        code: "CADENCE-101",
        title: "CADENCE-101",
        status: "ACTIVE",
        mask: null,
      } as never,
    ]);
    vi.spyOn(kernelApi, "listStudyRulePacks").mockResolvedValue({
      rule_packs: [],
      pinned_rule_pack_refs: [],
    });
    vi.spyOn(kernelApi, "listStudyValidationFindings").mockResolvedValue({
      findings: [
        {
          ref: "validation_finding:0192f3c4-5e6a-7b8c-9d0e-00000000a001", // deid-ignore: synthetic test finding ref
          rule_key: "VS-SYSBP-RANGE",
          severity: "Warning",
          status: "Open",
          message: "Systolic BP exceeds expected range",
          target_kind: "Record",
          record_key: FORM_UUID,
          variable: "SYSBP",
          detected_at: "2026-09-01T10:00:00Z", // deid-ignore: synthetic test timestamp
          decision: null,
        } as never,
        {
          ref: "validation_finding:0192f3c4-5e6a-7b8c-9d0e-00000000a002", // deid-ignore: synthetic test finding ref
          rule_key: "VS-OBS-VERIFY",
          severity: "Error",
          status: "Open",
          message: "Observation requires verification",
          target_kind: "Observation",
          record_key: OBSERVATION_UUID,
          variable: "DIABP",
          detected_at: "2026-09-01T10:01:00Z", // deid-ignore: synthetic test timestamp
          decision: null,
        } as never,
      ],
    });
    vi.spyOn(kernelApi, "canI").mockResolvedValue({
      permitted: true,
      explanation: { summary: "Permitted", reasons: [] },
    } as never);

    const router = buildTestRouter();
    await router.push({
      name: "study",
      params: { studyCode: "CADENCE-101" },
      query: {
        site: "SITE-101",
        node: PARTICIPANT_NODE_REF,
      },
    });
    await router.isReady();

    const sitesWrapper = mount(StudySitesPanel, {
      props: {
        studyCode: "CADENCE-101",
        compact: true,
        canManageSites: true,
        activeSite: "SITE-101",
        activeNode: PARTICIPANT_NODE_REF,
      },
      global: { plugins: [router, i18n] },
    });
    await flushPromises();

    const manageSitesLink = sitesWrapper.find("#btn-manage-study-sites");
    expect(manageSitesLink.exists()).toBe(true);
    expect(manageSitesLink.attributes("href")).toContain(
      "/studies/CADENCE-101"
    );
    expect(manageSitesLink.attributes("href")).toContain("stage=conduct");
    expect(manageSitesLink.attributes("href")).toContain("section=sites");
    expect(manageSitesLink.attributes("href")).toContain("site=SITE-101");
    expect(manageSitesLink.attributes("href")).toContain(
      `node=${PARTICIPANT_NODE_REF}`
    );
    sitesWrapper.unmount();

    const validationWrapper = mount(ValidationSection, {
      props: {
        studyCode: "CADENCE-101",
        canManageRules: true,
        site: "SITE-101",
        participant: PARTICIPANT_NODE_REF,
      },
      global: { plugins: [router, i18n] },
    });
    await flushPromises();

    const findingLinks = validationWrapper.findAll("a.form-link");
    expect(findingLinks).toHaveLength(2);
    expect(findingLinks[0].attributes("href")).toContain("site=SITE-101");
    expect(findingLinks[0].attributes("href")).toContain(
      `participant=${PARTICIPANT_NODE_REF}`
    );
    expect(findingLinks[0].attributes("href")).toContain(
      `form=collection_instance:${FORM_UUID}`
    );
    expect(findingLinks[1].attributes("href")).toContain("site=SITE-101");
    expect(findingLinks[1].attributes("href")).toContain(
      `participant=${PARTICIPANT_NODE_REF}`
    );
    expect(findingLinks[1].attributes("href")).toContain(
      `observation=observation:${OBSERVATION_UUID}`
    );
    validationWrapper.unmount();
  });

  it("redirects demo observation URLs carrying explicit CADENCE-101 and SITE-102 parameters to /studies/CADENCE-101 with site=SITE-102 while keeping unguided DEMO-01 lineage intact", async () => {
    vi.stubEnv("VITE_DEMO_MODE", "true");
    webDemoStore.reset();

    // Unguided DEMO-01 lineage remains intact for stateful MSW consumers
    const demoEnrollmentPath = webDemoStore.getZoomPath(
      "enrollment:00000000-0000-0000-0000-000000001001" // deid-ignore: synthetic demo enrollment ref
    );
    expect(
      demoEnrollmentPath.find(
        (step: { level: string }) => step.level === "study"
      )?.label
    ).toBe("DEMO-01");
    expect(
      demoEnrollmentPath.find(
        (step: { level: string }) => step.level === "study_site"
      )?.label
    ).toBe("SITE-101");
    expect(
      demoEnrollmentPath.find(
        (step: { level: string }) => step.level === "enrollment"
      )?.label
    ).toBe("SUBJ-1001");

    const demoObsRef = "observation:00000000-0000-0000-0000-000000000001"; // deid-ignore: synthetic demo observation ref
    vi.spyOn(kernelApi, "getObservation").mockImplementation(
      async (ref) => webDemoStore.getObservation(ref) as never
    );
    vi.spyOn(kernelApi, "getZoomPath").mockImplementation(
      async (ref) => webDemoStore.getZoomPath(ref) as never
    );
    vi.spyOn(kernelApi, "getNode").mockImplementation(
      async (ref) => webDemoStore.getNode(ref) as never
    );
    vi.spyOn(kernelApi, "listStudies").mockResolvedValue(
      webDemoStore.listStudies() as never
    );
    vi.spyOn(kernelApi, "listStudySites").mockResolvedValue(
      webDemoStore.listStudySites("CADENCE-101") as never
    );

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: "/studies/:studyCode?",
          name: "study",
          component: { template: "<div id='canvas-target'>Canvas</div>" },
        },
        {
          path: "/observations/:observationRef",
          name: "observation",
          component: ObservationView,
          props: true,
        },
      ],
    });
    await router.push(
      `/observations/${demoObsRef}?studyId=CADENCE-101&siteId=SITE-102`
    );
    await router.isReady();

    const wrapper = mount(
      { template: "<router-view />" },
      { global: { plugins: [router, i18n] }, attachTo: document.body }
    );
    await flushPromises();

    expect(router.currentRoute.value.name).toBe("study");
    expect(router.currentRoute.value.params.studyCode).toBe("CADENCE-101");
    expect(router.currentRoute.value.query).toEqual({
      stage: "conduct",
      site: "SITE-102",
      observation: demoObsRef,
    });

    wrapper.unmount();
  });

  it("preserves same-study scope in StudyCanvasView goToPaletteTarget while keeping #zoom-site, #zoom-study, and #zoom-studies intentional zoom-up clearing", async () => {
    vi.stubEnv("VITE_DEMO_MODE", "true");
    webDemoStore.reset();
    const pinia = createPinia();
    setActivePinia(pinia);
    const authStore = useAuthStore();
    signInAs(authStore, ["Sponsor Admin", "CRA", "Data Manager"], {
      username: "lead_operator",
      assignedStudies: ["CADENCE-101", "DEMO-01"],
      assignedSites: ["SITE-101", "SITE-102"],
    });

    const router = buildTestRouter();
    await router.push({
      name: "study",
      params: { studyCode: "CADENCE-101" },
      query: {
        stage: "conduct",
        section: "participants",
        site: "SITE-101",
        node: PARTICIPANT_NODE_REF,
      },
    });
    await router.isReady();

    const wrapper = mount(
      { template: "<router-view />" },
      {
        global: { plugins: [pinia, router, i18n] },
        attachTo: document.body,
      }
    );
    await flushPromises();

    // Open standalone CanvasPalette inside StudyCanvasView
    const paletteBtn = wrapper.find("#btn-open-palette");
    expect(paletteBtn.exists()).toBe(true);
    await paletteBtn.trigger("click");
    await flushPromises();

    let canvasPalette = wrapper.findComponent(CanvasPalette);
    expect(canvasPalette.exists()).toBe(true);

    // Same-study section jump preserves site and node
    canvasPalette.vm.$emit("go", {
      kind: "section",
      stage: "data",
      section: "validation",
    });
    await flushPromises();
    expect(router.currentRoute.value.params.studyCode).toBe("CADENCE-101");
    expect(router.currentRoute.value.query).toMatchObject({
      stage: "data",
      section: "validation",
      site: "SITE-101",
      node: PARTICIPANT_NODE_REF,
    });

    // Re-open palette and jump to Study home — preserves site and node
    await wrapper.find("#btn-open-palette").trigger("click");
    await flushPromises();
    canvasPalette = wrapper.findComponent(CanvasPalette);
    canvasPalette.vm.$emit("go", {
      kind: "study",
      studyCode: "CADENCE-101",
    });
    await flushPromises();
    expect(router.currentRoute.value.params.studyCode).toBe("CADENCE-101");
    expect(router.currentRoute.value.query).toEqual({
      site: "SITE-101",
      node: PARTICIPANT_NODE_REF,
    });

    // Intentional zoom-up links clear deeper scope
    const zoomSite = wrapper.find("#zoom-site");
    if (zoomSite.exists()) {
      await zoomSite.trigger("click");
      await flushPromises();
      expect(router.currentRoute.value.query.site).toBe("SITE-101");
      expect(router.currentRoute.value.query.node).toBeUndefined();
      expect(router.currentRoute.value.query.participant).toBeUndefined();
    }

    const zoomStudy = wrapper.find("#zoom-study");
    expect(zoomStudy.exists()).toBe(true);
    await zoomStudy.trigger("click");
    await flushPromises();
    expect(router.currentRoute.value.params.studyCode).toBe("CADENCE-101");
    expect(router.currentRoute.value.query).toEqual({});

    // Re-apply scope and test cross-study jump clears deeper scope
    await router.push({
      name: "study",
      params: { studyCode: "CADENCE-101" },
      query: {
        stage: "conduct",
        site: "SITE-101",
        node: PARTICIPANT_NODE_REF,
      },
    });
    await flushPromises();

    await wrapper.find("#btn-open-palette").trigger("click");
    await flushPromises();
    canvasPalette = wrapper.findComponent(CanvasPalette);
    canvasPalette.vm.$emit("go", {
      kind: "study",
      studyCode: "DEMO-01",
    });
    await flushPromises();
    expect(router.currentRoute.value.params.studyCode).toBe("DEMO-01");
    expect(router.currentRoute.value.query).toEqual({});

    const zoomStudies = wrapper.find("#zoom-studies");
    expect(zoomStudies.exists()).toBe(true);
    await zoomStudies.trigger("click");
    await flushPromises();
    expect(router.currentRoute.value.params.studyCode || "").toBe("");

    wrapper.unmount();
  });
});
