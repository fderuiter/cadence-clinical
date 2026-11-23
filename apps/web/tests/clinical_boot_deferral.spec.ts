import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import App from "../src/App.vue";
import LoginView from "../src/views/LoginView.vue";
import OverviewView from "../src/views/OverviewView.vue";
import PlansView from "../src/views/PlansView.vue";
import MdrView from "../src/views/MdrView.vue";
import { isScreenAvailable } from "../src/router/availability.js";
import { useAuthStore } from "../src/stores/auth.js";
import { scopedKey, storageScopeOf } from "../src/utils/userScopedStorage.js";
import { signInAs } from "./helpers/signIn.js";

function buildTestRouter() {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: "/login", name: "login", component: LoginView },
      { path: "/overview", name: "overview", component: OverviewView },
      { path: "/plans", name: "plans", component: PlansView },
      { path: "/mdr", name: "mdr", component: MdrView },
      { path: "/:pathMatch(.*)*", component: { template: "<div />" } },
    ],
  });
}

describe("Clinical store cold-boot deferral & synchronous session bridge", () => {
  beforeEach(() => {
    if (typeof window !== "undefined" && window.localStorage) {
      window.localStorage.clear();
      window.sessionStorage.clear();
    }
  });

  afterEach(() => {
    vi.unstubAllEnvs();
  });

  it.each(["/login", "/overview", "/plans"])(
    "does not instantiate stores/clinical when mounting App.vue on %s outside demo builds",
    async (targetPath) => {
      const pinia = createPinia();
      setActivePinia(pinia);
      const authStore = useAuthStore(pinia);
      if (targetPath !== "/login") {
        signInAs(authStore, ["Sponsor Admin"]);
      }

      const router = buildTestRouter();
      await router.push(targetPath);
      await router.isReady();

      const wrapper = mount(App, {
        global: {
          plugins: [pinia, router],
        },
      });
      await flushPromises();

      expect(
        (pinia as unknown as { _s: Map<string, unknown> })._s.has("clinical")
      ).toBe(false);
      expect(isScreenAvailable("mdr")).toBe(false);

      wrapper.unmount();
    }
  );

  it("dynamically loads useClinicalStore, records GENESIS, and unlocks /mdr in demo builds", async () => {
    vi.stubEnv("VITE_DEMO_MODE", "true");

    const pinia = createPinia();
    setActivePinia(pinia);
    const authStore = useAuthStore(pinia);
    authStore.startDemoSession();

    const router = buildTestRouter();
    await router.push("/overview");
    await router.isReady();

    const wrapper = mount(App, {
      global: {
        plugins: [pinia, router],
      },
    });
    await flushPromises();

    const storeMap = (pinia as unknown as { _s: Map<string, unknown> })._s;
    await vi.waitFor(() => {
      expect(storeMap.has("clinical")).toBe(true);
    });
    expect(isScreenAvailable("mdr")).toBe(true);

    const { useClinicalStore } = await import("../src/stores/clinical");
    const clinicalStore = useClinicalStore(pinia);
    await vi.waitFor(() => {
      expect(clinicalStore.ledgerBlocks.length).toBeGreaterThanOrEqual(1);
    });
    expect(clinicalStore.ledgerBlocks[0].action).toBe("GENESIS");

    wrapper.unmount();
  });

  it("keeps unmounted clinical stores lazy across auth transitions, hydrates on first call, and resets mounted stores synchronously", async () => {
    vi.stubEnv("VITE_DEMO_MODE", "true");

    const pinia = createPinia();
    setActivePinia(pinia);
    const storeMap = (pinia as unknown as { _s: Map<string, unknown> })._s;

    const authStore = useAuthStore(pinia);
    authStore.startDemoSession();
    expect(storeMap.has("clinical")).toBe(false);

    // Switch persona before clinical store has ever been mounted on this Pinia instance.
    expect(authStore.switchPersona("site_crc")).toBe(true);
    expect(storeMap.has("clinical")).toBe(false);

    // Seed persona-scoped storage for site_crc while clinical store is still unmounted.
    const crcFormValuesKey = scopedKey(
      storageScopeOf(authStore),
      "clinical",
      "site_crc",
      "formValues"
    );
    expect(crcFormValuesKey).toBeTruthy();
    window.localStorage.setItem(
      crcFormValuesKey!,
      JSON.stringify({ VS_SYSBP: 142 })
    );

    // First useClinicalStore call lazily hydrates the active persona's persisted state.
    const { useClinicalStore } = await import("../src/stores/clinical");
    const clinicalStore = useClinicalStore(pinia);
    expect(storeMap.has("clinical")).toBe(true);
    expect(clinicalStore.formValues.VS_SYSBP).toBe(142);

    // Mounted clinical store updates synchronously on switchPersona (no microtasks).
    expect(authStore.switchPersona("auditor")).toBe(true);
    expect(clinicalStore.formValues.VS_SYSBP).toBeUndefined();

    // Mutate mounted clinical store and verify logout resets it synchronously.
    clinicalStore.formValues.VS_SYSBP = 160;
    await authStore.logout();
    expect(clinicalStore.formValues.VS_SYSBP).toBeUndefined();
  });
});
