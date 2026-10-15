import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
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

  it("shows only available screens in the sidebar, even for an admin", () => {
    signInAsSuperAdmin();
    const notificationsStore = useNotificationsStore();
    const fetchNotifications = vi
      .spyOn(notificationsStore, "fetchNotifications")
      .mockResolvedValue();

    const wrapper = mount(AppShell, { global: { plugins: [router] } });

    const navIds = wrapper
      .findAll(".app-sidebar li[id^='tab-btn-']")
      .map((li) => li.attributes("id"));
    expect(navIds).toEqual([
      "tab-btn-overview",
      "tab-btn-ecrf",
      "tab-btn-ctms",
    ]);
    expect(wrapper.text()).not.toContain("Design & Metadata");
    // No .NET service serves the user's notifications yet.
    expect(fetchNotifications).not.toHaveBeenCalled();
  });

  it("offers only available screens in the command palette", () => {
    signInAsSuperAdmin();

    const wrapper = mount(CommandPaletteOverlay, {
      props: { isOpen: true },
      global: { plugins: [router] },
    });

    expect(wrapper.text()).toContain("eCRF Form Engine");
    expect(wrapper.text()).toContain("CTMS Dashboard");
    expect(wrapper.text()).not.toContain("MDR Protocol Designer");
    expect(wrapper.text()).not.toContain("Notifications");
  });

  it("explains a hidden screen and links to the tracking issue", async () => {
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
    expect(wrapper.find("button").exists()).toBe(true);
  });

  it("offers no way home when the user can open no available screen", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.isDemoMode = true;
    authStore.rawRoles = ["Auditor"];
    await router.push("/");

    const wrapper = mount(UnavailableView, { global: { plugins: [router] } });

    expect(wrapper.find("h2").text()).toBe(
      "No screen is available for your role yet"
    );
    expect(wrapper.find("button").exists()).toBe(false);
  });
});
