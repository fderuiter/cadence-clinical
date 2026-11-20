import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { reactive } from "vue";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import EcrfView from "../src/views/EcrfView.vue";
import EcrfFormPanel from "../src/components/ecrf/EcrfFormPanel.vue";
import { ecrfFormsApi } from "../src/api/ecrfFormsApi";
import { ecrfQueriesApi } from "../src/api/ecrfQueriesApi";

let leaveGuard = null;
let updateGuard = null;
const pushSpy = vi.fn();
const replaceSpy = vi.fn();
const routeQuery = reactive({
  formId: "0192f3c4-5e6a-7b8c-9d0e-00000000f001",
  enrollmentId: "",
});
vi.mock("vue-router", () => ({
  onBeforeRouteLeave: (guard) => {
    leaveGuard = guard;
  },
  onBeforeRouteUpdate: (guard) => {
    updateGuard = guard;
  },
  useRouter: () => ({ push: pushSpy, replace: replaceSpy }),
  useRoute: () => ({ query: routeQuery }),
}));

vi.mock("../src/api/executionApi", () => ({
  executionApi: { listSubjects: vi.fn() },
}));

vi.mock("../src/api/ecrfQueriesApi", async (importOriginal) => ({
  ...(await importOriginal()),
  ecrfQueriesApi: {
    raise: vi.fn(),
    listForForm: vi.fn(async () => []),
    listForEnrollment: vi.fn(async () => []),
    listForStudy: vi.fn(async () => []),
    transition: vi.fn(),
  },
}));

vi.mock("../src/api/ecrfFormsApi", async (importOriginal) => ({
  ...(await importOriginal()),
  ecrfFormsApi: { openForm: vi.fn(), getForm: vi.fn(), saveValues: vi.fn() },
}));

function formFixture(formId) {
  return {
    form_id: formId,
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
  };
}

// ADR-2622: the eCRF screen enters values in a kernel form instance, never in the deleted FormSubmission endpoints.
describe("eCRF screen: the participant's case report form", () => {
  const FIRST_FORM_ID = "0192f3c4-5e6a-7b8c-9d0e-00000000f001";
  const SECOND_FORM_ID = "0192f3c4-5e6a-7b8c-9d0e-00000000f002";
  let wrapper;

  beforeEach(() => {
    leaveGuard = null;
    updateGuard = null;
    pushSpy.mockReset();
    replaceSpy.mockReset();
    routeQuery.formId = FIRST_FORM_ID;
    routeQuery.enrollmentId = "";
    setActivePinia(createPinia());
    vi.mocked(ecrfQueriesApi.listForForm).mockResolvedValue([]);
    vi.mocked(ecrfFormsApi.getForm).mockImplementation(async (id) =>
      formFixture(id)
    );
  });

  afterEach(() => {
    wrapper?.unmount();
    vi.restoreAllMocks();
  });

  function mountView() {
    return mount(EcrfView, {
      global: {
        stubs: {
          DesignerSchemaPanel: true,
          EhrReconciliationDrawer: true,
          "router-link": true,
        },
      },
    });
  }

  it("loads the form the route names, and guards leaving or switching forms while a page is unsaved", async () => {
    wrapper = mountView();
    await flushPromises();

    const panel = wrapper.findComponent(EcrfFormPanel);
    expect(panel.props("formId")).toBe(FIRST_FORM_ID);
    expect(panel.props("enrollmentId")).toBeNull();
    expect(ecrfFormsApi.getForm).toHaveBeenCalledWith(FIRST_FORM_ID);
    expect(wrapper.find("#btn-pi-signoff").exists()).toBe(false);

    await wrapper.find("#ecrf-item-COMMENT").setValue("note");
    await flushPromises();
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    expect(leaveGuard()).toBe(false);
    expect(confirm).toHaveBeenCalledTimes(1);

    // Same-route formId switch or clear confirms via onBeforeRouteUpdate.
    expect(typeof updateGuard).toBe("function");
    expect(
      updateGuard(
        { query: { formId: SECOND_FORM_ID } },
        { query: { formId: FIRST_FORM_ID } }
      )
    ).toBe(false);
    expect(confirm).toHaveBeenCalledTimes(2);

    expect(
      updateGuard({ query: {} }, { query: { formId: FIRST_FORM_ID } })
    ).toBe(false);
    expect(confirm).toHaveBeenCalledTimes(3);

    // Unrelated query updates keeping the same formId do not prompt.
    expect(
      updateGuard(
        { query: { formId: FIRST_FORM_ID, itemId: "COMMENT" } },
        { query: { formId: FIRST_FORM_ID } }
      )
    ).toBeUndefined();
    expect(confirm).toHaveBeenCalledTimes(3);

    confirm.mockReturnValue(true);
    expect(
      updateGuard(
        { query: { formId: SECOND_FORM_ID } },
        { query: { formId: FIRST_FORM_ID } }
      )
    ).toBeUndefined();

    const event = new Event("beforeunload", { cancelable: true });
    window.dispatchEvent(event);
    expect(event.defaultPrevented).toBe(true);
  });

  it("syncs route.query.formId with router.replace on @form-loaded and @closed without duplicate prompts", async () => {
    routeQuery.formId = "";
    replaceSpy.mockImplementation(async ({ query }) => {
      routeQuery.formId = query.formId ?? "";
    });

    wrapper = mountView();
    await flushPromises();

    await wrapper.find("#ecrf-form-id").setValue(SECOND_FORM_ID);
    await wrapper.find("#ecrf-form-load").trigger("submit");
    await flushPromises();

    expect(replaceSpy).toHaveBeenCalledWith({
      query: expect.objectContaining({ formId: SECOND_FORM_ID }),
    });
    expect(routeQuery.formId).toBe(SECOND_FORM_ID);

    // Syncing URL to the already-loaded form does not trigger confirm even if dirty.
    await wrapper.find("#ecrf-item-COMMENT").setValue("unsaved entry");
    await flushPromises();
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(true);
    expect(
      updateGuard({ query: { formId: SECOND_FORM_ID } }, { query: {} })
    ).toBeUndefined();
    expect(confirm).not.toHaveBeenCalled();

    // Closing the form confirms once in closeForm and removes formId from route.query via router.replace.
    replaceSpy.mockClear();
    await wrapper.find("#ecrf-form-close").trigger("click");
    await flushPromises();

    expect(confirm).toHaveBeenCalledTimes(1);
    expect(replaceSpy).toHaveBeenCalledTimes(1);
    expect(replaceSpy.mock.calls[0][0].query.formId).toBeUndefined();
    expect(
      updateGuard({ query: {} }, { query: { formId: SECOND_FORM_ID } })
    ).toBeUndefined();
    expect(confirm).toHaveBeenCalledTimes(1);
  });
});
