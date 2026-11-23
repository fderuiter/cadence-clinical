import { flushPromises, mount, DOMWrapper } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import axe from "axe-core";
import { registerDraftSource } from "ui";
import App from "../../src/App.vue";
import StudyCanvasView from "../../src/views/StudyCanvasView.vue";
import { useAuthStore } from "../../src/stores/auth";

const STUDY_A_ID = "01960000-0000-7000-8000-000000000101";
const STUDY_A_REF = `study:${STUDY_A_ID}`;
const STUDY_B_ID = "01960000-0000-7000-8000-000000000202";
const STUDY_B_REF = `study:${STUDY_B_ID}`;
const PROPOSAL_REF = "proposal:01960000-0000-7000-8000-000000000301";
const BLOCKER_FINDING_REF =
  "validation_finding:01960000-0000-7000-8000-000000000401";

const mockListStudies = vi.fn();
const mockListOrganizations = vi.fn();
const mockListWorkQueues = vi.fn();
const mockCanI = vi.fn();
const mockSearch = vi.fn();
const mockGetNode = vi.fn();
const mockGetChildren = vi.fn();
const mockGetZoomPath = vi.fn();
const mockListProposals = vi.fn();
const mockListProtocolVersions = vi.fn();
const mockListStudyLocks = vi.fn();
const mockActivity = vi.fn();

vi.mock("../../src/api/kernelApi", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("../../src/api/kernelApi")>();
  return {
    ...actual,
    kernelApi: {
      ...actual.kernelApi,
      listStudies: (...args: unknown[]) => mockListStudies(...args),
      listOrganizations: (...args: unknown[]) => mockListOrganizations(...args),
      listWorkQueues: (...args: unknown[]) => mockListWorkQueues(...args),
      canI: (...args: unknown[]) => mockCanI(...args),
      search: (...args: unknown[]) => mockSearch(...args),
      getNode: (...args: unknown[]) => mockGetNode(...args),
      getChildren: (...args: unknown[]) => mockGetChildren(...args),
      getZoomPath: (...args: unknown[]) => mockGetZoomPath(...args),
      listProposals: (...args: unknown[]) => mockListProposals(...args),
      listProtocolVersions: (...args: unknown[]) =>
        mockListProtocolVersions(...args),
      listStudyLocks: (...args: unknown[]) => mockListStudyLocks(...args),
      activity: (...args: unknown[]) => mockActivity(...args),
    },
  };
});

vi.mock("../../src/api/executionApi", () => ({
  executionApi: {
    listSubjects: vi.fn(async () => ({
      items: [],
      total: 0,
      limit: 50,
      offset: 0,
    })),
    getSubjectCounts: vi.fn(async () => ({ total: 0, counts: {} })),
    getSubjectDetail: vi.fn(),
    getSiteComplianceStatus: vi.fn(),
    activateSite: vi.fn(),
  },
}));

async function expectNoAxeViolations(element: Element) {
  const results = await axe.run(element, {
    rules: {
      region: { enabled: false },
    },
  });
  expect(results.violations).toEqual([]);
}

