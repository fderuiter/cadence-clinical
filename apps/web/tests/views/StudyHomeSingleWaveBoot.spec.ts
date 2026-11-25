import { defineComponent, h } from "vue";
import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter, RouterView } from "vue-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { executionApi } from "../../src/api/executionApi";
import { ecrfQueriesApi } from "../../src/api/ecrfQueriesApi";
import { kernelApi } from "../../src/api/kernelApi";
import { knowledgeService } from "../../src/api/knowledge.js";
import {
  clearKernelStudyCache,
  seedKernelStudyRef,
} from "../../src/canvas/kernelStudy";
import { clearNodeResolutionCache } from "../../src/canvas/nodeResolver";
import { useAuthStore } from "../../src/stores/auth";
import ContextualHelpDrawer from "../../src/components/ContextualHelpDrawer.vue";
import NeedsAttention from "../../src/components/ai/NeedsAttention.vue";
import KernelLineagePanel from "../../src/components/canvas/KernelLineagePanel.vue";
import LocksSection from "../../src/components/canvas/LocksSection.vue";
import StudyCanvasView from "../../src/views/StudyCanvasView.vue";

vi.mock("../../src/api/executionApi", () => ({
  executionApi: {
    listSubjects: vi.fn(),
    getSubjectCounts: vi.fn(),
    getSiteComplianceStatus: vi.fn(),
    activateSite: vi.fn(),
  },
}));

vi.mock("../../src/api/ecrfQueriesApi", () => ({
  ecrfQueriesApi: {
    listSdvAlerts: vi.fn(async () => []),
  },
}));

vi.mock("../../src/api/kernelApi", () => ({
  kernelApi: {
    search: vi.fn(),
    getNode: vi.fn(),
    getNodes: vi.fn(),
    getChildren: vi.fn(),
    trace: vi.fn(),
    dependents: vi.fn(),
    amendmentImpact: vi.fn(),
    listStudySites: vi.fn(),
    getStudyAttention: vi.fn(),
    listProposals: vi.fn(),
    listStudyValidationFindings: vi.fn(),
    listWorkQueues: vi.fn(),
    activity: vi.fn(),
    listProtocolVersions: vi.fn(),
    listStudyLocks: vi.fn(),
    getStudyLockReadiness: vi.fn(),
    listStudyRulePacks: vi.fn(),
    canI: vi.fn(),
    getCommands: vi.fn(),
  },
  formatLockBlockerDetail: vi.fn(() => ""),
  isMasked: (object: { mask?: string | null }) =>
    object.mask !== null && object.mask !== undefined,
}));

const STUDY_CODE = "CADENCE-101";
const STUDY_REF = "study:0192f3c4-5e6a-7b8c-9d0e-000000000101@1";
const SITE_REF = "study_site:0192f3c4-5e6a-7b8c-9d0e-000000000201@1";
const ENROLLMENT_REF = "enrollment:0192f3c4-5e6a-7b8c-9d0e-000000000301@1";
const PROTOCOL_REF = "protocol_version:0192f3c4-5e6a-7b8c-9d0e-000000000401@2";
const PROPOSAL_REF = "proposal:0192f3c4-5e6a-7b8c-9d0e-000000000501@1";
const FINDING_REF = "validation_finding:0192f3c4-5e6a-7b8c-9d0e-000000000601";
const FORM_UUID = "0192f3c4-5e6a-7b8c-9d0e-000000000b01";
const OBSERVATION_UUID = "0192f3c4-5e6a-7b8c-9d0e-000000000c01";

