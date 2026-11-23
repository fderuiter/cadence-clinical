import fs from "node:fs";
import path from "node:path";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import { nextTick } from "vue";
import {
  useRovingTabindex as uiUseRovingTabindex,
  syncRovingTabindex as uiSyncRovingTabindex,
} from "ui";
import {
  useRovingTabindex,
  syncRovingTabindex,
} from "../src/composables/useRovingTabindex.js";
import AppShell from "../src/components/AppShell.vue";
import SynonymImportDrawer from "../src/components/SynonymImportDrawer.vue";
import ShareDrawer from "../src/components/fileshare/ShareDrawer.vue";
import UsdmConceptStagingDrawer from "../src/components/clinical/UsdmConceptStagingDrawer.vue";
import {
  BRAND_TITLE_SUFFIX,
  ROUTE_TITLES,
  formatRouteAnnouncement,
  resolveRouteTitle,
} from "../src/router/availability.js";
import { router, installRouterSemantics } from "../src/router/index.js";
import { useAuthStore } from "../src/stores/auth.js";
import { useCodingStore } from "../src/stores/coding.js";
import { useFileshareStore } from "../src/stores/fileshare.js";

const UX1_FORBIDDEN_ACRONYMS = /\b(EDC|CTMS|eTMF|RTSM|P21|QMS|Safety)\b/;

const DummyView = { template: '<div class="dummy-page">View body</div>' };

function walkVueFiles(dir, acc = []) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const fullPath = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      walkVueFiles(fullPath, acc);
    } else if (entry.name.endsWith(".vue")) {
      acc.push(fullPath);
    }
  }
  return acc;
}

