import { describe, it, expect, vi, beforeEach } from "vitest";
import { shallowMount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import viteConfig from "../../vite.config.js";
import App from "../../src/App.vue";
import { kernelApi } from "../../src/api/kernelApi";
import {
  clearKernelStudyCache,
  findKernelStudyRef,
} from "../../src/canvas/kernelStudy";
import { useAuthStore } from "../../src/stores/auth.js";
import {
  ensureUsdmSchemas,
  registerUsdmSchemas,
} from "../../src/stores/clinical";

function resolveManualChunk(id: string): string | undefined {
  const output = viteConfig.build?.rollupOptions?.output;
  if (
    !output ||
    Array.isArray(output) ||
    typeof output.manualChunks !== "function"
  ) {
    throw new Error("Expected manualChunks function in vite.config.js");
  }
  return output.manualChunks(id, {
    getModuleInfo: () => null,
  }) as string | undefined;
}

describe("vite.config.js manualChunks split", () => {
  const legacyStubs = [
    "/repo/apps/web/src/api/auditor.ts",
    "/repo/apps/web/src/api/designer.ts",
    "/repo/apps/web/src/api/econsent.ts",
    "/repo/apps/web/src/api/fileshare.ts",
    "/repo/apps/web/src/api/ingestionClient.ts",
    "/repo/apps/web/src/api/interop.ts",
    "/repo/apps/web/src/api/notifications.ts",
    "/repo/apps/web/src/api/soaClient.ts",
    "/repo/apps/web/src/api/statisticalComputing.ts",
    "/repo/apps/web/src/api/terminologyClient.ts",
    "/repo/apps/web/src/services/api.js",
  ];

  const liveClients = [
    "/repo/apps/web/src/api/apiClient.js",
    "/repo/apps/web/src/api/authorization.js",
    "/repo/apps/web/src/api/changeReason.ts",
    "/repo/apps/web/src/api/client.ts",
    "/repo/apps/web/src/api/ctmsApi.ts",
    "/repo/apps/web/src/api/ecrfFormsApi.ts",
    "/repo/apps/web/src/api/ecrfQueriesApi.ts",
    "/repo/apps/web/src/api/etmf.ts",
    "/repo/apps/web/src/api/executionApi.ts",
    "/repo/apps/web/src/api/kernelApi.ts",
    "/repo/apps/web/src/api/knowledge.ts",
    "/repo/apps/web/src/api/observationValue.ts",
    "/repo/apps/web/src/api/ticketsApi.ts",
    "/repo/apps/web/src/api/training.ts",
  ];

  it("routes all 11 unbacked stubs to api-legacy-stubs", () => {
    for (const moduleId of legacyStubs) {
      expect(resolveManualChunk(moduleId), moduleId).toBe("api-legacy-stubs");
    }
  });

  it("keeps all 14 live .NET clients and helpers in api-services", () => {
    for (const moduleId of liveClients) {
      expect(resolveManualChunk(moduleId), moduleId).toBe("api-services");
    }
  });

  it("keeps clinical store and usdm-schemas in isolated chunks", () => {
    expect(resolveManualChunk("/repo/apps/web/src/stores/clinical.ts")).toBe(
      "store-clinical"
    );
    expect(resolveManualChunk("/repo/packages/usdm-schemas/src/index.ts")).toBe(
      "workspace-usdm"
    );
  });
});

describe("lazy demo state on application boot", () => {
  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    clearKernelStudyCache();
  });

  it("skips instantiating clinical store and GENESIS ledger block on non-demo boot and logout", async () => {
    vi.stubEnv("VITE_DEMO_MODE", "false");
    const pinia = createPinia();
    setActivePinia(pinia);

    const wrapper = shallowMount(App, {
      global: { plugins: [pinia] },
    });
    await flushPromises();

    const storesMap = (pinia as unknown as { _s: Map<string, unknown> })._s;
    expect(storesMap.has("clinical")).toBe(false);

    const searchSpy = vi.spyOn(kernelApi, "search").mockResolvedValue([
      {
        ref: "study:0192f3c4-5e6a-7b8c-9d0e-000000000001@1",
        kind: "study",
        title: "Study",
        code: "CAD-101",
        detail: null,
        exact_code: true,
        rank: 1,
      },
    ]);
    await expect(findKernelStudyRef("CAD-101")).resolves.toBe(
      "study:0192f3c4-5e6a-7b8c-9d0e-000000000001@1"
    );
    expect(searchSpy).toHaveBeenCalledTimes(1);

    const auth = useAuthStore(pinia);
    await auth.logout();

    expect(storesMap.has("clinical")).toBe(false);
    await findKernelStudyRef("CAD-101");
    expect(searchSpy).toHaveBeenCalledTimes(2);

    searchSpy.mockRestore();
    wrapper.unmount();
  });

  it("dynamically loads clinical store and writes GENESIS ledger block in demo mode", async () => {
    vi.stubEnv("VITE_DEMO_MODE", "true");
    const pinia = createPinia();
    setActivePinia(pinia);

    const wrapper = shallowMount(App, {
      global: { plugins: [pinia] },
    });
    await flushPromises();

    const storesMap = (
      pinia as unknown as {
        _s: Map<string, { ledgerBlocks: Array<{ action: string }> }>;
      }
    )._s;
    expect(storesMap.has("clinical")).toBe(true);
    expect(storesMap.get("clinical")?.ledgerBlocks[0]?.action).toBe("GENESIS");
    wrapper.unmount();
  });

  it("lazy-loads usdm-schemas on demand via ensureUsdmSchemas", async () => {
    registerUsdmSchemas(null as never);
    const loaded = await ensureUsdmSchemas();
    expect(typeof loaded.validateUsdmGraph).toBe("function");
    expect(loaded.USDMStudySchema).toBeDefined();
  });
});
