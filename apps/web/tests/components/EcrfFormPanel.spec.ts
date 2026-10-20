import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises, type VueWrapper } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import EcrfFormPanel from "../../src/components/ecrf/EcrfFormPanel.vue";
import { ecrfFormsApi } from "../../src/api/ecrfFormsApi";
import { ApiError } from "../../src/api/client";
import { ecrfQueriesApi } from "../../src/api/ecrfQueriesApi";

vi.mock("../../src/api/ecrfQueriesApi", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("../../src/api/ecrfQueriesApi")>();
  return {
    ...actual,
    ecrfQueriesApi: {
      raise: vi.fn(),
      listForForm: vi.fn(),
      listForEnrollment: vi.fn(),
      transition: vi.fn(),
    },
  };
});

vi.mock("../../src/api/ecrfFormsApi", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("../../src/api/ecrfFormsApi")>();
  return {
    ...actual,
    ecrfFormsApi: { openForm: vi.fn(), getForm: vi.fn(), saveValues: vi.fn() },
  };
});

const FORM_ID = "0192f3c4-5e6a-7b8c-9d0e-00000000f001";
const OBS_ID = "0192f3c4-5e6a-7b8c-9d0e-00000000a001";

function item(overrides: Record<string, unknown>) {
  return {
    item_id: "X",
    concept: null,
    unit: null,
    value_kind: null,
    required: false,
    value: null,
    observation_id: null,
    revision: 0,
    recorded_by: null,
    recorded_at: null,
    ...overrides,
  };
}

function form(overrides: Record<string, unknown> = {}) {
  return {
    form_id: FORM_ID,
    enrollment_id: "e",
    participant_id: "p",
    study_id: "s",
    study_site_id: "ss",
    protocol_version_id: "pv",
    collection_spec_id: "VS",
    planned_activity_id: "WEEK2",
    collected_at: "2027-03-14T09:00:00Z",
    opened_by: "crc1",
    opened_at: "2027-03-14T09:00:00Z",
    version: 2,
    complete: false,
    missing_required: ["DIABP"],
    // Layout order, not alphabetical.
    items: [
      item({
        item_id: "SYSBP",
        concept: "C25298@1",
        unit: "mm[Hg]",
        value_kind: "Quantity",
        required: true,
        value: { type: "quantity", value: 120, unit: "mm[Hg]" },
        observation_id: OBS_ID,
        revision: 3,
        recorded_by: "crc1",
        recorded_at: "2027-03-14T09:05:00Z",
      }),
      item({
        item_id: "DIABP",
        unit: "mm[Hg]",
        value_kind: "Quantity",
        required: true,
      }),
      item({ item_id: "COMMENT", value_kind: "Text" }),
    ],
    locks: [],
    assertions: [],
    signatures: [],
    ...overrides,
  };
}

function resetQueries() {
  for (const fn of Object.values(ecrfQueriesApi)) vi.mocked(fn).mockReset();
  vi.mocked(ecrfQueriesApi.listForForm).mockResolvedValue([]);
}

