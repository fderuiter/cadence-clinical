import { describe, it, expect, vi, beforeEach } from "vitest";
import { nextTick } from "vue";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createRouter, createWebHistory } from "vue-router";
import AppShell from "../../src/components/AppShell.vue";
import { useAuthStore } from "../../src/stores/auth";
import { useKnowledgeStore } from "../../src/stores/knowledge";

const DummyView = { template: "<div>Dummy</div>" };

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: "/", name: "home", component: DummyView },
    { path: "/overview", name: "overview", component: DummyView },
    { path: "/studies/:studyCode?", name: "study", component: DummyView },
    { path: "/ecrf", name: "ecrf", component: DummyView },
    { path: "/ctms", name: "ctms", component: DummyView },
    { path: "/ssu", name: "ssu", component: DummyView },
  ],
});

describe("AppShell.vue - Unified Study-Centric Application Shell", () => {
  beforeEach(() => {
    const pinia = createPinia();
    setActivePinia(pinia);
  });

  it("renders unified header and slot content without sandbox banners, legacy sidebars, telemetry footers, or product acronyms (UX-1)", () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Admin"];

    const wrapper = mount(AppShell, {
      global: {
        plugins: [router],
      },
      slots: {
        default: '<div class="test-slot">Dynamic Workspace Page Content</div>',
      },
    });

    expect(wrapper.find(".header-title-area h1").text()).toBe(
      "Cadence Clinical"
    );
    expect(wrapper.find(".test-slot").text()).toBe(
      "Dynamic Workspace Page Content"
    );
    expect(wrapper.find(".app-sidebar").exists()).toBe(false);
    expect(wrapper.find("#toggle-diagnostic-panel").exists()).toBe(false);
    expect(wrapper.find(".diagnostic-panel").exists()).toBe(false);
    expect(wrapper.html()).not.toContain("localhost:5174");
    expect(wrapper.text()).not.toContain("Active Sandbox Mode");
    expect(wrapper.text()).not.toContain("EDC");
    expect(wrapper.text()).not.toContain("CTMS");
    expect(wrapper.text()).not.toContain("eTMF");
    expect(wrapper.text()).not.toContain("RTSM");
  });

  it("dynamically shows role-authorized UX-1 primary navigation items across clinical roles", () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;

    // 1. Site Investigator / Coordinator sees Studies, Data collection (Clinical Canvas Conduct), and Site readiness (/ssu)
    authStore.rawRoles = ["Site Investigator"];
    const crcWrapper = mount(AppShell, {
      global: { plugins: [router] },
    });
    expect(crcWrapper.find("#nav-studies").exists()).toBe(true);
    expect(crcWrapper.find("#nav-data-collection").attributes("href")).toBe(
      "/studies?stage=conduct&section=participants"
    );
    expect(crcWrapper.find("#nav-site-readiness").attributes("href")).toBe(
      "/ssu"
    );
    crcWrapper.unmount();

    // In static demo mode, #nav-data-collection targets /ecrf
    vi.stubEnv("VITE_DEMO_MODE", "true");
    const demoCrcWrapper = mount(AppShell, {
      global: { plugins: [router] },
    });
    expect(demoCrcWrapper.find("#nav-data-collection").attributes("href")).toBe(
      "/ecrf"
    );
    demoCrcWrapper.unmount();
    vi.unstubAllEnvs();

    // 2. CRA / Monitor sees Studies, Data collection (/ecrf), and Site readiness (/ctms)
    authStore.rawRoles = ["CRA"];
    const craWrapper = mount(AppShell, {
      global: { plugins: [router] },
    });
    expect(craWrapper.find("#nav-studies").exists()).toBe(true);
    expect(craWrapper.find("#nav-data-collection").exists()).toBe(true);
    expect(craWrapper.find("#nav-site-readiness").attributes("href")).toBe(
      "/ctms"
    );
    craWrapper.unmount();

    // 3. Data Manager sees Studies, Data collection (/ecrf), and Site readiness (/ctms)
    authStore.rawRoles = ["Data Manager"];
    const dmWrapper = mount(AppShell, {
      global: { plugins: [router] },
    });
    expect(dmWrapper.find("#nav-studies").exists()).toBe(true);
    expect(dmWrapper.find("#nav-data-collection").exists()).toBe(true);
    expect(dmWrapper.find("#nav-site-readiness").attributes("href")).toBe(
      "/ctms"
    );
    dmWrapper.unmount();

    // 4. Auditor and Study Designer see Studies without unauthorized workflow links
    authStore.rawRoles = ["Auditor"];
    const auditWrapper = mount(AppShell, {
      global: { plugins: [router] },
    });
    expect(auditWrapper.find("#nav-studies").exists()).toBe(true);
    expect(auditWrapper.find("#nav-data-collection").exists()).toBe(false);
    expect(auditWrapper.find("#nav-site-readiness").exists()).toBe(false);
    auditWrapper.unmount();
  });

  it("triggers login and logout actions on user actions", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.user = { username: "tester123" };
    authStore.rawRoles = ["Sponsor Admin"];

    const logoutSpy = vi.spyOn(authStore, "logout").mockResolvedValue(true);

    const wrapper = mount(AppShell, {
      global: {
        plugins: [router],
      },
    });

    const logoutBtn = wrapper.find("#btn-logout");
    expect(logoutBtn.exists()).toBe(true);
    await logoutBtn.trigger("click");
    expect(logoutSpy).toHaveBeenCalledTimes(1);
    wrapper.unmount();

    authStore.isAuthenticated = false;
    const loginSpy = vi.spyOn(authStore, "login").mockResolvedValue(true);

    const loginWrapper = mount(AppShell, {
      global: {
        plugins: [router],
      },
    });

    const loginBtn = loginWrapper.find("#btn-login");
    expect(loginBtn.exists()).toBe(true);
    await loginBtn.trigger("click");
    expect(loginSpy).toHaveBeenCalledTimes(1);
    loginWrapper.unmount();
  });

  it("renders a skip-to-content link as the very first focusable element inside the template root", () => {
    const wrapper = mount(AppShell, {
      global: {
        plugins: [router],
      },
    });

    const focusable = wrapper.element.querySelectorAll(
      "a, button, input, select, textarea, [tabindex]"
    );
    expect(focusable.length).toBeGreaterThan(0);
    const firstFocusable = focusable[0];
    expect(firstFocusable.classList.contains("skip-link")).toBe(true);
    expect(firstFocusable.getAttribute("href")).toBe("#main-content");
    expect(firstFocusable.textContent.trim()).toBe("Skip to main content");
  });

  it("targets the main content container with correct id and tabindex", () => {
    const wrapper = mount(AppShell, {
      global: {
        plugins: [router],
      },
    });

    const mainEl = wrapper.find("main");
    expect(mainEl.exists()).toBe(true);
    expect(mainEl.attributes("id")).toBe("main-content");
    expect(mainEl.attributes("tabindex")).toBe("-1");
  });

  it("passes the accessibility audit in strict mode when unauthenticated and authenticated", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = false;

    const unauthWrapper = mount(AppShell, {
      global: { plugins: [router] },
    });
    await expect(unauthWrapper).toBeAccessible({
      rules: {
        "landmark-one-main": { enabled: true },
        region: { enabled: true },
        bypass: { enabled: true },
      },
    });
    unauthWrapper.unmount();

    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Admin"];
    const authWrapper = mount(AppShell, {
      global: { plugins: [router] },
    });
    await expect(authWrapper).toBeAccessible({
      rules: {
        "landmark-one-main": { enabled: true },
        region: { enabled: true },
        bypass: { enabled: true },
      },
    });
    authWrapper.unmount();
  });

  it("fails the accessibility audit in strict mode if the main landmark or skip link targets are missing/removed", async () => {
    const wrapper = mount(AppShell, {
      global: {
        plugins: [router],
      },
    });

    const clonedEl = wrapper.element.cloneNode(true);
    wrapper.unmount();

    const skipLink = clonedEl.querySelector(".skip-link");
    if (skipLink) {
      skipLink.remove();
    }

    const mainEl = clonedEl.querySelector("main");
    if (mainEl) {
      const divEl = document.createElement("div");
      divEl.innerHTML = mainEl.innerHTML;
      mainEl.parentNode.replaceChild(divEl, mainEl);
    }

    const { toBeAccessible: auditFn } = await import("ui/vitest");
    const result = await auditFn(clonedEl, {
      rules: {
        "landmark-one-main": { enabled: true },
        region: { enabled: true },
        bypass: { enabled: true },
      },
    });

    expect(result.pass).toBe(false);
  });

  it("renders mobile menu toggle button and toggles mobile navigation drawer", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Admin"];

    const wrapper = mount(AppShell, {
      global: {
        plugins: [router],
      },
    });

    const toggleBtn = wrapper.find("#btn-mobile-menu-toggle");
    const mobileMenu = wrapper.find("#mobile-nav-menu");

    expect(toggleBtn.exists()).toBe(true);
    expect(toggleBtn.attributes("aria-label")).toBe("Toggle navigation menu");
    expect(toggleBtn.attributes("aria-controls")).toBe("mobile-nav-menu");
    expect(toggleBtn.attributes("aria-expanded")).toBe("false");
    expect(mobileMenu.classes()).not.toContain("is-open");

    await toggleBtn.trigger("click");
    expect(toggleBtn.attributes("aria-expanded")).toBe("true");
    expect(mobileMenu.classes()).toContain("is-open");

    await toggleBtn.trigger("click");
    expect(toggleBtn.attributes("aria-expanded")).toBe("false");
    expect(mobileMenu.classes()).not.toContain("is-open");
  });

  it("switches persona view without altering underlying security roles (UX-6) and announces to live region", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Admin"];
    const initialRoles = [...authStore.normalizedRoles];

    const wrapper = mount(AppShell, {
      global: {
        plugins: [router],
      },
    });

    const personaSelect = wrapper.find("#persona-select");
    expect(personaSelect.exists()).toBe(true);
    await personaSelect.setValue("site_crc");
    await personaSelect.trigger("change");

    expect(authStore.currentPersona).toBe("site_crc");
    expect(authStore.normalizedRoles).toEqual(initialRoles);

    const liveRegion = wrapper.find(".sr-only-status-alerts");
    expect(liveRegion.text()).toContain("Switched workspace view to");
    wrapper.unmount();

    // Single-role Auditor cannot select unauthorized personas in the switcher
    authStore.rawRoles = ["Auditor"];
    const auditWrapper = mount(AppShell, {
      global: { plugins: [router] },
    });
    const optionValues = auditWrapper
      .findAll("#persona-select option")
      .map((opt) => opt.attributes("value"));
    expect(optionValues).toEqual(["auditor"]);
    auditWrapper.unmount();
  });

  it("passes studyId, subjectId, and resolved enrollmentId on Data collection links and palette items when navigating from Clinical Canvas", async () => {
    const { useStudyCanvasStore } =
      await import("../../src/stores/studyCanvas");
    const { buildWorkspacePaletteItems } =
      await import("../../src/canvas/palette");
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Site Investigator"];

    const studyCanvas = useStudyCanvasStore();
    studyCanvas.studyCode = "CAD-101";
    studyCanvas.knownParticipants["sub-uuid-1"] = {
      id: "sub-uuid-1",
      subject_id: "017-0001",
      study_id: "CAD-101",
      site_id: "SITE-01",
      status: "ENROLLED",
    };
    studyCanvas.record.subjectCode = "017-0001";
    studyCanvas.record.enrollmentId = "0192f3c4-0000-7000-8000-00000000e001";

    await router.push(
      "/studies/CAD-101?stage=conduct&section=participants&participant=participant%3Asub-uuid-1"
    );
    await router.isReady();

    const wrapper = mount(AppShell, {
      global: { plugins: [router] },
    });

    expect(wrapper.find("#nav-data-collection").attributes("href")).toBe(
      "/studies/CAD-101?stage=conduct&subject=017-0001"
    );
    expect(wrapper.find("#shell-context-bar").exists()).toBe(false);

    const paletteItems = buildWorkspacePaletteItems({
      isAuthenticated: true,
      normalizedRoles: authStore.normalizedRoles,
      activeStudyCode: "CAD-101",
      activeSubjectCode: "017-0001",
      activeEnrollmentId: "0192f3c4-0000-7000-8000-00000000e001",
    });
    const dataCollectionItem = paletteItems.find(
      (item) => item.key === "workspace:data-collection"
    );
    expect(dataCollectionItem?.target.routePath).toBe(
      "/studies/CAD-101?stage=conduct&subject=017-0001"
    );

    wrapper.unmount();

    // Static demo builds preserve /ecrf query parameters
    vi.stubEnv("VITE_DEMO_MODE", "true");
    try {
      const demoWrapper = mount(AppShell, {
        global: { plugins: [router] },
      });
      expect(demoWrapper.find("#nav-data-collection").attributes("href")).toBe(
        "/ecrf?studyId=CAD-101&subjectId=017-0001&enrollmentId=0192f3c4-0000-7000-8000-00000000e001"
      );
      const demoPaletteItems = buildWorkspacePaletteItems({
        isAuthenticated: true,
        normalizedRoles: authStore.normalizedRoles,
        activeStudyCode: "CAD-101",
        activeSubjectCode: "017-0001",
        activeEnrollmentId: "0192f3c4-0000-7000-8000-00000000e001",
      });
      const demoItem = demoPaletteItems.find(
        (item) => item.key === "workspace:data-collection"
      );
      expect(demoItem?.target.routePath).toBe(
        "/ecrf?studyId=CAD-101&subjectId=017-0001&enrollmentId=0192f3c4-0000-7000-8000-00000000e001"
      );
      demoWrapper.unmount();
    } finally {
      vi.unstubAllEnvs();
    }
    await router.push("/");
  });

  it("exposes a visible-on-focus skip link to #canvas-workspace on study routes and coordinates Cmd/Ctrl+E while ignoring editable inputs", async () => {
    const { useStudyCanvasStore } =
      await import("../../src/stores/studyCanvas");
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Admin"];

    await router.push("/studies/CAD-101");
    await router.isReady();

    const wrapper = mount(AppShell, {
      attachTo: document.body,
      global: { plugins: [router] },
      slots: {
        default:
          '<div id="canvas-workspace" tabindex="-1"><input id="editable-field" type="text" /></div>',
      },
    });

    const focusable = wrapper.element.querySelectorAll(
      "a, button, input, select, textarea, [tabindex]"
    );
    expect(focusable[0].getAttribute("href")).toBe("#canvas-workspace");
    expect(focusable[0].classList.contains("skip-link")).toBe(true);
    expect(focusable[1].getAttribute("href")).toBe("#main-content");

    await wrapper.find("#skip-to-workspace").trigger("click");
    expect(document.activeElement?.id).toBe("canvas-workspace");
    expect(router.currentRoute.value.fullPath).toBe("/studies/CAD-101");

    const canvasStore = useStudyCanvasStore();

    // Without an explain coordinator / selection, Cmd+E does not preventDefault
    const emptyEvent = new KeyboardEvent("keydown", {
      key: "e",
      metaKey: true,
      bubbles: true,
      cancelable: true,
    });
    window.dispatchEvent(emptyEvent);
    expect(emptyEvent.defaultPrevented).toBe(false);

    const coordinator = vi.fn(() => true);
    const unregister = canvasStore.registerExplainCoordinator(coordinator);

    // Inside editable input, Cmd+E / Ctrl+E is ignored
    const inputEl = wrapper.find("#editable-field").element;
    inputEl.focus();
    const inputEvent = new KeyboardEvent("keydown", {
      key: "e",
      ctrlKey: true,
      bubbles: true,
      cancelable: true,
    });
    inputEl.dispatchEvent(inputEvent);
    expect(coordinator).not.toHaveBeenCalled();
    expect(inputEvent.defaultPrevented).toBe(false);

    // Outside editable inputs, Cmd+E / Ctrl+E triggers coordinator and prevents default
    wrapper.find("#canvas-workspace").element.focus();
    const activeEvent = new KeyboardEvent("keydown", {
      key: "e",
      ctrlKey: true,
      bubbles: true,
      cancelable: true,
    });
    window.dispatchEvent(activeEvent);
    expect(coordinator).toHaveBeenCalledWith({ fromShortcut: true });
    expect(activeEvent.defaultPrevented).toBe(true);

    unregister();
    wrapper.unmount();
    await router.push("/");
  });

  it("carries studyId and siteId to Site readiness (/ctms and /ssu) and preserves CADENCE-101 on Studies and Data collection links", async () => {
    const { useStudyCanvasStore } =
      await import("../../src/stores/studyCanvas");
    const { useClinicalStore } = await import("../../src/stores/clinical");
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;

    // Seed stale store state to prove URL parameters take precedence
    const studyCanvas = useStudyCanvasStore();
    studyCanvas.studyCode = "STALE-STUDY";
    const clinicalStore = useClinicalStore();
    clinicalStore.activeStudyId = "STALE-CLINICAL";
    clinicalStore.activeSiteId = "STALE-SITE";

    // 1. Sponsor/Monitor at /studies/CADENCE-101?site=SITE-101 -> /ctms?studyId=CADENCE-101&siteId=SITE-101
    authStore.rawRoles = ["CRA"];
    await router.push("/studies/CADENCE-101?site=SITE-101");
    await router.isReady();

    const craWrapper = mount(AppShell, {
      global: { plugins: [router] },
    });
    expect(craWrapper.find("#nav-site-readiness").attributes("href")).toBe(
      "/ctms?studyId=CADENCE-101&siteId=SITE-101"
    );
    expect(craWrapper.find("#nav-studies").attributes("href")).toBe(
      "/studies/CADENCE-101?site=SITE-101"
    );
    expect(craWrapper.find("#nav-data-collection").attributes("href")).toBe(
      "/studies/CADENCE-101?stage=conduct&site=SITE-101&section=participants"
    );
    expect(craWrapper.find("#shell-context-bar").exists()).toBe(false);
    craWrapper.unmount();

    // Static demo builds preserve /ecrf?studyId=CADENCE-101&siteId=SITE-101
    vi.stubEnv("VITE_DEMO_MODE", "true");
    try {
      const demoCraWrapper = mount(AppShell, {
        global: { plugins: [router] },
      });
      expect(
        demoCraWrapper.find("#nav-data-collection").attributes("href")
      ).toBe("/ecrf?studyId=CADENCE-101&siteId=SITE-101");
      demoCraWrapper.unmount();
    } finally {
      vi.unstubAllEnvs();
    }

    // 2. Site coordinator/investigator at /studies/CADENCE-101?site=SITE-101 -> /ssu?studyId=CADENCE-101&siteId=SITE-101
    authStore.rawRoles = ["Site Investigator"];
    const crcWrapper = mount(AppShell, {
      global: { plugins: [router] },
    });
    expect(crcWrapper.find("#nav-site-readiness").attributes("href")).toBe(
      "/ssu?studyId=CADENCE-101&siteId=SITE-101"
    );
    crcWrapper.unmount();

    // 3. Returning from /ecrf?studyId=CADENCE-101 or /ctms?studyId=CADENCE-101 preserves CADENCE-101
    authStore.rawRoles = ["CRA"];
    await router.push("/ecrf?studyId=CADENCE-101");
    const fromEcrf = mount(AppShell, {
      global: { plugins: [router] },
    });
    expect(fromEcrf.find("#nav-studies").attributes("href")).toBe(
      "/studies/CADENCE-101"
    );
    expect(fromEcrf.find("#nav-data-collection").attributes("href")).toBe(
      "/studies/CADENCE-101?stage=conduct&section=participants"
    );
    expect(fromEcrf.find("#nav-site-readiness").attributes("href")).toBe(
      "/ctms?studyId=CADENCE-101"
    );
    fromEcrf.unmount();

    await router.push("/ctms?studyId=CADENCE-101");
    const fromCtms = mount(AppShell, {
      global: { plugins: [router] },
    });
    expect(fromCtms.find("#nav-studies").attributes("href")).toBe(
      "/studies/CADENCE-101"
    );
    expect(fromCtms.find("#nav-data-collection").attributes("href")).toBe(
      "/studies/CADENCE-101?stage=conduct&section=participants"
    );
    expect(fromCtms.find("#shell-context-bar").exists()).toBe(true);
    fromCtms.unmount();

    await router.push(
      "/ecrf?studyId=CADENCE-101&siteId=SITE-101&subjectId=SUBJ-042"
    );
    const fullScopeWrapper = mount(AppShell, {
      global: { plugins: [router] },
    });
    const contextBar = fullScopeWrapper.find("#shell-context-bar");
    expect(contextBar.exists()).toBe(true);
    expect(contextBar.attributes("aria-label")).toBe("Clinical context");
    expect(contextBar.text()).toContain("Studies");
    expect(contextBar.text()).toContain("CADENCE-101");
    expect(contextBar.text()).toContain("SITE-101");
    expect(contextBar.text()).toContain("SUBJ-042");
    expect(fullScopeWrapper.find("#nav-studies").attributes("href")).toBe(
      "/studies/CADENCE-101?site=SITE-101&participant=SUBJ-042"
    );
    expect(fullScopeWrapper.find("#nav-support").attributes("href")).toBe(
      "/tickets?studyId=CADENCE-101&siteId=SITE-101&subjectId=SUBJ-042"
    );
    expect(fullScopeWrapper.find("#nav-knowledge").attributes("href")).toBe(
      "/knowledge?studyId=CADENCE-101&siteId=SITE-101&subjectId=SUBJ-042"
    );
    fullScopeWrapper.unmount();

    await router.push("/");
  });

  it("groups orientation, help, and sign-out inside #header-secondary-menu with #btn-header-actions-menu disclosure, Escape/outside close, and zero duplicate IDs", async () => {
    const authStore = useAuthStore();
    const knowledgeStore = useKnowledgeStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Admin"];
    authStore.identity = { username: "dm.lead", tenantId: "t-1" };
    authStore.switchPersona("sponsor_admin");

    const wrapper = mount(AppShell, {
      attachTo: document.body,
      global: { plugins: [router] },
    });

    try {
      expect(wrapper.findAll("#btn-onboarding-checklist-trigger")).toHaveLength(
        1
      );
      expect(wrapper.findAll("#btn-contextual-help-trigger")).toHaveLength(1);
      expect(wrapper.findAll("#btn-logout")).toHaveLength(1);

      const menuBtn = wrapper.find("#btn-header-actions-menu");
      const menuPanel = wrapper.find("#header-secondary-menu");
      expect(menuBtn.exists()).toBe(true);
      expect(menuBtn.attributes("aria-controls")).toBe("header-secondary-menu");
      expect(menuBtn.attributes("aria-expanded")).toBe("false");
      expect(menuPanel.classes()).not.toContain("is-open");

      await menuBtn.trigger("click");
      expect(menuBtn.attributes("aria-expanded")).toBe("true");
      expect(menuPanel.classes()).toContain("is-open");
      await expect(wrapper.element).toBeAccessible();

      // Escape closes menu and restores focus to toggle button
      window.dispatchEvent(
        new KeyboardEvent("keydown", { key: "Escape", bubbles: true })
      );
      await nextTick();
      expect(menuBtn.attributes("aria-expanded")).toBe("false");
      expect(menuPanel.classes()).not.toContain("is-open");
      expect(document.activeElement).toBe(menuBtn.element);

      // Outside pointerdown closes menu
      await menuBtn.trigger("click");
      expect(menuPanel.classes()).toContain("is-open");
      document.body.dispatchEvent(new Event("pointerdown", { bubbles: true }));
      await nextTick();
      expect(menuPanel.classes()).not.toContain("is-open");

      // Clicking contextual help closes menu and opens help drawer
      await menuBtn.trigger("click");
      await wrapper.find("#btn-contextual-help-trigger").trigger("click");
      expect(menuPanel.classes()).not.toContain("is-open");
      expect(knowledgeStore.isOpen).toBe(true);
    } finally {
      wrapper.unmount();
    }
  });
});
