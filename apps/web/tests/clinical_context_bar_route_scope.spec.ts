import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createMemoryHistory, createRouter } from "vue-router";
import {
  buildScopedWorkflowPath,
  buildStudiesWorkspacePath,
  buildWorkspacePaletteItems,
  readInstantiatedClinicalScope,
  resolveActiveWorkspaceScope,
} from "../src/canvas/palette";
import { i18n } from "../src/i18n/index.js";
import { webDemoStore } from "../src/mocks/demoStore.js";
import { useAuthStore } from "../src/stores/auth";
import { useClinicalStore } from "../src/stores/clinical";
import { useClinicalScopeStore } from "../src/stores/clinicalScope";
import { useConfirmStore } from "../src/stores/confirm";
import { useStudyCanvasStore } from "../src/stores/studyCanvas";
import CtmsView from "../src/views/CtmsView.vue";
import DocumentManagerView from "../src/views/DocumentManagerView.vue";
import EcrfView from "../src/views/EcrfView.vue";
import KnowledgeView from "../src/views/KnowledgeView.vue";
import ObservationView from "../src/views/ObservationView.vue";
import StudyCanvasView from "../src/views/StudyCanvasView.vue";
import StudyTrainingView from "../src/views/StudyTrainingView.vue";
import TicketsView from "../src/views/TicketsView.vue";
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
      "/ecrf?studyId=CADENCE-101&siteId=SITE-101&subjectId=017-0001&enrollmentId=enr-001"
    );
    expect(byKey["workspace:support"]).toBe(
      "/tickets?studyId=CADENCE-101&siteId=SITE-101&subjectId=017-0001"
    );
    expect(byKey["workspace:knowledge"]).toBe(
      "/knowledge?studyId=CADENCE-101&siteId=SITE-101&subjectId=017-0001"
    );

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
});
