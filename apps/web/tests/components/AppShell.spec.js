import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createRouter, createWebHistory } from "vue-router";
import AppShell from "../../src/components/AppShell.vue";
import { useAuthStore } from "../../src/stores/auth";

// Create mock views
const DummyView = { template: "<div>Dummy</div>" };

// Set up router for testing AppShell link rendering
const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: "/", name: "home", component: DummyView },
    { path: "/mdr", name: "mdr", component: DummyView },
    { path: "/ecrf", name: "ecrf", component: DummyView },
    { path: "/ctms", name: "ctms", component: DummyView },
    { path: "/rules", name: "rules", component: DummyView },
    { path: "/audit", name: "audit", component: DummyView },
    { path: "/etmf", name: "etmf", component: DummyView },
    { path: "/notifications", name: "notifications", component: DummyView },
  ],
});

describe("AppShell.vue - Application Shell Component", () => {
  beforeEach(() => {
    const pinia = createPinia();
    setActivePinia(pinia);
  });

  it("renders standard headers and handles slot content", () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = false;
    authStore.isDemoMode = true;

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
    expect(wrapper.text()).toContain("Demo Mode");
  });

  it("dynamically shows or hides sidebar navigation items based on normalized roles", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.isDemoMode = false;
    authStore.rawRoles = ["Site Investigator"]; // maps to site_investigator & crc

    const wrapper = mount(AppShell, {
      global: {
        plugins: [router],
      },
    });

    // Site Investigator has access to eCRF and Notifications, but NOT MDR, CTMS, Rules, Audit, eTMF
    expect(wrapper.find("#tab-btn-ecrf").exists()).toBe(true);
    expect(wrapper.find("#tab-btn-mdr").exists()).toBe(false);
    expect(wrapper.find("#tab-btn-ctms").exists()).toBe(false);
    expect(wrapper.find("#tab-btn-rules").exists()).toBe(false);
    expect(wrapper.find("#tab-btn-audit").exists()).toBe(false);
    expect(wrapper.find("#tab-btn-etmf").exists()).toBe(false);

    // Switch roles to Auditor
    authStore.rawRoles = ["Auditor"]; // maps to auditor & tmf_auditor
    const auditWrapper = mount(AppShell, {
      global: {
        plugins: [router],
      },
    });

    expect(auditWrapper.find("#tab-btn-audit").exists()).toBe(true);
    expect(auditWrapper.find("#tab-btn-etmf").exists()).toBe(true);
    expect(auditWrapper.find("#tab-btn-mdr").exists()).toBe(false);
    expect(auditWrapper.find("#tab-btn-ecrf").exists()).toBe(false);
  });

  it("triggers login and logout actions on user actions", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.isDemoMode = false;
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

    // Test Login trigger
    authStore.isAuthenticated = false;
    authStore.isDemoMode = false;
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
  });

  it("renders a skip-to-content link as the very first focusable element inside the template root", () => {
    const wrapper = mount(AppShell, {
      global: {
        plugins: [router],
      },
    });

    const focusable = wrapper.element.querySelectorAll("a, button, input, select, textarea, [tabindex]");
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

  it("passes the accessibility audit in strict mode", async () => {
    const wrapper = mount(AppShell, {
      global: {
        plugins: [router],
      },
    });

    await expect(wrapper).toBeAccessible({
      rules: {
        "landmark-one-main": { enabled: true },
        "region": { enabled: true },
        "bypass": { enabled: true },
      },
    });
  });

  it("fails the accessibility audit in strict mode if the main landmark or skip link targets are missing/removed", async () => {
    const wrapper = mount(AppShell, {
      global: {
        plugins: [router],
      },
    });

    const clonedEl = wrapper.element.cloneNode(true);
    
    // Unmount the original wrapper to clean up document.body
    wrapper.unmount();
    
    // 1. Remove the skip link completely
    const skipLink = clonedEl.querySelector(".skip-link");
    if (skipLink) {
      skipLink.remove();
    }

    // 2. Remove main landmark container (turn it into a div)
    const mainEl = clonedEl.querySelector("main");
    if (mainEl) {
      const divEl = document.createElement("div");
      divEl.innerHTML = mainEl.innerHTML;
      mainEl.parentNode.replaceChild(divEl, mainEl);
    }

    // Run the accessibility audit on the malformed clone using strict mode rules.
    // Imported directly from ui package
    const { toBeAccessible: auditFn } = await import("ui");
    const result = await auditFn(clonedEl, {
      rules: {
        "landmark-one-main": { enabled: true },
        "region": { enabled: true },
        "bypass": { enabled: true },
      },
    });

    expect(result.pass).toBe(false);
  });
});

