import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { useClinicalStore } from "../src/stores/clinical";
import { useToastStore } from "../src/stores/toasts.js";
import { executionService } from "../src/api/execution";
import EcrfView from "../src/views/EcrfView.vue";
import CrcFormRenderer from "../src/components/persona/CrcFormRenderer.vue";

vi.mock("vue-router", () => ({
  useRouter: () => ({ push: vi.fn() }),
  useRoute: () => ({ query: {} }),
}));

vi.mock("../src/api/executionApi", () => ({
  executionApi: { listSubjects: vi.fn() },
}));

vi.mock("../src/api/execution", () => ({
  executionService: {
    submitForm: vi.fn(),
    screenSubject: vi.fn(),
    listLabAlerts: vi.fn().mockResolvedValue([]),
    listQueries: vi.fn().mockResolvedValue([]),
    syncQueries: vi.fn().mockResolvedValue({}),
  },
}));

const QUERY_ACTIONS = [
  ["create-query", ["vssbp", "Please confirm the reading."], "Raising a query"],
  [
    "respond-query",
    ["vssbp", "Confirmed against source."],
    "Answering a query",
  ],
  ["close-query", ["vssbp"], "Closing a query"],
  ["reopen-query", ["vssbp"], "Reopening a query"],
];

// #4827: eCRF queries live on the sample form and sync only to the demo mock API, so other builds refuse them.
describe("eCRF queries outside a demo build", () => {
  let pinia;
  let store;
  let wrapper;

  async function mountView() {
    // Unmounted in afterEach. A debounced store action left over from an earlier test can still make that
    // test's pinia active, so re-activate this test's pinia before acting (the app has only one pinia).
    wrapper = mount(EcrfView, { global: { plugins: [pinia] } });
    await flushPromises();
    setActivePinia(pinia);
    return wrapper;
  }

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    store = useClinicalStore();
    store.formQueries = {
      vssbp: { status: "ANSWERED", message: "Check value", response: "OK" },
    };
    vi.spyOn(window, "alert").mockImplementation(() => {});
    vi.mocked(executionService.syncQueries).mockClear();
  });

  afterEach(() => {
    wrapper?.unmount();
    wrapper = undefined;
    vi.unstubAllEnvs();
    vi.restoreAllMocks();
  });

  it.each(QUERY_ACTIONS)(
    "%s is refused, changes no query and writes no ledger block",
    async (event, args, action) => {
      const view = await mountView();
      const ledger = vi.spyOn(store, "addLedgerBlock");
      const before = JSON.parse(JSON.stringify(store.formQueries));

      view.findComponent(CrcFormRenderer).vm.$emit(event, ...args);
      await flushPromises();

      expect(JSON.parse(JSON.stringify(store.formQueries))).toEqual(before);
      expect(ledger).not.toHaveBeenCalled();
      expect(view.vm.showReauthModal).toBe(false);
      expect(useToastStore().toasts[0]).toMatchObject({
        tone: "error",
        title: `${action} isn't available yet`,
      });
    }
  );

  it("does not start the query ledger sync", async () => {
    store.ledgerBlocks = [{ action: "QUERY_CREATE", synced: false }];

    await store.startSyncTimer();

    expect(store.syncInterval).toBeNull();
    expect(executionService.syncQueries).not.toHaveBeenCalled();
  });

  it("raises a query on sample data in a demo build", async () => {
    vi.stubEnv("VITE_DEMO_MODE", "true");
    const view = await mountView();

    view
      .findComponent(CrcFormRenderer)
      .vm.$emit("create-query", "vsdpb", "Diastolic looks high.");
    await flushPromises();

    expect(store.formQueries.vsdpb).toMatchObject({
      status: "OPEN",
      message: "Diastolic looks high.",
    });
  });
});
