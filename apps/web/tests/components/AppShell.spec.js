import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createRouter, createWebHistory } from "vue-router";
import AppShell from "../../src/components/AppShell.vue";
import { useAuthStore } from "../../src/stores/auth";

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

    // 1. Site Investigator / Coordinator sees Studies, Data collection (/ecrf), and Site readiness (/ssu)
    authStore.rawRoles = ["Site Investigator"];
    const crcWrapper = mount(AppShell, {
      global: { plugins: [router] },
    });
    expect(crcWrapper.find("#nav-studies").exists()).toBe(true);
    expect(crcWrapper.find("#nav-data-collection").attributes("href")).toBe(
      "/ecrf"
    );
    expect(crcWrapper.find("#nav-site-readiness").attributes("href")).toBe(
      "/ssu"
    );
    crcWrapper.unmount();

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

    const { toBeAccessible: auditFn } = await import("ui");
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
});
