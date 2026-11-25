import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { nextTick, reactive } from "vue";
import { mount, flushPromises, type VueWrapper } from "@vue/test-utils";
import { createPinia, setActivePinia, type Pinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import ParticipantsSection from "../src/components/canvas/ParticipantsSection.vue";
import QueriesSection from "../src/components/canvas/QueriesSection.vue";
import ProposalReview from "../src/components/ai/ProposalReview.vue";
import ProtocolImportPanel from "../src/components/canvas/ProtocolImportPanel.vue";
import ValidationSection from "../src/components/canvas/ValidationSection.vue";
import DesignerSchemaPanel from "../src/components/persona/DesignerSchemaPanel.vue";
import EcrfFormPanel from "../src/components/ecrf/EcrfFormPanel.vue";
import FormQueryList from "../src/components/ecrf/FormQueryList.vue";
import ParticipantWalletView from "../src/views/ParticipantWalletView.vue";
import TicketsView from "../src/views/TicketsView.vue";
import KnowledgeView from "../src/views/KnowledgeView.vue";
import StudyCanvasView from "../src/views/StudyCanvasView.vue";
import CtmsMilestonesSection from "../src/components/clinical/CtmsMilestonesSection.vue";
import CtmsDelegationSection from "../src/components/clinical/CtmsDelegationSection.vue";
import CtmsMonitoringSection from "../src/components/clinical/CtmsMonitoringSection.vue";
import CtmsGreenlightSection from "../src/components/clinical/CtmsGreenlightSection.vue";
import SiteActivationPanel from "../src/components/clinical/SiteActivationPanel.vue";
import RtsmSupplySection from "../src/components/clinical/RtsmSupplySection.vue";
import ParticipantStateChange from "../src/components/canvas/ParticipantStateChange.vue";
import ProtocolTrainingConsole from "../src/components/clinical/ProtocolTrainingConsole.vue";
import SsuGreenlightConsole from "../src/components/clinical/SsuGreenlightConsole.vue";
import TicketDetailDrawer from "../src/components/tickets/TicketDetailDrawer.vue";
import OnboardingTour from "../src/components/OnboardingTour.vue";
import { useStudyCanvasStore } from "../src/stores/studyCanvas";
import { useAuthStore } from "../src/stores/auth";
import { useOnboardingStore } from "../src/stores/onboarding";
import { ctmsApi } from "../src/api/ctmsApi";
import { kernelApi } from "../src/api/kernelApi";
import { kernelGovernanceApi } from "../src/api/kernelGovernanceApi";
import { ecrfFormsApi } from "../src/api/ecrfFormsApi";
import { ecrfQueriesApi } from "../src/api/ecrfQueriesApi";
import { executionApi } from "../src/api/executionApi";
import { ticketsApi } from "../src/api/ticketsApi";
import { trainingApi } from "../src/api/training";

vi.mock("../src/auth/keycloak.js", () => ({
  getKeycloakClient: () => null,
}));

vi.mock("../src/api/signatureVerification", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("../src/api/signatureVerification")>();
  return {
    ...actual,
    requestStepUpSignature: vi.fn(async () => ({
      sig_token: "sig-tok-focus-test",
    })),
  };
});

vi.mock("../src/api/ticketsApi", () => ({
  ticketsApi: {
    listTickets: vi.fn(async () => {
      throw new Error("Offline demo fallback");
    }),
  },
}));

vi.mock("../src/api/knowledge", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../src/api/knowledge")>();
  return {
    ...actual,
    knowledgeService: {
      ...actual.knowledgeService,
      listCategories: vi.fn(async () => [...actual.DEFAULT_CATEGORIES]),
      listArticles: vi.fn(async () => [...actual.DEFAULT_MOCK_ARTICLES]),
      listArticleVersions: vi.fn(async () => []),
      getArticleAuditLog: vi.fn(async () => []),
    },
  };
});

vi.mock("../src/api/kernelApi", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../src/api/kernelApi")>();
  return {
    ...actual,
    kernelApi: {
      ...actual.kernelApi,
      listProposals: vi.fn(async () => []),
      getProposal: vi.fn(),
      rejectProposal: vi.fn(),
      search: vi.fn(async () => []),
      getChildren: vi.fn(async () => []),
      listProtocolVersions: vi.fn(async () => []),
      listProtocolTemplates: vi.fn(async () => []),
      importProtocolDraft: vi.fn(),
      listStudyRulePacks: vi.fn(async () => ({
        rule_packs: [],
        pinned_rule_pack_refs: [],
      })),
      listStudyValidationFindings: vi.fn(async () => ({
        findings: [],
        total: 0,
      })),
      getNode: vi.fn(async () => {
        throw new Error("Not found");
      }),
      getZoomPath: vi.fn(async () => []),
      verifyObservation: vi.fn(),
      retractAssertion: vi.fn(),
      canI: vi.fn(async () => ({ allowed: true })),
    },
  };
});

vi.mock("../src/api/kernelGovernanceApi", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("../src/api/kernelGovernanceApi")>();
  return {
    ...actual,
    kernelGovernanceApi: {
      ...actual.kernelGovernanceApi,
      listStudySites: vi.fn(async () => []),
      linkStudySite: vi.fn(),
      activateStudySite: vi.fn(),
    },
  };
});

vi.mock("../src/api/ctmsApi", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../src/api/ctmsApi")>();
  return {
    ...actual,
    ctmsApi: {
      ...actual.ctmsApi,
      listSiteMilestones: vi.fn(async () => []),
      updateSiteMilestone: vi.fn(),
      getSiteDoaLog: vi.fn(),
      signOffDelegation: vi.fn(),
      revokeSiteTasks: vi.fn(),
      listMonitoringVisits: vi.fn(async () => []),
      listMonitoringVisitLetters: vi.fn(async () => []),
      completeMonitoringVisit: vi.fn(),
      compileVisitLetter: vi.fn(),
      signOffMonitoringVisit: vi.fn(),
      evaluateSiteGreenlight: vi.fn(),
      getCountryGreenlightMatrix: vi.fn(async () => []),
      getCountryMatrix: vi.fn(async () => []),
      certifySiteGreenlight: vi.fn(),
      batchCertifyCountrySites: vi.fn(),
    },
  };
});

vi.mock("../src/api/ecrfFormsApi", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("../src/api/ecrfFormsApi")>();
  return {
    ...actual,
    ecrfFormsApi: {
      ...actual.ecrfFormsApi,
      listForEnrollment: vi.fn(async () => []),
      openForm: vi.fn(),
      getForm: vi.fn(),
      saveValues: vi.fn(),
      signForm: vi.fn(),
    },
  };
});

vi.mock("../src/api/ecrfQueriesApi", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("../src/api/ecrfQueriesApi")>();
  return {
    ...actual,
    ecrfQueriesApi: {
      ...actual.ecrfQueriesApi,
      raise: vi.fn(),
      listForForm: vi.fn(async () => []),
      listForEnrollment: vi.fn(async () => []),
      listForStudy: vi.fn(async () => []),
      transition: vi.fn(),
    },
  };
});

vi.mock("../src/api/executionApi", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("../src/api/executionApi")>();
  return {
    ...actual,
    executionApi: {
      ...actual.executionApi,
      listSubjects: vi.fn(async () => ({
        items: [],
        total: 0,
        limit: 50,
        offset: 0,
      })),
      getSubjectDetail: vi.fn(),
      getSiteComplianceStatus: vi.fn(),
      activateSite: vi.fn(),
      listCgtCertificates: vi.fn(async () => []),
      listCgtReconciliations: vi.fn(async () => []),
      getSiteIpKitInventory: vi.fn(async () => []),
      listRandomizationSchemes: vi.fn(async () => []),
      draftRandomizationScheme: vi.fn(),
      approveRandomizationScheme: vi.fn(),
      changeSubjectState: vi.fn(),
      randomizeSubject: vi.fn(),
    },
  };
});

async function settleFocus() {
  await flushPromises();
  await nextTick();
  await flushPromises();
  await nextTick();
}

function makeTestRouter(initialPath = "/studies/CAD-101") {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: "/studies/:studyCode?",
        name: "study",
        component: StudyCanvasView,
        meta: { layout: "canvas" },
      },
      {
        path: "/observations/:observationRef",
        name: "observation",
        component: { template: "<div />" },
      },
      {
        path: "/studies/:studyCode/proposals/:proposalRef",
        name: "proposal",
        component: { template: "<div />" },
      },
      {
        path: "/ecrf",
        name: "ecrf",
        component: { template: "<div />" },
      },
    ],
  });
  void router.push(initialPath);
  return router;
}