describe("EcrfFormPanel: a kernel form instance saved a page at a time (ADR-2622)", () => {
  let wrapper: VueWrapper | undefined;

  beforeEach(() => {
    setActivePinia(createPinia());
    vi.mocked(ecrfFormsApi.getForm).mockReset();
    vi.mocked(ecrfFormsApi.openForm).mockReset();
    vi.mocked(ecrfFormsApi.saveValues).mockReset();
    resetQueries();
  });

  afterEach(() => {
    wrapper?.unmount();
    wrapper = undefined;
  });

  async function mountWith(props: Record<string, unknown> = {}) {
    wrapper = mount(EcrfFormPanel, { props });
    await flushPromises();
    return wrapper;
  }

  it("renders the items in the order the form returns them, with required markers and what is missing", async () => {
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(form() as never);
    const view = await mountWith({ formId: FORM_ID });

    expect(ecrfFormsApi.getForm).toHaveBeenCalledWith(FORM_ID);
    const rows = view.findAll("[data-item]");
    expect(rows.map((r) => r.attributes("data-item"))).toEqual([
      "SYSBP",
      "DIABP",
      "COMMENT",
    ]);
    expect(rows[0].find(".ecrf-required").exists()).toBe(true);
    expect(rows[2].find(".ecrf-required").exists()).toBe(false);
    expect(
      (view.find("#ecrf-item-SYSBP").element as HTMLInputElement).value
    ).toBe("120");
    expect(view.find('[data-testid="ecrf-form-status"]').text()).toContain(
      "1 required item without a value (DIABP)"
    );
    expect(view.find('[data-testid="ecrf-no-signatures"]').text()).toContain(
      "signing from this screen is not available yet"
    );
  });

  it("saves only the edited items, each with the revision it was edited from, and reloads the form", async () => {
    vi.mocked(ecrfFormsApi.getForm)
      .mockResolvedValueOnce(form() as never)
      .mockResolvedValueOnce(
        form({ version: 3, complete: true, missing_required: [] }) as never
      );
    vi.mocked(ecrfFormsApi.saveValues).mockResolvedValue({
      form_id: FORM_ID,
      version: 3,
      values: [
        {
          item_id: "SYSBP",
          observation_id: OBS_ID,
          value: { type: "quantity", value: "128", unit: "mm[Hg]" },
          revision: 4,
          changed: true,
        },
        {
          item_id: "DIABP",
          observation_id: "o2",
          value: { type: "quantity", value: "82", unit: "mm[Hg]" },
          revision: 1,
          changed: true,
        },
      ],
    } as never);
    const view = await mountWith({ formId: FORM_ID });

    await view.find("#ecrf-item-SYSBP").setValue("128");
    await view.find("#ecrf-item-DIABP").setValue("82");
    expect(view.find("#ecrf-form-save").text()).toBe("Save page (2)");
    expect(view.emitted("dirty-change")?.at(-1)).toEqual([true]);
    await view.find("#ecrf-save-reason").setValue("Initial entry");
    await view.find("#ecrf-form-page").trigger("submit");
    await flushPromises();

    const call = vi.mocked(ecrfFormsApi.saveValues).mock.calls[0][0];
    expect(call.formId).toBe(FORM_ID);
    expect(call.reason).toBe("Initial entry");
    expect(call.idempotencyKey).toMatch(/[0-9a-f-]{36}/);
    expect(call.values).toEqual([
      {
        item_id: "SYSBP",
        value: {
          type: "quantity",
          value: "128",
          unit: "mm[Hg]",
          comparator: null,
        },
        expected_revision: 3,
        observation_id: null,
        observed_at: null,
      },
      {
        item_id: "DIABP",
        value: {
          type: "quantity",
          value: "82",
          unit: "mm[Hg]",
          comparator: null,
        },
        expected_revision: 0,
        observation_id: null,
        observed_at: null,
      },
    ]);
    expect(ecrfFormsApi.getForm).toHaveBeenCalledTimes(2);
    expect(view.find('[data-testid="ecrf-form-outcome"]').text()).toContain(
      "Page saved: 2 values recorded, form version 3."
    );
    expect(view.find('[data-testid="ecrf-form-status"]').text()).toBe(
      "Complete"
    );
  });

  it("records a value as missing with its reason", async () => {
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(form() as never);
    vi.mocked(ecrfFormsApi.saveValues).mockResolvedValue({
      form_id: FORM_ID,
      version: 3,
      values: [],
    } as never);
    const view = await mountWith({ formId: FORM_ID });

    await view
      .find('select[aria-label="Why DIABP is missing"]')
      .setValue("NotDone");
    await view.find("#ecrf-save-reason").setValue("Not measured");
    await view.find("#ecrf-form-page").trigger("submit");
    await flushPromises();

    expect(vi.mocked(ecrfFormsApi.saveValues).mock.calls[0][0].values).toEqual([
      {
        item_id: "DIABP",
        value: { type: "missing", reason: "NotDone" },
        expected_revision: 0,
        observation_id: null,
        observed_at: null,
      },
    ]);
  });

  it("saves nothing and says so when an entry can't be read", async () => {
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(form() as never);
    const view = await mountWith({ formId: FORM_ID });

    await view.find("#ecrf-item-DIABP").setValue("high");
    await view.find("#ecrf-save-reason").setValue("Initial entry");
    await view.find("#ecrf-form-page").trigger("submit");
    await flushPromises();

    expect(ecrfFormsApi.saveValues).not.toHaveBeenCalled();
    expect(view.find("#ecrf-item-DIABP-error").text()).toContain(
      "Enter a number in mm[Hg]"
    );
  });

  it("tells the user to reload when someone saved first (409), and reloading discards the unsaved entries", async () => {
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(form() as never);
    vi.mocked(ecrfFormsApi.saveValues).mockRejectedValue(
      new ApiError("The form changed since you opened it.", 409, null, {
        code: "K.Collection.RevisionStale",
      })
    );
    const view = await mountWith({ formId: FORM_ID });

    await view.find("#ecrf-item-SYSBP").setValue("130");
    await view.find("#ecrf-save-reason").setValue("Correction");
    await view.find("#ecrf-form-page").trigger("submit");
    await flushPromises();

    const outcome = view.find('[data-testid="ecrf-form-outcome"]');
    expect(outcome.attributes("role")).toBe("alert");
    expect(outcome.text()).toContain("nothing was saved");
    expect(outcome.text()).toContain("Reload the form");
    // The entry stays on screen until the user reloads.
    expect(
      (view.find("#ecrf-item-SYSBP").element as HTMLInputElement).value
    ).toBe("130");

    await view.find("#ecrf-form-reload").trigger("click");
    await flushPromises();

    expect(ecrfFormsApi.getForm).toHaveBeenCalledTimes(2);
    expect(
      (view.find("#ecrf-item-SYSBP").element as HTMLInputElement).value
    ).toBe("120");
    expect(view.find('[data-testid="ecrf-form-outcome"]').exists()).toBe(false);
  });

  it("shows the kernel's per-item errors on an invalid save (422)", async () => {
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(form() as never);
    vi.mocked(ecrfFormsApi.saveValues).mockRejectedValue(
      new ApiError("One or more values do not fit.", 422, null, {
        code: "K.Collection.ValuesInvalid",
        errors: { COMMENT: ["Too long."] },
      })
    );
    const view = await mountWith({ formId: FORM_ID });

    await view.find("#ecrf-item-COMMENT").setValue("x");
    await view.find("#ecrf-save-reason").setValue("Entry");
    await view.find("#ecrf-form-page").trigger("submit");
    await flushPromises();

    expect(view.find("#ecrf-item-COMMENT-error").text()).toBe("Too long.");
    expect(view.find('[data-testid="ecrf-form-outcome"]').text()).toContain(
      "Nothing was saved."
    );
  });

  it("shows locks, assertions and signatures read-only and offers no save while locked", async () => {
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(
      form({
        locks: [
          {
            lock_id: "l1",
            kind: "Frozen",
            scope: "collection_instance:f",
            locked_at: "2027-04-01T00:00:00Z",
            locked_by: "dm1",
            reason: "Interim analysis",
          },
        ],
        assertions: [
          {
            assertion_id: "a1",
            kind: "Verified",
            target: `observation:${OBS_ID}`,
            revision: 3,
            text: null,
            asserted_by: "cra1",
            asserted_at: "2027-03-20T00:00:00Z",
            reason: "SDV",
            retracted_at: null,
          },
        ],
        signatures: [
          {
            attestation_id: "s1",
            meaning: "Approved",
            signed_by: "pi1",
            signed_at: "2027-03-21T00:00:00Z",
            reason: "Reviewed",
          },
        ],
      }) as never
    );
    const view = await mountWith({ formId: FORM_ID });

    expect(view.find('[data-testid="ecrf-form-locks"]').text()).toContain(
      "Frozen on collection_instance:f by dm1"
    );
    expect(view.find("#ecrf-form-save").exists()).toBe(false);
    expect(view.find("#ecrf-item-DIABP").attributes("disabled")).toBeDefined();
    expect(view.find('[data-testid="ecrf-form-assertions"]').text()).toContain(
      "Verified on observation:"
    );
    expect(view.find('[data-testid="ecrf-form-signatures"]').text()).toContain(
      "Approved by pi1"
    );
    expect(view.find("button.sign").exists()).toBe(false);
  });

  it("opens a new form for an enrollment and visit, and loads it", async () => {
    vi.mocked(ecrfFormsApi.openForm).mockResolvedValue({
      form_id: FORM_ID,
    } as never);
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(form() as never);
    const view = await mountWith({
      enrollmentId: "enr-1",
      collectionSpecId: "VS",
      plannedActivityId: "WEEK2",
    });

    expect(view.find("#ecrf-form-open").attributes("disabled")).toBeDefined();
    await view.find("#ecrf-collected-at").setValue("2027-03-14T09:00");
    await view.find("#ecrf-form-new").trigger("submit");
    await flushPromises();

    const request = vi.mocked(ecrfFormsApi.openForm).mock.calls[0][0];
    expect(request).toMatchObject({
      enrollmentId: "enr-1",
      collectionSpecId: "VS",
      plannedActivityId: "WEEK2",
      reason: "Form opened for data entry",
    });
    expect(request.collectedAt).toBe(
      new Date("2027-03-14T09:00").toISOString()
    );
    expect(request.formId).toMatch(/[0-9a-f-]{36}/);
    expect(ecrfFormsApi.getForm).toHaveBeenCalledWith(FORM_ID);
    expect(view.find('[data-testid="ecrf-form-id"]').text()).toBe(FORM_ID);
  });

  it("reports a form that can't be loaded", async () => {
    vi.mocked(ecrfFormsApi.getForm).mockRejectedValue(
      new ApiError("Form does not exist.", 404)
    );
    const view = await mountWith({ formId: FORM_ID });

    expect(view.find('[role="alert"]').text()).toContain(
      "The form could not be loaded: Form does not exist."
    );
  });
});

