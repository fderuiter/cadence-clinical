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
import { useStudyCanvasStore } from "../src/stores/studyCanvas";
import { useAuthStore } from "../src/stores/auth";
import { kernelApi } from "../src/api/kernelApi";
import { ecrfFormsApi } from "../src/api/ecrfFormsApi";
import { ecrfQueriesApi } from "../src/api/ecrfQueriesApi";
import { executionApi } from "../src/api/executionApi";
import { ticketsApi } from "../src/api/ticketsApi";

vi.mock("../src/auth/keycloak.js", () => ({
  getKeycloakClient: () => null,
}));

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
    expect(kanbanBoard.attributes("role")).toBe("region");
    expect(kanbanBoard.attributes("aria-label")).toBeTruthy();

    const columnCards = ticketsWrapper.find(".column-cards");
    expect(columnCards.attributes("tabindex")).toBe("0");
    expect(columnCards.attributes("role")).toBe("region");

    const firstTicketCard = ticketsWrapper.find(".ticket-card");
    expect(firstTicketCard.exists()).toBe(true);
    expect(firstTicketCard.attributes("role")).toBe("button");
    expect(firstTicketCard.attributes("tabindex")).toBe("0");

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
    expect(cards[0].attributes("role")).toBe("button");
    expect(cards[0].attributes("tabindex")).toBe("0");

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
});
