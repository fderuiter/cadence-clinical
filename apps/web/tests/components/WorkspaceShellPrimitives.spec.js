import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import { h } from "vue";
import AppShell from "../../src/components/AppShell.vue";
import StudyCanvasView from "../../src/views/StudyCanvasView.vue";
import DocumentManagerView from "../../src/views/DocumentManagerView.vue";
import DocumentsHubView from "../../src/views/DocumentsHubView.vue";
import KnowledgeView from "../../src/views/KnowledgeView.vue";
import { useAuthStore } from "../../src/stores/auth";
import { useEtmfStore } from "../../src/stores/etmf";
import { useFileshareStore } from "../../src/stores/fileshare";

vi.mock("../../src/api/kernelApi", async (importOriginal) => {
  const actual = await importOriginal();
  return {
    ...actual,
    kernelApi: {
      listProposals: vi.fn(async () => []),
      search: vi.fn(async () => []),
      lookupEnrollment: vi.fn(),
      getChildren: vi.fn(async () => []),
      listProtocolVersions: vi.fn(async () => []),
      getScheduleOfActivities: vi.fn(),
      listStudyLocks: vi.fn(async () => ({ locks: [] })),
      canI: vi.fn(async () => ({ permitted: false })),
      activity: vi.fn(async () => ({ entries: [], next: null })),
      getCommands: vi.fn(),
      getNode: vi.fn(async () => ({
        ref: { kind: "study", id: "0192f3c4-5e6a-7b8c-9d0e-000000000101" },
        label: "CADENCE-101",
      })),
      getObservation: vi.fn(),
      getZoomPath: vi.fn(async () => []),
    },
  };
});

vi.mock("../../src/api/ecrfFormsApi", async (importOriginal) => {
  const actual = await importOriginal();
  return {
    ...actual,
    ecrfFormsApi: {
      listForEnrollment: vi.fn(async () => []),
      openForm: vi.fn(),
      getForm: vi.fn(),
      saveValues: vi.fn(),
      signForm: vi.fn(),
    },
  };
});

vi.mock("../../src/api/ecrfQueriesApi", async (importOriginal) => {
  const actual = await importOriginal();
  return {
    ...actual,
    ecrfQueriesApi: {
      raise: vi.fn(),
      listForForm: vi.fn(async () => []),
      listForEnrollment: vi.fn(async () => []),
      transition: vi.fn(),
    },
  };
});

vi.mock("../../src/api/executionApi", () => ({
  executionApi: {
    listSubjects: vi.fn(async () => ({
      items: [
        {
          id: "0192f3c4-5e6a-7b8c-9d0e-000000000001",
          subject_id: "017-0001",
          study_id: "CADENCE-101",
          site_id: "017",
          status: "ACTIVE",
          active_protocol_version: "3.0",
          enrollment_index: 1,
        },
      ],
      total: 1,
      limit: 50,
      offset: 0,
    })),
    getSubjectCounts: vi.fn(async () => ({
      total: 1,
      counts: { ACTIVE: 1 },
    })),
    getSubjectDetail: vi.fn(async () => ({
      id: "0192f3c4-5e6a-7b8c-9d0e-000000000001",
      subject_id: "017-0001",
      study_id: "CADENCE-101",
      site_id: "017",
      status: "ACTIVE",
      active_protocol_version: "3.0",
      enrollment_index: 1,
    })),
    getSiteComplianceStatus: vi.fn(async () => []),
    activateSite: vi.fn(),
  },
}));

vi.mock("../../src/api/knowledge.js", async (importOriginal) => {
  const actual = await importOriginal();
  return {
    ...actual,
    knowledgeService: {
      ...actual.knowledgeService,
      listCategories: vi.fn(async () => [
        {
          id: "cat-sop",
          name: "Standard Operating Procedures",
          parent_id: null,
        },
      ]),
      listArticles: vi.fn(async () => [
        {
          id: "art-1",
          title: "Investigator Site File Verification SOP",
          slug: "isf-verification-sop",
          category_id: "cat-sop",
          status: "PUBLISHED",
          summary: "Verification guidance",
          body_markdown: "# Verification SOP",
          version_label: "1.0",
          tags: ["sop"],
          updated_at: "2026-10-01T00:00:00Z",
        },
      ]),
      getArticle: vi.fn(async () => ({
        id: "art-1",
        title: "Investigator Site File Verification SOP",
        slug: "isf-verification-sop",
        category_id: "cat-sop",
        status: "PUBLISHED",
        summary: "Verification guidance",
        body_markdown: "# Verification SOP",
        version_label: "1.0",
        tags: ["sop"],
        versions: [],
        updated_at: "2026-10-01T00:00:00Z",
      })),
      getVersions: vi.fn(async () => []),
      getAuditLog: vi.fn(async () => []),
    },
  };
});

