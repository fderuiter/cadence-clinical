import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createRouter, createWebHistory } from "vue-router";
import { nextTick } from "vue";
import CommandPaletteOverlay from "../../src/components/CommandPaletteOverlay.vue";
import { useAuthStore } from "../../src/stores/auth";

// Create mock views
const DummyView = { template: "<div>Dummy</div>" };

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

describe("CommandPaletteOverlay.vue - Searchable Command Palette Overlay", () => {
  beforeEach(() => {
    const pinia = createPinia();
    setActivePinia(pinia);
  });

  it("does not render when isOpen is false", () => {
    const wrapper = mount(CommandPaletteOverlay, {
      props: {
        isOpen: false,
      },
      global: {
        plugins: [router],
      },
    });

    expect(wrapper.find(".command-palette-backdrop").exists()).toBe(false);
  });

  it("renders when isOpen is true and focuses the input", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Admin"]; // Access to all routes

    const wrapper = mount(CommandPaletteOverlay, {
      props: {
        isOpen: true,
      },
      global: {
        plugins: [router],
      },
      attachTo: document.body, // Needed to test document.activeElement focus
    });

    await nextTick();
    await new Promise((resolve) => setTimeout(resolve, 10));

    expect(wrapper.find(".command-palette-backdrop").exists()).toBe(true);
    expect(wrapper.find(".command-palette-input").exists()).toBe(true);

    const input = wrapper.find(".command-palette-input").element;
    expect(document.activeElement).toBe(input);

    wrapper.unmount();
  });

  it("dynamically filters items based on the user's roles", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    // Sponsor Designer only has access to MDR, eConsent Authoring, Notifications
    authStore.rawRoles = ["Sponsor Designer"];

    const wrapper = mount(CommandPaletteOverlay, {
      props: {
        isOpen: true,
      },
      global: {
        plugins: [router],
      },
    });

    const items = wrapper.findAll(".command-item");

    // Total permitted items for Sponsor Designer:
    // 1. MDR Protocol Designer
    // 2. eConsent Authoring
    // 3. Rules Designer
    // 4. Notifications
    // Total = 4
    expect(items.length).toBe(4);

    const names = items.map((el) => el.find(".command-name").text());
    expect(names).toContain("MDR Protocol Designer");
    expect(names).toContain("eConsent Authoring");
    expect(names).toContain("Rules Designer");
    expect(names).toContain("Notifications");

    // Restricted routes like CTMS, Cryptographic Ledger, eTMF, eCRF should be omitted
    expect(names).not.toContain("CTMS Dashboard");
    expect(names).not.toContain("Cryptographic Ledger");
    expect(names).not.toContain("eTMF Document Manager");
    expect(names).not.toContain("eCRF Form Engine");
  });

  it("filters search results in real time as the user types", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Admin"]; // Access to all routes

    const wrapper = mount(CommandPaletteOverlay, {
      props: {
        isOpen: true,
      },
      global: {
        plugins: [router],
      },
    });

    const input = wrapper.find(".command-palette-input");

    // Type "CTMS"
    await input.setValue("CTMS");

    let items = wrapper.findAll(".command-item");
    expect(items.length).toBe(1);
    expect(items[0].find(".command-name").text()).toBe("CTMS Dashboard");

    // Type "informed consent"
    await input.setValue("informed consent");
    items = wrapper.findAll(".command-item");
    expect(items.length).toBe(1);
    expect(items[0].find(".command-name").text()).toBe("eConsent Authoring");

    // Type something that matches nothing
    await input.setValue("xyz123nonexistent");
    items = wrapper.findAll(".command-item");
    expect(items.length).toBe(0);
    expect(wrapper.find(".command-palette-no-results").text()).toContain(
      "No matching modules found."
    );
  });

  it("navigates list highlighting using ArrowUp/ArrowDown and resets selectedIndex on search query change", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Admin"];

    const wrapper = mount(CommandPaletteOverlay, {
      props: {
        isOpen: true,
      },
      global: {
        plugins: [router],
      },
    });

    // Default active item is the first one (MDR Protocol Designer)
    let items = wrapper.findAll(".command-item");
    expect(items[0].classes()).toContain("active");
    expect(items[1].classes()).not.toContain("active");

    const input = wrapper.find(".command-palette-input");

    // Press ArrowDown
    await input.trigger("keydown", { key: "ArrowDown" });
    expect(items[0].classes()).not.toContain("active");
    expect(items[1].classes()).toContain("active");

    // Press ArrowDown again
    await input.trigger("keydown", { key: "ArrowDown" });
    expect(items[2].classes()).toContain("active");

    // Press ArrowUp
    await input.trigger("keydown", { key: "ArrowUp" });
    expect(items[1].classes()).toContain("active");

    // Change query, selectedIndex must reset to 0
    await input.setValue("Engine");
    items = wrapper.findAll(".command-item");
    // eCRF Form Engine should be active (index 0 of current filtered list)
    expect(items[0].classes()).toContain("active");
    expect(items[0].find(".command-name").text()).toBe("eCRF Form Engine");
  });

  it("triggers routing on Enter key press", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Admin"];

    const pushSpy = vi.spyOn(router, "push");

    const wrapper = mount(CommandPaletteOverlay, {
      props: {
        isOpen: true,
      },
      global: {
        plugins: [router],
      },
    });

    const input = wrapper.find(".command-palette-input");

    // Press Enter on the first highlighted item (MDR Protocol Designer)
    await input.trigger("keydown", { key: "Enter" });
    expect(pushSpy).toHaveBeenCalledWith("/mdr");
    expect(wrapper.emitted("close")).toBeTruthy();
  });

  it("closes the command palette on Escape key, click outside, or clicking close button", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Admin"];

    // 1. Close via Close button (x)
    const wrapper1 = mount(CommandPaletteOverlay, {
      props: { isOpen: true },
      global: { plugins: [router] },
    });
    await wrapper1.find(".command-palette-close-btn").trigger("click");
    expect(wrapper1.emitted("close")).toBeTruthy();
    wrapper1.unmount();

    // 2. Close via click outside (clicking backdrop container)
    const wrapper2 = mount(CommandPaletteOverlay, {
      props: { isOpen: true },
      global: { plugins: [router] },
    });
    await wrapper2.find(".command-palette-backdrop").trigger("click");
    expect(wrapper2.emitted("close")).toBeTruthy();
    wrapper2.unmount();

    // 3. Close via Escape key
    const wrapper3 = mount(CommandPaletteOverlay, {
      props: { isOpen: true },
      global: { plugins: [router] },
    });
    const event = new KeyboardEvent("keydown", { key: "Escape" });
    document.dispatchEvent(event);
    expect(wrapper3.emitted("close")).toBeTruthy();
    wrapper3.unmount();
  });

  it("restores focus to the previously active element on unmount", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Admin"];

    // Create a dummy element to focus beforehand
    const previousFocusElement = document.createElement("input");
    document.body.appendChild(previousFocusElement);
    previousFocusElement.focus();
    expect(document.activeElement).toBe(previousFocusElement);

    const wrapper = mount(CommandPaletteOverlay, {
      props: {
        isOpen: true,
      },
      global: {
        plugins: [router],
      },
      attachTo: document.body,
    });

    await nextTick();
    await new Promise((resolve) => setTimeout(resolve, 10));

    // The palette input should now have focus
    const input = wrapper.find(".command-palette-input").element;
    expect(document.activeElement).toBe(input);

    // Unmount the component (simulating parent conditional unmount)
    wrapper.unmount();

    // Focus should be restored back to the previous element
    expect(document.activeElement).toBe(previousFocusElement);

    // Cleanup DOM
    document.body.removeChild(previousFocusElement);
  });

  it("only registers document keydown listeners when mounted (active) and removes them on unmount", () => {
    const addEventListenerSpy = vi.spyOn(document, "addEventListener");
    const removeEventListenerSpy = vi.spyOn(document, "removeEventListener");

    const wrapper = mount(CommandPaletteOverlay, {
      props: {
        isOpen: true,
      },
      global: {
        plugins: [router],
      },
    });

    // Spies should be called during mount
    expect(addEventListenerSpy).toHaveBeenCalled();
    const keydownAddedCalls = addEventListenerSpy.mock.calls.filter(call => call[0] === "keydown");
    expect(keydownAddedCalls.length).toBeGreaterThan(0);

    // Now unmount
    wrapper.unmount();

    // Spies should be called during unmount
    expect(removeEventListenerSpy).toHaveBeenCalled();
    const keydownRemovedCalls = removeEventListenerSpy.mock.calls.filter(call => call[0] === "keydown");
    expect(keydownRemovedCalls.length).toBeGreaterThan(0);

    // Restore spies
    addEventListenerSpy.mockRestore();
    removeEventListenerSpy.mockRestore();
  });
});
