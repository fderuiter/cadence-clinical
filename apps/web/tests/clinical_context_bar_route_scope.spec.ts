import { createRequire } from "node:module";
import path from "node:path";
import { fileURLToPath } from "node:url";
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
import OnboardingTour from "../src/components/OnboardingTour.vue";
import AssistPanel from "../src/components/ai/AssistPanel.vue";
import NeedsAttention from "../src/components/ai/NeedsAttention.vue";
import ProposalReview from "../src/components/ai/ProposalReview.vue";
import CanvasPalette from "../src/components/canvas/CanvasPalette.vue";
import ProtocolImportPanel from "../src/components/canvas/ProtocolImportPanel.vue";
import StudyPulse from "../src/components/canvas/StudyPulse.vue";
import StudySitesPanel from "../src/components/canvas/StudySitesPanel.vue";
import ValidationSection from "../src/components/canvas/ValidationSection.vue";
import WorkQueuesPanel from "../src/components/canvas/WorkQueuesPanel.vue";
import CrcFormRenderer from "../src/components/persona/CrcFormRenderer.vue";
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
import UnavailableView from "../src/views/UnavailableView.vue";
import * as availabilityModule from "../src/router/availability";
import { ecrfQueriesApi } from "../src/api/ecrfQueriesApi";
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

let activeTestPinia: ReturnType<typeof createPinia>;

