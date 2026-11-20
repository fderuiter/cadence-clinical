import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { router } from "../src/router";
import { useAuthStore } from "../src/stores/auth";
import { useOnboardingStore } from "../src/stores/onboarding";
import { useNotificationsStore } from "../src/stores/notifications";
import {
  AVAILABLE_SCREENS,
  UNAVAILABLE_SCREENS_ISSUE,
  isScreenAvailable,
  screenTitle,
} from "../src/router/availability";
import { signInAs } from "./helpers/signIn.js";
import AppShell from "../src/components/AppShell.vue";
import CommandPaletteOverlay from "../src/components/CommandPaletteOverlay.vue";
import OverviewView from "../src/views/OverviewView.vue";
import UnavailableView from "../src/views/UnavailableView.vue";

// Screens without a .NET backend are hidden until their module exists (#4829).
describe("Screen availability", () => {
  beforeEach(async () => {
    window.localStorage.clear();
    setActivePinia(createPinia());
    await router.push("/login");
  });

  function signInAsSuperAdmin() {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.isDemoMode = true;
    authStore.rawRoles = ["Sponsor Admin"];
  }

  it("marks only screens with a .NET backend as available", () => {
    expect([...AVAILABLE_SCREENS].sort()).toEqual([
      "ctms",
      "ecrf",
      "etmf",
      "observation",
      "ssu",
      "study",
      "training",
    ]);
    expect(isScreenAvailable("login")).toBe(true);
    expect(isScreenAvailable("unavailable")).toBe(true);
    expect(isScreenAvailable("mdr")).toBe(false);
    expect(isScreenAvailable(undefined)).toBe(false);
  });

  it("names every hidden route on the unavailable page", () => {
    const hidden = router
      .getRoutes()
      .map((route) => route.name)
      .filter((name) => name && !isScreenAvailable(name));

    expect(hidden.length).toBeGreaterThan(0);
    for (const name of hidden) {
      expect(screenTitle(name), name).not.toBe("This screen");
    }
  });

  it("shows only live UX-1 workflow destinations in primary navigation, without a legacy sidebar", () => {
    signInAsSuperAdmin();
    const notificationsStore = useNotificationsStore();
    const fetchNotifications = vi
      .spyOn(notificationsStore, "fetchNotifications")
      .mockResolvedValue();

    const wrapper = mount(AppShell, { global: { plugins: [router] } });

    expect(wrapper.find(".app-sidebar").exists()).toBe(false);
    const navIds = wrapper
      .findAll("#primary-navigation a")
      .map((a) => a.attributes("id"));
    expect(navIds).toEqual([
      "nav-studies",
      "nav-data-collection",
      "nav-site-readiness",
    ]);
    expect(wrapper.text()).not.toContain("Design & Metadata");
    expect(wrapper.text()).not.toContain("EDC");
    expect(wrapper.text()).not.toContain("CTMS");
    // No .NET service serves the user's notifications yet.
    expect(fetchNotifications).not.toHaveBeenCalled();
  });

  it("offers only available UX-1 screens in the global command palette", () => {
    signInAsSuperAdmin();

    const wrapper = mount(CommandPaletteOverlay, {
      props: { isOpen: true },
      global: { plugins: [router] },
    });

    expect(wrapper.text()).toContain("Studies");
    expect(wrapper.text()).toContain("Data collection");
    expect(wrapper.text()).toContain("Site readiness");
    expect(wrapper.text()).toContain("Orientation");
    expect(wrapper.text()).not.toContain("MDR Protocol Designer");
    expect(wrapper.text()).not.toContain("Notifications");
    expect(wrapper.text()).not.toContain("CTMS Dashboard");
    expect(wrapper.text()).not.toContain("eCRF Form Engine");
  });

  it("explains a hidden screen, links to the tracking issue, and offers study workspace recovery", async () => {
    signInAsSuperAdmin();
    await router.push("/quality");

    const wrapper = mount(UnavailableView, { global: { plugins: [router] } });

    expect(router.currentRoute.value.path).toBe("/unavailable");
    expect(wrapper.find("h2").text()).toBe(
      "Quality & RBQM Cockpit isn't available yet"
    );
    expect(wrapper.find("a").attributes("href")).toBe(
      UNAVAILABLE_SCREENS_ISSUE
    );
    expect(wrapper.find("#btn-unavailable-study").exists()).toBe(true);
    expect(wrapper.find("#btn-unavailable-home").exists()).toBe(true);
  });

  it("always offers study workspace recovery on /unavailable for every authenticated role", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.isDemoMode = true;
    authStore.rawRoles = ["Auditor"];
    await router.push("/unavailable");

    const wrapper = mount(UnavailableView, { global: { plugins: [router] } });

    expect(wrapper.find("h2").text()).toBe("This screen isn't available yet");
    expect(wrapper.find("#btn-unavailable-study").text()).toBe(
      "Open study workspace"
    );
    const pushSpy = vi.spyOn(router, "push");
    await wrapper.find("#btn-unavailable-study").trigger("click");
    await pushSpy.mock.results[0].value;
    expect(router.currentRoute.value.path).toBe("/studies");
    pushSpy.mockRestore();
  });

  it("completes first-login routing, guided tour steps, setup checklist tasks, and overview launchers cleanly across all six personas", async () => {
    const personas = [
      { key: "super_admin", roles: ["Sponsor Admin"] },
      { key: "sponsor_designer", roles: ["Sponsor Designer"] },
      { key: "data_manager", roles: ["Data Manager"] },
      { key: "site_crc", roles: ["CRC"] },
      { key: "cra_monitor", roles: ["CRA"] },
      { key: "auditor", roles: ["Auditor"] },
    ];

    for (const { key, roles } of personas) {
      window.localStorage.clear();
      setActivePinia(createPinia());
      await router.push("/login");

      const authStore = signInAs(useAuthStore(), roles, {
        username: `user_${key}`,
      });
      authStore.isDemoMode = true;
      const onboardingStore = useOnboardingStore();
      onboardingStore.loadPersonaState(key);

      // 1. First login lands on /overview without hitting /unavailable or /forbidden
      await router.push("/");
      expect(router.currentRoute.value.path, `${key} first login`).toBe(
        "/overview"
      );

      const shellWrapper = mount(AppShell, {
        global: { plugins: [router] },
        slots: { default: OverviewView },
      });
      await flushPromises();

      // 2. Launch guided tour from Overview and walk every step cleanly
      await shellWrapper.find(".btn-start-tour").trigger("click");
      await flushPromises();

      expect(onboardingStore.isActive, `${key} tour active`).toBe(true);
      expect(router.currentRoute.value.path, `${key} step 1 route`).not.toBe(
        "/unavailable"
      );
      expect(router.currentRoute.value.path, `${key} step 1 auth`).not.toBe(
        "/forbidden"
      );

      const stepCount = onboardingStore.totalSteps;
      expect(stepCount).toBeGreaterThan(0);

      for (let step = 1; step < stepCount; step++) {
        await shellWrapper.find(".btn-tour-next").trigger("click");
        await flushPromises();
        expect(
          router.currentRoute.value.path,
          `${key} step ${step + 1} available`
        ).not.toBe("/unavailable");
        expect(
          router.currentRoute.value.path,
          `${key} step ${step + 1} authorized`
        ).not.toBe("/forbidden");
        if (key === "site_crc" && onboardingStore.currentStep === 4) {
          expect(router.currentRoute.value.path).toBe("/ssu");
        }
      }

      await shellWrapper.find(".btn-tour-complete").trigger("click");
      await flushPromises();
      expect(onboardingStore.isActive).toBe(false);

      // 3. Verify every visible setup task resolves to an available, authorized route
      expect(onboardingStore.visibleTasks.length).toBeGreaterThan(0);
      for (const task of onboardingStore.visibleTasks) {
        await router.push(task.route);
        expect(router.currentRoute.value.path, `${key} task ${task.id}`).toBe(
          task.route
        );
      }

      shellWrapper.unmount();
    }
  });

  it("stays on /overview without opening study or /unavailable when AVAILABLE_SCREENS excludes every target screen", async () => {
    const saved = [...AVAILABLE_SCREENS];
    AVAILABLE_SCREENS.clear();
    try {
      for (const { key, roles } of [
        { key: "auditor", roles: ["Auditor"] },
        { key: "data_manager", roles: ["Data Manager"] },
      ]) {
        window.localStorage.clear();
        setActivePinia(createPinia());
        await router.push("/login");

        const authStore = signInAs(useAuthStore(), roles);
        authStore.isDemoMode = true;
        const onboardingStore = useOnboardingStore();
        onboardingStore.loadPersonaState(key);

        await router.push("/");
        expect(router.currentRoute.value.path).toBe("/overview");

        const wrapper = mount(AppShell, {
          global: { plugins: [router] },
          slots: { default: OverviewView },
        });
        await flushPromises();

        await wrapper.find(".btn-start-tour").trigger("click");
        await flushPromises();

        expect(router.currentRoute.value.path, `${key} stays on overview`).toBe(
          "/overview"
        );
        expect(
          onboardingStore.personaSteps.every((s) => s.routeName === "overview")
        ).toBe(true);

        wrapper.unmount();
      }
    } finally {
      saved.forEach((s) => AVAILABLE_SCREENS.add(s));
    }
  });
});
