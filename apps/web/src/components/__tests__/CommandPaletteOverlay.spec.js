import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createRouter, createWebHistory } from "vue-router";
import AppShell from "../AppShell.vue";
import CommandPaletteOverlay from "../CommandPaletteOverlay.vue";
import { useAuthStore } from "../../stores/auth";
import { kernelApi } from "../../api/kernelApi";

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

describe("Global Command Palette Shell Integration", () => {
  let pinia;

  beforeEach(async () => {
    pinia = createPinia();
    setActivePinia(pinia);
    await router.push("/ecrf");
    await router.isReady();
  });

  it("opens from AppShell #btn-open-palette and Cmd+K on non-canvas screens", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["CRC"];

    const wrapper = mount(AppShell, {
      global: { plugins: [pinia, router] },
    });

    expect(wrapper.find("#canvas-palette").exists()).toBe(false);
    await wrapper.find("#btn-open-palette").trigger("click");
    expect(wrapper.find("#canvas-palette").exists()).toBe(true);

    // Site CRC Site readiness navigates to /ssu
    const pushSpy = vi.spyOn(router, "push");
    await wrapper.find("#canvas-palette-input").setValue("Site readiness");
    await wrapper.find("[role='option']").trigger("click");
    expect(pushSpy).toHaveBeenCalledWith("/ssu");
    expect(wrapper.find("#canvas-palette").exists()).toBe(false);

    // Reopen with global Cmd+K shortcut
    window.dispatchEvent(
      new KeyboardEvent("keydown", { key: "k", metaKey: true })
    );
    await wrapper.vm.$nextTick();
    expect(wrapper.find("#canvas-palette").exists()).toBe(true);

    wrapper.unmount();
  });

  it("keeps Open study and static destinations usable when kernelApi.search fails", async () => {
    vi.useFakeTimers();
    const searchSpy = vi
      .spyOn(kernelApi, "search")
      .mockRejectedValueOnce(new Error("Kernel unavailable"));

    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Auditor"];

    const wrapper = mount(CommandPaletteOverlay, {
      props: { isOpen: true },
      global: { plugins: [pinia, router] },
    });

    await wrapper.find("#canvas-palette-input").setValue("CAD-404");
    vi.advanceTimersByTime(250);
    await flushPromises();

    expect(wrapper.find("#canvas-palette-kernel-note").text()).toContain(
      "Studies can't be searched right now"
    );
    expect(wrapper.find(".palette-group").text()).toBe("Open study");
    expect(wrapper.find(".palette-label").text()).toBe("CAD-404");

    searchSpy.mockRestore();
    vi.useRealTimers();
  });
});