function setViewportWidth(width) {
  Object.defineProperty(window, "innerWidth", {
    configurable: true,
    writable: true,
    value: width,
  });
  window.matchMedia = vi.fn().mockImplementation((query) => {
    const minMatch = query.match(/\(min-width:\s*(\d+)px\)/);
    const maxMatch = query.match(/\(max-width:\s*(\d+)px\)/);
    let matches = false;
    if (minMatch) matches = width >= Number(minMatch[1]);
    else if (maxMatch) matches = width <= Number(maxMatch[1]);
    return {
      matches,
      media: query,
      onchange: null,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    };
  });
}

function buildTestRouter() {
  const Dummy = { template: "<div>Dummy</div>" };
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: "/", name: "home", component: Dummy },
      {
        path: "/studies/:studyCode?",
        name: "study",
        component: StudyCanvasView,
      },
      { path: "/etmf", name: "etmf", component: DocumentManagerView },
      { path: "/documents", name: "documents", component: DocumentsHubView },
      { path: "/knowledge", name: "knowledge", component: KnowledgeView },
      { path: "/tickets", name: "tickets", component: Dummy },
      { path: "/plans", name: "plans", component: Dummy },
      { path: "/ecrf", name: "ecrf", component: Dummy },
      { path: "/ctms", name: "ctms", component: Dummy },
      { path: "/ssu", name: "ssu", component: Dummy },
    ],
  });
}

