import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { nextTick } from "vue";
import { mount, flushPromises, type VueWrapper } from "@vue/test-utils";
import { createPinia, setActivePinia, type Pinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import { ClinicalDropzone } from "ui";
import ContextualHelpDrawer from "../src/components/ContextualHelpDrawer.vue";
import BatchUploadDrawer from "../src/components/etmf/BatchUploadDrawer.vue";
import DocumentManagerView from "../src/views/DocumentManagerView.vue";
import DocumentGrid from "../src/components/etmf/DocumentGrid.vue";
import LocksSection from "../src/components/canvas/LocksSection.vue";
import QueriesSection from "../src/components/canvas/QueriesSection.vue";
import QueriesPanel from "../src/components/canvas/QueriesPanel.vue";
import KernelActPanel from "../src/components/canvas/KernelActPanel.vue";
import KernelLineagePanel from "../src/components/canvas/KernelLineagePanel.vue";
import ProtocolVersionsSection from "../src/components/canvas/ProtocolVersionsSection.vue";
import SitesSection from "../src/components/canvas/SitesSection.vue";
import StudySitesPanel from "../src/components/canvas/StudySitesPanel.vue";
import { useAuthStore } from "../src/stores/auth";
import { useKnowledgeStore } from "../src/stores/knowledge";
import { useEtmfStore } from "../src/stores/etmf";
import { etmfService } from "../src/api/etmf";
import {
  ecrfQueriesApi,
  type FormQueryResponse,
} from "../src/api/ecrfQueriesApi";
import { kernelApi, type KernelStudySiteSummary } from "../src/api/kernelApi";
import {
  kernelGovernanceApi,
  type KernelArtifact,
} from "../src/api/kernelGovernanceApi";
import { clearKernelStudyCache } from "../src/canvas/kernelStudy";
import { signInAs } from "./helpers/signIn.js";

interface MockDrawerTicket {
  id: string;
  reference: string;
  title: string;
  status?: string;
  priority?: string;
  category?: string;
  created_at?: string;
}

interface KnowledgeStoreTestable {
  isOpen: boolean;
  currentRoute: string;
  activeDrawerTab: string;
  loading: boolean;
  error: string | null;
  lastFailedAction: { type: string; route: string } | null;
  primaryArticle: Record<string, unknown> | null;
  hasLoadedContextualHelp: boolean;
  hasLoadedDrawerTickets: boolean;
  ticketsError: string | null;
  drawerTickets: MockDrawerTicket[];
  selectedTicket: MockDrawerTicket | null;
  retryLastAction: () => Promise<void>;
  fetchDrawerTickets: (
    filtersOverride?: unknown
  ) => Promise<MockDrawerTicket[]>;
  selectDrawerTicket: (
    ticket: MockDrawerTicket
  ) => Promise<MockDrawerTicket | null>;
}

vi.mock("../src/components/SignatureCaptureModal.vue", () => ({
  default: {
    name: "SignatureCaptureModal",
    props: [
      "isOpen",
      "username",
      "signerName",
      "signerRole",
      "reasons",
      "actionUrl",
      "semanticAction",
      "onSign",
    ],
    emits: ["cancel", "success"],
    template: `
      <div v-if="isOpen" data-testid="mock-sig-modal">
        <button type="button" data-testid="confirm-step-up" @click="confirm">Confirm signature</button>
      </div>
    `,
    methods: {
      async confirm(this: {
        onSign: (tok: string, reason: string) => Promise<unknown>;
        $emit: (e: string) => void;
      }) {
        await this.onSign("sig-tok-verified", "Approved");
        this.$emit("success");
      },
    },
  },
}));

async function settleFocus() {
  await flushPromises();
  await nextTick();
  await new Promise((resolve) => setTimeout(resolve, 0));
  await flushPromises();
  await nextTick();
}

describe("Focus-Trap Detachment Recovery & Inline Focus Handoffs (ADR-2675)", () => {
  let pinia: Pinia;
  const mountedWrappers: VueWrapper<unknown>[] = [];

  function track<T extends VueWrapper<unknown>>(wrapper: T): T {
    mountedWrappers.push(wrapper);
    return wrapper;
  }

  function unmountTracked(wrapper: VueWrapper<unknown>) {
    const idx = mountedWrappers.indexOf(wrapper);
    if (idx !== -1) {
      mountedWrappers.splice(idx, 1);
    }
    wrapper.unmount();
  }

  function makeRouter(initialPath = "/studies/CAD-101") {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
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
          path: "/knowledge",
          name: "knowledge",
          component: { template: "<div />" },
        },
        {
          path: "/tickets",
          name: "tickets",
          component: { template: "<div />" },
        },
        {
          path: "/etmf",
          name: "etmf",
          component: { template: "<div />" },
        },
      ],
    });
    void router.push(initialPath);
    return router;
  }

  beforeEach(() => {
    vi.stubEnv("VITE_DEMO_MODE", "true");
    clearKernelStudyCache();
    pinia = createPinia();
    setActivePinia(pinia);
    signInAs(
      useAuthStore(),
      [
        "Sponsor Admin",
        "Sponsor Designer",
        "CRA",
        "CRC",
        "TMF_ARCHIVIST",
        "Data Manager",
      ],
      {
        username: "sponsor_lead",
      }
    );
  });

  afterEach(() => {
    while (mountedWrappers.length > 0) {
      mountedWrappers.pop()?.unmount();
    }
    document.body.innerHTML = "";
    vi.restoreAllMocks();
    vi.unstubAllEnvs();
  });

  it("hands focus to .clinical-dropzone-browse-btn when ClinicalDropzone removes a file", async () => {
    const wrapper = track(
      mount(ClinicalDropzone, {
        attachTo: document.body,
        props: {
          hasSelection: true,
          removable: true,
          buttonLabel: "Select protocol PDF",
          removeLabel: "Remove selected PDF",
        },
      })
    );
    await settleFocus();

    const removeBtn = wrapper.find(".clinical-dropzone-remove-btn")
      .element as HTMLButtonElement;
    removeBtn.focus();
    expect(document.activeElement).toBe(removeBtn);

    await wrapper.find(".clinical-dropzone-remove-btn").trigger("click");
    await wrapper.setProps({ hasSelection: false });
    await settleFocus();

    const browseBtn = wrapper.find(".clinical-dropzone-browse-btn")
      .element as HTMLButtonElement;
    expect(document.activeElement).toBe(browseBtn);
    expect(document.activeElement).not.toBe(document.body);
  });

  it("keeps focus inside #contextual-help-drawer across ticket list/detail swaps and SOP/ticket retries", async () => {
    const router = makeRouter("/ecrf");
    await router.isReady();

    const knowledgeStore =
      useKnowledgeStore() as unknown as KnowledgeStoreTestable;
    knowledgeStore.isOpen = true;
    knowledgeStore.currentRoute = "/ecrf";
    knowledgeStore.activeDrawerTab = "guidance";

    const wrapper = track(
      mount(ContextualHelpDrawer, {
        attachTo: document.body,
        global: { plugins: [pinia, router] },
      })
    );
    await settleFocus();

    knowledgeStore.loading = false;
    knowledgeStore.error = "Gateway timeout resolving contextual SOP";
    knowledgeStore.lastFailedAction = { type: "contextual", route: "/ecrf" };
    await settleFocus();

    vi.spyOn(knowledgeStore, "retryLastAction")
      .mockImplementationOnce(async () => {
        knowledgeStore.error = "Still offline";
      })
      .mockImplementationOnce(async () => {
        knowledgeStore.error = null;
        knowledgeStore.hasLoadedContextualHelp = true;
        knowledgeStore.primaryArticle = {
          id: "art-1",
          title: "SOP-ECRF-201: Subject Enrollment",
          slug: "sop-ecrf-201",
          status: "PUBLISHED",
          version_index: 1,
          version_label: "1.0",
          body_html: "<h2>Purpose</h2><p>Guidance text.</p>",
        };
      });

    const drawerEl = wrapper.find("#contextual-help-drawer")
      .element as HTMLElement;

    // 1. Failed SOP retry refocuses #btn-contextual-help-retry
    const sopRetryBtn = wrapper.find("#btn-contextual-help-retry")
      .element as HTMLButtonElement;
    sopRetryBtn.focus();
    await wrapper.find("#btn-contextual-help-retry").trigger("click");
    await settleFocus();
    expect(document.activeElement).toBe(
      wrapper.find("#btn-contextual-help-retry").element
    );

    // 2. Successful SOP retry focuses recovered content inside #contextual-help-drawer
    await wrapper.find("#btn-contextual-help-retry").trigger("click");
    await settleFocus();
    expect(document.activeElement).not.toBe(document.body);
    expect(drawerEl.contains(document.activeElement)).toBe(true);

    // 3. Switch to tickets tab in error state -> retry error -> retry success -> select ticket -> back to list
    knowledgeStore.activeDrawerTab = "tickets";
    knowledgeStore.hasLoadedDrawerTickets = true;
    knowledgeStore.ticketsError = "Ticket service unreachable";
    knowledgeStore.drawerTickets = [];

    const mockedTickets: MockDrawerTicket[] = [
      {
        id: "tkt-101",
        reference: "TKT-101",
        title: "Visit window query",
        status: "OPEN",
        priority: "HIGH",
        category: "CLINICAL_DATA",
        created_at: "2026-11-20T10:00:00Z",
      },
      {
        id: "tkt-102",
        reference: "TKT-102",
        title: "Lab range clarification",
        status: "IN_PROGRESS",
        priority: "MEDIUM",
        category: "PROTOCOL",
        created_at: "2026-11-21T10:00:00Z",
      },
    ];

    vi.spyOn(knowledgeStore, "fetchDrawerTickets")
      .mockImplementationOnce(async () => {
        knowledgeStore.ticketsError = "Ticket service still unreachable";
        return [];
      })
      .mockImplementationOnce(async () => {
        knowledgeStore.ticketsError = null;
        knowledgeStore.drawerTickets = mockedTickets;
        return mockedTickets;
      });
    vi.spyOn(knowledgeStore, "selectDrawerTicket").mockImplementation(
      async (ticket: MockDrawerTicket) => {
        knowledgeStore.selectedTicket = { ...ticket };
        return knowledgeStore.selectedTicket;
      }
    );

    await settleFocus();

    const retryTicketsBtn = wrapper.find("#btn-retry-drawer-tickets")
      .element as HTMLButtonElement;
    retryTicketsBtn.focus();
    await wrapper.find("#btn-retry-drawer-tickets").trigger("click");
    await settleFocus();
    expect(document.activeElement).toBe(
      wrapper.find("#btn-retry-drawer-tickets").element
    );

    await wrapper.find("#btn-retry-drawer-tickets").trigger("click");
    await settleFocus();
    expect(document.activeElement).not.toBe(document.body);
    expect(drawerEl.contains(document.activeElement)).toBe(true);

    // Select second ticket card -> focus moves to #btn-back-to-ticket-list
    const ticketCards = wrapper.findAll(".drawer-ticket-card");
    expect(ticketCards.length).toBe(2);
    const secondCardEl = ticketCards[1].element as HTMLButtonElement;
    secondCardEl.focus();
    await ticketCards[1].trigger("click");
    await settleFocus();

    const backBtn = wrapper.find("#btn-back-to-ticket-list")
      .element as HTMLButtonElement;
    expect(document.activeElement).toBe(backBtn);

    // Click #btn-back-to-ticket-list -> focus returns to matching TKT-102 card
    await wrapper.find("#btn-back-to-ticket-list").trigger("click");
    await settleFocus();
    const restoredSecondCard = wrapper.find(
      '.drawer-ticket-card[data-ticket-ref="TKT-102"]'
    ).element as HTMLButtonElement;
    expect(document.activeElement).toBe(restoredSecondCard);
    expect(drawerEl.contains(document.activeElement)).toBe(true);
  });

  it("hands focus to adjacent .btn-remove-item or .clinical-dropzone-browse-btn in BatchUploadDrawer", async () => {
    const wrapper = track(
      mount(BatchUploadDrawer, {
        attachTo: document.body,
        props: {
          isOpen: true,
          studyId: "STUDY-USDM-001",
        },
        global: { plugins: [pinia] },
      })
    );
    await settleFocus();

    const files = [
      new File(["a"], "doc_a.pdf", { type: "application/pdf" }),
      new File(["b"], "doc_b.pdf", { type: "application/pdf" }),
      new File(["c"], "doc_c.pdf", { type: "application/pdf" }),
    ];
    const input = wrapper.find('input[type="file"]');
    Object.defineProperty(input.element, "files", {
      value: files,
      configurable: true,
    });
    await input.trigger("change");
    await settleFocus();

    let removeButtons = wrapper.findAll(".btn-remove-item");
    expect(removeButtons.length).toBe(3);

    // Removing first item focuses the next remaining .btn-remove-item at index 0
    (removeButtons[0].element as HTMLButtonElement).focus();
    await removeButtons[0].trigger("click");
    await settleFocus();

    removeButtons = wrapper.findAll(".btn-remove-item");
    expect(removeButtons.length).toBe(2);
    expect(document.activeElement).toBe(removeButtons[0].element);

    // Clearing the queue focuses .clinical-dropzone-browse-btn
    const clearBtnWrapper = wrapper
      .findAll(".queue-section button")
      .find((b) => b.text().includes("Clear Queue"));
    expect(clearBtnWrapper).toBeDefined();
    (clearBtnWrapper!.element as HTMLButtonElement).focus();
    await clearBtnWrapper!.trigger("click");
    await settleFocus();

    expect(wrapper.findAll(".btn-remove-item").length).toBe(0);
    expect(document.activeElement).toBe(
      wrapper.find(".clinical-dropzone-browse-btn").element
    );
  });

  it("hands focus from #btn-submit-technical-qc to #qc-accept-reason and from #btn-accept-ai-classification to .btn-preview-from-drawer in DocumentManagerView", async () => {
    const store = useEtmfStore(pinia);
    const draftDoc = {
      id: "DOC-DRAFT-1",
      study_id: "STUDY-USDM-001",
      site_id: "SITE-101",
      zone: 1,
      section: "01.01",
      artifact_code: "01.01.01",
      artifact_type: "Clinical Trial Protocol",
      filename: "protocol_draft.pdf",
      mime_type: "application/pdf",
      created_at: "2026-11-01T12:00:00Z",
      created_by: "sponsor_lead",
      version_index: 1,
      status: "DRAFT",
      reason_for_change: "Initial upload",
    };

    vi.spyOn(etmfService, "getDocuments")
      .mockResolvedValueOnce([draftDoc])
      .mockResolvedValueOnce([{ ...draftDoc, status: "APPROVED" }]);
    vi.spyOn(etmfService, "getQcQueue").mockResolvedValue([]);
    vi.spyOn(store, "submitDraftForQc").mockResolvedValue({
      ...draftDoc,
      status: "TECHNICAL_QC",
    });
    vi.spyOn(etmfService, "submitQcReview").mockResolvedValue({
      id: draftDoc.id,
      status: "APPROVED",
    } as never);

    const router = makeRouter("/etmf");
    await router.isReady();
    const wrapper = track(
      mount(DocumentManagerView, {
        attachTo: document.body,
        props: { studyId: "STUDY-USDM-001" },
        global: { plugins: [pinia, router] },
      })
    );
    await store.fetchDocuments("01.01.01");
    await settleFocus();

    const grid = wrapper.findComponent(DocumentGrid);
    await grid.vm.$emit("inspect", store.documentsList[0]);
    await settleFocus();

    // 1. DRAFT -> TECHNICAL_QC focuses #qc-accept-reason
    await wrapper
      .find("#draft-submit-qc-reason")
      .setValue("Ready for technical QC verification");
    const submitDraftBtn = wrapper.find("#btn-submit-technical-qc")
      .element as HTMLButtonElement;
    submitDraftBtn.focus();
    await wrapper.find("#btn-submit-technical-qc").trigger("click");
    await settleFocus();

    const qcAcceptInput = wrapper.find("#qc-accept-reason")
      .element as HTMLInputElement;
    expect(document.activeElement).toBe(qcAcceptInput);
    expect(document.activeElement).not.toBe(document.body);

    // 2. TECHNICAL_QC -> APPROVED focuses .btn-preview-from-drawer
    await wrapper
      .find("#qc-accept-reason")
      .setValue("Verified taxonomy and investigator signature");
    const acceptBtn = wrapper.find("#btn-accept-ai-classification")
      .element as HTMLButtonElement;
    acceptBtn.focus();
    await wrapper.find("#btn-accept-ai-classification").trigger("click");
    await settleFocus();

    const previewBtn = wrapper.find(".btn-preview-from-drawer")
      .element as HTMLButtonElement;
    expect(document.activeElement).toBe(previewBtn);
    expect(document.activeElement).not.toBe(document.body);
  });

  it("recovers focus across LocksSection #btn-retry-study-locks and #btn-retry-locks-list retries", async () => {
    const studyId = "01960000-0000-7000-8000-000000000001";
    const studyRef = `study:${studyId}`;
    const router = makeRouter("/studies/CAD-101");
    await router.isReady();

    vi.spyOn(kernelApi, "canI").mockResolvedValue({
      capability: "kernel.lock.apply",
      verb_class: "command",
      resource: studyRef,
      permitted: true,
      reason: null,
      explanation: {
        subject: null,
        claim: "permitted",
        producer: "test",
        steps: [],
        conditions: [],
        next_actions: [],
      },
    });
    const searchSpy = vi
      .spyOn(kernelApi, "search")
      .mockRejectedValueOnce(new Error("Study lookup failed"))
      .mockRejectedValueOnce(new Error("Study lookup failed again"))
      .mockResolvedValue([
        {
          ref: studyRef,
          kind: "study",
          title: "Cadence Trial",
          code: "CAD-101",
          detail: null,
          exact_code: true,
          rank: 1,
        },
      ]);
    const listLocksSpy = vi
      .spyOn(kernelApi, "listStudyLocks")
      .mockRejectedValueOnce(new Error("Locks query failed"))
      .mockRejectedValueOnce(new Error("Locks query failed again"))
      .mockResolvedValue({
        study_ref: studyRef,
        study_code: "CAD-101",
        valid_at: "2026-11-24T00:00:00Z",
        known_at: "2026-11-24T00:00:00Z",
        locks: [],
        hidden: {
          more_exist: false,
          reasons: [],
        },
      });
    vi.spyOn(kernelApi, "getChildren").mockResolvedValue([]);

    const wrapper = track(
      mount(LocksSection, {
        attachTo: document.body,
        props: { studyCode: "CAD-101" },
        global: { plugins: [pinia, router] },
      })
    );
    await settleFocus();

    // 1. Study lookup error -> retry fails -> stays on #btn-retry-study-locks
    const retryStudyBtn = wrapper.find("#btn-retry-study-locks")
      .element as HTMLButtonElement;
    retryStudyBtn.focus();
    await wrapper.find("#btn-retry-study-locks").trigger("click");
    await settleFocus();
    expect(searchSpy).toHaveBeenCalledTimes(2);
    expect(document.activeElement).toBe(
      wrapper.find("#btn-retry-study-locks").element
    );

    // 2. Study lookup succeeds, locks list fails -> focus lands inside LocksSection (not BODY), then retry locks list
    clearKernelStudyCache();
    await wrapper.find("#btn-retry-study-locks").trigger("click");
    await settleFocus();
    expect(document.activeElement).not.toBe(document.body);

    const retryLocksListBtn = wrapper.find("#btn-retry-locks-list")
      .element as HTMLButtonElement;
    retryLocksListBtn.focus();
    await wrapper.find("#btn-retry-locks-list").trigger("click");
    await settleFocus();
    expect(listLocksSpy).toHaveBeenCalledTimes(2);
    expect(document.activeElement).toBe(
      wrapper.find("#btn-retry-locks-list").element
    );

    await wrapper.find("#btn-retry-locks-list").trigger("click");
    await settleFocus();
    expect(document.activeElement).not.toBe(document.body);
    expect(
      wrapper
        .find("#study-locks-section")
        .element.contains(document.activeElement)
    ).toBe(true);
  });

  it("hands focus across ProtocolVersionsSection submitForReview, returnToDraft, handleApprovalSigned, and transitionEffectivity swaps", async () => {
    const studyId = "01960000-0000-7000-8000-000000000099";
    const studyRef = `study:${studyId}`;
    const versionRef =
      "protocol_version:01960000-0000-7000-8000-000000000101@1";
    const effId = "01960000-0000-7000-8000-000000000201";
    const effRef = `protocol_effectivity:${effId}`;
    const artVerRef = "artifact_version:01960000-0000-7000-8000-000000000301";

    let currentStatus: "Draft" | "InReview" | "Approved" = "Draft";
    let currentEffStatus: "Pending" | "Effective" = "Pending";

    const buildVersion = () => ({
      ref: versionRef,
      study: studyRef,
      number: 1,
      status: currentStatus,
      parent_version: null,
      amendment_intent: null,
      content_hash: "sha256:abc123",
      approval_attestation: null,
      withdrawal_reason: null,
      mask: null,
    });

    vi.spyOn(kernelApi, "search").mockResolvedValue([
      {
        ref: studyRef,
        kind: "study",
        title: "Cadence Trial",
        code: "CAD-101",
        detail: null,
        exact_code: true,
        rank: 1,
      },
    ]);
    vi.spyOn(kernelApi, "listProtocolVersions").mockImplementation(async () => [
      buildVersion(),
    ]);
    vi.spyOn(kernelApi, "listProtocolTemplates").mockResolvedValue([]);
    const mockArtifact = {
      artifact_ref: "artifact:01960000-0000-7000-8000-000000000300",
      artifact_id: "01960000-0000-7000-8000-000000000300",
      artifact_version_ref: artVerRef,
      artifact_version_id: "01960000-0000-7000-8000-000000000301",
      version_number: 1,
      kind: "EthicsApproval",
      study_ref: studyRef,
      title: "Central IRB Approval",
      content_hash: "sha256:irb1",
      size_bytes: 1024,
      media_type: "application/pdf",
      storage_ref: "urn:cadence:artifact:irb1",
      recorded_at: "2026-11-20T00:00:00Z",
    } as unknown as KernelArtifact;
    vi.spyOn(kernelGovernanceApi, "listStudyArtifacts").mockResolvedValue([
      mockArtifact,
    ]);
    vi.spyOn(kernelGovernanceApi, "listStudyEffectivities").mockImplementation(
      async () =>
        currentStatus === "Approved"
          ? [
              {
                effectivity_id: effId,
                effectivity_ref: effRef,
                study_ref: studyRef,
                protocol_version_ref: versionRef,
                protocol_version_number: 1,
                scope_level: "Study",
                status: currentEffStatus,
                status_since: "2026-11-20T00:00:00Z",
                country: null,
                study_site_ref: null,
                evidence_artifact_version_refs: [artVerRef],
              },
            ]
          : []
    );
    vi.spyOn(kernelGovernanceApi, "listStudySites").mockResolvedValue([]);
    vi.spyOn(
      kernelGovernanceApi,
      "submitProtocolVersionForReview"
    ).mockImplementation(async () => {
      currentStatus = "InReview";
      return buildVersion() as never;
    });
    vi.spyOn(
      kernelGovernanceApi,
      "returnProtocolVersionToDraft"
    ).mockImplementation(async () => {
      currentStatus = "Draft";
      return buildVersion() as never;
    });
    vi.spyOn(kernelGovernanceApi, "approveProtocolVersion").mockImplementation(
      async () => {
        currentStatus = "Approved";
        return buildVersion() as never;
      }
    );
    vi.spyOn(kernelGovernanceApi, "changeEffectivityStatus").mockImplementation(
      async () => {
        currentEffStatus = "Effective";
        return { status: "Effective" } as never;
      }
    );

    const router = makeRouter("/studies/CAD-101");
    await router.isReady();

    const wrapper = track(
      mount(ProtocolVersionsSection, {
        attachTo: document.body,
        props: {
          studyCode: "CAD-101",
          selectedRef: versionRef,
        },
        global: { plugins: [pinia, router] },
      })
    );
    await settleFocus();

    // 1. Draft -> InReview via #btn-submit-protocol-review
    await wrapper
      .find("#protocol-governance-reason")
      .setValue("Freeze draft v1 for sponsor QA review");
    const submitBtn = wrapper.find("#btn-submit-protocol-review")
      .element as HTMLButtonElement;
    submitBtn.focus();
    await wrapper.find("#btn-submit-protocol-review").trigger("click");
    await settleFocus();

    expect(document.activeElement).toBe(
      wrapper.find("#btn-approve-protocol-version").element
    );

    // 2. InReview -> Draft via #btn-return-protocol-draft
    await wrapper
      .find("#protocol-governance-reason")
      .setValue("Update visit window tolerance before sign-off");
    const returnBtn = wrapper.find("#btn-return-protocol-draft")
      .element as HTMLButtonElement;
    returnBtn.focus();
    await wrapper.find("#btn-return-protocol-draft").trigger("click");
    await settleFocus();

    expect(document.activeElement).toBe(
      wrapper.find("#protocol-governance-reason").element
    );

    // 3. Submit again -> Sign & Approve -> focuses effectivity / governance target
    await wrapper
      .find("#protocol-governance-reason")
      .setValue("Re-submit protocol v1 after tolerance update");
    await wrapper.find("#btn-submit-protocol-review").trigger("click");
    await settleFocus();

    await wrapper.find("#btn-approve-protocol-version").trigger("click");
    await settleFocus();
    await wrapper.find('[data-testid="confirm-step-up"]').trigger("click");
    await settleFocus();

    expect(document.activeElement).not.toBe(document.body);
    expect(
      wrapper
        .find("#protocol-versions-section")
        .element.contains(document.activeElement)
    ).toBe(true);

    // Mount Approved version with active Pending effectivity and transition Pending -> Effective
    unmountTracked(wrapper);

    const approvedWrapper = track(
      mount(ProtocolVersionsSection, {
        attachTo: document.body,
        props: {
          studyCode: "CAD-101",
          selectedRef: versionRef,
        },
        global: { plugins: [pinia, router] },
      })
    );
    await settleFocus();

    const effReasonSelector = `#effectivity-reason-${effId}`;
    await approvedWrapper
      .find(effReasonSelector)
      .setValue("Activate study-wide following IRB approval");
    const transitionBtn = approvedWrapper.find(
      `#btn-transition-effectivity-${effId}`
    ).element as HTMLButtonElement;
    transitionBtn.focus();
    await approvedWrapper
      .find(`#btn-transition-effectivity-${effId}`)
      .trigger("click");
    await settleFocus();

    expect(document.activeElement).not.toBe(document.body);
    expect(
      approvedWrapper
        .find("#protocol-versions-section")
        .element.contains(document.activeElement)
    ).toBe(true);
  });

  it("hands focus when linking the last unlinked site in SitesSection and StudySitesPanel", async () => {
    const studyRef = "study:01960000-0000-7000-8000-000000000501";
    const siteRef = "site:01960000-0000-7000-8000-000000000601";
    const studySiteRef = "study_site:01960000-0000-7000-8000-000000000701";

    let linkedInSitesSection = false;
    vi.spyOn(kernelApi, "search").mockResolvedValue([
      {
        ref: studyRef,
        kind: "study",
        title: "Cadence Trial",
        code: "CAD-101",
        detail: null,
        exact_code: true,
        rank: 1,
      },
    ]);
    vi.spyOn(kernelApi, "listOrganizations").mockResolvedValue([
      {
        ref: "organization:01960000-0000-7000-8000-000000000801",
        code: "ORG-1",
        name: "Meridian Health",
        kind: "SiteInstitution",
      },
    ]);
    vi.spyOn(kernelApi, "listSites").mockResolvedValue([
      {
        ref: siteRef,
        code: "SITE-101",
        name: "Boston Clinical Center",
        country: "US",
        institution_ref: "organization:01960000-0000-7000-8000-000000000801",
        institution_code: "ORG-1",
        institution_name: "Meridian Health",
        is_active: true,
        linked_study_count: 0,
      },
    ]);
    const linkedSummary = {
      ref: studySiteRef,
      study_site_id: "01960000-0000-7000-8000-000000000701",
      study_site_ref: studySiteRef,
      study_ref: studyRef,
      site_ref: siteRef,
      site_code: "SITE-101",
      site_name: "Boston Clinical Center",
      country: "US",
      institution_ref: "organization:01960000-0000-7000-8000-000000000801",
      institution_code: "ORG-1",
      institution_name: "Meridian Health",
      status: "Selected",
      is_active: true,
      mask: null,
    } as unknown as KernelStudySiteSummary;
    vi.spyOn(kernelApi, "listStudySites").mockImplementation(async () =>
      linkedInSitesSection ? [linkedSummary] : []
    );
    vi.spyOn(kernelApi, "linkSiteToStudy").mockImplementation(async () => {
      linkedInSitesSection = true;
      return { ref: studySiteRef, site_code: "SITE-101" } as never;
    });

    const router = makeRouter("/studies/CAD-101");
    await router.isReady();

    // Part A: SitesSection
    const sitesWrapper = track(
      mount(SitesSection, {
        attachTo: document.body,
        props: { studyCode: "CAD-101" },
        global: { plugins: [pinia, router] },
      })
    );
    await settleFocus();

    expect(sitesWrapper.find("#link-existing-site-section").exists()).toBe(
      true
    );
    await sitesWrapper
      .find("#link-site-reason")
      .setValue("Link Boston site for study startup");
    const linkBtn = sitesWrapper.find("#btn-link-existing-site")
      .element as HTMLButtonElement;
    linkBtn.focus();
    await sitesWrapper.find("#btn-link-existing-site").trigger("click");
    await settleFocus();

    expect(sitesWrapper.find("#link-existing-site-section").exists()).toBe(
      false
    );
    expect(document.activeElement).not.toBe(document.body);
    expect(
      sitesWrapper
        .find("#sites-section")
        .element.contains(document.activeElement)
    ).toBe(true);
    unmountTracked(sitesWrapper);

    // Part B: StudySitesPanel
    linkedInSitesSection = false;
    clearKernelStudyCache();
    const panelWrapper = track(
      mount(StudySitesPanel, {
        attachTo: document.body,
        props: {
          studyCode: "CAD-101",
          canManageSites: true,
        },
        global: { plugins: [pinia, router] },
      })
    );
    await settleFocus();

    expect(panelWrapper.find("#link-existing-site-form").exists()).toBe(true);
    await panelWrapper
      .find("#link-site-reason")
      .setValue("Link Boston site from overview panel");
    const panelLinkBtn = panelWrapper.find("#btn-link-study-site")
      .element as HTMLButtonElement;
    panelLinkBtn.focus();
    await panelWrapper.find("#link-existing-site-form").trigger("submit");
    await settleFocus();

    expect(panelWrapper.find("#link-existing-site-form").exists()).toBe(false);
    expect(document.activeElement).not.toBe(document.body);
    expect(
      panelWrapper
        .find("#study-sites-panel")
        .element.contains(document.activeElement)
    ).toBe(true);
  });

  it("hands focus across QueriesSection, QueriesPanel, KernelActPanel, and KernelLineagePanel CanvasErrorAlert retries", async () => {
    const studyId = "01960000-0000-7000-8000-000000000901";
    const studyRef = `study:${studyId}`;
    const enrollmentId = "01960000-0000-7000-8000-000000000902";
    const router = makeRouter("/studies/CAD-101");
    await router.isReady();

    vi.spyOn(kernelApi, "search").mockResolvedValue([
      {
        ref: studyRef,
        kind: "study",
        title: "Cadence Trial",
        code: "CAD-101",
        detail: null,
        exact_code: true,
        rank: 1,
      },
    ]);

    const sampleQuery: FormQueryResponse = {
      query_id: "01960000-0000-7000-8000-000000000903",
      study_id: studyId,
      site_code: "SITE-101",
      subject_code: "SUBJ-001",
      enrollment_id: enrollmentId,
      form_id: "01960000-0000-7000-8000-000000000904",
      item_id: "SYSBP",
      observation_id: "01960000-0000-7000-8000-000000000905",
      revision: 1,
      current_revision: 1,
      value_changed: false,
      status: "open",
      origin: "manual",
      priority: "high",
      owner_role: "site",
      text: "Confirm systolic BP reading",
      messages: [],
      raised_by: "u:cra_lead",
      raised_at: "2026-11-24T00:00:00Z",
      version: 1,
    };

    vi.spyOn(ecrfQueriesApi, "listForStudy")
      .mockRejectedValueOnce(new Error("Execution queries unavailable"))
      .mockRejectedValueOnce(new Error("Execution queries still unavailable"))
      .mockResolvedValueOnce([sampleQuery]);

    // 1. QueriesSection: repeated failure -> retry button; recovery -> query row control inside #study-queries-section
    const queriesWrapper = track(
      mount(QueriesSection, {
        attachTo: document.body,
        props: { studyCode: "CAD-101", canManage: true, canAnswer: true },
        global: { plugins: [pinia, router] },
      })
    );
    await settleFocus();

    const retryQueriesBtn = queriesWrapper.find(
      '[data-testid="canvas-error-retry"]'
    ).element as HTMLButtonElement;
    retryQueriesBtn.focus();
    await queriesWrapper
      .find('[data-testid="canvas-error-retry"]')
      .trigger("click");
    await settleFocus();
    expect(document.activeElement).toBe(
      queriesWrapper.find('[data-testid="canvas-error-retry"]').element
    );

    await queriesWrapper
      .find('[data-testid="canvas-error-retry"]')
      .trigger("click");
    await settleFocus();
    expect(document.activeElement).not.toBe(document.body);
    expect(
      queriesWrapper
        .find("#study-queries-section")
        .element.contains(document.activeElement)
    ).toBe(true);
    unmountTracked(queriesWrapper);

    // 2. QueriesPanel: repeated failure -> retry button; empty recovery -> #record-queries-title (tabindex="-1")
    vi.spyOn(ecrfQueriesApi, "listForEnrollment")
      .mockRejectedValueOnce(new Error("Enrollment queries offline"))
      .mockRejectedValueOnce(new Error("Enrollment queries still offline"))
      .mockResolvedValueOnce([]);

    const panelWrapper = track(
      mount(QueriesPanel, {
        attachTo: document.body,
        props: {
          studyCode: "CAD-101",
          subjectCode: "SUBJ-001",
          enrollmentId,
          canManage: true,
        },
        global: { plugins: [pinia, router] },
      })
    );
    await settleFocus();

    (
      panelWrapper.find('[data-testid="canvas-error-retry"]')
        .element as HTMLButtonElement
    ).focus();
    await panelWrapper
      .find('[data-testid="canvas-error-retry"]')
      .trigger("click");
    await settleFocus();
    expect(document.activeElement).toBe(
      panelWrapper.find('[data-testid="canvas-error-retry"]').element
    );

    await panelWrapper
      .find('[data-testid="canvas-error-retry"]')
      .trigger("click");
    await settleFocus();
    expect(document.activeElement).toBe(
      panelWrapper.find("#record-queries-title").element
    );
    unmountTracked(panelWrapper);

    // 3. KernelActPanel & KernelLineagePanel empty recovery -> tabindex="-1" panel headings
    vi.spyOn(kernelApi, "getCommands")
      .mockRejectedValueOnce(new Error("Kernel commands unavailable"))
      .mockResolvedValueOnce([]);
    const actWrapper = track(
      mount(KernelActPanel, {
        attachTo: document.body,
        props: { nodeRef: studyRef },
        global: { plugins: [pinia, router] },
      })
    );
    await settleFocus();
    (
      actWrapper.find('[data-testid="canvas-error-retry"]')
        .element as HTMLButtonElement
    ).focus();
    await actWrapper
      .find('[data-testid="canvas-error-retry"]')
      .trigger("click");
    await settleFocus();
    expect(document.activeElement).toBe(
      actWrapper.find("#kernel-act-title").element
    );
    unmountTracked(actWrapper);

    vi.spyOn(kernelApi, "trace")
      .mockRejectedValueOnce(new Error("Lineage unavailable"))
      .mockResolvedValueOnce({
        target: studyRef,
        direction: "upstream",
        depth: 1,
        truncated: false,
        nodes: [],
        edges: [],
      } as never);
    const lineageWrapper = track(
      mount(KernelLineagePanel, {
        attachTo: document.body,
        props: {
          nodeRef: studyRef,
          mode: "trace",
        },
        global: { plugins: [pinia, router] },
      })
    );
    await settleFocus();
    (
      lineageWrapper.find('[data-testid="canvas-error-retry"]')
        .element as HTMLButtonElement
    ).focus();
    await lineageWrapper
      .find('[data-testid="canvas-error-retry"]')
      .trigger("click");
    await settleFocus();
    expect(document.activeElement).toBe(
      lineageWrapper.find("#kernel-trace-title").element
    );
  });
});