describe("Shared ClinicalContextBar & Canonical Route Scope Codec", () => {
  beforeEach(() => {
    vi.stubEnv("VITE_DEMO_MODE", "false");
    activeTestPinia = createPinia();
    setActivePinia(activeTestPinia);
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

  it("enforces custom-vue/no-unscoped-navigation AST rule against raw study objects, buildStudyScopedPath calls, and bare workflow paths", async () => {
    const repoRoot = path.resolve(
      path.dirname(fileURLToPath(import.meta.url)),
      "../../.."
    );
    const rootRequire = createRequire(path.join(repoRoot, "package.json"));
    const { ESLint } = rootRequire("eslint") as {
      ESLint: new (options: { cwd: string; overrideConfigFile: string }) => {
        lintText(
          code: string,
          options: { filePath: string }
        ): Promise<
          Array<{
            messages: Array<{
              ruleId: string | null;
              messageId?: string;
              severity: number;
            }>;
          }>
        >;
      };
    };
    const eslint = new ESLint({
      cwd: repoRoot,
      overrideConfigFile: path.join(repoRoot, "eslint.config.mjs"),
    });

    const invalidVueSnippet = `<template>
  <div>
    <router-link :to="{ name: 'study', params: { studyCode: 'CADENCE-101' }, query: { stage: 'data' } }">Validation</router-link>
    <router-link to="/training">Complete training</router-link>
  </div>
</template>
<script setup lang="ts">
import { useRouter } from "vue-router";
import { buildStudyScopedPath } from "../canvas/palette";
const router = useRouter();
const menuTarget = buildStudyScopedPath("/etmf", "CADENCE-101");
function goBare(entry: { defaultPath: string }) {
  void router.push("/studies");
  return entry.defaultPath;
}
void menuTarget;
void goBare;
</script>
`;
    const [invalidRes] = await eslint.lintText(invalidVueSnippet, {
      filePath: path.join(
        repoRoot,
        "apps/web/src/components/__SyntheticUnscopedNav.vue"
      ),
    });
    const navErrors = invalidRes.messages.filter(
      (m) =>
        m.ruleId === "custom-vue/no-unscoped-navigation" && m.severity === 2
    );
    expect(navErrors.map((m) => m.messageId)).toEqual(
      expect.arrayContaining([
        "unscopedStudyObject",
        "noBuildStudyScopedPath",
        "bareWorkflowPath",
      ])
    );
    expect(navErrors).toHaveLength(5);

    const validVueSnippet = `<template>
  <div>
    <router-link :to="validationLink">Validation</router-link>
    <router-link :to="trainingLink">Training</router-link>
  </div>
</template>
<script setup lang="ts">
import { computed } from "vue";
import { useScopedNavigation } from "../composables/useScopedNavigation";
const { scopedStudyLocation, scopedWorkflowPath } = useScopedNavigation(() => "CADENCE-101");
const validationLink = computed(() => scopedStudyLocation({ stage: "data", section: "validation" }));
const trainingLink = computed(() => scopedWorkflowPath("/training"));
</script>
`;
    const [validRes] = await eslint.lintText(validVueSnippet, {
      filePath: path.join(
        repoRoot,
        "apps/web/src/components/__SyntheticScopedNav.vue"
      ),
    });
    const validNavErrors = validRes.messages.filter(
      (m) => m.ruleId === "custom-vue/no-unscoped-navigation"
    );
    expect(validNavErrors).toHaveLength(0);
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
      `/studies/CADENCE-101?stage=start&section=startup-greenlight&site=SITE-101&participant=${encodeURIComponent(PARTICIPANT_NODE_REF)}`
    );
    expect(byKey["workspace:trial-documents"]).toBe(
      `/studies/CADENCE-101?stage=start&section=documents&site=SITE-101&participant=${encodeURIComponent(PARTICIPANT_NODE_REF)}`
    );
    expect(byKey["workspace:training"]).toBe(
      `/studies/CADENCE-101?stage=start&section=training&site=SITE-101&participant=${encodeURIComponent(PARTICIPANT_NODE_REF)}`
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
      expect(demoByKey["workspace:site-readiness"]).toBe(
        "/ctms?studyId=CADENCE-101&siteId=SITE-101&subjectId=017-0001"
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
        ref: "study_site:0192f3c4-5e6a-7b8c-9d0e-000000000201",
        study_site_id: "0192f3c4-5e6a-7b8c-9d0e-000000000201",
        study_site_ref: "study_site:0192f3c4-5e6a-7b8c-9d0e-000000000201",
        study_ref: "study:0192f3c4-5e6a-7b8c-9d0e-000000000101",
        study_code: "CADENCE-101",
        site_ref: "site:0192f3c4-5e6a-7b8c-9d0e-000000000201",
        site_code: "SITE-101",
        site_name: "Boston General",
        country: "US",
        institution_ref: "organization:0192f3c4-5e6a-7b8c-9d0e-000000000301",
        institution_code: "INST-101",
        institution_name: "Boston General Hospital",
        status: "ACTIVE",
        is_active: true,
        effective_versions: [],
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
      active_protocol_version: "1.0",
      enrollment_index: 1,
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
      active_protocol_version: "1.0",
      enrollment_index: 1,
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
    const ctmsWrapper = mount(CtmsView, {
      global: { plugins: [router, i18n] },
    });
    await flushPromises();
    scopeStore.activeSiteId = "SITE-101";
    scopeStore.activeSubjectId = "017-0001";
    await flushPromises();
    expect(router.currentRoute.value.query).toMatchObject({
      studyId: "CADENCE-101",
      siteId: "SITE-101",
      subjectId: "017-0001",
    });
    ctmsWrapper.unmount();

    // 2. /ssu
    await router.push("/ssu?studyId=CADENCE-101&siteId=SITE-101");
    await router.isReady();
    const ssuWrapper = mount(CtmsView, { global: { plugins: [router, i18n] } });
    await flushPromises();
    scopeStore.activeSiteId = "SITE-102";
    await flushPromises();
    expect(router.currentRoute.value.query.siteId).toBe("SITE-102");
    ssuWrapper.unmount();

    // 3. /training
    await router.push("/training?studyId=CADENCE-101");
    await router.isReady();
    const trainingWrapper = mount(StudyTrainingView, {
      global: { plugins: [router, i18n] },
    });
    await flushPromises();
    await trainingWrapper.find("#training-site-filter").setValue("SITE-101");
    await trainingWrapper.find("#training-site-filter").trigger("change");
    await flushPromises();
    expect(router.currentRoute.value.query).toMatchObject({
      studyId: "CADENCE-101",
      siteId: "SITE-101",
    });
    trainingWrapper.unmount();

    // 4. /tickets
    await router.push("/tickets?studyId=CADENCE-101");
    await router.isReady();
    const ticketsWrapper = mount(TicketsView, {
      global: { plugins: [router, i18n] },
    });
    await flushPromises();
    await ticketsWrapper.find("#tickets-site-filter").setValue("SITE-101");
    await ticketsWrapper.find("#tickets-subject-filter").setValue("017-0001");
    await flushPromises();
    expect(router.currentRoute.value.query).toMatchObject({
      studyId: "CADENCE-101",
      siteId: "SITE-101",
      subjectId: "017-0001",
    });
    ticketsWrapper.unmount();

    // 5. /knowledge
    await router.push("/knowledge?studyId=CADENCE-101");
    await router.isReady();
    const knowledgeWrapper = mount(KnowledgeView, {
      global: { plugins: [router, i18n] },
    });
    await flushPromises();
    await knowledgeWrapper.find("#knowledge-site-filter").setValue("SITE-101");
    await knowledgeWrapper
      .find("#knowledge-subject-filter")
      .setValue("017-0001");
    await flushPromises();
    expect(router.currentRoute.value.query).toMatchObject({
      studyId: "CADENCE-101",
      siteId: "SITE-101",
      subjectId: "017-0001",
    });
    knowledgeWrapper.unmount();

    // 6. /etmf
    await router.push("/etmf?studyId=CADENCE-101");
    await router.isReady();
    const etmfWrapper = mount(DocumentManagerView, {
      global: { plugins: [router, i18n] },
    });
    await flushPromises();
    await etmfWrapper.find("#etmf-site-select").setValue("SITE-101");
    await etmfWrapper.find("#etmf-site-select").trigger("change");
    await flushPromises();
    expect(router.currentRoute.value.query).toMatchObject({
      studyId: "CADENCE-101",
      siteId: "SITE-101",
    });
    etmfWrapper.unmount();

    // 7. /ecrf (verify router.replace syncs scope WITHOUT firing false unsaved draft dialog when formId stays open)
    const confirmStore = useConfirmStore();
    const confirmOpenSpy = vi.spyOn(confirmStore, "open");
    useClinicalStore();
    await router.push(
      `/ecrf?studyId=CADENCE-101&siteId=SITE-101&formId=${FORM_UUID}`
    );
    await router.isReady();
    const ecrfWrapper = mount(
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
    const ecrfViewVm = ecrfWrapper.findComponent(EcrfView);
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

    ecrfWrapper.unmount();
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

  it("includes study, site, and participant scope on Trial documents, Study training, and Orientation workspace palette items across live and demo builds", () => {
    vi.spyOn(availabilityModule, "isScreenAvailable").mockReturnValue(true);
    const liveItems = buildWorkspacePaletteItems({
      isAuthenticated: true,
      normalizedRoles: ["sponsor_admin", "cra", "data_manager"],
      activeStudyCode: "CADENCE-101",
      activeSiteId: "SITE-102",
      activeSubjectCode: "SUBJ-102-001",
    });

    const liveByKey = Object.fromEntries(
      liveItems.map((item) => [item.key, item.target])
    );
    expect(liveByKey["workspace:trial-documents"]?.routePath).toBe(
      "/studies/CADENCE-101?stage=start&section=documents&site=SITE-102&participant=SUBJ-102-001"
    );
    expect(liveByKey["workspace:training"]?.routePath).toBe(
      "/studies/CADENCE-101?stage=start&section=training&site=SITE-102&participant=SUBJ-102-001"
    );
    expect(liveByKey["workspace:orientation"]).toEqual({
      action: { kind: "open-checklist" },
    });

    vi.stubEnv("VITE_DEMO_MODE", "true");
    const demoItems = buildWorkspacePaletteItems({
      isAuthenticated: true,
      normalizedRoles: ["sponsor_admin", "cra", "data_manager"],
      activeStudyCode: "CADENCE-101",
      activeSiteId: "SITE-102",
      activeSubjectCode: "SUBJ-102-001",
    });

    const demoByKey = Object.fromEntries(
      demoItems.map((item) => [item.key, item.target.routePath])
    );
    expect(demoByKey["workspace:trial-documents"]).toBe(
      "/etmf?studyId=CADENCE-101&siteId=SITE-102&subjectId=SUBJ-102-001"
    );
    expect(demoByKey["workspace:training"]).toBe(
      "/training?studyId=CADENCE-101&siteId=SITE-102&subjectId=SUBJ-102-001"
    );
    expect(demoByKey["workspace:orientation"]).toBe(
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
        demoMode === "true"
          ? "/ctms?studyId=CADENCE-101&siteId=SITE-101&subjectId=SUBJ-101-001"
          : `/studies/CADENCE-101?stage=start&section=startup-greenlight&site=SITE-101&participant=${encodeURIComponent(PARTICIPANT_NODE_REF)}`
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
      if (demoMode === "true") {
        expect(orientationBtn).toBeTruthy();
        orientationBtn?.click();
        await flushPromises();
        expect(pushSpy).toHaveBeenLastCalledWith(
          "/overview?studyId=CADENCE-101&siteId=SITE-101&subjectId=SUBJ-101-001"
        );
      } else {
        expect(orientationBtn).toBeNull();
        const taskButtons = drawerWrapper.findAll(".btn-navigate-task");
        expect(taskButtons.length).toBeGreaterThan(0);
        await taskButtons[0].trigger("click");
        await flushPromises();
        const drawerPushed = String(pushSpy.mock.calls.at(-1)?.[0] || "");
        expect(drawerPushed).toContain("CADENCE-101");
        expect(drawerPushed).toContain("SITE-101");
        expect(drawerPushed).toContain("SUBJ-101-001");
      }
      drawerWrapper.unmount();

      const overviewWrapper = mount(OverviewView, {
        global: { plugins: [pinia, router, i18n] },
      });
      await flushPromises();

      const taskLaunchButtons = overviewWrapper.findAll(".btn-task-launch");
      expect(taskLaunchButtons.length).toBeGreaterThan(0);
      await taskLaunchButtons[0].trigger("click");
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
      study_ref: "study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d80", // deid-ignore: synthetic test study ref
      profile_ref: null,
      rule_packs: [],
      pinned_rule_pack_refs: [],
    });
    vi.spyOn(kernelApi, "listStudyValidationFindings").mockResolvedValue({
      total: 2,
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

  it("preserves CADENCE-101, SITE-102, and SUBJ-102-001 across StudyPulse, selectVersion, #record-not-loaded, NeedsAttention (proposals, findings, SDV alerts), WorkQueuesPanel, AssistPanel, ProtocolImportPanel, and ProposalReview", async () => {
    clearKernelStudyCache();
    const subj102Uuid = "7b536802-4122-8225-b5f7-875c4392fcdc"; // deid-ignore: canonical demo fixture participant UUID
    const subj102EnrollmentUuid = "e1d4e034-a7ac-856a-ae47-674b0c2f5ae0"; // deid-ignore: canonical demo fixture enrollment UUID
    const subj102EnrollmentRef = `enrollment:${subj102EnrollmentUuid}`;
    const studyRef = "study:535717ef-3976-8a05-9b8f-16701fe0febe"; // deid-ignore: canonical demo fixture study ref
    const versionRef = "protocol_version:0192f3c4-5e6a-7b8c-9d0e-00000000b101"; // deid-ignore: synthetic test protocol version ref
    const proposalRef = "proposal:0192f3c4-5e6a-7b8c-9d0e-00000000c201"; // deid-ignore: synthetic test proposal ref
    const findingRef =
      "validation_finding:0192f3c4-5e6a-7b8c-9d0e-00000000d301"; // deid-ignore: synthetic test finding ref

    const mockOpenProposals = [
      {
        ref: proposalRef,
        study: studyRef,
        intent: "Reconcile SITE-102 blood pressure window",
        status: "Open",
        proposed_by_kind: "AutomationAgent",
        proposed_by: "agent:clinical-copilot",
        model_id: "cadence-copilot-v1",
        proposed_at: "2026-09-02T09:00:00Z", // deid-ignore
        expires_at: "2026-09-09T09:00:00Z", // deid-ignore
        content_hash: "sha256:1111",
        plan: null,
        affected: [
          { ref: subj102EnrollmentRef, mask: null },
          { ref: versionRef, mask: null },
        ],
        requested_capabilities: ["observation.correct"],
        mask: null,
      },
    ];
    const mockOpenFindings = [
      {
        ref: findingRef,
        study_ref: studyRef,
        rule_id: "VS-102",
        rule_key: "VS-102",
        severity: "Warning",
        status: "Open",
        message: "Systolic BP out of window",
        target_kind: "Observation",
        record_key: OBSERVATION_UUID,
        variable: "SYSBP",
        detected_at: "2026-09-02T09:05:00Z", // deid-ignore
        decision: null,
      },
    ];

    vi.spyOn(kernelApi, "listStudies").mockResolvedValue([
      {
        ref: studyRef,
        code: "CADENCE-101",
        title: "CADENCE-101",
        status: "ACTIVE",
        mask: null,
      } as never,
    ]);
    vi.spyOn(kernelApi, "search").mockResolvedValue([
      {
        ref: `${studyRef}@1`,
        kind: "study",
        code: "CADENCE-101",
        title: "CADENCE-101",
        exact_code: true,
      } as never,
    ]);
    vi.spyOn(kernelApi, "listStudySites").mockResolvedValue([
      {
        ref: "study_site:d6079442-fc0b-82d7-affa-bf94e06f960a", // deid-ignore: canonical demo fixture study-site ref
        study_site_id: "d6079442-fc0b-82d7-affa-bf94e06f960a", // deid-ignore
        study_site_ref: "study_site:d6079442-fc0b-82d7-affa-bf94e06f960a", // deid-ignore
        study_ref: studyRef,
        study_code: "CADENCE-101",
        site_ref: "site:4be976d7-2880-892b-85e5-5d990257cd51", // deid-ignore
        site_code: "SITE-102",
        site_name: "Lakeshore Medical Center",
        country: "US",
        institution_ref: "organization:8fdf5c3d-eda1-89a2-9b49-a44a51daf128", // deid-ignore
        institution_code: "INST-SITE-102",
        institution_name: "Lakeshore Medical Center",
        status: "ACTIVE",
        is_active: true,
        effective_versions: [],
      } as never,
    ]);
    vi.spyOn(kernelApi, "listProtocolVersions").mockResolvedValue([
      {
        ref: versionRef,
        version: "2.0",
        status: "Effective",
        recorded_at: "2026-09-01T10:00:00Z", // deid-ignore
        mask: null,
      } as never,
    ]);
    vi.spyOn(kernelApi, "getStudyAttention").mockResolvedValue({
      study_ref: studyRef,
      proposals: mockOpenProposals,
      findings: mockOpenFindings,
    } as never);
    vi.spyOn(kernelApi, "listProposals").mockResolvedValue(
      mockOpenProposals as never
    );
    vi.spyOn(ecrfQueriesApi, "listSdvAlerts").mockResolvedValue([
      {
        alert_id: "0192f3c4-5e6a-7b8c-9d0e-00000000a901", // deid-ignore
        form_id: FORM_UUID,
        item_id: "SYSBP",
        observation_id: OBSERVATION_UUID,
        superseded_revision: 1,
        current_revision: 2,
        verified_by: "cra_lead",
        modified_by: "crc_lead",
        modified_at: "2026-09-02T09:10:00Z", // deid-ignore
        message: "Systolic BP modified after SDV",
      } as never,
    ]);
    vi.spyOn(kernelApi, "getProposal").mockResolvedValue({
      ref: proposalRef,
      intent: "Reconcile SITE-102 blood pressure window",
      status: "Open",
      proposed_by_kind: "Person",
      proposed_by: "person:0192f3c4-5e6a-7b8c-9d0e-00000000e401", // deid-ignore
      proposed_at: "2026-09-02T09:00:00Z", // deid-ignore
      expires_at: "2026-09-09T09:00:00Z", // deid-ignore
      content_hash: "sha256:1111",
      plan: null,
      affected: [
        { ref: versionRef, mask: null },
        { ref: `observation:${OBSERVATION_UUID}`, mask: null },
        { ref: studyRef, mask: null },
      ],
      requested_capabilities: ["observation.correct"],
      mask: null,
    } as never);
    vi.spyOn(kernelApi, "getNode").mockImplementation(async (ref) => {
      if (String(ref).startsWith("protocol_version:")) {
        return {
          ref: versionRef,
          kind: "protocol_version",
          id: "0192f3c4-5e6a-7b8c-9d0e-00000000b101", // deid-ignore
          code: "v2.0",
          title: "Protocol v2.0",
          status: "Effective",
          mask: null,
        } as never;
      }
      if (String(ref).startsWith("study:")) {
        return {
          ref: studyRef,
          kind: "study",
          id: "535717ef-3976-8a05-9b8f-16701fe0febe", // deid-ignore
          code: "CADENCE-101",
          title: "CADENCE-101",
          status: "ACTIVE",
          mask: null,
        } as never;
      }
      return {
        ref: `observation:${OBSERVATION_UUID}`,
        kind: "observation",
        id: OBSERVATION_UUID,
        code: "SYSBP",
        title: "Systolic BP",
        status: "Recorded",
        mask: null,
      } as never;
    });
    vi.spyOn(kernelApi, "listStudyValidationFindings").mockResolvedValue({
      total: 1,
      findings: mockOpenFindings as never,
    });
    vi.spyOn(kernelApi, "listWorkQueues").mockResolvedValue({
      study_ref: studyRef,
      queues: [
        {
          code: "proposals",
          title: "Proposals",
          capability: "proposal.decide",
          truncated: false,
          items: [
            {
              subject: proposalRef,
              subject_kind: "proposal",
              title: "Decide SITE-102 proposal",
              since: "2026-09-02T09:00:00Z", // deid-ignore
              masked: null,
              target: { proposal_ref: proposalRef },
            },
            {
              subject: versionRef,
              subject_kind: "protocol_version",
              title: "Inspect Protocol v2.0",
              since: "2026-09-01T10:00:00Z", // deid-ignore
              masked: null,
              target: { protocol_version_ref: versionRef },
            },
            {
              subject: findingRef,
              subject_kind: "validation_finding",
              title: "Review VS-102 finding",
              since: "2026-09-02T09:05:00Z", // deid-ignore
              masked: null,
              target: { finding_ref: findingRef },
            },
          ],
        },
      ],
    } as never);
    vi.spyOn(kernelApi, "activity").mockResolvedValue({
      entries: [],
      next: null,
    } as never);
    vi.spyOn(kernelApi, "listProtocolTemplates").mockResolvedValue([
      {
        id: "tpl-vs",
        category: "Vital Signs",
        form_oid: "VS",
        form_name: "Vital Signs",
        file_name: "vs.xml",
      } as never,
    ]);
    vi.spyOn(kernelApi, "importProtocolDraft").mockResolvedValue({
      version: {
        ref: versionRef,
        number: "2.1-draft",
        status: "Draft",
      },
      form_count: 1,
      item_count: 4,
      imported_rule_pack: {
        ref: "rule_pack:0192f3c4-5e6a-7b8c-9d0e-00000000f501", // deid-ignore
        key: "CADENCE-101-ODM",
        version: 1,
        rule_count: 2,
        status: "Draft",
      },
      import_notes: [],
    } as never);
    vi.spyOn(kernelApi, "canI").mockResolvedValue({
      permitted: true,
      explanation: { summary: "Permitted", reasons: [] },
    } as never);

    const canvasStore = useStudyCanvasStore();
    vi.spyOn(canvasStore, "loadParticipants").mockResolvedValue(true);
    vi.spyOn(canvasStore, "loadPulse").mockResolvedValue(true);
    vi.spyOn(canvasStore, "loadScheduleOptions").mockResolvedValue(null);
    vi.spyOn(canvasStore, "ensureParticipantById").mockImplementation(
      async (id: string) => {
        canvasStore.failedParticipantIds[id] = true;
        return null;
      }
    );
    canvasStore.studyCode = "CADENCE-101";
    canvasStore.pulse = {
      total: 3,
      byState: [
        { code: "ENROLLED", label: "Enrolled", count: 1 },
        { code: "SCREENED", label: "Screened", count: 2 },
      ],
    };
    canvasStore.rememberParticipants([
      {
        id: subj102Uuid,
        subject_id: "SUBJ-102-001",
        study_id: "CADENCE-101",
        site_id: "SITE-102",
        status: "ENROLLED",
        active_protocol_version: "2.0",
        enrollment_index: 2,
        enrollment_ref: subj102EnrollmentRef,
      },
    ]);

    const router = buildTestRouter();
    await router.push({
      name: "study",
      params: { studyCode: "CADENCE-101" },
      query: {
        site: "SITE-102",
        node: subj102EnrollmentRef,
      },
    });
    await router.isReady();

    // 1. StudyCanvasView: StudyPulse drillToParticipants, NeedsAttention, WorkQueuesPanel
    const canvasWrapper = mount(StudyCanvasView, {
      global: { plugins: [activeTestPinia, router, i18n] },
    });
    await flushPromises();

    const attentionProposalLink = canvasWrapper.find(
      "#needs-attention-proposal-0"
    );
    expect(attentionProposalLink.exists()).toBe(true);
    expect(attentionProposalLink.attributes("href")).toContain("site=SITE-102");
    expect(attentionProposalLink.attributes("href")).toContain(
      `node=${subj102EnrollmentRef}`
    );
    expect(attentionProposalLink.attributes("href")).toContain(
      `proposal=${proposalRef}`
    );

    const attentionFindingLink = canvasWrapper.find(
      "#needs-attention-finding-0"
    );
    expect(attentionFindingLink.exists()).toBe(true);
    expect(attentionFindingLink.attributes("href")).toContain("site=SITE-102");
    expect(attentionFindingLink.attributes("href")).toContain(
      `node=${subj102EnrollmentRef}`
    );

    const attentionSdvLink = canvasWrapper.find("#needs-attention-sdv-alert-0");
    expect(attentionSdvLink.exists()).toBe(true);
    expect(attentionSdvLink.attributes("href")).toContain("site=SITE-102");
    expect(attentionSdvLink.attributes("href")).toContain(
      `node=${subj102EnrollmentRef}`
    );

    const workItemProposalLink = canvasWrapper.find(
      '[data-testid="work-item-link-proposals-0"]'
    );
    expect(workItemProposalLink.exists()).toBe(true);
    expect(workItemProposalLink.attributes("href")).toContain("site=SITE-102");
    expect(workItemProposalLink.attributes("href")).toContain(
      `node=${subj102EnrollmentRef}`
    );

    const workItemVersionLink = canvasWrapper.find(
      '[data-testid="work-item-link-proposals-1"]'
    );
    expect(workItemVersionLink.exists()).toBe(true);
    expect(workItemVersionLink.attributes("href")).toContain("site=SITE-102");
    expect(workItemVersionLink.attributes("href")).toContain(
      `participant=${subj102EnrollmentRef}`
    );
    expect(workItemVersionLink.attributes("href")).toContain(
      `node=${versionRef}`
    );

    // Drill via StudyPulse retains SITE-102 and SUBJ-102-001 node
    const pulseComp = canvasWrapper.findComponent(StudyPulse);
    expect(pulseComp.exists()).toBe(true);
    pulseComp.vm.$emit("drill", "ENROLLED");
    await flushPromises();
    expect(router.currentRoute.value.query).toMatchObject({
      stage: "conduct",
      section: "participants",
      status: "ENROLLED",
      site: "SITE-102",
      node: subj102EnrollmentRef,
    });

    // Navigate to protocol-versions and trigger selectVersion: promotes participant node & retains SITE-102
    await router.push({
      name: "study",
      params: { studyCode: "CADENCE-101" },
      query: {
        stage: "design",
        section: "protocol-versions",
        site: "SITE-102",
        node: subj102EnrollmentRef,
      },
    });
    await flushPromises();
    const versionRowBtn = canvasWrapper.find(
      `[data-version-ref="${versionRef}"], .version-row button, .versions-list button`
    );
    if (versionRowBtn.exists()) {
      await versionRowBtn.trigger("click");
    } else {
      const pvSection = canvasWrapper.findComponent({
        name: "ProtocolVersionsSection",
      });
      pvSection.vm.$emit("select", { ref: versionRef, version: "2.0" });
    }
    await flushPromises();
    expect(router.currentRoute.value.query).toMatchObject({
      stage: "design",
      section: "protocol-versions",
      site: "SITE-102",
      participant: subj102EnrollmentRef,
      node: versionRef,
    });

    // Missing participant record (#record-not-loaded) preserves SITE-102 and participant
    const unknownParticipantRef =
      "participant:0192f3c4-5e6a-7b8c-9d0e-999999999999"; // deid-ignore
    await router.push({
      name: "study",
      params: { studyCode: "CADENCE-101" },
      query: {
        stage: "conduct",
        site: "SITE-102",
        participant: unknownParticipantRef,
      },
    });
    await flushPromises();
    const notLoadedLink = canvasWrapper.find("#record-not-loaded a");
    expect(notLoadedLink.exists()).toBe(true);
    expect(notLoadedLink.attributes("href")).toContain("site=SITE-102");
    expect(notLoadedLink.attributes("href")).toContain(
      `participant=${unknownParticipantRef}`
    );
    await notLoadedLink.trigger("click");
    await flushPromises();
    expect(router.currentRoute.value.query).toMatchObject({
      stage: "conduct",
      section: "participants",
      site: "SITE-102",
      participant: unknownParticipantRef,
    });
    canvasWrapper.unmount();

    // 2. AssistPanel & ProtocolImportPanel preserve active site, participant, stage, and section
    await router.push({
      name: "study",
      params: { studyCode: "CADENCE-101" },
      query: {
        stage: "conduct",
        section: "participants",
        site: "SITE-102",
        node: subj102EnrollmentRef,
      },
    });
    await flushPromises();

    const assistWrapper = mount(AssistPanel, {
      props: { studyCode: "CADENCE-101", nodeRef: subj102EnrollmentRef },
      global: { plugins: [activeTestPinia, router, i18n] },
    });
    await flushPromises();
    const assistProposalLink = assistWrapper.find("#ai-assist-proposal-0");
    expect(assistProposalLink.exists()).toBe(true);
    expect(assistProposalLink.attributes("href")).toContain("stage=conduct");
    expect(assistProposalLink.attributes("href")).toContain(
      "section=participants"
    );
    expect(assistProposalLink.attributes("href")).toContain("site=SITE-102");
    expect(assistProposalLink.attributes("href")).toContain(
      `node=${subj102EnrollmentRef}`
    );
    expect(assistProposalLink.attributes("href")).toContain(
      `proposal=${proposalRef}`
    );
    assistWrapper.unmount();

    const importWrapper = mount(ProtocolImportPanel, {
      props: {
        studyCode: "CADENCE-101",
        studyRef,
        idPrefix: "test-import",
      },
      global: { plugins: [activeTestPinia, router, i18n] },
    });
    await flushPromises();
    const tplCheckbox = importWrapper.find('input[type="checkbox"]');
    if (tplCheckbox.exists()) {
      await tplCheckbox.setValue(true);
    }
    const reasonInput = importWrapper.find("#test-import-reason");
    if (reasonInput.exists()) {
      await reasonInput.setValue("Import vital signs template");
    }
    const importForm = importWrapper.find("form");
    if (importForm.exists()) {
      await importForm.trigger("submit");
      await flushPromises();
    }
    const openValidationLink = importWrapper.find(
      "#test-import-open-validation"
    );
    if (openValidationLink.exists()) {
      expect(openValidationLink.attributes("href")).toContain("stage=data");
      expect(openValidationLink.attributes("href")).toContain(
        "section=validation"
      );
      expect(openValidationLink.attributes("href")).toContain("site=SITE-102");
      expect(openValidationLink.attributes("href")).toContain(
        `node=${subj102EnrollmentRef}`
      );
    }
    importWrapper.unmount();

    // 3. ProposalReview #proposal-review-back and source object links remove proposal while keeping SITE-102 & participant
    await router.push({
      name: "study",
      params: { studyCode: "CADENCE-101" },
      query: {
        stage: "conduct",
        section: "participants",
        site: "SITE-102",
        node: subj102EnrollmentRef,
        proposal: proposalRef,
      },
    });
    await flushPromises();

    const reviewWrapper = mount(ProposalReview, {
      props: {
        studyCode: "CADENCE-101",
        proposalRef,
        reviewer: "clinical_lead",
      },
      global: { plugins: [activeTestPinia, router, i18n] },
    });
    await flushPromises();

    const backLink = reviewWrapper.find("#proposal-review-back");
    expect(backLink.exists()).toBe(true);
    const backHref = backLink.attributes("href") || "";
    expect(backHref).not.toContain("proposal=");
    expect(backHref).toContain("stage=conduct");
    expect(backHref).toContain("section=participants");
    expect(backHref).toContain("site=SITE-102");
    expect(backHref).toContain(`node=${subj102EnrollmentRef}`);

    // Open protocol_version source and verify source link promotes participant & strips proposal
    const openFirstSource = reviewWrapper.find("#proposal-source-open-0");
    expect(openFirstSource.exists()).toBe(true);
    await openFirstSource.trigger("click");
    await flushPromises();
    const sourceLinks = reviewWrapper.findAll(".source-detail a");
    expect(sourceLinks.length).toBeGreaterThan(0);
    const versionSourceHref = sourceLinks[0].attributes("href") || "";
    expect(versionSourceHref).not.toContain("proposal=");
    expect(versionSourceHref).toContain("site=SITE-102");
    expect(versionSourceHref).toContain(`participant=${subj102EnrollmentRef}`);
    expect(versionSourceHref).toContain(`node=${versionRef}`);

    await backLink.trigger("click");
    await flushPromises();
    expect(router.currentRoute.value.query).toEqual({
      stage: "conduct",
      section: "participants",
      site: "SITE-102",
      node: subj102EnrollmentRef,
    });
    reviewWrapper.unmount();

    // Ensure NeedsAttention mounted standalone also preserves active scope
    const standaloneAttention = mount(NeedsAttention, {
      props: { studyCode: "CADENCE-101" },
      global: { plugins: [activeTestPinia, router, i18n] },
    });
    await flushPromises();
    expect(
      standaloneAttention.find("#needs-attention-proposal-0").attributes("href")
    ).toContain("site=SITE-102");
    standaloneAttention.unmount();

    const standaloneQueues = mount(WorkQueuesPanel, {
      props: { studyCode: "CADENCE-101" },
      global: { plugins: [activeTestPinia, router, i18n] },
    });
    await flushPromises();
    expect(
      standaloneQueues
        .find('[data-testid="work-item-link-proposals-0"]')
        .attributes("href")
    ).toContain("site=SITE-102");
    standaloneQueues.unmount();
  });

  it("maps studyCode, site, and node to studyId=CADENCE-101, siteId=SITE-102, and subjectId=SUBJ-102-001 across OnboardingTour steps without overwriting SUBJ-102-001 with SUBJ-101-001", async () => {
    vi.stubEnv("VITE_DEMO_MODE", "true");
    vi.spyOn(availabilityModule, "isScreenAvailable").mockReturnValue(true);

    const pinia = createPinia();
    setActivePinia(pinia);
    const authStore = useAuthStore();
    signInAs(
      authStore,
      ["Sponsor Admin", "CRA", "Data Manager", "Site Investigator", "Auditor"],
      {
        username: "monitor_lead",
        assignedStudies: ["CADENCE-101"],
        assignedSites: ["SITE-101", "SITE-102"],
      }
    );
    authStore.currentPersona = "super_admin";

    // Initialize clinicalScope with default demo values (SITE-101 / SUBJ-101-001) to prove it never overwrites SUBJ-102-001
    const scopeStore = useClinicalScopeStore();
    expect(scopeStore.activeSiteId).toBe("SITE-101");
    expect(scopeStore.activeSubjectId).toBe("SUBJ-101-001");

    const subj102EnrollmentRef =
      "enrollment:e1d4e034-a7ac-856a-ae47-674b0c2f5ae0"; // deid-ignore: canonical demo fixture enrollment ref
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: "/studies/:studyCode?",
          name: "study",
          component: { template: "<div id='nav-studies'>Study</div>" },
        },
        {
          path: "/ecrf",
          name: "ecrf",
          component: { template: "<div id='nav-data-collection'>eCRF</div>" },
        },
        {
          path: "/ctms",
          name: "ctms",
          component: { template: "<div id='nav-site-readiness'>CTMS</div>" },
        },
        {
          path: "/overview",
          name: "overview",
          component: {
            template:
              "<div id='btn-onboarding-checklist-trigger'>Overview</div>",
          },
        },
      ],
    });

    await router.push({
      name: "study",
      params: { studyCode: "CADENCE-101" },
      query: {
        stage: "conduct",
        section: "participants",
        site: "SITE-102",
        node: subj102EnrollmentRef,
      },
    });
    await router.isReady();

    const onboardingStore = useOnboardingStore();
    onboardingStore.loadPersonaState("super_admin");
    onboardingStore.isActive = true;
    onboardingStore.disabled = false;
    onboardingStore.currentStep = 2; // study step

    const tourWrapper = mount(
      {
        components: { OnboardingTour },
        template: "<div><router-view /><OnboardingTour /></div>",
      },
      {
        global: { plugins: [pinia, router, i18n] },
        attachTo: document.body,
      }
    );
    await flushPromises();

    // Advance to step 3 -> /ecrf
    onboardingStore.nextStep();
    await flushPromises();
    expect(router.currentRoute.value.name).toBe("ecrf");
    expect(router.currentRoute.value.query).toEqual({
      studyId: "CADENCE-101",
      siteId: "SITE-102",
      subjectId: "SUBJ-102-001",
    });
    expect(router.currentRoute.value.query.node).toBeUndefined();
    expect(router.currentRoute.value.query.stage).toBeUndefined();
    expect(router.currentRoute.value.query.section).toBeUndefined();

    // Advance to step 4 -> /ctms
    onboardingStore.nextStep();
    await flushPromises();
    expect(router.currentRoute.value.name).toBe("ctms");
    expect(router.currentRoute.value.query).toEqual({
      studyId: "CADENCE-101",
      siteId: "SITE-102",
      subjectId: "SUBJ-102-001",
    });

    // Switch tour persona to auditor step 3 (study) and advance to step 4 -> /overview
    authStore.currentPersona = "auditor";
    await flushPromises();
    onboardingStore.loadPersonaState("auditor");
    onboardingStore.disabled = false;
    onboardingStore.isActive = true;
    onboardingStore.currentStep = 3;
    await flushPromises();
    onboardingStore.nextStep(); // step 4 -> overview
    await flushPromises();
    expect(router.currentRoute.value.name).toBe("overview");
    expect(router.currentRoute.value.query).toEqual({
      studyId: "CADENCE-101",
      siteId: "SITE-102",
      subjectId: "SUBJ-102-001",
    });

    // Advance to auditor step 5 -> back to /studies/CADENCE-101 with site=SITE-102 & participant=SUBJ-102-001
    onboardingStore.nextStep();
    await flushPromises();
    expect(router.currentRoute.value.name).toBe("study");
    expect(router.currentRoute.value.params.studyCode).toBe("CADENCE-101");
    expect(router.currentRoute.value.query).toEqual({
      site: "SITE-102",
      participant: "SUBJ-102-001",
    });

    tourWrapper.unmount();
  });

  it("retains studyId=CADENCE-101, siteId=SITE-102, and subjectId=SUBJ-102-001 across Preview palette items, #demo-launch-observation in OverviewView, onboardingSummary.js, and ObservationView", async () => {
    vi.stubEnv("VITE_DEMO_MODE", "true");
    webDemoStore.reset();
    vi.spyOn(availabilityModule, "isScreenAvailable").mockReturnValue(true);

    const pinia = createPinia();
    setActivePinia(pinia);
    const authStore = useAuthStore();
    signInAs(authStore, ["Sponsor Admin", "CRA", "Data Manager", "CRC"], {
      username: "monitor_lead",
      assignedStudies: ["CADENCE-101"],
      assignedSites: ["SITE-101", "SITE-102"],
    });

    // 1. palette.ts Preview items carry studyId, siteId, and subjectId
    const paletteItems = buildWorkspacePaletteItems({
      isAuthenticated: true,
      normalizedRoles: authStore.normalizedRoles,
      activeStudyCode: "CADENCE-101",
      activeSiteId: "SITE-102",
      activeSubjectCode: "SUBJ-102-001",
    });
    const previewItems = paletteItems.filter(
      (item) => item.group === "Preview"
    );
    expect(previewItems.length).toBeGreaterThan(0);
    for (const previewItem of previewItems) {
      expect(previewItem.target.routePath).toContain(
        "studyId=CADENCE-101&siteId=SITE-102&subjectId=SUBJ-102-001"
      );
    }
    const obsPreview = previewItems.find(
      (item) => item.key === "preview:observation"
    );
    expect(obsPreview?.target.routePath).toBe(
      "/observations/observation:00000000-0000-0000-0000-000000000001?studyId=CADENCE-101&siteId=SITE-102&subjectId=SUBJ-102-001" // deid-ignore
    );

    // 2. onboardingSummary.js resolveAuthorizedTaskRoute for preview & observation tasks
    expect(
      resolveAuthorizedTaskRoute(
        { routeName: "observation" },
        authStore.normalizedRoles,
        {
          studyCode: "CADENCE-101",
          siteId: "SITE-102",
          subjectCode: "SUBJ-102-001",
        }
      )
    ).toBe(
      "/observations/observation:00000000-0000-0000-0000-000000000001?studyId=CADENCE-101&siteId=SITE-102&subjectId=SUBJ-102-001" // deid-ignore
    );

    // 3. OverviewView #demo-launch-observation into ObservationView preserves SITE-102 and SUBJ-102-001 without resetting to SITE-101
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
          path: "/overview",
          name: "overview",
          component: OverviewView,
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
      "/overview?studyId=CADENCE-101&siteId=SITE-102&subjectId=SUBJ-102-001"
    );
    await router.isReady();

    const appWrapper = mount(
      { template: "<router-view />" },
      { global: { plugins: [pinia, router, i18n] }, attachTo: document.body }
    );
    await flushPromises();

    const launchObsBtn = appWrapper.find("#demo-launch-observation");
    expect(launchObsBtn.exists()).toBe(true);
    await launchObsBtn.trigger("click");
    await flushPromises();

    expect(router.currentRoute.value.name).toBe("study");
    expect(router.currentRoute.value.params.studyCode).toBe("CADENCE-101");
    expect(router.currentRoute.value.query).toEqual({
      stage: "conduct",
      site: "SITE-102",
      participant: "SUBJ-102-001",
      observation: "observation:00000000-0000-0000-0000-000000000001", // deid-ignore
    });

    appWrapper.unmount();
  });

  it("preserves active study, site, and participant scope on CrcFormRenderer #btn-complete-training, unavailable route redirects, and UnavailableView #btn-unavailable-study", async () => {
    vi.spyOn(trainingApi, "listCurricula").mockResolvedValue([]);
    vi.spyOn(trainingApi, "listAssignments").mockResolvedValue([]);
    vi.spyOn(trainingApi, "listDelegations").mockResolvedValue([]);

    const pinia = createPinia();
    setActivePinia(pinia);
    const authStore = useAuthStore();
    signInAs(authStore, ["Sponsor Admin", "Site Investigator", "CRC"], {
      username: "crc_lead",
      assignedStudies: ["CADENCE-101"],
      assignedSites: ["SITE-102"],
    });

    const clinicalStore = useClinicalStore();
    clinicalStore.activeStudyId = "CADENCE-101";
    clinicalStore.activeSiteId = "SITE-102";
    clinicalStore.activeSubjectId = "SUBJ-102-001";

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: "/studies/:studyCode?",
          name: "study",
          component: { template: "<div>Study</div>" },
        },
        {
          path: "/ecrf",
          name: "ecrf",
          component: { template: "<div>eCRF</div>" },
        },
        {
          path: "/training",
          name: "training",
          component: StudyTrainingView,
        },
        {
          path: "/lakehouse",
          name: "lakehouse",
          component: { template: "<div>Lakehouse</div>" },
        },
        {
          path: "/unavailable",
          name: "unavailable",
          component: UnavailableView,
        },
      ],
    });

    const appRouterModule = await import("../src/router/index.js");
    expect(appRouterModule.router).toBeTruthy();

    await router.push(
      "/ecrf?studyId=CADENCE-101&siteId=SITE-102&subjectId=SUBJ-102-001"
    );
    await router.isReady();

    const crcWrapper = mount(CrcFormRenderer, {
      props: {
        store: clinicalStore,
        selectedSubjectId: "SUBJ-102-001",
        selectedVisitId: "Screening",
        isReconsentGated: false,
        isTrainingGated: true,
        showEconsentModal: false,
        showPaperIcfModal: false,
        reconsentSubmitting: false,
        econsentSignerName: "",
        paperIcfDate: "",
        paperIcfNote: "",
        lookupStatuses: {},
        getValidationError: () => null,
        isCraUser: false,
        sdvStates: {},
        getSdvKey: () => "",
      },
      global: { plugins: [pinia, router, i18n] },
    });
    await flushPromises();

    const completeTrainingBtn = crcWrapper.find("#btn-complete-training");
    expect(completeTrainingBtn.exists()).toBe(true);
    expect(completeTrainingBtn.attributes("data-href")).toBe(
      "/training?studyId=CADENCE-101&siteId=SITE-102&subjectId=SUBJ-102-001"
    );
    await completeTrainingBtn.trigger("click");
    await flushPromises();
    expect(router.currentRoute.value.path).toBe("/training");
    expect(router.currentRoute.value.query).toMatchObject({
      studyId: "CADENCE-101",
      siteId: "SITE-102",
      subjectId: "SUBJ-102-001",
    });
    crcWrapper.unmount();

    // Verify real router/index.js guard preserves studyId, siteId, and subjectId when redirecting to /unavailable
    vi.spyOn(availabilityModule, "isScreenAvailable").mockImplementation(
      (screen) => screen !== "lakehouse"
    );
    const realRouter = appRouterModule.router;
    await realRouter.push(
      "/lakehouse?studyId=CADENCE-101&siteId=SITE-102&subjectId=SUBJ-102-001"
    );
    await flushPromises();
    expect(realRouter.currentRoute.value.name).toBe("unavailable");
    expect(realRouter.currentRoute.value.query).toEqual({
      screen: "lakehouse",
      studyId: "CADENCE-101",
      siteId: "SITE-102",
      subjectId: "SUBJ-102-001",
    });

    // Verify UnavailableView #btn-unavailable-study routes via buildStudiesWorkspacePath with active scope
    await router.push({
      name: "unavailable",
      query: {
        screen: "lakehouse",
        studyId: "CADENCE-101",
        siteId: "SITE-102",
        subjectId: "SUBJ-102-001",
      },
    });
    await flushPromises();

    const unavailableWrapper = mount(UnavailableView, {
      global: { plugins: [pinia, router, i18n] },
    });
    await flushPromises();

    const openStudyBtn = unavailableWrapper.find("#btn-unavailable-study");
    expect(openStudyBtn.exists()).toBe(true);
    await openStudyBtn.trigger("click");
    await flushPromises();
    expect(router.currentRoute.value.name).toBe("study");
    expect(router.currentRoute.value.params.studyCode).toBe("CADENCE-101");
    expect(router.currentRoute.value.query).toEqual({
      site: "SITE-102",
      participant: "SUBJ-102-001",
    });
    unavailableWrapper.unmount();
  });
});