describe("Shared Workspace Shell Primitives & Three-Tier Responsive Panes", () => {
  let originalInnerWidth;
  let originalMatchMedia;
  let pinia;

  beforeEach(() => {
    originalInnerWidth = window.innerWidth;
    originalMatchMedia = window.matchMedia;
    vi.stubEnv("VITE_DEMO_MODE", "true");
    pinia = createPinia();
    setActivePinia(pinia);
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Admin"];
  });

  afterEach(() => {
    vi.unstubAllEnvs();
    Object.defineProperty(window, "innerWidth", {
      configurable: true,
      writable: true,
      value: originalInnerWidth,
    });
    window.matchMedia = originalMatchMedia;
  });

  it("consolidates AppShell and StudyCanvasView into one sticky header bar via #shell-study-context", async () => {
    setViewportWidth(1440);
    const router = buildTestRouter();
    await router.push("/studies/CADENCE-101?section=participants");
    await router.isReady();

    const Host = {
      render() {
        return h(AppShell, null, {
          default: () => h(StudyCanvasView),
        });
      },
    };

    const wrapper = mount(Host, {
      attachTo: document.body,
      global: { plugins: [pinia, router] },
    });
    await flushPromises();

    // Standalone .topbar is suppressed inside AppShell; zoom path + AI toggle mount in #shell-study-context
    expect(wrapper.find(".study-canvas .topbar").exists()).toBe(false);
    const shellContext = wrapper.find("#shell-study-context");
    expect(shellContext.exists()).toBe(true);
    expect(shellContext.find("#canvas-zoom-path").exists()).toBe(true);
    expect(shellContext.find("#btn-ai-assistance").exists()).toBe(true);

    wrapper.unmount();
  });

  it("adapts StudyCanvasView across desktop inline panes, tablet ClinicalIconRail + slide-over inspector, and mobile bottom sheets", async () => {
    const router = buildTestRouter();
    await router.push(
      "/studies/CADENCE-101?section=participants&node=participant:0192f3c4-5e6a-7b8c-9d0e-000000000001"
    );
    await router.isReady();

    // 1. Wide desktop (1440px): full inline navigator + docked inline inspector
    setViewportWidth(1440);
    const desktopWrapper = mount(StudyCanvasView, {
      attachTo: document.body,
      global: { plugins: [pinia, router] },
    });
    await flushPromises();
    expect(
      desktopWrapper
        .find("#canvas-navigator.clinical-icon-rail.is-expanded")
        .exists()
    ).toBe(true);
    expect(
      desktopWrapper
        .find("#canvas-navigator.clinical-icon-rail.is-compact")
        .exists()
    ).toBe(false);
    const desktopInspector = desktopWrapper.find("aside#canvas-inspector");
    expect(desktopInspector.exists()).toBe(true);
    expect(desktopInspector.attributes("data-collapsed")).toBe("false");
    desktopWrapper.unmount();

    // 2. Tablet (1024px): 60px ClinicalIconRail + slide-over right ClinicalDrawer inspector
    setViewportWidth(1024);
    const tabletWrapper = mount(StudyCanvasView, {
      attachTo: document.body,
      global: { plugins: [pinia, router] },
    });
    await flushPromises();
    expect(
      tabletWrapper
        .find("#canvas-navigator.clinical-icon-rail.is-compact")
        .exists()
    ).toBe(true);
    await tabletWrapper.find("#btn-open-inspector-overlay").trigger("click");
    const tabletInspectorDrawer = document.body.querySelector(
      ".clinical-drawer-panel.position-right #canvas-inspector"
    );
    expect(tabletInspectorDrawer).not.toBeNull();
    tabletWrapper.unmount();

    // 3. Mobile (390px): navigator toggle button + bottom-sheet drawers (even with no node selected)
    await router.push("/studies/CADENCE-101?section=participants");
    await router.isReady();
    setViewportWidth(390);
    const mobileWrapper = mount(StudyCanvasView, {
      attachTo: document.body,
      global: { plugins: [pinia, router] },
    });
    await flushPromises();
    expect(mobileWrapper.find("#btn-open-navigator-sheet").exists()).toBe(true);
    await mobileWrapper.find("#btn-open-inspector-overlay").trigger("click");
    expect(
      document.body.querySelector(
        ".clinical-drawer-panel.position-bottom #canvas-inspector"
      )
    ).not.toBeNull();
    await mobileWrapper.find("#btn-open-navigator-sheet").trigger("click");
    expect(
      document.body.querySelectorAll(".clinical-drawer-panel.position-bottom")
        .length
    ).toBeGreaterThanOrEqual(2);
    mobileWrapper.unmount();
  });

  it("adapts DocumentManagerView (standalone and embedded), DocumentsHubView, and KnowledgeView across desktop, tablet, and mobile tiers", async () => {
    const router = buildTestRouter();
    await router.push("/etmf");
    await router.isReady();

    const etmfStore = useEtmfStore();
    etmfStore.fetchStructure = vi.fn(async () => {});
    etmfStore.fetchDocuments = vi.fn(async () => {});
    etmfStore.structure = [
      { id: "zone-01", code: "01", name: "Trial Management", children: [] },
    ];

    const fileshareStore = useFileshareStore();
    fileshareStore.bootstrap = vi.fn(async () => {});
    fileshareStore.fetchFiles = vi.fn(async () => {});
    fileshareStore.folders = [
      { id: "fld-root", name: "Trial Master Share", parent_id: null },
    ];
    fileshareStore.files = [
      {
        id: "file-1",
        folder_id: "fld-root",
        name: "Protocol-v3.pdf",
        mime_type: "application/pdf",
        size_bytes: 2048,
        current_version: 1,
        sha256: "0".repeat(64),
        status: "approved",
        updated_at: "2026-10-01T00:00:00Z",
      },
    ];
    fileshareStore.selectedFileId = "file-1";

    // Embedded DocumentManagerView at tablet (1024px) collapses binder tree into a left slide-over drawer
    setViewportWidth(1024);
    const embeddedEtmf = mount(DocumentManagerView, {
      attachTo: document.body,
      props: { studyId: "CADENCE-101", embedded: true },
      global: { plugins: [pinia, router] },
    });
    await flushPromises();
    expect(embeddedEtmf.find("aside.sidebar-binder-tree").exists()).toBe(false);
    const binderBtn = embeddedEtmf.find("#btn-etmf-toggle-binder-tree");
    expect(binderBtn.exists()).toBe(true);
    await binderBtn.trigger("click");
    expect(
      document.body.querySelector(
        ".clinical-drawer-panel.sidebar-binder-tree.position-left"
      )
    ).not.toBeNull();
    embeddedEtmf.unmount();

    // DocumentsHubView at mobile (390px) uses bottom sheets for folders and file inspector
    setViewportWidth(390);
    const mobileDocs = mount(DocumentsHubView, {
      attachTo: document.body,
      global: { plugins: [pinia, router] },
    });
    await flushPromises();
    await mobileDocs.find("#btn-hub-toggle-folder-tree").trigger("click");
    expect(
      document.body.querySelector(
        ".clinical-drawer-panel.hub-sidebar.position-bottom"
      )
    ).not.toBeNull();
    mobileDocs.unmount();

    // KnowledgeView at desktop (1280px) vs mobile (320px)
    await router.push("/knowledge");
    await router.isReady();

    setViewportWidth(1280);
    const desktopKnowledge = mount(KnowledgeView, {
      attachTo: document.body,
      global: { plugins: [pinia, router] },
    });
    await flushPromises();
    expect(desktopKnowledge.find("aside.knowledge-sidebar").exists()).toBe(
      true
    );
    expect(desktopKnowledge.find("#knowledge-reader-pane").exists()).toBe(true);
    desktopKnowledge.unmount();

    setViewportWidth(320);
    const mobileKnowledge = mount(KnowledgeView, {
      attachTo: document.body,
      global: { plugins: [pinia, router] },
    });
    await flushPromises();
    expect(mobileKnowledge.find("aside.knowledge-sidebar").exists()).toBe(
      false
    );
    await mobileKnowledge
      .find("#btn-knowledge-toggle-categories")
      .trigger("click");
    expect(
      document.body.querySelector(
        ".clinical-drawer-panel.knowledge-sidebar.position-bottom"
      )
    ).not.toBeNull();
    mobileKnowledge.unmount();
  });
});