describe("Cross-Study Home, EntityLink contextual preview, Study Home work queues, and unsaved-draft guards", () => {
  let pinia: ReturnType<typeof createPinia>;
  let router: ReturnType<typeof createRouter>;
  let wrapper: ReturnType<typeof mount> | undefined;
  const cleanupFns: Array<() => void> = [];

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: "/studies/:studyCode?",
          name: "study",
          component: StudyCanvasView,
          meta: { layout: "canvas" },
        },
        {
          path: "/tickets",
          name: "tickets",
          component: { template: "<div id='tickets-stub'>Support</div>" },
        },
        {
          path: "/knowledge",
          name: "knowledge",
          component: { template: "<div id='knowledge-stub'>Knowledge</div>" },
        },
        {
          path: "/plans",
          name: "plans",
          component: { template: "<div id='plans-stub'>Plans</div>" },
        },
      ],
    });

    vi.clearAllMocks();
    mockListOrganizations.mockResolvedValue([]);
    mockGetChildren.mockResolvedValue([]);
    mockGetZoomPath.mockResolvedValue([]);
    mockListProposals.mockResolvedValue([]);
    mockListProtocolVersions.mockResolvedValue([]);
    mockListStudyLocks.mockResolvedValue({ locks: [] });
    mockActivity.mockResolvedValue({ entries: [], next: null });

    mockListStudies.mockResolvedValue([
      {
        ref: STUDY_A_REF,
        code: "CAD-101",
        title: "Cardiovascular Outcomes Phase 3",
        phase: "Phase 3",
        sponsor_organization_ref:
          "organization:01960000-0000-7000-8000-000000000900",
        sponsor_code: "CADENCE",
        sponsor_name: "Cadence Pharma",
        site_count: 4,
      },
      {
        ref: STUDY_B_REF,
        code: "ONC-204",
        title: "Precision Oncology Basket Trial",
        phase: "Phase 2",
        sponsor_organization_ref:
          "organization:01960000-0000-7000-8000-000000000900",
        sponsor_code: "CADENCE",
        sponsor_name: "Cadence Pharma",
        site_count: 2,
      },
    ]);

    mockSearch.mockImplementation(async (q?: { text?: string }) => {
      if (q?.text === "CAD-101") {
        return [
          {
            ref: STUDY_A_REF,
            kind: "study",
            code: "CAD-101",
            title: "Cardiovascular Outcomes Phase 3",
            exact_code: true,
          },
        ];
      }
      return [];
    });

    mockGetNode.mockImplementation(async (ref: string) => {
      if (ref === PROPOSAL_REF) {
        return {
          ref: PROPOSAL_REF,
          kind: "proposal",
          code: "PROP-301",
          title: "Review out-of-window visit query draft",
          status: "Open",
          mask: null,
        };
      }
      if (ref === BLOCKER_FINDING_REF) {
        return {
          ref: BLOCKER_FINDING_REF,
          kind: "validation_finding",
          code: "VS_SYSBP_RANGE",
          title: "Systolic BP exceeds upper limit",
          status: "Open",
          mask: null,
        };
      }
      throw new Error(`Unknown node ${ref}`);
    });
  });

  afterEach(() => {
    while (cleanupFns.length > 0) {
      cleanupFns.pop()?.();
    }
    wrapper?.unmount();
    wrapper = undefined;
  });

  async function mountWorkspace(initialUrl: string) {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Admin"];

    await router.push(initialUrl);
    await router.isReady();
    wrapper = mount(App, {
      global: { plugins: [pinia, router] },
      attachTo: document.body,
    });
    await flushPromises();
    return wrapper;
  }

  it("renders Cross-Study Home on /studies with My Action Items (within omitted), portfolio cards, contextual EntityLink drawer preview, and canonical Open full view navigation", async () => {
    mockListWorkQueues.mockResolvedValue({
      queues: [
        {
          definition:
            "work_queue_definition:01960000-0000-7000-8000-000000000302@1",
          code: "ai_proposals",
          version: 1,
          title: "Proposals awaiting approval",
          capability: "proposal.approve",
          truncated: false,
          items: [
            {
              subject: PROPOSAL_REF,
              subject_kind: "proposal",
              title: "Review out-of-window visit query draft",
              since: "2026-06-01T09:50:00Z",
              masked: null,
              target: {
                study_ref: STUDY_A_REF,
                study_id: STUDY_A_ID,
                enrollment_id: null,
                form_id: null,
                observation_ref: null,
                finding_ref: null,
                protocol_version_ref: null,
                proposal_ref: PROPOSAL_REF,
              },
            },
          ],
        },
      ],
    });

    const app = await mountWorkspace("/studies");
    const body = new DOMWrapper(document.body);

    expect(app.find("#canvas-study-title").text()).toBe("Studies");
    expect(mockListWorkQueues).toHaveBeenCalledWith();
    expect(mockListStudies).toHaveBeenCalledTimes(1);

    // Portfolio cards rendered from kernelApi.listStudies()
    expect(app.find("#study-item-CAD-101").text()).toContain(
      "Cardiovascular Outcomes Phase 3"
    );
    expect(app.find("#study-item-ONC-204").text()).toContain(
      "Precision Oncology Basket Trial"
    );

    // My Action Items renders plain-English capability and resolves study_ref -> CAD-101
    const actionItems = app.find("[data-testid='my-action-items']");
    expect(actionItems.exists()).toBe(true);
    expect(actionItems.text()).toContain("My Action Items");
    expect(actionItems.text()).toContain(
      "Review and adjudicate proposed changes"
    );
    expect(actionItems.text()).not.toContain("proposal.approve");

    const itemTrigger = app.find(
      "[data-testid='work-item-link-ai_proposals-0']"
    );
    expect(itemTrigger.exists()).toBe(true);
    expect(
      body.find("[data-testid='entity-link-preview-drawer']").exists()
    ).toBe(false);

    // Clicking EntityLink opens contextual ClinicalDrawer preview first instead of losing page context
    await itemTrigger.trigger("click");
    await flushPromises();

    expect(router.currentRoute.value.fullPath).toBe("/studies");
    expect(
      body.find("[data-testid='entity-link-preview-drawer']").exists()
    ).toBe(true);

    const fullViewLink = body.find(
      "[data-testid='entity-link-open-full-view']"
    );
    expect(fullViewLink.exists()).toBe(true);
    expect(fullViewLink.text()).toContain("Open full view");

    await expectNoAxeViolations(app.element);

    // Clicking Open full view navigates to the canonical Clinical Canvas proposal route for CAD-101
    await fullViewLink.trigger("click");
    await flushPromises();

    expect(router.currentRoute.value.path).toBe("/studies/CAD-101");
    expect(router.currentRoute.value.query).toMatchObject({
      proposal: PROPOSAL_REF,
    });
  });

  it("renders study-scoped WorkQueuesPanel above StudyPulse on /studies/:studyCode with plain-English capability Explanation links to blocking NodeRef entities", async () => {
    mockListWorkQueues.mockResolvedValue({
      queues: [
        {
          definition:
            "work_queue_definition:01960000-0000-7000-8000-000000000302@1",
          code: "ai_proposals",
          version: 1,
          title: "Proposals awaiting approval",
          capability: "proposal.approve",
          truncated: false,
          items: [
            {
              subject: PROPOSAL_REF,
              subject_kind: "proposal",
              title: "Review out-of-window visit query draft",
              since: "2026-06-01T09:50:00Z",
              masked: null,
              target: {
                study_ref: STUDY_A_REF,
                study_id: STUDY_A_ID,
                enrollment_id: null,
                form_id: null,
                observation_ref: null,
                finding_ref: null,
                protocol_version_ref: null,
                proposal_ref: PROPOSAL_REF,
              },
            },
          ],
        },
      ],
    });

    mockCanI.mockImplementation(
      async (capability: string, resource: string) => {
        if (capability === "proposal.approve" && resource === PROPOSAL_REF) {
          return {
            capability,
            verb_class: "Approve",
            resource,
            permitted: false,
            reason: "Open validation finding blocks proposal approval.",
            explanation: {
              claim:
                "Approval waits until blocking validation findings are resolved.",
              steps: [
                {
                  claim:
                    "Checked open validation findings before permitting proposal sign-off.",
                  evidence: [BLOCKER_FINDING_REF],
                },
              ],
              conditions: [
                {
                  code: "finding.resolved",
                  met: false,
                  description:
                    "Resolve the open systolic blood pressure finding first.",
                  evidence: [BLOCKER_FINDING_REF],
                },
              ],
              next_actions: [
                {
                  description: "Inspect the blocking validation finding.",
                  target: BLOCKER_FINDING_REF,
                },
              ],
            },
          };
        }
        return { permitted: false };
      }
    );

    const app = await mountWorkspace("/studies/CAD-101?site=SITE-017");
    const body = new DOMWrapper(document.body);

    expect(mockListWorkQueues).toHaveBeenCalledWith({ within: STUDY_A_REF });

    const queuesSection = app.find("#study-work-queues");
    const pulseSection = app.find("#study-pulse");
    expect(queuesSection.exists()).toBe(true);
    expect(pulseSection.exists()).toBe(true);

    // WorkQueuesPanel must appear BEFORE StudyPulse in DOM order
    const orderMask = queuesSection.element.compareDocumentPosition(
      pulseSection.element
    );
    expect(Boolean(orderMask & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true);

    // Disclose plain-English capability Explanation with blocking NodeRef EntityLink chips
    const explainBtn = app.find(
      "[data-testid='work-queue-explain-btn-ai_proposals']"
    );
    expect(explainBtn.exists()).toBe(true);
    await explainBtn.trigger("click");
    await flushPromises();

    const explanationRegion = app.find(
      "[data-testid='work-queue-explanation-ai_proposals']"
    );
    expect(explanationRegion.exists()).toBe(true);
    expect(explanationRegion.text()).toContain(
      "Approval waits until blocking validation findings are resolved."
    );
    expect(explanationRegion.text()).toContain(
      "Checked open validation findings before permitting proposal sign-off."
    );
    expect(explanationRegion.text()).toContain(
      "Resolve the open systolic blood pressure finding first."
    );
    expect(explanationRegion.text()).toContain(
      "Inspect the blocking validation finding."
    );
    expect(explanationRegion.text()).toContain(
      "Systolic BP exceeds upper limit"
    );
    // Never leak raw UUIDs or internal capability dot-codes
    expect(explanationRegion.text()).not.toContain(
      "01960000-0000-7000-8000-000000000401"
    );

    await expectNoAxeViolations(app.element);

    // Opening full view from study work item preserves active site=SITE-017 scope
    await app
      .find("[data-testid='work-item-link-ai_proposals-0']")
      .trigger("click");
    await flushPromises();
    await body
      .find("[data-testid='entity-link-open-full-view']")
      .trigger("click");
    await flushPromises();

    expect(router.currentRoute.value.path).toBe("/studies/CAD-101");
    expect(router.currentRoute.value.query).toMatchObject({
      proposal: PROPOSAL_REF,
      site: "SITE-017",
    });
  });

  it("guards StudyCanvasView route transitions when unsaved clinical drafts are registered", async () => {
    mockListWorkQueues.mockResolvedValue({ queues: [] });

    const app = await mountWorkspace("/studies/CAD-101");
    const body = new DOMWrapper(document.body);

    let dirty = true;
    const unregister = registerDraftSource({
      hasDrafts: () => dirty,
      flush: () => undefined,
    });
    cleanupFns.push(unregister);

    // Attempt to leave /studies/CAD-101 -> /tickets while draft is dirty; cancel confirmation modal
    const blockedNav = router.push("/tickets");
    await vi.dynamicImportSettled();
    await flushPromises();

    const discardCancelBtn = body
      .findAll("button")
      .find((btn) => btn.text().includes("Stay on Form"));
    expect(discardCancelBtn?.exists()).toBe(true);
    await discardCancelBtn!.trigger("click");
    await blockedNav;
    await flushPromises();

    expect(router.currentRoute.value.fullPath).toBe("/studies/CAD-101");
    expect(app.find("#canvas-study-title").text()).toBe("CAD-101");

    // Attempt navigation again and confirm discard
    const allowedNav = router.push("/tickets");
    await vi.dynamicImportSettled();
    await flushPromises();

    const discardConfirmBtn = body
      .findAll("button")
      .find((btn) => btn.text().includes("Leave Without Saving"));
    expect(discardConfirmBtn?.exists()).toBe(true);
    await discardConfirmBtn!.trigger("click");
    await allowedNav;
    await flushPromises();

    dirty = false;
    expect(router.currentRoute.value.fullPath).toBe("/tickets");
  });
});