describe("Single-wave Study Home boot & seeded stage transitions", () => {
  let wrapper: ReturnType<typeof mount> | null = null;

  beforeEach(() => {
    localStorage.clear();
    clearNodeResolutionCache();
    clearKernelStudyCache();
    vi.clearAllMocks();
    vi.spyOn(console, "error").mockImplementation(() => {});
  });

  afterEach(() => {
    wrapper?.unmount();
    wrapper = null;
    vi.restoreAllMocks();
  });

  it("boots Study Home in a single wave without /kernel/search, /nodes, or closed /contextual-help calls and reuses seeded study_ref across stages", async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.currentPersona = "data_manager";
    authStore.user = {
      id: "dm-1",
      username: "dm@sponsor.test",
      tenantId: "tenant-cadence",
    } as never;

    const resolveHelpSpy = vi
      .spyOn(knowledgeService, "resolveContextualHelp")
      .mockResolvedValue({ sop: null, related: [] } as never);

    vi.mocked(executionApi.getSubjectCounts).mockResolvedValue({
      total: 12,
      counts: { ENROLLED: 12 },
    } as never);
    vi.mocked(executionApi.listSubjects).mockResolvedValue({
      items: [],
      total: 0,
      limit: 50,
      offset: 0,
    } as never);

    vi.mocked(kernelApi.listStudySites).mockResolvedValue([
      {
        ref: SITE_REF,
        study_site_id: "0192f3c4-5e6a-7b8c-9d0e-000000000201",
        study_site_ref: SITE_REF,
        study_ref: STUDY_REF,
        study_code: STUDY_CODE,
        site_ref: "site:0192f3c4-5e6a-7b8c-9d0e-000000000210",
        site_code: "SITE-01",
        site_name: "Boston General",
        country: "US",
        institution_ref: "organization:0192f3c4-5e6a-7b8c-9d0e-000000000211",
        institution_code: "BGH",
        institution_name: "Boston General Hospital",
        status: "Active",
        is_active: true,
        effective_versions: [],
        mask: null,
      },
    ]);

    vi.mocked(kernelApi.getStudyAttention).mockResolvedValue({
      study_ref: STUDY_REF,
      proposals: [
        {
          ref: PROPOSAL_REF,
          study: STUDY_REF,
          intent: "Review out-of-window visit 2 vital signs",
          plan: null,
          affected: [],
          simulated_impact: null,
          requested_capabilities: ["query.raise"],
          proposed_at: "2026-10-20T09:00:00Z",
          expires_at: "2026-10-27T09:00:00Z",
          proposed_by: "agent:data-reviewer",
          requested_by: "dm@sponsor.test",
          proposed_by_kind: "AutomationAgent",
          model_id: "gemini-2.5-pro",
          confidence_score: 0.91,
          content_hash: "sha256:prop",
          status: "Open",
          decided_at: null,
          decided_by: null,
          approval_attestation: null,
          decision_reason: null,
          is_escalated: false,
          escalated_to: null,
          escalation_reason: null,
          mask: null,
        },
      ],
      findings: [
        {
          ref: FINDING_REF,
          study_ref: STUDY_REF,
          stage: "Collection",
          pack_key: "cadence-edit-checks",
          rule_pack_ref: "library_item:0192f3c4-5e6a-7b8c-9d0e-000000000701@1",
          rule_id: "VS_SYSBP_HIGH",
          rule_version: 1,
          severity: "Error",
          status: "Open",
          blocks: false,
          classification: null,
          decision: null,
          target_kind: "DatasetRecord",
          dataset: "VS",
          record_key: "0192f3c4-5e6a-7b8c-9d0e-000000000801",
          variable: "SYSBP",
          message: "Systolic BP 232 exceeds upper range.",
          expected: "<= 220",
          observed: [{ variable: "SYSBP", value: "232" }],
          first_detected_run_ref:
            "validation_run:0192f3c4-5e6a-7b8c-9d0e-000000000901",
          last_detected_run_ref:
            "validation_run:0192f3c4-5e6a-7b8c-9d0e-000000000901",
          resolved_by_run_ref: null,
        },
      ],
      sdv_alerts: [
        {
          alert_id: "0192f3c4-5e6a-7b8c-9d0e-000000000d01",
          study_id: "0192f3c4-5e6a-7b8c-9d0e-000000000101",
          study_site_id: "0192f3c4-5e6a-7b8c-9d0e-000000000201",
          enrollment_id: "0192f3c4-5e6a-7b8c-9d0e-000000000301",
          form_id: FORM_UUID,
          observation_id: OBSERVATION_UUID,
          study_code: STUDY_CODE,
          site_code: "SITE-01",
          subject_code: "001-0004",
          collection_spec_id: "VS",
          planned_activity_id: "VISIT2",
          item_id: "SYSBP",
          superseded_revision: 1,
          current_revision: 2,
          verified_by: "cra@sponsor.test",
          verified_at: "2026-10-20T08:00:00Z",
          modified_by: "crc@site.test",
          modified_at: "2026-10-20T09:45:00Z",
          reason_for_change: "Corrected source transcription",
          message:
            "Previously verified field modified on Subject 001-0004 - Visit VISIT2 (SYSBP)",
        },
      ],
    });

    vi.mocked(kernelApi.listWorkQueues).mockResolvedValue({
      queues: [],
    });

    vi.mocked(kernelApi.activity).mockResolvedValue({
      entries: [
        {
          event_id: "0192f3c4-5e6a-7b8c-9d0e-000000000a01",
          type: "ParticipantScreened",
          family: "Execution",
          recorded: "2026-10-20T10:00:00Z",
          occurred_valid: null,
          actor_id: "crc@site.test",
          subject_kind: "enrollment",
          subject: ENROLLMENT_REF,
          mask: null,
          correlation_id: "0192f3c4-5e6a-7b8c-9d0e-000000000a02",
        },
        {
          event_id: "0192f3c4-5e6a-7b8c-9d0e-000000000a03",
          type: "ProtocolVersionApproved",
          family: "Knowledge",
          recorded: "2026-10-20T09:30:00Z",
          occurred_valid: null,
          actor_id: "dm@sponsor.test",
          subject_kind: "protocol_version",
          subject: PROTOCOL_REF,
          mask: null,
          correlation_id: "0192f3c4-5e6a-7b8c-9d0e-000000000a04",
        },
      ],
      next: null,
      nodes: {
        [ENROLLMENT_REF]: {
          ref: ENROLLMENT_REF,
          kind: "enrollment",
          code: "001-0004",
          title: "Enrollment 001-0004",
          status: "Screened",
          mask: null,
        },
        [PROTOCOL_REF]: {
          ref: PROTOCOL_REF,
          kind: "protocol_version",
          code: "v2",
          title: "Protocol v2.0",
          status: "Approved",
          mask: null,
        },
      },
    });

    vi.mocked(kernelApi.listProtocolVersions).mockResolvedValue([]);
    vi.mocked(kernelApi.listStudyLocks).mockResolvedValue({
      study_ref: STUDY_REF,
      study_code: STUDY_CODE,
      valid_at: "2026-10-20T10:00:00Z",
      known_at: "2026-10-20T10:00:00Z",
      locks: [],
      hidden: { more_exist: false, reasons: [] },
    });
    vi.mocked(kernelApi.listStudyRulePacks).mockResolvedValue({
      study_ref: STUDY_REF,
      profile_ref: null,
      pinned_rule_pack_refs: [],
      rule_packs: [],
    });
    vi.mocked(kernelApi.getChildren).mockResolvedValue([]);
    vi.mocked(kernelApi.canI).mockResolvedValue({
      capability: "any",
      verb_class: "Execute",
      resource: STUDY_REF,
      permitted: false,
      reason: "Denied",
      explanation: {
        role_bindings: [],
        policies: [],
        entitlements: [],
        abac_rules: [],
        locks: [],
      },
    } as never);

    const ShellHarness = defineComponent({
      name: "ShellHarness",
      setup() {
        return () => h("div", [h(RouterView), h(ContextualHelpDrawer)]);
      },
    });

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: "/studies/:studyCode?",
          name: "study",
          component: StudyCanvasView,
          meta: { layout: "canvas" },
        },
      ],
    });

    await router.push(`/studies/${STUDY_CODE}`);
    await router.isReady();

    wrapper = mount(ShellHarness, {
      global: {
        plugins: [pinia, router],
      },
      attachTo: document.body,
    });
    await flushPromises();

    // Study Home panels issue independent Wave-1 requests by studyCode with zero /kernel/search or /nodes waterfalls
    expect(kernelApi.search).not.toHaveBeenCalled();
    expect(resolveHelpSpy).not.toHaveBeenCalled();
    expect(kernelApi.listStudySites).toHaveBeenCalledWith(STUDY_CODE);
    expect(kernelApi.getStudyAttention).toHaveBeenCalledWith(STUDY_CODE, {
      limit: 20,
    });
    expect(kernelApi.listProposals).not.toHaveBeenCalled();
    expect(kernelApi.listStudyValidationFindings).not.toHaveBeenCalled();
    expect(kernelApi.listWorkQueues).toHaveBeenCalledWith({
      within: STUDY_CODE,
    });
    expect(kernelApi.activity).toHaveBeenCalledWith({
      scope: STUDY_CODE,
      families: undefined,
      before: undefined,
      limit: 25,
      includeNodes: true,
    });
    expect(kernelApi.getNodes).not.toHaveBeenCalled();
    expect(kernelApi.getNode).not.toHaveBeenCalled();
    expect(ecrfQueriesApi.listSdvAlerts).not.toHaveBeenCalled();

    const countTrackedApiCalls = () =>
      [
        ...Object.values(executionApi),
        ...Object.values(ecrfQueriesApi),
        ...Object.values(kernelApi),
        resolveHelpSpy,
      ].reduce(
        (sum, fn) =>
          sum +
          (typeof fn === "function" && "mock" in fn ? fn.mock.calls.length : 0),
        0
      );

    const coldBootCalls = countTrackedApiCalls();
    expect(coldBootCalls).toBeLessThanOrEqual(6);

    // Seeded activity node summaries and Wave-1 attention SDV alerts render immediately
    expect(wrapper.find("#study-activity").text()).toContain(
      "001-0004 · Enrollment 001-0004"
    );
    expect(wrapper.find("#study-activity").text()).toContain(
      "v2 · Protocol v2.0"
    );
    expect(wrapper.find("#needs-attention").text()).toContain(
      "Review out-of-window visit 2 vital signs"
    );
    expect(wrapper.find("#needs-attention").text()).toContain(
      "[VS_SYSBP_HIGH]"
    );
    const sdvAlertItem = wrapper.find(
      '[data-testid="needs-attention-sdv-alert"]'
    );
    expect(sdvAlertItem.exists()).toBe(true);
    expect(sdvAlertItem.text()).toContain("SDV re-verification required");
    expect(sdvAlertItem.text()).toContain(
      "Rev 1 verified by cra@sponsor.test · edited to rev 2 by crc@site.test"
    );
    const sdvLink = wrapper.find("#needs-attention-sdv-alert-0");
    expect(decodeURIComponent(sdvLink.attributes("href") ?? "")).toBe(
      `/studies/${STUDY_CODE}?stage=conduct&site=SITE-01&form=collection_instance:${FORM_UUID}&item=SYSBP&observation=observation:${OBSERVATION_UUID}`
    );
    await expect(wrapper.find("#needs-attention").element).toBeAccessible();

    // Navigating across lifecycle stages reuses the seeded study_ref without calling /kernel/search or /sdv-alerts
    let priorCalls = countTrackedApiCalls();
    await router.push({
      name: "study",
      params: { studyCode: STUDY_CODE },
      query: { stage: "design", section: "protocol-versions" },
    });
    await flushPromises();
    expect(countTrackedApiCalls() - priorCalls).toBeLessThanOrEqual(6);

    priorCalls = countTrackedApiCalls();
    await router.push({
      name: "study",
      params: { studyCode: STUDY_CODE },
      query: { stage: "setup", section: "sites" },
    });
    await flushPromises();
    expect(countTrackedApiCalls() - priorCalls).toBeLessThanOrEqual(6);

    priorCalls = countTrackedApiCalls();
    await router.push({
      name: "study",
      params: { studyCode: STUDY_CODE },
      query: { stage: "conduct", section: "participants" },
    });
    await flushPromises();
    expect(countTrackedApiCalls() - priorCalls).toBeLessThanOrEqual(6);

    priorCalls = countTrackedApiCalls();
    await router.push({
      name: "study",
      params: { studyCode: STUDY_CODE },
      query: { stage: "closeout", section: "locks" },
    });
    await flushPromises();
    expect(countTrackedApiCalls() - priorCalls).toBeLessThanOrEqual(6);

    expect(kernelApi.search).not.toHaveBeenCalled();
    expect(ecrfQueriesApi.listSdvAlerts).not.toHaveBeenCalled();
    expect(resolveHelpSpy).not.toHaveBeenCalled();
  });

  it("caps NeedsAttention findings at 20 and avoids per-node waterfalls on an active study with 25 activity subjects", async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.currentPersona = "data_manager";
    authStore.user = {
      id: "dm-1",
      username: "dm@sponsor.test",
      tenantId: "tenant-cadence",
    } as never;

    vi.mocked(executionApi.getSubjectCounts).mockResolvedValue({
      total: 25,
      counts: { ENROLLED: 25 },
    } as never);
    vi.mocked(kernelApi.listStudySites).mockResolvedValue([
      {
        ref: SITE_REF,
        study_site_id: "0192f3c4-5e6a-7b8c-9d0e-000000000201",
        study_site_ref: SITE_REF,
        study_ref: STUDY_REF,
        study_code: STUDY_CODE,
        site_ref: "site:0192f3c4-5e6a-7b8c-9d0e-000000000210",
        site_code: "SITE-01",
        site_name: "Boston General",
        country: "US",
        institution_ref: "organization:0192f3c4-5e6a-7b8c-9d0e-000000000211",
        institution_code: "BGH",
        institution_name: "Boston General Hospital",
        status: "Active",
        is_active: true,
        effective_versions: [],
        mask: null,
      },
    ]);

    const twoHundredFindings = Array.from({ length: 200 }, (_, i) => {
      const hex = (i + 1).toString(16).padStart(4, "0");
      return {
        ref: `validation_finding:0192f3c4-5e6a-7b8c-9d0e-f1a2b3c4${hex}`,
        study_ref: STUDY_REF,
        stage: "Collection",
        pack_key: "cadence-edit-checks",
        rule_pack_ref: "library_item:0192f3c4-5e6a-7b8c-9d0e-000000000701@1",
        rule_id: `VS_CHECK_${i + 1}`,
        rule_version: 1,
        severity: "Error",
        status: i % 2 === 0 ? "Open" : "Investigating",
        blocks: false,
        classification: null,
        decision: null,
        target_kind: "DatasetRecord",
        dataset: "VS",
        record_key: `0192f3c4-5e6a-7b8c-9d0e-d1e2f3a4${hex}`,
        variable: "SYSBP",
        message: `Systolic BP finding ${i + 1}`,
        expected: "<= 220",
        observed: [{ variable: "SYSBP", value: "232" }],
        first_detected_run_ref:
          "validation_run:0192f3c4-5e6a-7b8c-9d0e-000000000901",
        last_detected_run_ref:
          "validation_run:0192f3c4-5e6a-7b8c-9d0e-000000000901",
        resolved_by_run_ref: null,
      };
    });

    vi.mocked(kernelApi.getStudyAttention).mockImplementation(
      async (_study, opts) => ({
        study_ref: STUDY_REF,
        proposals: [],
        findings: twoHundredFindings.slice(0, opts?.limit ?? 20),
      })
    );
    vi.mocked(kernelApi.listWorkQueues).mockResolvedValue({ queues: [] });

    const activityNodes: Record<
      string,
      {
        ref: string;
        kind: string;
        code: string;
        title: string;
        status: string;
        mask: null;
      }
    > = {};
    const activityEntries = Array.from({ length: 25 }, (_, i) => {
      const hex = (i + 1).toString(16).padStart(4, "0");
      const subjectRef = `enrollment:0192f3c4-5e6a-7b8c-9d0e-e1f2a3b4${hex}@1`;
      const subjCode = `001-${String(i + 1).padStart(4, "0")}`;
      activityNodes[subjectRef] = {
        ref: subjectRef,
        kind: "enrollment",
        code: subjCode,
        title: `Participant ${subjCode}`,
        status: "Enrolled",
        mask: null,
      };
      return {
        event_id: `0192f3c4-5e6a-7b8c-9d0e-b1c2d3e4${hex}`,
        type: "ParticipantScreened",
        family: "Execution",
        recorded: "2026-10-20T10:00:00Z",
        occurred_valid: null,
        actor_id: "crc@site.test",
        subject_kind: "enrollment",
        subject: subjectRef,
        mask: null,
        correlation_id: `0192f3c4-5e6a-7b8c-9d0e-c1d2e3f4${hex}`,
      };
    });

    vi.mocked(kernelApi.activity).mockResolvedValue({
      entries: activityEntries,
      next: null,
      nodes: activityNodes,
    });

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: "/studies/:studyCode?",
          name: "study",
          component: StudyCanvasView,
          meta: { layout: "canvas" },
        },
      ],
    });
    await router.push(`/studies/${STUDY_CODE}`);
    await router.isReady();

    const Harness = defineComponent({
      name: "ActiveStudyHarness",
      setup() {
        return () => h(RouterView);
      },
    });

    wrapper = mount(Harness, {
      global: { plugins: [pinia, router] },
      attachTo: document.body,
    });
    await flushPromises();

    expect(kernelApi.search).not.toHaveBeenCalled();
    expect(kernelApi.getNodes).not.toHaveBeenCalled();
    expect(kernelApi.getNode).not.toHaveBeenCalled();
    expect(kernelApi.getStudyAttention).toHaveBeenCalledWith(STUDY_CODE, {
      limit: 20,
    });
    expect(
      wrapper.findAll("#needs-attention .finding-item, #needs-attention li")
        .length
    ).toBeLessThanOrEqual(20);
    expect(wrapper.find("#needs-attention").text()).toContain("[VS_CHECK_20]");
    expect(wrapper.find("#needs-attention").text()).not.toContain(
      "[VS_CHECK_21]"
    );
    expect(wrapper.find("#study-activity").text()).toContain(
      "001-0025 · Participant 001-0025"
    );
  });

  it("coalesces 12 unseeded lock scope chips in LocksSection.vue into 1 batch GET /api/v1/kernel/nodes request", async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.currentPersona = "data_manager";
    authStore.user = {
      id: "dm-1",
      username: "dm@sponsor.test",
      tenantId: "tenant-cadence",
    } as never;

    seedKernelStudyRef(STUDY_CODE, STUDY_REF);

    const lockEntries = Array.from({ length: 12 }, (_, i) => {
      const hex = (i + 1).toString(16).padStart(4, "0");
      const scopeRef = `enrollment:0192f3c4-5e6a-7b8c-9d0e-a1b2c3d4${hex}`;
      return {
        ref: `assertion:0192f3c4-5e6a-7b8c-9d0e-91a2b3c4${hex}@1`,
        study_ref: STUDY_REF,
        kind: "Frozen" as const,
        scope_kind: "enrollment",
        scope_ref: scopeRef,
        study_site_ref: SITE_REF,
        enrollment_ref: scopeRef,
        subject_code: null,
        masked: false,
        mask: null,
        attestation_ref: `attestation:0192f3c4-5e6a-7b8c-9d0e-81a2b3c4${hex}`,
        attestation_meaning: "Approved",
        locked_at: "2026-10-20T11:00:00Z",
        locked_by: "dm@sponsor.test",
        reason_for_change: "Interim data review freeze",
        released: false,
        release_ref: null,
        release_attestation_ref: null,
        released_at: null,
        released_by: null,
        release_reason: null,
      };
    });

    vi.mocked(kernelApi.canI).mockResolvedValue({
      capability: "lock.apply",
      verb_class: "Kernel",
      resource: STUDY_REF,
      permitted: false,
      reason: "Denied",
      explanation: {
        subject: STUDY_REF,
        claim: "Denied",
        producer: "test",
        steps: [],
        conditions: [],
        next_actions: [],
      },
    } as never);
    vi.mocked(kernelApi.getChildren).mockResolvedValue([]);
    vi.mocked(kernelApi.listStudyLocks).mockResolvedValue({
      study_ref: STUDY_REF,
      study_code: STUDY_CODE,
      valid_at: "2026-10-20T11:00:00Z",
      known_at: "2026-10-20T11:00:00Z",
      locks: lockEntries,
      hidden: { more_exist: false, reasons: [] },
    });
    vi.mocked(kernelApi.getNodes).mockImplementation(async (refs) => ({
      nodes: Object.fromEntries(
        refs.map((ref, idx) => [
          ref,
          {
            ref: `${ref}@1`,
            kind: "enrollment",
            code: `002-${String(idx + 1).padStart(4, "0")}`,
            title: `Participant 002-${String(idx + 1).padStart(4, "0")}`,
            status: "Enrolled",
            mask: null,
          },
        ])
      ),
    }));

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: "/studies/:studyCode?",
          name: "study",
          component: { template: "<div />" },
        },
      ],
    });
    await router.push(`/studies/${STUDY_CODE}`);
    await router.isReady();

    wrapper = mount(LocksSection, {
      props: { studyCode: STUDY_CODE },
      global: { plugins: [pinia, router] },
    });
    await flushPromises();

    expect(kernelApi.getNode).not.toHaveBeenCalled();
    expect(kernelApi.getNodes).toHaveBeenCalledTimes(1);
    expect(vi.mocked(kernelApi.getNodes).mock.calls[0]?.[0]).toHaveLength(12);
    expect(wrapper.text()).toContain("002-0012 · Participant 002-0012");
  });

  it("coalesces 20 unseeded lineage nodes in KernelLineagePanel.vue into 1 batch GET /api/v1/kernel/nodes request", async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.currentPersona = "data_manager";
    authStore.user = {
      id: "dm-1",
      username: "dm@sponsor.test",
      tenantId: "tenant-cadence",
    } as never;

    const lineageNodes = Array.from({ length: 20 }, (_, i) => {
      const hex = (i + 1).toString(16).padStart(4, "0");
      const nodeRef = `protocol_element:0192f3c4-5e6a-7b8c-9d0e-71a2b3c4${hex}@1`;
      return {
        handle: nodeRef,
        kind: "protocol_element",
        ref: nodeRef,
        mask: null,
        distance: i === 0 ? 0 : 1,
      };
    });
    const lineageEdges = lineageNodes.slice(1).map((node) => ({
      from: lineageNodes[0]!.handle,
      to: node.handle,
      kind: "DerivedFrom",
      activity: null,
      recorded_at: "2026-10-20T10:00:00Z",
      valid_from: null,
    }));

    vi.mocked(kernelApi.trace).mockResolvedValue({
      start: lineageNodes[0]!.handle,
      direction: "Backward",
      depth: 5,
      valid_at: "2026-10-20T10:00:00Z",
      known_at: "2026-10-20T10:00:00Z",
      nodes: lineageNodes,
      edges: lineageEdges,
      truncated: false,
    });
    vi.mocked(kernelApi.getNodes).mockImplementation(async (refs) => ({
      nodes: Object.fromEntries(
        refs.map((ref, idx) => [
          ref,
          {
            ref,
            kind: "protocol_element",
            code: `EL-${idx + 1}`,
            title: `Protocol Element ${idx + 1}`,
            status: "Active",
            mask: null,
          },
        ])
      ),
    }));

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: "/studies/:studyCode?",
          name: "study",
          component: { template: "<div />" },
        },
      ],
    });
    await router.push(`/studies/${STUDY_CODE}`);
    await router.isReady();

    wrapper = mount(KernelLineagePanel, {
      props: {
        nodeRef: lineageNodes[0]!.handle,
        mode: "trace",
      },
      global: { plugins: [pinia, router] },
    });
    await flushPromises();

    expect(kernelApi.getNode).not.toHaveBeenCalled();
    expect(kernelApi.getNodes).toHaveBeenCalledTimes(1);
    expect(vi.mocked(kernelApi.getNodes).mock.calls[0]?.[0]).toHaveLength(20);
    expect(wrapper.text()).toContain("EL-20 · Protocol Element 20");
  });

  it("coalesces Open and Investigating findings into 1 listStudyValidationFindings request in NeedsAttention fallback", async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.currentPersona = "data_manager";
    authStore.user = {
      id: "dm-1",
      username: "dm@sponsor.test",
      tenantId: "tenant-cadence",
    } as never;

    const originalGetStudyAttention = kernelApi.getStudyAttention;
    (
      kernelApi as { getStudyAttention?: typeof kernelApi.getStudyAttention }
    ).getStudyAttention = undefined;
    try {
      vi.mocked(kernelApi.listProposals).mockResolvedValue([]);
      vi.mocked(kernelApi.listStudyValidationFindings).mockResolvedValue({
        total: 2,
        findings: [
          {
            ref: FINDING_REF,
            study_ref: STUDY_REF,
            stage: "Collection",
            pack_key: "cadence-edit-checks",
            rule_pack_ref:
              "library_item:0192f3c4-5e6a-7b8c-9d0e-000000000701@1",
            rule_id: "VS_SYSBP_HIGH",
            rule_version: 1,
            severity: "Error",
            status: "Open",
            blocks: false,
            classification: null,
            decision: null,
            target_kind: "DatasetRecord",
            dataset: "VS",
            record_key: FORM_UUID,
            variable: "SYSBP",
            message: "Systolic BP 232 exceeds upper range.",
            expected: "<= 220",
            observed: [{ variable: "SYSBP", value: "232" }],
            first_detected_run_ref:
              "validation_run:0192f3c4-5e6a-7b8c-9d0e-000000000901",
            last_detected_run_ref:
              "validation_run:0192f3c4-5e6a-7b8c-9d0e-000000000901",
            resolved_by_run_ref: null,
          },
          {
            ref: "validation_finding:0192f3c4-5e6a-7b8c-9d0e-000000000602",
            study_ref: STUDY_REF,
            stage: "Standardization",
            pack_key: "sdtm-core",
            rule_pack_ref:
              "library_item:0192f3c4-5e6a-7b8c-9d0e-000000000702@1",
            rule_id: "SD1002",
            rule_version: 1,
            severity: "Warning",
            status: "Investigating",
            blocks: false,
            classification: "MappingIssue",
            decision: {
              status: "Investigating",
              decided_at: "2026-10-20T09:15:00Z",
              decided_by: "dm@sponsor.test",
              text: null,
              decided_on_run_ref: null,
              attestation_ref: null,
            },
            target_kind: "DatasetRecord",
            dataset: "DM",
            record_key: "row-4",
            variable: "AGE",
            message: "Derived AGE requires mapping review.",
            expected: ">= 18",
            observed: [{ variable: "AGE", value: "17" }],
            first_detected_run_ref:
              "validation_run:0192f3c4-5e6a-7b8c-9d0e-000000000901",
            last_detected_run_ref:
              "validation_run:0192f3c4-5e6a-7b8c-9d0e-000000000901",
            resolved_by_run_ref: null,
          },
        ],
      });

      const router = createRouter({
        history: createMemoryHistory(),
        routes: [
          {
            path: "/studies/:studyCode?",
            name: "study",
            component: { template: "<div />" },
          },
        ],
      });
      await router.push(`/studies/${STUDY_CODE}`);
      await router.isReady();

      wrapper = mount(NeedsAttention, {
        props: { studyCode: STUDY_CODE },
        global: { plugins: [pinia, router] },
      });
      await flushPromises();

      expect(kernelApi.listStudyValidationFindings).toHaveBeenCalledTimes(1);
      expect(kernelApi.listStudyValidationFindings).toHaveBeenCalledWith(
        STUDY_CODE,
        {
          status: ["Open", "Investigating"],
          limit: 20,
        }
      );
      expect(wrapper.text()).toContain("Systolic BP 232 exceeds upper range.");
      expect(wrapper.text()).toContain("Derived AGE requires mapping review.");
    } finally {
      kernelApi.getStudyAttention = originalGetStudyAttention;
    }
  });
});
