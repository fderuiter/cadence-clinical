import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { router } from "../../src/router";
import { useAuthStore } from "../../src/stores/auth";

// The sign-in and access-denied screens are always available (#4529: every screen a user can open has a
// view test).

describe("Sign-in and access-denied screens", () => {
  beforeEach(async () => {
    setActivePinia(createPinia());
    await router.push("/login");
  });

  afterEach(() => {
    vi.unstubAllEnvs();
    vi.resetModules();
  });

  async function mountLogin() {
    const { default: LoginView } =
      await import("../../src/views/LoginView.vue");
    const wrapper = mount(LoginView, { global: { plugins: [router] } });
    await flushPromises();
    return wrapper;
  }

  it("offers only Keycloak sign-in outside a demo build", async () => {
    const wrapper = await mountLogin();

    expect(wrapper.find(".btn-login-keycloak").exists()).toBe(true);
    expect(wrapper.find(".btn-login-demo").exists()).toBe(false);
  });

  it("offers the offline demo sign-in instead of Keycloak in a demo build", async () => {
    vi.stubEnv("VITE_DEMO_MODE", "true");
    vi.resetModules();

    const wrapper = await mountLogin();

    expect(wrapper.find(".btn-login-demo").exists()).toBe(true);
    expect(wrapper.find(".btn-login-keycloak").exists()).toBe(false);
  });

  it("starts the Keycloak redirect directly, with no offline PIN step", async () => {
    const { setKeycloakClient } = await import("../../src/auth/keycloak.js");
    const login = vi.fn().mockResolvedValue(undefined);
    setKeycloakClient({ login });
    const wrapper = await mountLogin();

    await wrapper.find(".btn-login-keycloak").trigger("click");
    await flushPromises();

    expect(login).toHaveBeenCalledTimes(1);
    expect(wrapper.text()).not.toContain("PIN");
    expect(useAuthStore().isAuthenticated).toBe(false);
    setKeycloakClient(null);
  });

  it("names the signed-in user and roles on the access-denied screen", async () => {
    const { default: ForbiddenView } =
      await import("../../src/views/ForbiddenView.vue");
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Auditor"];

    const wrapper = mount(ForbiddenView, { global: { plugins: [router] } });

    expect(wrapper.find("h2").text()).toBe("403 - Access Denied");
    expect(wrapper.text()).toContain(authStore.normalizedRoles.join(", "));
  });

  it("signs the user out from the access-denied screen", async () => {
    const { default: ForbiddenView } =
      await import("../../src/views/ForbiddenView.vue");
    const authStore = useAuthStore();
    const logout = vi.spyOn(authStore, "logout").mockResolvedValue();
    const push = vi.spyOn(router, "push");

    const wrapper = mount(ForbiddenView, { global: { plugins: [router] } });
    await wrapper.findAll("button")[1].trigger("click");
    await flushPromises();

    expect(logout).toHaveBeenCalled();
    expect(push).toHaveBeenCalledWith("/login");
  });
});