describe("Web Router Semantics, Mobile Landmark, Single <main>, and Shared Roving Tablists (ADR-2656)", () => {
  let pinia;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    document.title = "";
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      json: () => Promise.resolve([]),
    });
  });

  afterEach(() => {
    vi.unstubAllEnvs();
  });

  it("re-exports shared useRovingTabindex and syncRovingTabindex from packages/ui", () => {
    expect(useRovingTabindex).toBe(uiUseRovingTabindex);
    expect(syncRovingTabindex).toBe(uiSyncRovingTabindex);
  });

  it("resolves UX-1 compliant, PII-free page titles and polite announcements across all workspace routes", () => {
    const registeredRoutes = router.getRoutes().filter((r) => Boolean(r.name));
    expect(registeredRoutes.length).toBeGreaterThanOrEqual(30);

    for (const routeRecord of registeredRoutes) {
      const name = String(routeRecord.name);
      expect(
        ROUTE_TITLES[name],
        `Expected ROUTE_TITLES entry for route "${name}"`
      ).toBeTruthy();

      const mockTo = {
        name,
        path: routeRecord.path.replace(":studyCode?", "ONCO-101"),
        params: routeRecord.path.includes(":studyCode")
          ? { studyCode: "ONCO-101" }
          : {},
        query: {
          participant: "SUBJ-PII-SECRET",
          subjectId: "PATIENT-JANE-DOE",
          email: "patient@example.com",
        },
      };

      const title = resolveRouteTitle(mockTo);
      expect(title).toContain(`— ${BRAND_TITLE_SUFFIX}`);
      expect(title).not.toMatch(UX1_FORBIDDEN_ACRONYMS);
      expect(title).not.toContain("SUBJ-PII-SECRET");
      expect(title).not.toContain("PATIENT-JANE-DOE");
      expect(title).not.toContain("patient@example.com");

      const announcement = formatRouteAnnouncement(mockTo);
      expect(announcement).toMatch(/^Navigated to /);
      expect(announcement).not.toMatch(UX1_FORBIDDEN_ACRONYMS);
      expect(announcement).not.toContain("SUBJ-PII-SECRET");
      expect(announcement).not.toContain("PATIENT-JANE-DOE");
    }

    // Study canvas lifecycle stage & section formatting without participant PII
    const canvasLocation = {
      name: "study",
      path: "/studies/ONCO-204",
      params: { studyCode: "ONCO-204" },
      query: {
        stage: "conduct",
        section: "participants",
        participant: "SUBJ-0042-PII",
      },
    };
    expect(resolveRouteTitle(canvasLocation)).toBe(
      "Study ONCO-204 · Study Conduct (Participants) — Cadence Clinical"
    );
    expect(formatRouteAnnouncement(canvasLocation)).toBe(
      "Navigated to Study ONCO-204 · Study Conduct (Participants)"
    );

    // Pre-release /unavailable?screen=... route formatting
    const unavailableLocation = {
      name: "unavailable",
      path: "/unavailable",
      query: { screen: "safety" },
    };
    expect(resolveRouteTitle(unavailableLocation)).toBe(
      "Pharmacovigilance & Aggregate Reporting (Unavailable) — Cadence Clinical"
    );
  });

  it("sets document.title silently on initial load and announces only post-load transitions in AppShell", async () => {
    const testRouter = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: "/studies/:studyCode?", name: "study", component: DummyView },
        { path: "/ssu", name: "ssu", component: DummyView },
        { path: "/tickets", name: "tickets", component: DummyView },
        { path: "/knowledge", name: "knowledge", component: DummyView },
        { path: "/plans", name: "plans", component: DummyView },
      ],
    });
    installRouterSemantics(testRouter);

    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Site Investigator"];

    // Initial navigation before/during shell mount stays silent in polite status region
    await testRouter.push("/studies");
    await testRouter.isReady();
    expect(document.title).toBe("Studies — Cadence Clinical");

    const wrapper = mount(AppShell, {
      global: { plugins: [pinia, testRouter] },
      slots: { default: '<div class="slot-body">Workspace</div>' },
    });
    await nextTick();

    const liveRegion = wrapper.find(".sr-only-status-alerts");
    expect(liveRegion.exists()).toBe(true);
    expect(liveRegion.attributes("role")).toBe("status");
    expect(liveRegion.attributes("aria-live")).toBe("polite");
    expect(liveRegion.text().trim()).toBe("");

    // Subsequent client-side route transitions update document.title and announce politely
    await testRouter.push("/ssu");
    await nextTick();
    expect(document.title).toBe("Site Startup & Activation — Cadence Clinical");
    expect(liveRegion.text().trim()).toBe(
      "Navigated to Site Startup & Activation"
    );

    await testRouter.push("/tickets");
    await nextTick();
    expect(document.title).toBe(
      "Operational Issues & Support — Cadence Clinical"
    );
    expect(liveRegion.text().trim()).toBe(
      "Navigated to Operational Issues & Support"
    );

    wrapper.unmount();
  });

  it("renders #mobile-nav-menu as a hidden-when-closed <nav> landmark with aria-current='page' and auto-close above 860px", async () => {
    const testRouter = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: "/studies/:studyCode?", name: "study", component: DummyView },
        { path: "/ecrf", name: "ecrf", component: DummyView },
        { path: "/ctms", name: "ctms", component: DummyView },
        { path: "/tickets", name: "tickets", component: DummyView },
        { path: "/knowledge", name: "knowledge", component: DummyView },
        { path: "/plans", name: "plans", component: DummyView },
      ],
    });
    await testRouter.push("/ctms");
    await testRouter.isReady();

    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["CRA"];

    Object.defineProperty(window, "innerWidth", {
      configurable: true,
      writable: true,
      value: 390,
    });

    const wrapper = mount(AppShell, {
      global: { plugins: [pinia, testRouter] },
    });
    await nextTick();

    const mobileNav = wrapper.find("#mobile-nav-menu");
    expect(mobileNav.exists()).toBe(true);
    expect(mobileNav.element.tagName).toBe("NAV");
    expect(mobileNav.attributes("aria-label")).toBe("Mobile navigation");
    expect(mobileNav.attributes("hidden")).toBeDefined();

    // Open mobile drawer via hamburger toggle
    const toggleBtn = wrapper.find("#btn-mobile-menu-toggle");
    await toggleBtn.trigger("click");
    await nextTick();

    expect(mobileNav.attributes("hidden")).toBeUndefined();
    const links = mobileNav.findAll("a.mobile-nav-link");
    expect(links.length).toBeGreaterThanOrEqual(2);

    const activeLinks = links.filter(
      (link) => link.attributes("aria-current") === "page"
    );
    expect(activeLinks).toHaveLength(1);
    expect(activeLinks[0].text()).toBe("Site readiness");

    const inactiveLinks = links.filter(
      (link) => link.text() !== "Site readiness"
    );
    for (const link of inactiveLinks) {
      expect(link.attributes("aria-current")).toBeUndefined();
    }

    // Viewport resize above 860px closes mobile nav so only one nav landmark is visible
    window.innerWidth = 1024;
    window.dispatchEvent(new Event("resize"));
    await nextTick();
    expect(mobileNav.attributes("hidden")).toBeDefined();

    wrapper.unmount();
  });

  it("enforces a single <main> landmark in AppShell.vue with zero nested <main> elements across all views and consoles", () => {
    const srcDir = path.resolve(import.meta.dirname, "../src");
    const vueFiles = walkVueFiles(srcDir);
    const filesWithMain = [];

    for (const filePath of vueFiles) {
      const content = fs.readFileSync(filePath, "utf8");
      if (/<main\b/.test(content)) {
        filesWithMain.push(path.relative(srcDir, filePath));
      }
    }

    expect(filesWithMain).toEqual(["components/AppShell.vue"]);
  });

  it("enforces WAI-ARIA tablist, tab, aria-selected, and roving tabindex across conditionally mounted drawers and all web tablists", async () => {
    // 1. SynonymImportDrawer (v-if controlled via Pinia codingStore.isSynonymDrawerOpen)
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Data Manager"];

    const codingStore = useCodingStore();
    codingStore.isSynonymDrawerOpen = false;
    codingStore.fetchSynonymImportJobs = vi.fn().mockResolvedValue([]);
    codingStore.fetchActiveSynonymRules = vi.fn().mockResolvedValue([]);

    const synonymWrapper = mount(SynonymImportDrawer, {
      attachTo: document.body,
      global: { plugins: [pinia] },
    });
    expect(synonymWrapper.find('[role="tablist"]').exists()).toBe(false);

    codingStore.isSynonymDrawerOpen = true;
    await nextTick();
    await nextTick();

    const synonymTabs = synonymWrapper.findAll('[role="tablist"] [role="tab"]');
    expect(synonymTabs).toHaveLength(3);
    expect(synonymTabs[0].attributes("aria-selected")).toBe("true");
    expect(synonymTabs[0].attributes("tabindex")).toBe("0");
    expect(synonymTabs[1].attributes("tabindex")).toBe("-1");

    synonymTabs[0].element.focus();
    await synonymTabs[0].trigger("keydown", { key: "ArrowRight" });
    await nextTick();
    expect(synonymTabs[1].attributes("aria-selected")).toBe("true");
    expect(synonymTabs[1].attributes("tabindex")).toBe("0");
    expect(document.activeElement).toBe(synonymTabs[1].element);

    await synonymTabs[1].trigger("keydown", { key: "End" });
    await nextTick();
    expect(synonymTabs[2].attributes("aria-selected")).toBe("true");
    expect(document.activeElement).toBe(synonymTabs[2].element);

    await synonymTabs[2].trigger("keydown", { key: "Home" });
    await nextTick();
    expect(synonymTabs[0].attributes("aria-selected")).toBe("true");
    expect(document.activeElement).toBe(synonymTabs[0].element);
    synonymWrapper.unmount();

    // 2. ShareDrawer (v-if controlled via isOpen prop)
    const fileshareStore = useFileshareStore();
    fileshareStore.fetchGrants = vi.fn().mockResolvedValue([]);

    const shareWrapper = mount(ShareDrawer, {
      attachTo: document.body,
      props: {
        isOpen: false,
        files: [{ id: "file-1", filename: "protocol-v1.pdf" }],
      },
      global: { plugins: [pinia] },
    });
    expect(shareWrapper.find('[role="tablist"]').exists()).toBe(false);

    await shareWrapper.setProps({ isOpen: true });
    await nextTick();
    await nextTick();

    const shareTabs = shareWrapper.findAll('[role="tablist"] [role="tab"]');
    expect(shareTabs).toHaveLength(3);
    expect(shareTabs[0].attributes("aria-selected")).toBe("true");
    shareTabs[0].element.focus();
    await shareTabs[0].trigger("keydown", { key: "ArrowLeft" });
    await nextTick();
    expect(shareTabs[2].attributes("aria-selected")).toBe("true");
    expect(document.activeElement).toBe(shareTabs[2].element);
    shareWrapper.unmount();

    // 3. UsdmConceptStagingDrawer (per-item dynamic v-for tablists)
    const stagingWrapper = mount(UsdmConceptStagingDrawer, {
      attachTo: document.body,
      props: {
        isOpen: true,
        unmappedTokens: [
          { id: "tok-1", rawText: "Custom Biomarker Score", domain: "LB" },
        ],
      },
      global: { plugins: [pinia] },
    });
    await nextTick();
    await nextTick();

    const stagingTabs = stagingWrapper.findAll('[role="tablist"] [role="tab"]');
    expect(stagingTabs).toHaveLength(3);
    expect(stagingTabs[0].attributes("aria-selected")).toBe("true");
    stagingTabs[0].element.focus();
    await stagingTabs[0].trigger("keydown", { key: "ArrowRight" });
    await nextTick();
    expect(stagingTabs[1].attributes("aria-selected")).toBe("true");
    expect(document.activeElement).toBe(stagingTabs[1].element);
    stagingWrapper.unmount();

    // 4. Static template audit across all Vue files: every role="tablist" file binds role="tab", aria-selected, and tabindex
    const srcDir = path.resolve(import.meta.dirname, "../src");
    const allVueFiles = walkVueFiles(srcDir);
    const tablistFiles = allVueFiles.filter((filePath) =>
      fs.readFileSync(filePath, "utf8").includes('role="tablist"')
    );
    const clinicalTabsFiles = allVueFiles.filter((filePath) =>
      fs.readFileSync(filePath, "utf8").includes("<ClinicalTabs")
    );
    expect(tablistFiles.length).toBeGreaterThanOrEqual(28);
    expect(
      tablistFiles.length + clinicalTabsFiles.length
    ).toBeGreaterThanOrEqual(34);
    for (const filePath of tablistFiles) {
      const content = fs.readFileSync(filePath, "utf8");
      const rel = path.relative(srcDir, filePath);
      expect(content, `${rel} must declare role="tab"`).toContain('role="tab"');
      expect(content, `${rel} must bind aria-selected`).toContain(
        "aria-selected"
      );
      expect(content, `${rel} must bind roving tabindex`).toContain("tabindex");
    }
  });
});