describe("Web Inline Focus Handoffs & Keyboard Scrollable Cards (ADR-2593)", () => {
  let pinia: Pinia;
  let wrappers: VueWrapper<unknown>[] = [];
  const originalFetch = global.fetch;

  function track<T>(wrapper: VueWrapper<T>): VueWrapper<T> {
    wrappers.push(wrapper as VueWrapper<unknown>);
    return wrapper;
  }

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    vi.stubEnv("VITE_DEMO_MODE", "true");
    document.body.innerHTML = "";
  });

  afterEach(() => {
    for (const w of wrappers) {
      w.unmount();
    }
    wrappers = [];
    global.fetch = originalFetch;
    vi.unstubAllEnvs();
    vi.clearAllMocks();
    document.body.innerHTML = "";
  });

  it("ParticipantsSection + AddParticipantForm preserve focus across site-chip clear and inline form toggle", async () => {
    const canvas = useStudyCanvasStore();
    canvas.participants = [
      {
        id: "0192f3c4-5e6a-7b8c-9d0e-000000000001",
        subject_id: "017-0001",
        study_id: "CAD-101",
        site_id: "017",
        status: "ACTIVE",
        active_protocol_version: "1.0",
        enrollment_index: 1,
      },
    ];

    const wrapper = track(
      mount(ParticipantsSection, {
        attachTo: document.body,
        global: { plugins: [pinia] },
        props: {
          status: "",
          siteId: "017",
          selectedId: "",
          canManageParticipants: true,
        },
      })
    );
    await settleFocus();

    const clearChipBtn = wrapper.find("#btn-clear-site-filter");
    expect(clearChipBtn.exists()).toBe(true);
    (clearChipBtn.element as HTMLElement).focus();
    await clearChipBtn.trigger("click");
    await wrapper.setProps({ siteId: "" });
    await settleFocus();

    expect(document.activeElement?.id).toBe("participants-site");
    expect(document.activeElement).not.toBe(document.body);

    const openAddBtn = wrapper.find("#btn-add-participant");
    (openAddBtn.element as HTMLElement).focus();
    await openAddBtn.trigger("click");
    await settleFocus();

    expect(document.activeElement?.id).toBe("add-participant-code");

    const cancelAddBtn = wrapper.find("#btn-cancel-add-participant");
    await cancelAddBtn.trigger("click");
    await settleFocus();

    expect(document.activeElement?.id).toBe("btn-add-participant");
  });

  it("QueriesSection restores focus to #study-queries-status when #btn-clear-query-filters unmounts", async () => {
    vi.mocked(kernelApi.search).mockResolvedValue([
      {
        exact_code: true,
        ref: "study:0192f3c4-5e6a-7b8c-9d0e-000000000101",
        kind: "study",
        code: "CAD-101",
      },
    ] as never);
    const router = makeTestRouter(
      "/studies/CAD-101?section=queries&qstatus=open"
    );
    await router.isReady();

    const wrapper = track(
      mount(QueriesSection, {
        attachTo: document.body,
        global: { plugins: [pinia, router] },
        props: {
          studyCode: "CAD-101",
          canManage: true,
          canAnswer: true,
          status: "open",
        },
      })
    );
    await settleFocus();

    const clearBtn = wrapper.find("#btn-clear-query-filters");
    expect(clearBtn.exists()).toBe(true);
    (clearBtn.element as HTMLElement).focus();
    await clearBtn.trigger("click");
    await wrapper.setProps({ status: "" });
    await settleFocus();

    expect(document.activeElement?.id).toBe("study-queries-status");
    expect(document.activeElement).not.toBe(document.body);
  });

  it("ProposalReview focuses incoming heading on load and #proposal-review-decided when decision actions unmount", async () => {
    const proposalRef = "proposal:0192f3c4-5e6a-7b8c-9d0e-000000009999@1";
    let currentStatus = "Open";
    vi.mocked(kernelApi.getProposal).mockImplementation(
      async () =>
        ({
          ref: proposalRef,
          intent: "Flag protocol deviation for review",
          status: currentStatus,
          proposer_id: "usr-crc-1",
          proposer_type: "Human",
          proposed_at: "2026-11-20T10:00:00Z",
          expires_at: "2099-12-20T10:00:00Z",
          content_hash: "sha256:proposal-1",
          requested_capabilities: ["query.raise"],
          reasoning_sources: [],
        }) as never
    );
    vi.mocked(kernelApi.rejectProposal).mockImplementation(async () => {
      currentStatus = "Rejected";
      return { status: "Rejected" } as never;
    });

    const router = makeTestRouter("/studies/CAD-101");
    await router.isReady();

    const wrapper = track(
      mount(ProposalReview, {
        attachTo: document.body,
        global: { plugins: [pinia, router] },
        props: {
          studyCode: "CAD-101",
          proposalRef,
          reviewer: "usr-crc-1",
        },
      })
    );
    await settleFocus();

    expect(document.activeElement?.id).toBe("proposal-review-title");

    await wrapper.find("#proposal-reject-reason").setValue("Duplicate check");
    const rejectBtn = wrapper.find("#btn-reject-proposal");
    (rejectBtn.element as HTMLElement).focus();
    await wrapper.find("#proposal-reject-form").trigger("submit");
    await settleFocus();

    expect(document.activeElement?.id).toBe("proposal-review-decided");
    expect(document.activeElement).not.toBe(document.body);
  });

  it("ProtocolImportPanel hands off focus to #proto-choose-file when Clear file unmounts and #proto-summary-title after import", async () => {
    const router = makeTestRouter("/studies/CAD-101");
    await router.isReady();

    vi.mocked(kernelApi.listProtocolTemplates).mockResolvedValue([]);
    vi.mocked(kernelApi.importProtocolDraft).mockResolvedValue({
      version: {
        ref: "ProtocolVersion:0192f3c4-5e6a-7b8c-9d0e-000000000201@1",
        number: "2.0",
        status: "Draft",
      },
      title: "CADENCE Phase II Imported Draft",
      form_count: 2,
      item_count: 8,
      forms: [],
      import_notes: [],
    } as never);

    const wrapper = track(
      mount(ProtocolImportPanel, {
        attachTo: document.body,
        global: { plugins: [pinia, router] },
        props: {
          studyRef: "study:0192f3c4-5e6a-7b8c-9d0e-000000000101",
          studyCode: "CAD-101",
          idPrefix: "proto",
        },
      })
    );
    await settleFocus();

    const fileInput = wrapper.find("#proto-file");
    const xmlFile = new File(["<ODM/>"], "study.xml", {
      type: "application/xml",
    });
    Object.defineProperty(fileInput.element, "files", {
      value: [xmlFile],
      configurable: true,
    });
    await fileInput.trigger("change");
    await settleFocus();

    const clearBtn = wrapper.find("#proto-clear-file");
    expect(clearBtn.exists()).toBe(true);
    (clearBtn.element as HTMLElement).focus();
    await clearBtn.trigger("click");
    await settleFocus();

    expect(document.activeElement?.id).toBe("proto-choose-file");

    Object.defineProperty(fileInput.element, "files", {
      value: [xmlFile],
      configurable: true,
    });
    await fileInput.trigger("change");
    const reasonInput = wrapper.find("#proto-reason");
    if (reasonInput.exists()) {
      await reasonInput.setValue("Initial protocol draft import");
    }
    await wrapper.find("#proto-form").trigger("submit");
    await settleFocus();

    expect(document.activeElement?.id).toBe("proto-summary-title");
  });

  it("ValidationSection focuses #validation-pin-reason-input on pin toggle and restores #btn-pin-pack trigger on cancel", async () => {
    const router = makeTestRouter("/studies/CAD-101");
    await router.isReady();

    vi.mocked(kernelApi.search).mockResolvedValue([
      {
        exact_code: true,
        ref: "study:0192f3c4-5e6a-7b8c-9d0e-000000000101",
        kind: "study",
        code: "CAD-101",
      },
    ] as never);
    vi.mocked(kernelApi.listStudyRulePacks).mockResolvedValue({
      rule_packs: [
        {
          ref: "rule_pack:0192f3c4-5e6a-7b8c-9d0e-000000000301@1",
          key: "VS-CORE",
          version: 1,
          title: "Vital Signs Range Checks",
          status: "Approved",
          pinned: false,
          rules: [],
        },
      ],
      pinned_rule_pack_refs: [],
    } as never);
    vi.mocked(kernelApi.listStudyValidationFindings).mockResolvedValue({
      findings: [],
      total: 0,
    } as never);

    const wrapper = track(
      mount(ValidationSection, {
        attachTo: document.body,
        global: { plugins: [pinia, router] },
        props: {
          studyCode: "CAD-101",
          canManageRules: true,
        },
      })
    );
    await settleFocus();

    const pinBtn = wrapper.find("#btn-pin-pack-VS-CORE-1");
    expect(pinBtn.exists()).toBe(true);
    (pinBtn.element as HTMLElement).focus();
    await pinBtn.trigger("click");
    await settleFocus();

    expect(document.activeElement?.id).toBe("validation-pin-reason-input");

    await wrapper.find("#btn-cancel-pin-VS-CORE").trigger("click");
    await settleFocus();

    expect(document.activeElement?.id).toBe("btn-pin-pack-VS-CORE-1");
  });

  it("DesignerSchemaPanel moves focus across template loading and candidate edit/reject/promote transitions", async () => {
    const storeState = reactive({
      loading: false,
      candidateDraft: {
        id: "cand-1",
        status: "IN_REVIEW",
        items: [
          {
            id: "item-1",
            field_name: "SYSBP",
            suggested_value: "Quantity (mmHg)",
            confidence: 0.94,
            review_status: "PENDING",
          },
        ],
      },
    });

    const wrapper = track(
      mount(DesignerSchemaPanel, {
        attachTo: document.body,
        global: { plugins: [pinia] },
        props: {
          store: storeState,
          selectedFileName: "",
          importChangeReason: "Add standard vital signs",
          selectedTemplateIds: [],
          availableTemplates: [],
          unreviewedCount: 0,
          getConfidenceClass: () => "badge-standard",
          getStatusClass: () => "badge-standard",
        },
      })
    );
    await settleFocus();

    await wrapper.setProps({
      availableTemplates: [{ id: "VS", title: "Vital Signs", domain: "VS" }],
    });
    await settleFocus();
    expect(document.activeElement?.id).toBe("designer-tpl-VS");

    await wrapper.setProps({ editingItemId: "item-1" });
    await settleFocus();
    expect(document.activeElement?.id).toBe("designer-edit-item-value-item-1");

    await wrapper.setProps({ editingItemId: null });
    await settleFocus();
    expect(document.activeElement?.id).toBe("btn-edit-candidate-item-1");

    await wrapper.setProps({ rejectingItemId: "item-1" });
    await settleFocus();
    expect(document.activeElement?.id).toBe(
      "designer-reject-item-reason-item-1"
    );

    await wrapper.setProps({ rejectingItemId: null });
    await settleFocus();
    expect(document.activeElement?.id).toBe("btn-reject-candidate-item-1");

    storeState.candidateDraft.status = "PROMOTED";
    await settleFocus();
    expect(document.activeElement?.id).toBe(
      "designer-candidate-promoted-banner"
    );
  });

  it("EcrfFormPanel falls back to #ecrf-form-title when highlightItemId targets a disabled field and manages SDV/query focus", async () => {
    const formPayload = {
      form_id: "0192f3c4-5e6a-7b8c-9d0e-00000000f001",
      enrollment_id: "e-1",
      participant_id: "p-1",
      study_id: "s-1",
      study_site_id: "ss-1",
      protocol_version_id: "pv-1",
      collection_spec_id: "VS",
      planned_activity_id: "WEEK2",
      collected_at: "2027-03-14T09:00:00Z",
      opened_by: "crc1",
      opened_at: "2027-03-14T09:00:00Z",
      version: 2,
      complete: false,
      missing_required: [],
      items: [
        {
          item_id: "SYSBP",
          concept: "C25298@1",
          unit: "mm[Hg]",
          value_kind: "Quantity",
          required: true,
          value: { type: "quantity", value: 120, unit: "mm[Hg]" },
          observation_id: "0192f3c4-5e6a-7b8c-9d0e-00000000a001",
          revision: 3,
          recorded_by: "crc1",
          recorded_at: "2027-03-14T09:05:00Z",
        },
        {
          item_id: "DIABP",
          unit: "mm[Hg]",
          value_kind: "Quantity",
          required: true,
          value: { type: "missing", reason: "not_done" },
          observation_id: "0192f3c4-5e6a-7b8c-9d0e-00000000a002",
          revision: 1,
          recorded_by: "crc1",
          recorded_at: "2027-03-14T09:05:00Z",
        },
      ],
      locks: [],
      assertions: [],
      signatures: [],
      content_hash: "sha256:form-v2",
    };

    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(formPayload as never);
    vi.mocked(ecrfQueriesApi.listForForm).mockResolvedValue([]);

    const auth = useAuthStore();
    auth.isAuthenticated = true;
    auth.rawRoles = ["CRA"];

    const router = makeTestRouter();
    await router.isReady();

    const wrapper = track(
      mount(EcrfFormPanel, {
        attachTo: document.body,
        global: { plugins: [pinia, router] },
        props: {
          formId: formPayload.form_id,
          highlightItemId: "DIABP",
          canEdit: false,
          canVerifySource: true,
          canManageQueries: true,
        },
      })
    );
    await settleFocus();

    expect(document.activeElement?.id).toBe("ecrf-form-title");
    expect(document.activeElement).not.toBe(document.body);

    // Bulk SDV clear selection restores focus to #ecrf-sdv-select-all
    await wrapper.find("#ecrf-sdv-select-all").trigger("click");
    await settleFocus();
    const clearSdvBtn = wrapper.find("#ecrf-sdv-clear-selection");
    expect(clearSdvBtn.exists()).toBe(true);
    (clearSdvBtn.element as HTMLElement).focus();
    await clearSdvBtn.trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("ecrf-sdv-select-all");

    // Per-field SDV disclosure & cancel
    const verifyBtn = wrapper.find("#ecrf-verify-SYSBP");
    (verifyBtn.element as HTMLElement).focus();
    await verifyBtn.trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("ecrf-sdv-reason");

    await wrapper.find("#ecrf-sdv-cancel").trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("ecrf-verify-SYSBP");

    // Per-field Raise Query disclosure & cancel
    const raiseBtn = wrapper.find("#ecrf-raise-SYSBP");
    (raiseBtn.element as HTMLElement).focus();
    await raiseBtn.trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("ecrf-raise-text");

    await wrapper.find("#ecrf-raise-cancel").trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("ecrf-raise-SYSBP");
  });

  it("FormQueryList coordinates focus across bulk clear selection and per-query action disclosures", async () => {
    const queries = [
      {
        query_id: "q-101",
        form_id: "f-1",
        item_id: "SYSBP",
        observation_id: "obs-1",
        revision: 1,
        current_revision: 1,
        value_changed: false,
        status: "open",
        messages: [
          {
            kind: "raised",
            by: "cra1",
            at: "2026-11-20T12:00:00Z",
            text: "Confirm cuff size",
          },
        ],
      },
    ];

    const wrapper = track(
      mount(FormQueryList, {
        attachTo: document.body,
        global: { plugins: [pinia] },
        props: {
          queries: queries as never,
          busy: false,
          canManage: true,
          canAnswer: true,
          act: vi.fn(async () => true),
          actMany: vi.fn(async () => []),
        },
      })
    );
    await settleFocus();

    await wrapper.find("#btn-select-eligible-queries").trigger("click");
    await settleFocus();
    const clearBtn = wrapper.find("#btn-clear-selected-queries");
    expect(clearBtn.exists()).toBe(true);
    (clearBtn.element as HTMLElement).focus();
    await clearBtn.trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("btn-select-eligible-queries");

    const answerBtn = wrapper.find("#btn-answer-q-101");
    (answerBtn.element as HTMLElement).focus();
    await answerBtn.trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("query-text-q-101");

    const backBtn = wrapper
      .findAll(".form-query-form button")
      .find((b) => b.text().trim().startsWith("Back"));
    expect(backBtn).toBeDefined();
    await backBtn!.trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("btn-answer-q-101");
  });

  it("ParticipantWalletView moves focus into #selected-wallet-heading on review and restores the row trigger on close", async () => {
    const mockWallet = {
      id: "wallet-focus-1",
      subject_id: "SUBJ-2001",
      study_id: "STUDY-DCT-001",
      site_id: "SITE-101",
      currency: "USD",
      current_balance: 150.0,
      lifetime_stipends: 300.0,
      lifetime_reimbursements: 150.0,
      taxable_ytd: 450.0,
      virtual_card_token: "VTOK-12345",
      debit_card_status: "ACTIVE",
      w9_status: "VERIFIED",
      tax_form_submitted: true,
      tax_form_verified: true,
      tax_id_last_four: "4321",
    };

    global.fetch = vi.fn(async (input: RequestInfo | URL) => {
      const url = typeof input === "string" ? input : input.toString();
      if (url === "/api/v1/payments/participant/wallets") {
        return new Response(JSON.stringify([mockWallet]), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        });
      }
      if (
        url === "/api/v1/payments/participant/stipend-rules" ||
        url === "/api/v1/payments/participant/expenses"
      ) {
        return new Response(JSON.stringify([]), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        });
      }
      if (url.includes("/tax-1099-status")) {
        return new Response(
          JSON.stringify({
            taxable_ytd: 450.0,
            threshold: 600.0,
            requires_w9: false,
            status: "VERIFIED",
          }),
          { status: 200, headers: { "Content-Type": "application/json" } }
        );
      }
      if (url.includes("/balance")) {
        return new Response(
          JSON.stringify({
            current_balance: 150.0,
            available_balance: 150.0,
            pending_claims_total: 0.0,
            lifetime_stipends: 300.0,
            lifetime_reimbursements: 150.0,
            taxable_ytd: 450.0,
            currency: "USD",
          }),
          { status: 200, headers: { "Content-Type": "application/json" } }
        );
      }
      return new Response(JSON.stringify({}), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      });
    });

    const wrapper = track(
      mount(ParticipantWalletView, {
        attachTo: document.body,
        global: { plugins: [pinia] },
      })
    );
    await settleFocus();

    const reviewBtn = wrapper.find(`#btn-inspect-wallet-${mockWallet.id}`);
    expect(reviewBtn.exists()).toBe(true);
    (reviewBtn.element as HTMLElement).focus();
    await reviewBtn.trigger("click");
    await settleFocus();

    expect(document.activeElement?.id).toBe("selected-wallet-heading");

    await wrapper.find("#btn-close-selected-wallet").trigger("click");
    await settleFocus();

    expect(document.activeElement?.id).toBe(
      `btn-inspect-wallet-${mockWallet.id}`
    );
  });

  it("StudyCanvasView focuses incoming section headings across workspace v-if swaps and handles #create-study-clear-file", async () => {
    const auth = useAuthStore();
    auth.isAuthenticated = true;
    auth.rawRoles = ["Sponsor Admin", "Data Manager"];

    vi.mocked(kernelApi.search).mockResolvedValue([
      {
        exact_code: true,
        ref: "study:0192f3c4-5e6a-7b8c-9d0e-000000000101",
        kind: "study",
        code: "CAD-101",
        title: "CADENCE Phase II Hypertension Study",
      },
    ] as never);

    const router = makeTestRouter("/studies/CAD-101");
    await router.isReady();

    const wrapper = track(
      mount(
        { template: "<router-view />" },
        {
          attachTo: document.body,
          global: { plugins: [pinia, router] },
        }
      )
    );
    await settleFocus();

    await router.push("/studies/CAD-101?stage=conduct&section=participants");
    await settleFocus();
    expect(document.activeElement?.id).toBe("participants-title");

    await router.push("/studies/CAD-101?stage=data&section=queries");
    await settleFocus();
    expect(document.activeElement?.id).toBe("study-queries-title");

    await router.push("/studies/CAD-101?stage=data&section=validation");
    await settleFocus();
    expect(document.activeElement?.id).toBe("study-validation-title");

    // Open studies switcher ("/studies") to test #create-study-clear-file focus handoff
    await router.push("/studies");
    await settleFocus();

    const createToggle = wrapper.find("#btn-toggle-create-study");
    if (createToggle.exists()) {
      await createToggle.trigger("click");
      await settleFocus();
    }

    const createFileInput = wrapper.find("#create-study-file");
    if (createFileInput.exists()) {
      const jsonFile = new File(["{}"], "usdm.json", {
        type: "application/json",
      });
      Object.defineProperty(createFileInput.element, "files", {
        value: [jsonFile],
        configurable: true,
      });
      await createFileInput.trigger("change");
      await settleFocus();

      const clearFileBtn = wrapper.find("#create-study-clear-file");
      expect(clearFileBtn.exists()).toBe(true);
      (clearFileBtn.element as HTMLElement).focus();
      await clearFileBtn.trigger("click");
      await settleFocus();

      expect(document.activeElement?.id).toBe("create-study-choose-file");
    }
  });

  it("TicketsView and KnowledgeView expose keyboard-focusable scroll regions and activate cards via Enter and Space", async () => {
    vi.mocked(ticketsApi.listTickets).mockRejectedValue(
      new Error("Use demo tickets")
    );

    const ticketsWrapper = track(
      mount(TicketsView, {
        attachTo: document.body,
        global: { plugins: [pinia] },
      })
    );
    await settleFocus();

    const kanbanBoard = ticketsWrapper.find(".kanban-board");
    expect(kanbanBoard.attributes("tabindex")).toBe("0");
    expect(kanbanBoard.attributes("role")).toBe("tabpanel");
    expect(kanbanBoard.attributes("aria-labelledby")).toBe(
      "tickets-view-tab-kanban"
    );

    const columnCards = ticketsWrapper.find(".column-cards");
    expect(columnCards.attributes("tabindex")).toBe("0");
    expect(columnCards.attributes("role")).toBe("region");

    const firstTicketCard = ticketsWrapper.find(".ticket-card");
    expect(firstTicketCard.exists()).toBe(true);
    expect(firstTicketCard.element.tagName).toBe("BUTTON");
    expect(firstTicketCard.attributes("type")).toBe("button");

    await firstTicketCard.trigger("keydown.enter");
    await settleFocus();
    expect(document.querySelector('[role="dialog"]')).not.toBeNull();

    // Close drawer and reopen via Space key
    document.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Escape",
        bubbles: true,
        cancelable: true,
      })
    );
    await settleFocus();

    await firstTicketCard.trigger("keydown.space");
    await settleFocus();
    expect(document.querySelector('[role="dialog"]')).not.toBeNull();

    // Mount KnowledgeView and verify .articles-stream, .article-card, and .reader-body-card
    const knowledgeWrapper = track(
      mount(KnowledgeView, {
        attachTo: document.body,
        global: { plugins: [pinia] },
      })
    );
    await settleFocus();

    const articlesStream = knowledgeWrapper.find(".articles-stream");
    expect(articlesStream.exists()).toBe(true);
    expect(articlesStream.attributes("tabindex")).toBe("0");
    expect(articlesStream.attributes("role")).toBe("region");
    expect(articlesStream.attributes("aria-label")).toBeTruthy();

    const cards = knowledgeWrapper.findAll(".article-card");
    expect(cards.length).toBeGreaterThanOrEqual(2);
    expect(cards[0].element.tagName).toBe("BUTTON");
    expect(cards[0].attributes("type")).toBe("button");

    await cards[0].trigger("keydown.enter");
    await settleFocus();
    expect(cards[0].attributes("aria-pressed")).toBe("true");
    expect(
      knowledgeWrapper.find(".reader-body-card").attributes("tabindex")
    ).toBe("0");

    await cards[1].trigger("keydown.space");
    await settleFocus();
    expect(cards[1].attributes("aria-pressed")).toBe("true");
  });

  it("CtmsMilestonesSection coordinates focus across milestone achievement open, cancel, and confirm", async () => {
    const auth = useAuthStore();
    auth.isAuthenticated = true;
    auth.rawRoles = ["Sponsor Admin", "CRA"];

    const milestoneState = [
      {
        id: "ms-1",
        study_id: "CAD-101",
        site_id: "SITE-101",
        milestone_type: "INITIATION_VISIT",
        status: "PLANNED",
        planned_date: "2026-11-01",
        actual_date: null as string | null,
      },
      {
        id: "ms-2",
        study_id: "CAD-101",
        site_id: "SITE-101",
        milestone_type: "SITE_ACTIVATION",
        status: "PLANNED",
        planned_date: "2026-11-15",
        actual_date: null as string | null,
      },
    ];
    vi.mocked(ctmsApi.listSiteMilestones).mockImplementation(
      async () => milestoneState.map((m) => ({ ...m })) as never
    );
    vi.mocked(ctmsApi.updateSiteMilestone).mockImplementation(
      async (milestoneId) => {
        const target = milestoneState.find((m) => m.id === milestoneId);
        if (target) {
          target.status = "ACHIEVED";
          target.actual_date = "2026-11-20";
        }
        return target as never;
      }
    );

    const wrapper = track(
      mount(CtmsMilestonesSection, {
        attachTo: document.body,
        global: { plugins: [pinia] },
        props: { studyCode: "CAD-101", siteId: "SITE-101" },
      })
    );
    await settleFocus();

    const achieveFirstBtn = wrapper.find("#btn-achieve-milestone-ms-1");
    expect(achieveFirstBtn.exists()).toBe(true);
    (achieveFirstBtn.element as HTMLElement).focus();
    await achieveFirstBtn.trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("achieve-milestone-date");

    // Cancel returns focus to the triggering row button
    const cancelBtn = wrapper
      .findAll("#achieve-milestone-form button")
      .find((b) => b.text().trim() === "Cancel");
    await cancelBtn!.trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("btn-achieve-milestone-ms-1");

    // Confirm ms-1 -> focus moves to remaining #btn-achieve-milestone-ms-2
    await wrapper.find("#btn-achieve-milestone-ms-1").trigger("click");
    await settleFocus();
    await wrapper.find("#achieve-milestone-date").setValue("2026-11-20");
    await wrapper
      .find("#achieve-milestone-reason")
      .setValue("Initiation visit completed on schedule");
    await wrapper.find("#btn-confirm-achieve-milestone").trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("btn-achieve-milestone-ms-2");

    // Confirm last milestone ms-2 -> focus falls back to #ctms-milestones-title
    await wrapper.find("#btn-achieve-milestone-ms-2").trigger("click");
    await settleFocus();
    await wrapper.find("#achieve-milestone-date").setValue("2026-11-21");
    await wrapper
      .find("#achieve-milestone-reason")
      .setValue("Site activation milestone completed");
    await wrapper.find("#btn-confirm-achieve-milestone").trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("ctms-milestones-title");
  });

  it("CtmsDelegationSection coordinates focus across PI sign-off and revocation disclosures", async () => {
    const auth = useAuthStore();
    auth.isAuthenticated = true;
    auth.user = { id: "u-pi-1", username: "pi.site101" } as never;
    auth.rawRoles = ["Principal Investigator", "Sponsor Admin"];

    let staffStatus = "PENDING_PI_SIGNATURE";
    vi.mocked(ctmsApi.getSiteDoaLog).mockImplementation(
      async () =>
        ({
          site_id: "SITE-101",
          pi_name: "Dr. Evelyn Chen",
          delegated_staff: [
            {
              record_id: "rec-1",
              staff_user_id: "crc_01",
              staff_name: "Jordan Lee",
              role: "CRC",
              task_codes: ["CRF_DATA_ENTRY"],
              start_date: "2026-10-01",
              is_active: staffStatus !== "REVOKED",
              signed_off: staffStatus === "ACTIVE",
              end_date: staffStatus === "REVOKED" ? "2026-11-23" : null,
              status: staffStatus,
            },
          ],
          audit_history: [],
        }) as never
    );
    vi.mocked(ctmsApi.signOffDelegation).mockImplementation(async () => {
      staffStatus = "ACTIVE";
      return {} as never;
    });
    vi.mocked(ctmsApi.revokeSiteTasks).mockImplementation(async () => {
      staffStatus = "REVOKED";
      return {} as never;
    });

    const wrapper = track(
      mount(CtmsDelegationSection, {
        attachTo: document.body,
        global: { plugins: [pinia] },
        props: { studyCode: "CAD-101", siteId: "SITE-101" },
      })
    );
    await settleFocus();
    await wrapper.find("#delegation-scope-form").trigger("submit");
    await settleFocus();

    const signOffBtn = wrapper.find("#btn-doa-signoff-rec-1");
    expect(signOffBtn.exists()).toBe(true);
    (signOffBtn.element as HTMLElement).focus();
    await signOffBtn.trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("doa-signoff-reason");

    // Cancel sign-off restores #btn-doa-signoff-rec-1
    const cancelSignOffBtn = wrapper
      .findAll(".mutation-card button")
      .find((b) => b.text().trim() === "Cancel");
    await cancelSignOffBtn!.trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("btn-doa-signoff-rec-1");

    // Confirm sign-off -> moves to remaining row action (#btn-doa-revoke-rec-1)
    await wrapper.find("#btn-doa-signoff-rec-1").trigger("click");
    await settleFocus();
    await wrapper
      .find("#doa-signoff-reason")
      .setValue("Verified training and CV qualifications");
    await wrapper
      .find("#doa-signoff-password")
      .setValue("CorrectHorseBatteryStaple!");
    await wrapper.find("#btn-confirm-doa-signoff").trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("btn-doa-revoke-rec-1");

    // Open & cancel revocation
    await wrapper.find("#btn-doa-revoke-rec-1").trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("doa-revoke-reason");

    const cancelRevokeBtn = wrapper
      .findAll(".mutation-card button")
      .find((b) => b.text().trim() === "Cancel");
    await cancelRevokeBtn!.trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("btn-doa-revoke-rec-1");

    // Confirm revocation -> row actions exhausted, falls back to #ctms-delegation-title
    await wrapper.find("#btn-doa-revoke-rec-1").trigger("click");
    await settleFocus();
    await wrapper
      .find("#doa-revoke-reason")
      .setValue("Coordinator reassigned to another protocol");
    await wrapper.find("#btn-confirm-doa-revoke").trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("ctms-delegation-title");
  });

  it("CtmsMonitoringSection coordinates focus across letters panel, structured findings removal, visit completion, letter compile, and supervisory sign-off", async () => {
    const auth = useAuthStore();
    auth.isAuthenticated = true;
    auth.user = { id: "u-dm-1", username: "dm.lead" } as never;
    auth.rawRoles = ["Sponsor Admin", "CRA", "Data Manager"];

    let visitStatus = "SCHEDULED";
    vi.mocked(ctmsApi.listMonitoringVisits).mockImplementation(
      async () =>
        [
          {
            id: "vis-1",
            study_id: "CAD-101",
            site_id: "SITE-101",
            visit_type: "IMV",
            cra_id: "cra_01",
            scheduled_date: "2026-11-18",
            status: visitStatus,
            findings: [],
          },
        ] as never
    );
    vi.mocked(ctmsApi.listMonitoringVisitLetters).mockResolvedValue([]);
    vi.mocked(ctmsApi.completeMonitoringVisit).mockImplementation(async () => {
      visitStatus = "COMPLETED";
      return {} as never;
    });
    vi.mocked(ctmsApi.compileVisitLetter).mockResolvedValue({
      letterId: "ltr-1",
      letterType: "FOLLOW_UP_LETTER",
      status: "DRAFT",
      pdfSha256Hash: "sha256:ltr1",
    } as never);
    vi.mocked(ctmsApi.signOffMonitoringVisit).mockImplementation(async () => {
      visitStatus = "SIGNED_OFF";
      return {} as never;
    });

    const wrapper = track(
      mount(CtmsMonitoringSection, {
        attachTo: document.body,
        global: { plugins: [pinia] },
        props: { studyCode: "CAD-101", siteId: "SITE-101" },
      })
    );
    await settleFocus();

    // 1. Letters panel open & close
    const lettersBtn = wrapper.find("#btn-visit-letters-vis-1");
    (lettersBtn.element as HTMLElement).focus();
    await lettersBtn.trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("btn-close-monitoring-letters");

    await wrapper.find("#btn-close-monitoring-letters").trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("btn-visit-letters-vis-1");

    // 2. Visit completion open, structured findings removal, cancel, and confirm
    const completeBtn = wrapper.find(
      '[data-testid="btn-complete-visit-vis-1"]'
    );
    (completeBtn.element as HTMLElement).focus();
    await completeBtn.trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("complete-visit-date");

    await wrapper.find("#btn-add-visit-finding").trigger("click");
    await wrapper.find("#btn-add-visit-finding").trigger("click");
    await settleFocus();

    const removeFindingBtns = wrapper
      .findAll("#modal-complete-visit button")
      .filter((b) => b.text().trim() === "Remove");
    expect(removeFindingBtns.length).toBe(2);
    await removeFindingBtns[1].trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("finding-text-0");

    const remainingRemoveBtn = wrapper
      .findAll("#modal-complete-visit button")
      .find((b) => b.text().trim() === "Remove");
    await remainingRemoveBtn!.trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("btn-add-visit-finding");

    await wrapper.find("#complete-visit-date").setValue("2026-11-19");
    await wrapper
      .find("#complete-visit-reason")
      .setValue("Interim monitoring visit completed");
    await wrapper.find("#btn-confirm-complete-visit").trigger("click");
    await settleFocus();
    expect(document.activeElement).not.toBe(document.body);

    // 3. Compile letter open, cancel, and confirm -> focuses Download PDF button
    const compileBtn = wrapper.find('[data-testid="btn-compile-letter-vis-1"]');
    (compileBtn.element as HTMLElement).focus();
    await compileBtn.trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("compile-letter-template");

    const cancelCompileBtn = wrapper
      .findAll("#modal-compile-letter button")
      .find((b) => b.text().trim() === "Cancel");
    await cancelCompileBtn!.trigger("click");
    await settleFocus();
    expect(document.activeElement).toBe(compileBtn.element);

    await compileBtn.trigger("click");
    await settleFocus();
    await wrapper.find("#compile-letter-finalize").setValue(false);
    await wrapper
      .find("#compile-letter-reason")
      .setValue("Generate draft follow-up letter");
    await wrapper.find("#btn-confirm-compile-letter").trigger("click");
    await settleFocus();
    expect(document.activeElement).toBe(
      wrapper.find('[data-testid="btn-download-letter-ltr-1"]').element
    );

    // 4. Supervisory sign-off open, cancel, and confirm
    const signOffBtn = wrapper.find('[data-testid="btn-signoff-visit-vis-1"]');
    expect(signOffBtn.exists()).toBe(true);
    (signOffBtn.element as HTMLElement).focus();
    await signOffBtn.trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("signoff-visit-reason");

    await wrapper
      .find("#signoff-visit-reason")
      .setValue("Supervisory review complete");
    await wrapper
      .find("#signoff-visit-password")
      .setValue("CorrectHorseBatteryStaple!");
    await wrapper.find("#btn-confirm-signoff-visit").trigger("click");
    await settleFocus();
    expect(document.activeElement).not.toBe(document.body);
  });

  it("CtmsGreenlightSection hands off focus to #ctms-greenlight-title after single-site and batch certification", async () => {
    const auth = useAuthStore();
    auth.isAuthenticated = true;
    auth.user = { id: "u-dm-1", username: "dm.lead" } as never;
    auth.rawRoles = ["Sponsor Admin", "Data Manager"];

    let gateCertified = false;
    const makeGate = (siteId: string) => ({
      study_id: "CAD-101",
      site_id: siteId,
      country_code: "US",
      overall_status: gateCertified ? "CERTIFIED" : "READY",
      is_compliant: true,
      is_ready_for_certification: !gateCertified,
      facet_regulatory_irb: true,
      facet_cta_budget: true,
      facet_eisf_baseline: true,
      facet_doa_log: true,
      facet_staff_training: true,
      facet_ip_release: true,
      unfulfilled_reasons: [],
      blocking_reasons: [],
      facets: [
        {
          facet_code: "IRB",
          display_name: "IRB Approval",
          is_satisfied: true,
          evidence_summary: "Approved",
        },
      ],
      greenlight_certified_by: gateCertified ? "dm_01" : null,
      greenlight_certified_at: gateCertified ? "2026-11-20T12:00:00Z" : null,
      certified_by: gateCertified ? "dm_01" : null,
      certified_at: gateCertified ? "2026-11-20T12:00:00Z" : null,
    });

    vi.mocked(ctmsApi.evaluateSiteGreenlight).mockImplementation(
      async (siteId) => makeGate(siteId) as never
    );
    vi.mocked(ctmsApi.getCountryGreenlightMatrix).mockImplementation(
      async () => [makeGate("SITE-101"), makeGate("SITE-102")] as never
    );
    vi.mocked(ctmsApi.certifySiteGreenlight).mockImplementation(async () => {
      gateCertified = true;
      return makeGate("SITE-101") as never;
    });
    vi.mocked(ctmsApi.batchCertifyCountrySites).mockImplementation(async () => {
      gateCertified = true;
      return { certified_count: 1, country_code: "US" } as never;
    });

    const wrapper = track(
      mount(CtmsGreenlightSection, {
        attachTo: document.body,
        global: { plugins: [pinia] },
        props: { studyCode: "CAD-101", siteId: "SITE-101" },
      })
    );
    await settleFocus();

    await wrapper
      .find("#greenlight-certify-reason")
      .setValue("All startup facets verified");
    await wrapper
      .find("#greenlight-certify-password")
      .setValue("CorrectHorseBatteryStaple!");
    const certifySingleBtn = wrapper.find("#btn-certify-site-greenlight");
    (certifySingleBtn.element as HTMLElement).focus();
    await certifySingleBtn.trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("ctms-greenlight-title");

    // Reset gateCertified to test batch certification handoff
    gateCertified = false;
    await wrapper.find("#greenlight-scope-form").trigger("submit");
    await settleFocus();

    const rowCheckbox = wrapper.find('tbody input[type="checkbox"]');
    await rowCheckbox.setValue(true);
    await settleFocus();
    await wrapper
      .find("#batch-certify-reason")
      .setValue("Batch country certification approved");
    await wrapper
      .find("#batch-certify-password")
      .setValue("CorrectHorseBatteryStaple!");
    const batchBtn = wrapper.find("#btn-batch-certify-greenlight");
    (batchBtn.element as HTMLElement).focus();
    await batchBtn.trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("ctms-greenlight-title");
  });

  it("SiteActivationPanel moves focus across Execution activation and Kernel link/activate banners", async () => {
    const auth = useAuthStore();
    auth.isAuthenticated = true;
    auth.rawRoles = ["Sponsor Admin", "Data Manager"];

    let executionActivated = false;
    let kernelStatus: "UNLINKED" | "Pending" | "Active" = "UNLINKED";

    vi.mocked(kernelApi.search).mockResolvedValue([
      {
        exact_code: true,
        ref: "study:0192f3c4-5e6a-7b8c-9d0e-000000000101",
        kind: "study",
        code: "CAD-101",
      },
    ] as never);
    vi.mocked(kernelApi.canI).mockResolvedValue({ allowed: true } as never);
    vi.mocked(ctmsApi.evaluateSiteGreenlight).mockResolvedValue({
      study_id: "CAD-101",
      site_id: "SITE-101",
      overall_status: "CERTIFIED",
      is_ready_for_certification: false,
      facets: [],
      blocking_reasons: [],
    } as never);
    vi.mocked(executionApi.getSiteComplianceStatus).mockImplementation(
      async () =>
        ({
          study_id: "CAD-101",
          site_id: "SITE-101",
          milestone: "SITE_ACTIVATION",
          is_complete: true,
          missing_documents: [],
          is_activated: executionActivated,
          activated_at: executionActivated ? "2026-11-20T12:00:00Z" : null,
          activated_by: executionActivated ? "dm_01" : null,
        }) as never
    );
    vi.mocked(executionApi.activateSite).mockImplementation(async () => {
      executionActivated = true;
      return {
        study_id: "CAD-101",
        site_id: "SITE-101",
        milestone: "SITE_ACTIVATION",
        is_complete: true,
        missing_documents: [],
        is_activated: true,
        activated_at: "2026-11-20T12:00:00Z",
        activated_by: "dm_01",
      } as never;
    });
    vi.mocked(kernelGovernanceApi.listStudySites).mockImplementation(
      async () => {
        if (kernelStatus === "UNLINKED") return [];
        return [
          {
            study_site_ref: "study_site:0192f3c4-5e6a-7b8c-9d0e-000000000501",
            site_code: "SITE-101",
            status: kernelStatus,
            effective_versions: [{ number: "1.0", scope_level: "Study" }],
          },
        ] as never;
      }
    );
    vi.mocked(kernelGovernanceApi.linkStudySite).mockImplementation(
      async () => {
        kernelStatus = "Pending";
        return {} as never;
      }
    );
    vi.mocked(kernelGovernanceApi.activateStudySite).mockImplementation(
      async () => {
        kernelStatus = "Active";
        return {} as never;
      }
    );

    const wrapper = track(
      mount(SiteActivationPanel, {
        attachTo: document.body,
        global: { plugins: [pinia] },
        props: { studyCode: "CAD-101", siteId: "SITE-101" },
      })
    );
    await settleFocus();

    // 1. Execution site activation focuses #site-activated-summary
    await wrapper
      .find("#site-activation-reason")
      .setValue("Essential documents verified");
    const activateBtn = wrapper.find("#btn-activate-site");
    (activateBtn.element as HTMLElement).focus();
    await activateBtn.trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("site-activated-summary");

    // 2. Kernel site link focuses #kernel-site-activate-reason so user can enter activation reason
    await wrapper
      .find("#kernel-site-link-reason")
      .setValue("Link site in Clinical Kernel");
    const linkBtn = wrapper.find("#btn-link-kernel-site");
    (linkBtn.element as HTMLElement).focus();
    await linkBtn.trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("kernel-site-activate-reason");

    // 3. Kernel site activation focuses #kernel-site-active-banner
    await wrapper
      .find("#kernel-site-activate-reason")
      .setValue("Activate study site under effective protocol v1.0");
    const activateKernelBtn = wrapper.find("#btn-activate-kernel-site");
    (activateKernelBtn.element as HTMLElement).focus();
    await activateKernelBtn.trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("kernel-site-active-banner");
  });

  it("RtsmSupplySection coordinates focus across draft randomization scheme form toggle, save, and approval", async () => {
    const auth = useAuthStore();
    auth.isAuthenticated = true;
    auth.user = {
      id: "u-stat-1",
      username: "stat_approver",
      email: "stat@cadence.clinical",
      firstName: "Lead",
      lastName: "Statistician",
      tenantId: "tenant-1",
    };
    auth.rawRoles = ["Unblinded Statistician", "Sponsor Admin"];

    const schemeList: Array<Record<string, unknown>> = [];
    vi.mocked(executionApi.getSiteIpKitInventory).mockResolvedValue([]);
    vi.mocked(executionApi.listRandomizationSchemes).mockImplementation(
      async () => schemeList.map((s) => ({ ...s })) as never
    );
    vi.mocked(executionApi.draftRandomizationScheme).mockImplementation(
      async () => {
        schemeList.splice(0, schemeList.length, {
          id: "sch-1",
          study_id: "CAD-101",
          version: 1,
          method: "permuted_block",
          status: "DRAFT",
          requires_kit: true,
          drafted_by: "stat_drafter",
          approved_by: null,
        });
        return schemeList[0] as never;
      }
    );
    vi.mocked(executionApi.approveRandomizationScheme).mockImplementation(
      async () => {
        schemeList[0].status = "APPROVED";
        schemeList[0].approved_by = "stat_approver";
        return schemeList[0] as never;
      }
    );

    const wrapper = track(
      mount(RtsmSupplySection, {
        attachTo: document.body,
        global: { plugins: [pinia] },
        props: { studyCode: "CAD-101", siteId: "SITE-101" },
      })
    );
    await settleFocus();

    const toggleBtn = wrapper.find("#btn-toggle-draft-scheme");
    expect(toggleBtn.exists()).toBe(true);
    (toggleBtn.element as HTMLElement).focus();
    await toggleBtn.trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("scheme-method");

    // Toggle closed restores #btn-toggle-draft-scheme
    await wrapper.find("#btn-toggle-draft-scheme").trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("btn-toggle-draft-scheme");

    // Reopen and submit draft scheme -> focuses newly rendered #btn-approve-scheme-sch-1
    await wrapper.find("#btn-toggle-draft-scheme").trigger("click");
    await settleFocus();
    await wrapper
      .find("#scheme-draft-reason")
      .setValue("Initial SAP randomization block specification");
    await wrapper.find("#rtsm-draft-scheme-form").trigger("submit");
    await settleFocus();
    expect(document.activeElement?.id).toBe("btn-approve-scheme-sch-1");

    // Approve scheme via SignatureCaptureModal onSign -> focuses #btn-toggle-draft-scheme
    await wrapper.find("#btn-approve-scheme-sch-1").trigger("click");
    await settleFocus();
    const sigModal = wrapper.findComponent({ name: "SignatureCaptureModal" });
    await (
      sigModal.props("onSign") as (
        sigToken: string,
        signingReason: string
      ) => Promise<unknown>
    )("sig-tok-scheme-1", "APPROVAL");
    await settleFocus();
    expect(document.activeElement?.id).toBe("btn-toggle-draft-scheme");
  });

  it("ParticipantStateChange manages focus across stratification factor removal, randomization, emergency unblind, and terminal state", async () => {
    const auth = useAuthStore();
    auth.isAuthenticated = true;
    auth.rawRoles = ["Principal Investigator", "Sponsor Admin"];

    const canvas = useStudyCanvasStore();
    canvas.studyCode = "CAD-101";

    vi.mocked(executionApi.randomizeSubject).mockResolvedValue({
      status: "RANDOMIZED",
      kit_number: "KIT-1001",
    } as never);
    vi.mocked(executionApi.changeSubjectState).mockImplementation(
      async (_id, nextState) =>
        ({
          id: "subj-1",
          subject_id: "017-0009",
          study_id: "CAD-101",
          site_id: "017",
          status: nextState,
        }) as never
    );

    const wrapper = track(
      mount(ParticipantStateChange, {
        attachTo: document.body,
        global: { plugins: [pinia] },
        props: {
          participant: {
            id: "subj-1",
            subject_id: "017-0009",
            study_id: "CAD-101",
            site_id: "017",
            status: "ENROLLED",
            active_protocol_version: "1.0",
            enrollment_index: 9,
          },
          canManageParticipants: true,
        },
      })
    );
    await settleFocus();

    // 1. Add two stratification factor rows and remove them sequentially
    await wrapper.find("#btn-add-strat-factor").trigger("click");
    await wrapper.find("#btn-add-strat-factor").trigger("click");
    await settleFocus();

    const removeBtn1 = wrapper.find("#btn-remove-strat-factor-1");
    expect(removeBtn1.exists()).toBe(true);
    (removeBtn1.element as HTMLElement).focus();
    await removeBtn1.trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("strat-factor-key-0");

    const removeBtn0 = wrapper.find("#btn-remove-strat-factor-0");
    expect(removeBtn0.exists()).toBe(true);
    (removeBtn0.element as HTMLElement).focus();
    await removeBtn0.trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("btn-add-strat-factor");

    // 2. Randomize signed -> participant updates to RANDOMIZED and focus lands on #state-change-target
    await wrapper
      .find("#randomize-reason")
      .setValue("Protocol eligibility verified for randomization");
    await wrapper.find("#btn-randomize-participant").trigger("click");
    await settleFocus();
    const sigModal = wrapper.findComponent({ name: "SignatureCaptureModal" });
    const signPromise = (
      sigModal.props("onSign") as (
        sigToken: string,
        signingReason: string
      ) => Promise<unknown>
    )("sig-rand-1", "Protocol eligibility verified for randomization");
    await wrapper.setProps({
      participant: {
        id: "subj-1",
        subject_id: "017-0009",
        study_id: "CAD-101",
        site_id: "017",
        status: "RANDOMIZED",
        active_protocol_version: "1.0",
        enrollment_index: 9,
      },
    });
    await signPromise;
    await settleFocus();
    expect(document.activeElement?.id).toBe("state-change-target");

    // 3. Emergency unblind disclosure open, cancel, and confirm
    const unblindToggle = wrapper.find("#btn-toggle-emergency-unblind");
    expect(unblindToggle.exists()).toBe(true);
    (unblindToggle.element as HTMLElement).focus();
    await unblindToggle.trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("emergency-unblind-reason");

    await wrapper.find("#btn-toggle-emergency-unblind").trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("btn-toggle-emergency-unblind");

    await wrapper.find("#btn-toggle-emergency-unblind").trigger("click");
    await settleFocus();
    const unblindJustification =
      "Suspected acute anaphylactic reaction requiring immediate clinical treatment allocation disclosure";
    await wrapper
      .find("#emergency-unblind-reason")
      .setValue(unblindJustification);
    await wrapper.find("#btn-confirm-emergency-unblind").trigger("click");
    await settleFocus();
    const unblindSigModal = wrapper.findComponent({
      name: "SignatureCaptureModal",
    });
    const submitUnblindPromise = (
      unblindSigModal.props("onSign") as (
        sigToken: string,
        signingReason: string
      ) => Promise<unknown>
    )("sig-unblind-1", unblindJustification);
    await wrapper.setProps({
      participant: {
        id: "subj-1",
        subject_id: "017-0009",
        study_id: "CAD-101",
        site_id: "017",
        status: "UNBLINDED",
        active_protocol_version: "1.0",
        enrollment_index: 9,
      },
    });
    await submitUnblindPromise;
    await settleFocus();
    expect(
      ["state-change-final", "inspector-state-change-title"].includes(
        document.activeElement?.id || ""
      )
    ).toBe(true);

    // 4. Completing WITHDRAWN unmounts the state-change form and focuses #state-change-final or #btn-toggle-emergency-unblind
    await wrapper.setProps({
      participant: {
        id: "subj-1",
        subject_id: "017-0009",
        study_id: "CAD-101",
        site_id: "017",
        status: "RANDOMIZED",
        active_protocol_version: "1.0",
        enrollment_index: 9,
      },
    });
    await settleFocus();
    const withdrawJustification =
      "Participant formally withdrew informed consent at Visit 3 following investigator discussion";
    await wrapper.find("#state-change-target").setValue("WITHDRAWN");
    await wrapper.find("#state-change-reason").setValue(withdrawJustification);
    const changeBtn = wrapper.find("#btn-change-state");
    (changeBtn.element as HTMLElement).focus();
    await changeBtn.trigger("click");
    await settleFocus();
    const withdrawSigModal = wrapper.findComponent({
      name: "SignatureCaptureModal",
    });
    const withdrawPromise = (
      withdrawSigModal.props("onSign") as (
        sigToken: string,
        signingReason: string
      ) => Promise<unknown>
    )("sig-withdraw-1", withdrawJustification);
    await wrapper.setProps({
      participant: {
        id: "subj-1",
        subject_id: "017-0009",
        study_id: "CAD-101",
        site_id: "017",
        status: "WITHDRAWN",
        active_protocol_version: "1.0",
        enrollment_index: 9,
      },
    });
    await withdrawPromise;
    await settleFocus();
    expect(
      [
        "state-change-final",
        "btn-toggle-emergency-unblind",
        "inspector-state-change-title",
      ].includes(document.activeElement?.id || "")
    ).toBe(true);
  });

  it("ProtocolTrainingConsole hands off focus to .reader-title when Next Section unmounts or Previous Section disables", async () => {
    const auth = useAuthStore();
    auth.isAuthenticated = true;
    auth.user = {
      id: "u-crc-1",
      username: "crc_01",
      email: "crc@cadence.clinical",
      firstName: "Jordan",
      lastName: "CRC",
      tenantId: "tenant-1",
    };
    auth.rawRoles = ["CRC"];

    const curricula = [
      {
        id: "curr-1",
        study_id: "CAD-101",
        protocol_version: "2.0",
        title: "Protocol v2.0 Amendment Training",
        description: "Updated inclusion criteria and dose reduction schedule.",
        is_active: true,
        modules: [
          {
            id: "mod-1",
            title: "Section 1: Eligibility Updates",
            content_html: "<p>Updated inclusion criteria.</p>",
          },
          {
            id: "mod-2",
            title: "Section 2: Dosing Modifications",
            content_html: "<p>Updated dose reduction schedule.</p>",
          },
        ],
      },
    ];
    vi.spyOn(trainingApi, "listAssignments").mockResolvedValue([]);
    vi.spyOn(trainingApi, "listCurricula").mockResolvedValue(
      curricula as never
    );
    vi.spyOn(trainingApi, "getCurriculum").mockImplementation(
      async (id) =>
        (curricula.find((c) => c.id === id) || curricula[0]) as never
    );
    vi.spyOn(trainingApi, "listDelegations").mockResolvedValue([]);

    const wrapper = track(
      mount(ProtocolTrainingConsole, {
        attachTo: document.body,
        global: { plugins: [pinia] },
        props: { studyId: "CAD-101", protocolVersion: "2.0" },
      })
    );
    await settleFocus();

    const nextBtn = wrapper.find("#btn-next-training-module");
    expect(nextBtn.exists()).toBe(true);
    (nextBtn.element as HTMLElement).focus();
    await nextBtn.trigger("click");
    await settleFocus();

    expect(wrapper.find("#btn-next-training-module").exists()).toBe(false);
    expect(document.activeElement).toBe(wrapper.find(".reader-title").element);

    const prevBtn = wrapper.find("#btn-prev-training-module");
    expect(prevBtn.exists()).toBe(true);
    (prevBtn.element as HTMLElement).focus();
    await prevBtn.trigger("click");
    await settleFocus();

    expect(prevBtn.attributes("disabled")).toBeDefined();
    expect(document.activeElement).toBe(wrapper.find(".reader-title").element);
  });

  it("SsuGreenlightConsole moves focus on batch banner dismiss and restores row trigger across matrix/single view swaps", async () => {
    const auth = useAuthStore();
    auth.isAuthenticated = true;
    auth.rawRoles = ["Sponsor Admin", "Data Manager"];

    const matrixRows = [
      {
        site_id: "SITE-101",
        site_name: "Boston Medical Center",
        country_code: "US",
        is_compliant: true,
        readiness_percentage: 100,
        overall_status: "READY",
        facet_regulatory_irb: true,
        facet_cta_budget: true,
        facet_eisf_baseline: true,
        facet_doa_log: true,
        facet_staff_training: true,
        facet_ip_release: true,
        unfulfilled_reasons: [],
      },
    ];
    vi.mocked(ctmsApi.getCountryMatrix).mockResolvedValue(matrixRows as never);
    vi.mocked(ctmsApi.evaluateSiteGreenlight).mockResolvedValue({
      study_id: "PROTOCOL-2026-01",
      site_id: "SITE-101",
      country_code: "US",
      overall_status: "READY",
      facet_regulatory_irb: true,
      facet_cta_budget: true,
      facet_eisf_baseline: true,
      facet_doa_log: true,
      facet_staff_training: true,
      facet_ip_release: true,
    } as never);
    vi.mocked(ctmsApi.batchCertifyCountrySites).mockResolvedValue({
      study_id: "PROTOCOL-2026-01",
      country_code: "US",
      certified_count: 1,
      certified_sites: [
        {
          site_id: "SITE-101",
          site_name: "Boston Medical Center",
          greenlight_token: "GL-TOK-101-ABCDEF123456",
        },
      ],
    } as never);

    const wrapper = track(
      mount(SsuGreenlightConsole, {
        attachTo: document.body,
        global: { plugins: [pinia] },
        props: {
          studyId: "PROTOCOL-2026-01",
          siteId: "SITE-101",
          countryCode: "US",
          initialViewMode: "matrix",
        },
      })
    );
    await settleFocus();
    await wrapper
      .find('button[title="Refresh"], .header-actions .btn-secondary')
      .trigger("click");
    await settleFocus();

    // Select compliant site and run batch certification to render .batch-success-banner
    await wrapper.find(".site-select-checkbox").setValue(true);
    await wrapper.find(".btn-batch-certify").trigger("click");
    await settleFocus();
    await wrapper.find(".btn-submit-batch-certify").trigger("click");
    await settleFocus();

    const dismissBannerBtn = wrapper.find(
      ".batch-success-banner button, .batch-success-banner .btn-icon-only"
    );
    expect(dismissBannerBtn.exists()).toBe(true);
    (dismissBannerBtn.element as HTMLElement).focus();
    await dismissBannerBtn.trigger("click");
    await settleFocus();
    expect(document.activeElement).toBe(
      wrapper.find(".matrix-view-container .console-title").element
    );

    // Inspect Site -> focuses .single-view-container .console-title
    const inspectBtn = wrapper.find("#btn-inspect-site-SITE-101");
    expect(inspectBtn.exists()).toBe(true);
    (inspectBtn.element as HTMLElement).focus();
    await inspectBtn.trigger("click");
    await settleFocus();
    expect(document.activeElement).toBe(
      wrapper.find(".single-view-container .console-title").element
    );

    // Back to Country Matrix -> restores #btn-inspect-site-SITE-101
    const backBtn = wrapper
      .findAll(".single-view-container button")
      .find((b) => b.text().includes("Back to Country Matrix"));
    expect(backBtn).toBeDefined();
    (backBtn!.element as HTMLElement).focus();
    await backBtn!.trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("btn-inspect-site-SITE-101");
  });

  it("TicketDetailDrawer keeps focus strictly inside drawerRef across workflow transitions and .resolve-panel disclosures", async () => {
    vi.stubEnv("VITE_DEMO_MODE", "false");
    const auth = useAuthStore();
    auth.isAuthenticated = true;
    auth.rawRoles = ["Data Manager"];

    const bgMain = document.createElement("main");
    bgMain.id = "main-content";
    bgMain.tabIndex = -1;
    document.body.appendChild(bgMain);

    const ticket = reactive({
      id: "tkt-focus-1",
      reference: "TKT-101",
      title: "Cold-chain excursion query",
      description: "Verify excursion log.",
      category: "SUPPLY",
      gxp_severity: "MAJOR",
      priority: "HIGH",
      status: "OPEN",
      study_id: "CAD-101",
      site_id: "SITE-101",
      created_by: "cra_01",
      created_at: "2026-11-20T09:00:00Z",
      signature_token: null as string | null,
    });

    const wrapper = track(
      mount(TicketDetailDrawer, {
        attachTo: document.body,
        global: { plugins: [pinia] },
        props: {
          isOpen: true,
          ticket,
          comments: [],
          attachments: [],
          auditLogs: [],
          onTransition: (payload: { newStatus: string }) => {
            ticket.status = payload.newStatus;
          },
        } as never,
      })
    );
    await settleFocus();

    const drawerEl = wrapper.find(".drawer-panel").element as HTMLElement;

    // 1. Start Work (OPEN -> IN_PROGRESS)
    const startWorkBtn = wrapper
      .findAll(".action-btn-group button")
      .find((b) => b.text().includes("Start Work"));
    expect(startWorkBtn).toBeDefined();
    (startWorkBtn!.element as HTMLElement).focus();
    await startWorkBtn!.trigger("click");
    await settleFocus();
    expect(drawerEl.contains(document.activeElement)).toBe(true);
    expect(document.activeElement).not.toBe(bgMain);

    // 2. Pause (IN_PROGRESS -> WAITING_ON_SITE)
    const pauseBtn = wrapper
      .findAll(".action-btn-group button")
      .find((b) => b.text().includes("Pause (Wait Site)"));
    (pauseBtn!.element as HTMLElement).focus();
    await pauseBtn!.trigger("click");
    await settleFocus();
    expect(drawerEl.contains(document.activeElement)).toBe(true);
    expect(document.activeElement?.textContent).toContain("Resume Work");

    // 3. Resume Work (WAITING_ON_SITE -> IN_PROGRESS)
    const resumeBtn = wrapper
      .findAll(".action-btn-group button")
      .find((b) => b.text().includes("Resume Work"));
    (resumeBtn!.element as HTMLElement).focus();
    await resumeBtn!.trigger("click");
    await settleFocus();
    expect(drawerEl.contains(document.activeElement)).toBe(true);

    // 4. Open .resolve-panel -> focuses #resolve-rca-category
    const openResolveBtn = wrapper.find("#btn-open-resolve");
    (openResolveBtn.element as HTMLElement).focus();
    await openResolveBtn.trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("resolve-rca-category");
    expect(drawerEl.contains(document.activeElement)).toBe(true);

    // 5. Cancel .resolve-panel -> restores #btn-open-resolve
    const cancelResolveBtn = wrapper
      .findAll(".resolve-panel button")
      .find((b) => b.text().trim() === "Cancel");
    await cancelResolveBtn!.trigger("click");
    await settleFocus();
    expect(document.activeElement?.id).toBe("btn-open-resolve");

    // 6. Reopen and Confirm Resolution -> transitions to RESOLVED, focuses Final Close
    await wrapper.find("#btn-open-resolve").trigger("click");
    await settleFocus();
    await wrapper.find("#btn-confirm-resolve").trigger("click");
    await settleFocus();
    expect(ticket.status).toBe("RESOLVED");
    expect(drawerEl.contains(document.activeElement)).toBe(true);
    expect(document.activeElement?.textContent).toContain("Final Close");

    // 7. Final Close (RESOLVED -> CLOSED) -> stays inside drawerRef (#btn-open-part11-sign or #drawer-ticket-title), never #main-content
    const finalCloseBtn = wrapper
      .findAll(".action-btn-group button")
      .find((b) => b.text().includes("Final Close"));
    (finalCloseBtn!.element as HTMLElement).focus();
    await finalCloseBtn!.trigger("click");
    await settleFocus();
    expect(ticket.status).toBe("CLOSED");
    expect(drawerEl.contains(document.activeElement)).toBe(true);
    expect(document.activeElement).not.toBe(bgMain);
    expect(
      ["btn-open-part11-sign", "drawer-ticket-title"].includes(
        document.activeElement?.id || ""
      )
    ).toBe(true);
  });

  it("OnboardingTour focuses .onboarding-popover-card on anchored step 2 and restores .btn-start-tour or #btn-onboarding-checklist-trigger on Escape", async () => {
    const startTourBtn = document.createElement("button");
    startTourBtn.type = "button";
    startTourBtn.className = "btn btn-primary btn-start-tour";
    startTourBtn.textContent = "Start Guided Tour";
    document.body.appendChild(startTourBtn);

    const checklistTriggerBtn = document.createElement("button");
    checklistTriggerBtn.type = "button";
    checklistTriggerBtn.id = "btn-onboarding-checklist-trigger";
    checklistTriggerBtn.textContent = "Setup Checklist";
    document.body.appendChild(checklistTriggerBtn);

    const drawerStartBtn = document.createElement("button");
    drawerStartBtn.type = "button";
    drawerStartBtn.id = "btn-drawer-start-tour";
    drawerStartBtn.textContent = "Launch Tour from Drawer";
    document.body.appendChild(drawerStartBtn);

    const navAnchor = document.createElement("a");
    navAnchor.id = "nav-studies";
    navAnchor.href = "#studies";
    navAnchor.textContent = "Studies";
    navAnchor.getBoundingClientRect = () =>
      ({
        top: 64,
        bottom: 96,
        left: 120,
        right: 220,
        width: 100,
        height: 32,
        x: 120,
        y: 64,
        toJSON: () => ({}),
      }) as DOMRect;
    document.body.appendChild(navAnchor);

    const router = makeTestRouter("/studies/CAD-101");
    await router.isReady();

    const onboardingStore = useOnboardingStore();
    onboardingStore.disabled = false;
    onboardingStore.isActive = false;

    const wrapper = track(
      mount(OnboardingTour, {
        attachTo: document.body,
        global: { plugins: [pinia, router] },
      })
    );
    await settleFocus();

    // Scenario A: Launch from .btn-start-tour -> advance to Step 2 -> Escape restores .btn-start-tour
    startTourBtn.focus();
    onboardingStore.startTour();
    await settleFocus();

    await wrapper.find(".btn-tour-next").trigger("click");
    await settleFocus();

    const popoverEl = wrapper.find(".onboarding-popover-card")
      .element as HTMLElement;
    expect(onboardingStore.currentStep).toBe(2);
    expect(navAnchor.hasAttribute("inert")).toBe(false);
    expect(document.activeElement).toBe(popoverEl);

    document.dispatchEvent(
      new KeyboardEvent("keydown", { key: "Escape", bubbles: true })
    );
    await settleFocus();
    expect(onboardingStore.isActive).toBe(false);
    expect(document.activeElement).toBe(startTourBtn);

    // Scenario B: Launch from #btn-drawer-start-tour while checklist drawer is open -> Step 2 -> Escape restores #btn-onboarding-checklist-trigger
    onboardingStore.openDrawer();
    drawerStartBtn.focus();
    onboardingStore.startTour();
    onboardingStore.closeDrawer();
    drawerStartBtn.style.display = "none";
    await settleFocus();

    await wrapper.find(".btn-tour-next").trigger("click");
    await settleFocus();
    expect(
      wrapper.find(".onboarding-popover-card").element ===
        document.activeElement
    ).toBe(true);

    document.dispatchEvent(
      new KeyboardEvent("keydown", { key: "Escape", bubbles: true })
    );
    await settleFocus();
    expect(onboardingStore.isActive).toBe(false);
    expect(document.activeElement).toBe(checklistTriggerBtn);

    // Scenario C: Finishing the tour via .btn-tour-complete restores the launch trigger
    drawerStartBtn.style.display = "";
    startTourBtn.focus();
    onboardingStore.startTour();
    onboardingStore.currentStep = onboardingStore.totalSteps;
    await settleFocus();

    const completeBtn = wrapper.find(".btn-tour-complete");
    expect(completeBtn.exists()).toBe(true);
    (completeBtn.element as HTMLElement).focus();
    await completeBtn.trigger("click");
    await settleFocus();
    expect(onboardingStore.isActive).toBe(false);
    expect(document.activeElement).toBe(startTourBtn);
  });
});
