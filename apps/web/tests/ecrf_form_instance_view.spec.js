import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import EcrfView from "../src/views/EcrfView.vue";
import EcrfFormPanel from "../src/components/ecrf/EcrfFormPanel.vue";
import { ecrfFormsApi } from "../src/api/ecrfFormsApi";

let leaveGuard = null;
const routeQuery = {
  formId: "0192f3c4-5e6a-7b8c-9d0e-00000000f001",
  enrollmentId: "",
};
vi.mock("vue-router", () => ({
  onBeforeRouteLeave: (guard) => {
    leaveGuard = guard;
  },
  useRouter: () => ({ push: vi.fn() }),
  useRoute: () => ({ query: routeQuery }),
}));

vi.mock("../src/api/executionApi", () => ({
  executionApi: { listSubjects: vi.fn() },
}));

vi.mock("../src/api/ecrfFormsApi", async (importOriginal) => ({
  ...(await importOriginal()),
  ecrfFormsApi: { openForm: vi.fn(), getForm: vi.fn(), saveValues: vi.fn() },
}));

// ADR-2622: the eCRF screen enters values in a kernel form instance, never in the deleted FormSubmission endpoints.
describe("eCRF screen: the participant's case report form", () => {
  let wrapper;

  beforeEach(() => {
    setActivePinia(createPinia());
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue({
      form_id: routeQuery.formId,
      collection_spec_id: "VS",
      planned_activity_id: null,
      collected_at: "2027-03-14T09:00:00Z",
      version: 1,
      complete: true,
      missing_required: [],
      items: [
        {
          item_id: "COMMENT",
          concept: null,
          unit: null,
          value_kind: "Text",
          required: false,
          value: null,
          observation_id: null,
          revision: 0,
          recorded_by: null,
          recorded_at: null,
        },
      ],
      locks: [],
      assertions: [],
      signatures: [],
    });
  });

  afterEach(() => {
    wrapper?.unmount();
    vi.restoreAllMocks();
  });

  it("loads the form the route names, and guards leaving while a page is unsaved", async () => {
    wrapper = mount(EcrfView, {
      global: {
        stubs: {
          DesignerSchemaPanel: true,
          EhrReconciliationDrawer: true,
          "router-link": true,
        },
      },
    });
    await flushPromises();

    const panel = wrapper.findComponent(EcrfFormPanel);
    expect(panel.props("formId")).toBe(routeQuery.formId);
    expect(panel.props("enrollmentId")).toBeNull();
    expect(ecrfFormsApi.getForm).toHaveBeenCalledWith(routeQuery.formId);
    expect(wrapper.find("#btn-pi-signoff").exists()).toBe(false);

    await wrapper.find("#ecrf-item-COMMENT").setValue("note");
    await flushPromises();
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    expect(leaveGuard()).toBe(false);
    expect(confirm).toHaveBeenCalled();

    const event = new Event("beforeunload", { cancelable: true });
    window.dispatchEvent(event);
    expect(event.defaultPrevented).toBe(true);
  });
});
