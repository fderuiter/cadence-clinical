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

  it("writes zero clinical keys or GENESIS blocks when signing in and mounting App.vue outside demo mode and clears leftover clinical sandbox keys while keeping ecrf_form_draft intact", async () => {
    const pinia = createPinia();
    setActivePinia(pinia);

    // Seed leftover clinical sandbox records + an unsaved kernel form draft for the same user
    const leftoverLedgerKey =
      "cadence_user:tenant-1:user-1:clinical:site_crc:ledgerBlocks";
    const leftoverValuesKey =
      "cadence_user:tenant-1:user-1:clinical:site_crc:formValues";
    const unsavedFormDraftKey =
      "cadence_user:tenant-1:user-1:ecrf_form_draft:0192f3c4-5e6a-7b8c-9d0e-00000000f001";
    window.localStorage.setItem(
      leftoverLedgerKey,
      JSON.stringify([{ index: 0, action: "GENESIS" }])
    );
    window.localStorage.setItem(
      leftoverValuesKey,
      JSON.stringify({ vssbp: "150" })
    );
    window.localStorage.setItem(
      unsavedFormDraftKey,
      JSON.stringify({ entries: { SYSBP: { text: "136", missing: "" } } })
    );

    const authStore = useAuthStore(pinia);
    authStore.setAuth({
      authenticated: true,
      token: "prod-jwt",
      tokenParsed: {
        sub: "user-1",
        tenant_id: "tenant-1",
        preferred_username: "crc.one",
        realm_access: { roles: ["Site Investigator"] },
      },
    });

    const router = buildTestRouter();
    await router.push("/overview");
    await router.isReady();

    const wrapper = mount(App, {
      global: {
        plugins: [pinia, router],
      },
    });
    await flushPromises();

    // Zero `:clinical:` keys or GENESIS blocks exist in localStorage
    const allKeys: string[] = [];
    for (let i = 0; i < window.localStorage.length; i++) {
      const k = window.localStorage.key(i);
      if (k) allKeys.push(k);
    }
    expect(allKeys.some((k) => k.includes(":clinical:"))).toBe(false);
    for (const k of allKeys) {
      expect(window.localStorage.getItem(k) ?? "").not.toContain("GENESIS");
    }

    // Account-scoped unsaved form draft remains intact for session-expiry recovery
    expect(window.localStorage.getItem(unsavedFormDraftKey)).toContain("136");

    // Production clinicalScope omits sample study/visit defaults
    const { useClinicalScopeStore } =
      await import("../src/stores/clinicalScope");
    const scopeStore = useClinicalScopeStore(pinia);
    expect(scopeStore.activeStudyId).toBe("");
    expect(scopeStore.activeVisitId).toBe("");
    expect(scopeStore.subjects).toEqual([]);

    wrapper.unmount();
  });
});