describe("useEcrfForm outcomes (ADR-2622)", () => {
  let wrapper: VueWrapper | undefined;

  beforeEach(() => {
    setActivePinia(createPinia());
    vi.mocked(ecrfFormsApi.getForm).mockReset();
    vi.mocked(ecrfFormsApi.openForm).mockReset();
    vi.mocked(ecrfFormsApi.saveValues).mockReset();
    resetQueries();
  });

  afterEach(() => {
    wrapper?.unmount();
    wrapper = undefined;
  });

  async function edit(text: string) {
    wrapper = mount(EcrfFormPanel, { props: { formId: FORM_ID } });
    await flushPromises();
    await wrapper.find("#ecrf-item-COMMENT").setValue(text);
    await wrapper.find("#ecrf-save-reason").setValue("Entry");
    await wrapper.find("#ecrf-form-page").trigger("submit");
    await flushPromises();
    return wrapper;
  }

  it("reports a locked form (423), saves nothing and reloads it", async () => {
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(form() as never);
    vi.mocked(ecrfFormsApi.saveValues).mockRejectedValue(
      new ApiError("Locked", 423)
    );

    const view = await edit("note");

    expect(view.find('[data-testid="ecrf-form-outcome"]').text()).toBe(
      "This form is locked; nothing was saved."
    );
    expect(ecrfFormsApi.getForm).toHaveBeenCalledTimes(2);
  });

  it("retries a request that never reached the kernel with the same idempotency key", async () => {
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(form() as never);
    vi.mocked(ecrfFormsApi.saveValues)
      .mockRejectedValueOnce(new TypeError("Failed to fetch"))
      .mockResolvedValueOnce({
        form_id: FORM_ID,
        version: 3,
        values: [],
      } as never);

    const view = await edit("note");
    expect(view.find('[data-testid="ecrf-form-outcome"]').text()).toBe(
      "Failed to fetch"
    );
    await view.find("#ecrf-form-page").trigger("submit");
    await flushPromises();

    const [first, second] = vi
      .mocked(ecrfFormsApi.saveValues)
      .mock.calls.map(([request]) => request.idempotencyKey);
    expect(second).toBe(first);
    expect(view.find('[data-testid="ecrf-form-outcome"]').text()).toContain(
      "Page saved: 0 values recorded"
    );
  });

  it("reports a form that can't be opened and closes an open form", async () => {
    vi.mocked(ecrfFormsApi.openForm).mockRejectedValue(
      new ApiError("No protocol version governs the visit.", 422)
    );
    wrapper = mount(EcrfFormPanel, {
      props: { enrollmentId: "enr-1", collectionSpecId: "VS" },
    });
    await wrapper.find("#ecrf-collected-at").setValue("2027-03-14T09:00");
    await wrapper.find("#ecrf-form-new").trigger("submit");
    await flushPromises();

    expect(wrapper.find('[role="alert"]').text()).toContain(
      "The form could not be opened: No protocol version governs the visit."
    );

    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(form() as never);
    await wrapper.find("#ecrf-form-id").setValue(FORM_ID);
    await wrapper.find("#ecrf-form-load").trigger("submit");
    await flushPromises();
    expect(wrapper.find('[data-testid="ecrf-form-id"]').text()).toBe(FORM_ID);

    await wrapper.find("#ecrf-form-close").trigger("click");
    expect(wrapper.find("#ecrf-form-id").exists()).toBe(true);
  });

  it("treats an unchanged value or an unchanged missing reason as no edit", async () => {
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(
      form({
        items: [
          item({
            item_id: "RESULT",
            value: { type: "missing", reason: "NotDone" },
            revision: 1,
          }),
          item({
            item_id: "DONE",
            value_kind: "Boolean",
            value: { type: "boolean", value: true },
            revision: 2,
          }),
        ],
      }) as never
    );
    wrapper = mount(EcrfFormPanel, { props: { formId: FORM_ID } });
    await flushPromises();

    expect(wrapper.find("#ecrf-item-DONE").element).toHaveProperty(
      "value",
      "Yes"
    );
    expect(
      wrapper.find("#ecrf-form-save").attributes("disabled")
    ).toBeDefined();

    await wrapper.find("#ecrf-item-DONE").setValue("no");
    await wrapper
      .find('select[aria-label="Why RESULT is missing"]')
      .setValue("Unknown");
    expect(wrapper.find("#ecrf-form-save").text()).toBe("Save page (2)");
  });
});

