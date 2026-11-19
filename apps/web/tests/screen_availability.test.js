import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { router } from "../src/router";
import { useAuthStore } from "../src/stores/auth";
import { useNotificationsStore } from "../src/stores/notifications";
import {
  AVAILABLE_SCREENS,
  UNAVAILABLE_SCREENS_ISSUE,
  isScreenAvailable,
  screenTitle,
} from "../src/router/availability";
import AppShell from "../src/components/AppShell.vue";
import CommandPaletteOverlay from "../src/components/CommandPaletteOverlay.vue";
import UnavailableView from "../src/views/UnavailableView.vue";

// Screens without a .NET backend are hidden until their module exists (#4829).
describe("Screen availability", () => {
  beforeEach(async () => {
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
      "observation",
      "ssu",
      "study",
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
    await router.push("/etmf");

    const wrapper = mount(UnavailableView, { global: { plugins: [router] } });

    expect(router.currentRoute.value.path).toBe("/unavailable");
    expect(wrapper.find("h2").text()).toBe(
      "eTMF Document Vault isn't available yet"
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
});
