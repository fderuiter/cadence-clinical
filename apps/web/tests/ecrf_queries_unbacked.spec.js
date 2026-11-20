import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { useClinicalStore } from "../src/stores/clinical";
import { useToastStore } from "../src/stores/toasts.js";
import EcrfView from "../src/views/EcrfView.vue";
import CrcFormRenderer from "../src/components/persona/CrcFormRenderer.vue";

vi.mock("vue-router", () => ({
  onBeforeRouteLeave: () => {},
  useRouter: () => ({ push: vi.fn() }),
  useRoute: () => ({ query: {} }),
}));

vi.mock("../src/api/executionApi", () => ({
  executionApi: { listSubjects: vi.fn() },
}));

const QUERY_ACTIONS = [
  ["createQuery", ["vssbp", "Please confirm the reading."], "Raising a query"],
  ["respondQuery", ["vssbp", "Confirmed against source."], "Answering a query"],
  ["closeQuery", ["vssbp"], "Closing a query"],
  ["reopenQuery", ["vssbp"], "Reopening a query"],
];

// #4827: eCRF queries live on the sample form, so builds other than a demo build hide the sample form and refuse them.
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
  });

  afterEach(() => {
    wrapper?.unmount();
    wrapper = undefined;
    vi.unstubAllEnvs();
    vi.restoreAllMocks();
  });

  it.each(QUERY_ACTIONS)(
    "%s hides the sample form, is refused, changes no query and writes no ledger block",
    async (methodName, args, action) => {
      const view = await mountView();
      expect(view.findComponent(CrcFormRenderer).exists()).toBe(false);

      const ledger = vi.spyOn(store, "addLedgerBlock");
      const before = JSON.parse(JSON.stringify(store.formQueries));

      view.vm[methodName](...args);
      await flushPromises();

      expect(JSON.parse(JSON.stringify(store.formQueries))).toEqual(before);
      expect(ledger).not.toHaveBeenCalled();
      expect(window.alert).not.toHaveBeenCalled();
      expect(useToastStore().toasts[0]).toMatchObject({
        tone: "error",
        title: `${action} isn't available yet`,
      });
    }
  );

  it("raises a query on sample data in a demo build and replaces empty-message alerts with toasts", async () => {
    vi.stubEnv("VITE_DEMO_MODE", "true");
    const view = await mountView();
    const renderer = view.findComponent(CrcFormRenderer);
    expect(renderer.exists()).toBe(true);

    renderer.vm.$emit("create-query", "vsdpb", "");
    await flushPromises();
    expect(window.alert).not.toHaveBeenCalled();
    expect(useToastStore().toasts[0]).toMatchObject({
      tone: "error",
      title: "Message required",
    });

    renderer.vm.$emit("create-query", "vsdpb", "Diastolic looks high.");
    await flushPromises();

    expect(store.formQueries.vsdpb).toMatchObject({
      status: "OPEN",
      message: "Diastolic looks high.",
    });

    renderer.vm.$emit("respond-query", "vsdpb", "");
    await flushPromises();
    expect(window.alert).not.toHaveBeenCalled();
    expect(
      useToastStore().toasts.some((t) => t.title === "Response required")
    ).toBe(true);
  });
});