function formQuery(overrides: Record<string, unknown> = {}) {
  return {
    query_id: "query-a",
    form_id: FORM_ID,
    enrollment_id: "e",
    item_id: "SYSBP",
    observation_id: OBS_ID,
    revision: 3,
    current_revision: 3,
    value_changed: false,
    status: "open",
    text: "Please confirm the value",
    messages: [
      {
        kind: "raised",
        text: "Please confirm the value",
        by: "monitor",
        at: "2027-03-14T10:00:00Z",
      },
    ],
    raised_by: "monitor",
    raised_at: "2027-03-14T10:00:00Z",
    version: 1,
    ...overrides,
  };
}

describe("EcrfFormPanel: queries on form values (#5461)", () => {
  let wrapper: VueWrapper | undefined;

  beforeEach(() => {
    setActivePinia(createPinia());
    vi.mocked(ecrfFormsApi.getForm).mockReset();
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(form() as never);
    resetQueries();
  });

  afterEach(() => {
    wrapper?.unmount();
    wrapper = undefined;
  });

  async function mountWith(props: Record<string, unknown> = {}) {
    wrapper = mount(EcrfFormPanel, { props: { formId: FORM_ID, ...props } });
    await flushPromises();
    return wrapper;
  }

  async function raiseOnSysbp(view: VueWrapper, text: string) {
    await view.find("#ecrf-raise-SYSBP").trigger("click");
    await view.find("#ecrf-raise-text").setValue(text);
    await view.find("#ecrf-raise-confirm").trigger("click");
    await flushPromises();
  }

  it("lists the form's queries and marks each item's unresolved ones and changed values", async () => {
    vi.mocked(ecrfQueriesApi.listForForm).mockResolvedValue([
      formQuery({ value_changed: true, current_revision: 4 }),
      formQuery({ query_id: "query-b", status: "closed" }),
    ] as never);
    const view = await mountWith();

    expect(ecrfQueriesApi.listForForm).toHaveBeenCalledWith(FORM_ID);
    expect(view.find('[data-testid="ecrf-query-badge-SYSBP"]').text()).toBe(
      "1 unresolved query"
    );
    expect(view.find('[data-testid="ecrf-query-changed-SYSBP"]').exists()).toBe(
      true
    );
    expect(view.find('[data-testid="ecrf-query-badge-DIABP"]').exists()).toBe(
      false
    );
    expect(view.findAll("#ecrf-form-queries .form-query")).toHaveLength(2);
    // Raising is offered to a monitor only.
    expect(view.find("#ecrf-raise-SYSBP").exists()).toBe(false);
  });

  it("raises a query on an item's value at the revision the form read returned", async () => {
    vi.mocked(ecrfQueriesApi.raise).mockResolvedValue(formQuery() as never);
    const view = await mountWith({ canManageQueries: true });

    // An item without a value has nothing to query.
    expect(view.find("#ecrf-raise-DIABP").exists()).toBe(false);
    await view.find("#ecrf-raise-SYSBP").trigger("click");
    expect(view.find('label[for="ecrf-raise-text"]').text()).toContain(
      "Query on SYSBP (revision 3)"
    );
    await view.find("#ecrf-raise-text").setValue(" Please confirm the value ");
    await view.find("#ecrf-raise-confirm").trigger("click");
    await flushPromises();

    expect(vi.mocked(ecrfQueriesApi.raise).mock.calls[0][0]).toMatchObject({
      formId: FORM_ID,
      observationId: OBS_ID,
      revision: 3,
      text: "Please confirm the value",
      reason: "Please confirm the value",
    });
    expect(view.find("#ecrf-raise-text").exists()).toBe(false);
    expect(view.find('[data-testid="ecrf-query-badge-SYSBP"]').text()).toBe(
      "1 unresolved query"
    );
    expect(view.find('[data-testid="ecrf-query-outcome"]').text()).toBe(
      "Query raised."
    );
  });

  it("says the value changed and offers a reload when the revision is stale", async () => {
    vi.mocked(ecrfQueriesApi.raise).mockRejectedValue(
      new ApiError("Conflict", 409, "Conflict", {
        code: "M.Execution.Query.RevisionStale",
      })
    );
    const view = await mountWith({ canManageQueries: true });
    await raiseOnSysbp(view, "Please confirm");

    const outcome = view.find('[data-testid="ecrf-query-outcome"]');
    expect(outcome.attributes("aria-live")).toBe("polite");
    expect(outcome.text()).toContain(
      "The value changed since you loaded the form"
    );
    await view.find("#ecrf-query-reload").trigger("click");
    await flushPromises();
    expect(ecrfFormsApi.getForm).toHaveBeenCalledTimes(2);
    expect(view.find("#ecrf-query-reload").exists()).toBe(false);
  });

  it("says when the value is not on the form", async () => {
    vi.mocked(ecrfQueriesApi.raise).mockRejectedValue(
      new ApiError("Not found", 404, "Not Found", {
        code: "M.Execution.Query.ObservationUnknown",
      })
    );
    const view = await mountWith({ canManageQueries: true });
    await raiseOnSysbp(view, "Please confirm");

    expect(view.find('[data-testid="ecrf-query-outcome"]').text()).toContain(
      "That value is not on this form any more"
    );
    expect(view.find("#ecrf-query-reload").exists()).toBe(false);
  });

  it("answers an open query through the form's query list", async () => {
    vi.mocked(ecrfQueriesApi.listForForm).mockResolvedValue([
      formQuery(),
    ] as never);
    vi.mocked(ecrfQueriesApi.transition).mockResolvedValue(
      formQuery({ status: "answered", version: 2 }) as never
    );
    const view = await mountWith();

    await view.find("#btn-answer-query-a").trigger("click");
    await view.find("#query-text-query-a").setValue("Confirmed against source");
    await view.find("#query-query-a form").trigger("submit");
    await flushPromises();

    expect(ecrfQueriesApi.transition).toHaveBeenCalledWith(
      "answer",
      expect.objectContaining({
        formId: FORM_ID,
        queryId: "query-a",
        text: "Confirmed against source",
      })
    );
    expect(view.find("#query-query-a").text()).toContain("Answered");
  });
});
