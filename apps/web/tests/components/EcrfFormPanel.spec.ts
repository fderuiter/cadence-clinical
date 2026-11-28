import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { nextTick } from "vue";
import { mount, flushPromises, type VueWrapper } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import EcrfFormPanel from "../../src/components/ecrf/EcrfFormPanel.vue";
import ScopeLockControls from "../../src/components/canvas/ScopeLockControls.vue";
import { ecrfFormsApi } from "../../src/api/ecrfFormsApi";
import { ApiError } from "../../src/api/client";
import { ecrfQueriesApi } from "../../src/api/ecrfQueriesApi";
import { kernelApi } from "../../src/api/kernelApi";
import { useAuthStore } from "../../src/stores/auth.js";
import { useConfirmStore } from "../../src/stores/confirm.js";
import ConfirmationDialogHost from "../../src/components/ConfirmationDialogHost.vue";
import { scopedKey } from "../../src/utils/userScopedStorage.js";

vi.mock("../../src/auth/keycloak.js", () => ({
  getKeycloakClient: () => null,
}));

vi.mock("../../src/api/kernelApi", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("../../src/api/kernelApi")>();
  return {
    ...actual,
    kernelApi: {
      ...actual.kernelApi,
      verifyObservation: vi.fn(),
      retractAssertion: vi.fn(),
    },
  };
});

vi.mock("../../src/api/ecrfQueriesApi", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("../../src/api/ecrfQueriesApi")>();
  return {
    ...actual,
    ecrfQueriesApi: {
      raise: vi.fn(),
      listForForm: vi.fn(),
      listForEnrollment: vi.fn(),
      listForStudy: vi.fn(),
      transition: vi.fn(),
      reassign: vi.fn(),
    },
  };
});

vi.mock("../../src/api/ecrfFormsApi", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("../../src/api/ecrfFormsApi")>();
  return {
    ...actual,
    ecrfFormsApi: {
      openForm: vi.fn(),
      getForm: vi.fn(),
      saveValues: vi.fn(),
      previewValues: vi.fn(async () => ({
        form_id: "0192f3c4-5e6a-7b8c-9d0e-00000000f001",
        evaluated_rule_packs: 0,
        warnings: [],
      })),
      signForm: vi.fn(),
    },
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
    content_hash: "sha256:form-v2",
    ...overrides,
  };
}

function resetQueries() {
  for (const fn of Object.values(ecrfQueriesApi)) vi.mocked(fn).mockReset();
  vi.mocked(ecrfQueriesApi.listForForm).mockResolvedValue([]);
  vi.mocked(kernelApi.verifyObservation).mockReset();
  vi.mocked(kernelApi.retractAssertion).mockReset();
}

function signInUser(sub = "crc-1", tenantId: string | undefined = "tenant-1") {
  const auth = useAuthStore();
  auth.setAuth({
    authenticated: true,
    token: `token-${sub}`,
    tokenParsed: {
      sub,
      ...(tenantId === undefined ? {} : { tenant_id: tenantId }),
      realm_access: { roles: ["CRC"] },
    },
  });
  return auth;
}

describe("EcrfFormPanel: a kernel form instance saved a page at a time (ADR-2622)", () => {
  let wrapper: VueWrapper | undefined;

  beforeEach(() => {
    localStorage.clear();
    setActivePinia(createPinia());
    vi.mocked(ecrfFormsApi.getForm).mockReset();
    vi.mocked(ecrfFormsApi.openForm).mockReset();
    vi.mocked(ecrfFormsApi.saveValues).mockReset();
    vi.mocked(ecrfFormsApi.previewValues).mockReset();
    vi.mocked(ecrfFormsApi.previewValues).mockResolvedValue({
      form_id: FORM_ID,
      evaluated_rule_packs: 0,
      warnings: [],
    } as never);
    resetQueries();
  });

  afterEach(() => {
    wrapper?.unmount();
    wrapper = undefined;
    vi.useRealTimers();
    vi.restoreAllMocks();
    localStorage.clear();
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
      "Not signed."
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

  it("shows per-item 409 errors when someone saved first, and reloading resets conflicting items while retaining non-stale edits", async () => {
    vi.mocked(ecrfFormsApi.getForm)
      .mockResolvedValueOnce(form() as never)
      .mockResolvedValueOnce(
        form({
          version: 3,
          items: [
            item({
              item_id: "SYSBP",
              concept: "C25298@1",
              unit: "mm[Hg]",
              value_kind: "Quantity",
              required: true,
              value: { type: "quantity", value: 126, unit: "mm[Hg]" },
              observation_id: OBS_ID,
              revision: 4,
              recorded_by: "crc2",
              recorded_at: "2027-03-14T09:06:00Z",
            }),
            item({
              item_id: "DIABP",
              unit: "mm[Hg]",
              value_kind: "Quantity",
              required: true,
              revision: 0,
            }),
            item({ item_id: "COMMENT", value_kind: "Text", revision: 0 }),
          ],
        }) as never
      );
    vi.mocked(ecrfFormsApi.saveValues).mockRejectedValue(
      new ApiError("The form changed since you opened it.", 409, null, {
        code: "K.Collection.RevisionStale",
        errors: {
          SYSBP: ["Item SYSBP is at revision 4, not 3."],
        },
      })
    );
    const view = await mountWith({ formId: FORM_ID });

    await view.find("#ecrf-item-SYSBP").setValue("130");
    await view.find("#ecrf-item-DIABP").setValue("82");
    await view.find("#ecrf-save-reason").setValue("Correction");
    await view.find("#ecrf-form-page").trigger("submit");
    await flushPromises();

    const outcome = view.find('[data-testid="ecrf-form-outcome"]');
    expect(outcome.attributes("role")).toBe("alert");
    expect(outcome.text()).toContain("nothing was saved");
    expect(outcome.text()).toContain("Reload the form");
    expect(view.find("#ecrf-item-SYSBP-error").text()).toBe(
      "Item SYSBP is at revision 4, not 3."
    );
    expect(view.find("#ecrf-item-SYSBP").attributes("aria-invalid")).toBe(
      "true"
    );
    // Both entries stay on screen until the user reloads.
    expect(
      (view.find("#ecrf-item-SYSBP").element as HTMLInputElement).value
    ).toBe("130");
    expect(
      (view.find("#ecrf-item-DIABP").element as HTMLInputElement).value
    ).toBe("82");

    await view.find("#ecrf-form-reload").trigger("click");
    await flushPromises();

    expect(ecrfFormsApi.getForm).toHaveBeenCalledTimes(2);
    // Conflicting SYSBP resets to server's revision 4 value; non-stale DIABP retains its edit.
    expect(
      (view.find("#ecrf-item-SYSBP").element as HTMLInputElement).value
    ).toBe("126");
    expect(view.find("#ecrf-item-SYSBP-error").text()).toBe("");
    expect(
      (view.find("#ecrf-item-DIABP").element as HTMLInputElement).value
    ).toBe("82");
    expect(view.find("#ecrf-form-save").text()).toBe("Save page (1)");
    expect(view.find('[data-testid="ecrf-form-outcome"]').exists()).toBe(false);
    const staleBanner = view.find('[data-testid="ecrf-draft-stale-banner"]');
    expect(staleBanner.exists()).toBe(true);
    expect(staleBanner.attributes("role")).toBe("alert");
    expect(staleBanner.text()).toContain("SYSBP");
    expect(staleBanner.text()).toContain("130 mm[Hg]");
    expect(staleBanner.text()).toContain("rev 3");
    expect(staleBanner.text()).toContain("rev 4");
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
    await view.find("#ecrf-save-reason").setValue("Initial entry");
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
            content_hash: "sha256:form-v2",
            superseded: false,
            signed_values: null,
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
    expect(view.find('[data-testid="ecrf-form-assertions"]').text()).toContain(
      "(reason:"
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

  it("reports a form that can't be loaded and retries the read even when enrollmentContext hides #ecrf-form-load", async () => {
    vi.mocked(ecrfFormsApi.getForm)
      .mockRejectedValueOnce(new ApiError("Form service unavailable.", 503))
      .mockResolvedValueOnce(form() as never);
    const view = await mountWith({
      formId: FORM_ID,
      enrollmentContext: {
        enrollmentId: "0192f3c4-5e6a-7b8c-9d0e-00000000e001",
        subjectCode: "017-0001",
      },
    });

    expect(view.find("#ecrf-form-load").exists()).toBe(false);
    expect(view.find('[role="alert"]').text()).toContain(
      "The form could not be loaded: Form service unavailable."
    );
    const retryBtn = view.find("#ecrf-form-retry");
    expect(retryBtn.exists()).toBe(true);

    await retryBtn.trigger("click");
    await flushPromises();

    expect(ecrfFormsApi.getForm).toHaveBeenCalledTimes(2);
    expect(ecrfFormsApi.getForm).toHaveBeenLastCalledWith(FORM_ID);
    expect(view.find('[data-testid="ecrf-form-id"]').text()).toBe(FORM_ID);
  });
});

describe("useEcrfForm outcomes (ADR-2622)", () => {
  let wrapper: VueWrapper | undefined;

  beforeEach(() => {
    localStorage.clear();
    setActivePinia(createPinia());
    vi.mocked(ecrfFormsApi.getForm).mockReset();
    vi.mocked(ecrfFormsApi.openForm).mockReset();
    vi.mocked(ecrfFormsApi.saveValues).mockReset();
    resetQueries();
  });

  afterEach(() => {
    wrapper?.unmount();
    wrapper = undefined;
    vi.useRealTimers();
    vi.restoreAllMocks();
    localStorage.clear();
  });

  async function edit(text: string) {
    wrapper = mount(EcrfFormPanel, { props: { formId: FORM_ID } });
    await flushPromises();
    await wrapper.find("#ecrf-item-COMMENT").setValue(text);
    await wrapper.find("#ecrf-save-reason").setValue("Initial entry");
    await wrapper.find("#ecrf-form-page").trigger("submit");
    await flushPromises();
    return wrapper;
  }

  it("reports a locked form (423), reloads it, and preserves unsaved item values in the outcome", async () => {
    vi.mocked(ecrfFormsApi.getForm)
      .mockResolvedValueOnce(form() as never)
      .mockResolvedValueOnce(
        form({
          locks: [
            {
              lock_id: "l1",
              kind: "Frozen",
              scope: `collection_instance:${FORM_ID}`,
              locked_at: "2027-04-01T00:00:00Z",
              locked_by: "dm1",
              reason: "Database lock",
            },
          ],
        }) as never
      );
    vi.mocked(ecrfFormsApi.saveValues).mockRejectedValue(
      new ApiError("Locked", 423)
    );

    wrapper = mount(EcrfFormPanel, { props: { formId: FORM_ID } });
    await flushPromises();
    await wrapper.find("#ecrf-item-SYSBP").setValue("128");
    await wrapper
      .find('select[aria-label="Why DIABP is missing"]')
      .setValue("NotDone");
    await wrapper.find("#ecrf-item-COMMENT").setValue("note");
    await wrapper.find("#ecrf-save-reason").setValue("Initial entry");
    await wrapper.find("#ecrf-form-page").trigger("submit");
    await flushPromises();

    const outcome = wrapper.find('[data-testid="ecrf-form-outcome"]');
    expect(outcome.text()).toContain("This form is locked; nothing was saved.");
    const unsaved = wrapper.find('[data-testid="ecrf-locked-unsaved"]');
    expect(unsaved.exists()).toBe(true);
    expect(unsaved.text()).toContain("SYSBP: 128 mm[Hg]");
    expect(unsaved.text()).toContain("DIABP: Not done");
    expect(unsaved.text()).toContain("COMMENT: note");
    expect(ecrfFormsApi.getForm).toHaveBeenCalledTimes(2);
    expect(
      (wrapper.find("#ecrf-item-SYSBP").element as HTMLInputElement).value
    ).toBe("120");
    expect(
      wrapper.find("#ecrf-item-SYSBP").attributes("disabled")
    ).toBeDefined();
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

  it("confirms before closing a dirty form and clears the saved draft when confirmed", async () => {
    vi.useFakeTimers();
    signInUser("crc-1", "tenant-1");
    const draftKey = scopedKey(
      { tenantId: "tenant-1", userId: "crc-1" },
      "ecrf_form_draft",
      FORM_ID
    )!;
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(form() as never);
    wrapper = mount(EcrfFormPanel, {
      props: { formId: FORM_ID },
      attachTo: document.body,
    });
    const hostWrapper = mount(ConfirmationDialogHost, {
      attachTo: document.body,
    });
    await flushPromises();

    await wrapper.find("#ecrf-item-COMMENT").setValue("unsaved note");
    await nextTick();
    vi.advanceTimersByTime(500);
    expect(localStorage.getItem(draftKey)).not.toBeNull();

    const confirmSpy = vi.spyOn(window, "confirm").mockReturnValue(false);
    const closeBtn = wrapper.find("#ecrf-form-close").element as HTMLElement;
    closeBtn.focus();
    expect(document.activeElement).toBe(closeBtn);

    await wrapper.find("#ecrf-form-close").trigger("click");
    await nextTick();
    expect(confirmSpy).not.toHaveBeenCalled();

    const alertDialog = document.querySelector(
      '.clinical-modal-dialog[role="alertdialog"]'
    );
    expect(alertDialog).not.toBeNull();
    (alertDialog?.querySelector(".btn-cancel") as HTMLElement).click();
    await flushPromises();
    await nextTick();

    expect(document.activeElement).toBe(closeBtn);
    expect(wrapper.find('[data-testid="ecrf-form-id"]').exists()).toBe(true);
    expect(localStorage.getItem(draftKey)).not.toBeNull();

    await wrapper.find("#ecrf-form-close").trigger("click");
    await nextTick();
    expect(confirmSpy).not.toHaveBeenCalled();
    (
      document.querySelector(
        '.clinical-modal-dialog[role="alertdialog"] .btn-confirm'
      ) as HTMLElement
    ).click();
    await flushPromises();

    expect(wrapper.find("#ecrf-form-id").exists()).toBe(true);
    expect(localStorage.getItem(draftKey)).toBeNull();
    expect(wrapper.emitted("dirty-change")?.at(-1)).toEqual([false]);
    hostWrapper.unmount();
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

  async function raiseOnSysbp(
    view: VueWrapper,
    text: string,
    changeReason = "Source chart discrepancy"
  ) {
    await view.find("#ecrf-raise-SYSBP").trigger("click");
    await view.find("#ecrf-raise-text").setValue(text);
    await view.find("#ecrf-raise-reason").setValue(changeReason);
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

  it("raises a query on an item's value at the revision the form read returned, and raises missing-data queries on blank items", async () => {
    vi.mocked(ecrfQueriesApi.raise).mockResolvedValueOnce(formQuery() as never);
    const view = await mountWith({ canManageQueries: true });

    // Blank required items can also be queried without marking the form complete.
    expect(view.find("#ecrf-raise-DIABP").exists()).toBe(true);
    await view.find("#ecrf-raise-SYSBP").trigger("click");
    expect(view.find('label[for="ecrf-raise-text"]').text()).toContain(
      "Query on SYSBP (revision 3)"
    );
    await view.find("#ecrf-raise-text").setValue(" Please confirm the value ");
    expect(
      view.find("#ecrf-raise-confirm").attributes("disabled")
    ).toBeDefined();
    await view
      .find("#ecrf-raise-reason")
      .setValue(" Monitor source data audit ");
    await view.find("#ecrf-raise-confirm").trigger("click");
    await flushPromises();

    expect(vi.mocked(ecrfQueriesApi.raise).mock.calls[0][0]).toMatchObject({
      formId: FORM_ID,
      observationId: OBS_ID,
      revision: 3,
      text: "Please confirm the value",
      reason: "Monitor source data audit",
    });
    expect(view.find("#ecrf-raise-text").exists()).toBe(false);
    expect(view.find('[data-testid="ecrf-query-badge-SYSBP"]').text()).toBe(
      "1 unresolved query"
    );
    expect(view.find('[data-testid="ecrf-query-outcome"]').text()).toBe(
      "Query raised."
    );

    // Raise a missing-data query on blank required item DIABP: reloads form with NotAsked at revision 1 while keeping form incomplete.
    const blankObsId = "0192f3c4-5e6a-7b8c-9d0e-00000000a002";
    vi.mocked(ecrfQueriesApi.raise).mockResolvedValueOnce(
      formQuery({
        query_id: "query-blank",
        item_id: "DIABP",
        observation_id: blankObsId,
        revision: 1,
        current_revision: 1,
      }) as never
    );
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValueOnce(
      form({
        complete: false,
        missing_required: ["DIABP"],
        items: [
          form().items[0],
          item({
            item_id: "DIABP",
            unit: "mm[Hg]",
            value_kind: "Quantity",
            required: true,
            value: { type: "missing", reason: "NotAsked" },
            observation_id: blankObsId,
            revision: 1,
            recorded_by: "monitor",
            recorded_at: "2027-03-14T10:01:00Z",
          }),
          form().items[2],
        ],
      }) as never
    );

    await view.find("#ecrf-raise-DIABP").trigger("click");
    expect(view.find('label[for="ecrf-raise-text"]').text()).toContain(
      "Missing-data query on DIABP (no value recorded)"
    );
    await view
      .find("#ecrf-raise-text")
      .setValue("Diastolic BP missing on source");
    await view
      .find("#ecrf-raise-reason")
      .setValue("Required baseline field blank");
    await view.find("#ecrf-raise-confirm").trigger("click");
    await flushPromises();

    expect(vi.mocked(ecrfQueriesApi.raise).mock.calls[1][0]).toMatchObject({
      formId: FORM_ID,
      observationId: null,
      revision: null,
      itemId: "DIABP",
      text: "Diastolic BP missing on source",
      reason: "Required baseline field blank",
    });
    expect(view.find('[data-testid="ecrf-form-status"]').text()).toContain(
      "Incomplete"
    );
    expect(
      (view.find("#ecrf-item-DIABP").element as HTMLInputElement).value
    ).toBe("");
    expect(view.find('[data-testid="ecrf-sdv-status-DIABP"]').text()).toBe(
      "No value"
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
    await view.find("#query-reason-query-a").setValue("Signed flowsheet check");
    await view.find("#query-query-a form").trigger("submit");
    await flushPromises();

    expect(ecrfQueriesApi.transition).toHaveBeenCalledWith(
      "answer",
      expect.objectContaining({
        formId: FORM_ID,
        queryId: "query-a",
        text: "Confirmed against source",
        reason: "Signed flowsheet check",
      })
    );
    expect(view.find("#query-query-a").text()).toContain("Answered");
  });

  it("retries failed query reads via #ecrf-queries-retry while preserving unsaved item entries", async () => {
    vi.mocked(ecrfQueriesApi.listForForm)
      .mockRejectedValueOnce(new ApiError("Query read failed", 503))
      .mockResolvedValueOnce([formQuery()] as never);

    const view = await mountWith();
    await view.find("#ecrf-item-DIABP").setValue("84");

    const errorAlert = view.find('#ecrf-form-queries [role="alert"]');
    expect(errorAlert.text()).toContain("The queries could not be loaded:");
    const retryBtn = view.find("#ecrf-queries-retry");
    expect(retryBtn.exists()).toBe(true);

    await retryBtn.trigger("click");
    await flushPromises();

    expect(ecrfQueriesApi.listForForm).toHaveBeenCalledTimes(2);
    expect(ecrfQueriesApi.listForForm).toHaveBeenLastCalledWith(FORM_ID);
    expect(view.find("#ecrf-queries-retry").exists()).toBe(false);
    expect(view.findAll("#ecrf-form-queries .form-query")).toHaveLength(1);
    expect(
      (view.find("#ecrf-item-DIABP").element as HTMLInputElement).value
    ).toBe("84");
    expect(view.find("#ecrf-form-save").text()).toBe("Save page (1)");
  });

  it("raises a query with an optional assignee and performs audited inline reassignment", async () => {
    vi.mocked(ecrfQueriesApi.raise).mockResolvedValueOnce(
      formQuery({ assignee: "crc.alice" }) as never
    );
    vi.mocked(ecrfQueriesApi.reassign).mockResolvedValueOnce(
      formQuery({
        assignee: "crc.bob",
        version: 2,
        messages: [
          ...formQuery().messages,
          {
            kind: "reassigned",
            text: "Handed off to Bob",
            reason_for_change: "Coordinator shift handoff",
            by: "monitor",
            at: "2027-03-14T11:00:00Z",
          },
        ],
      }) as never
    );

    const view = await mountWith({ canManageQueries: true });
    await view.find("#ecrf-raise-SYSBP").trigger("click");
    await view.find("#ecrf-raise-text").setValue("Verify cuff calibration");
    await view.find("#ecrf-raise-assignee").setValue(" crc.alice ");
    await view.find("#ecrf-raise-reason").setValue("Baseline SDV");
    await view.find("#ecrf-raise-confirm").trigger("click");
    await flushPromises();

    expect(vi.mocked(ecrfQueriesApi.raise).mock.calls[0][0]).toMatchObject({
      assignee: "crc.alice",
      reason: "Baseline SDV",
    });
    expect(
      view.find('[data-testid="query-assignee-query-a"]').text()
    ).toContain("crc.alice");

    await view.find("#btn-reassign-query-a").trigger("click");
    await view.find("#reassign-assignee-query-a").setValue("crc.bob");
    await view.find("#reassign-note-query-a").setValue("Handed off to Bob");
    await view
      .find("#reassign-reason-query-a")
      .setValue("Coordinator shift handoff");
    expect(
      view.find("#btn-confirm-reassign-query-a").attributes("disabled")
    ).toBeUndefined();
    await view.find("#query-query-a form").trigger("submit");
    await flushPromises();

    expect(ecrfQueriesApi.reassign).toHaveBeenCalledWith(
      expect.objectContaining({
        formId: FORM_ID,
        queryId: "query-a",
        assignee: "crc.bob",
        text: "Handed off to Bob",
        reason: "Coordinator shift handoff",
      })
    );
    expect(
      view.find('[data-testid="query-assignee-query-a"]').text()
    ).toContain("crc.bob");
    expect(view.find("#query-query-a").text()).toContain("Reassigned");
  });
});

function signature(overrides: Record<string, unknown> = {}) {
  return {
    attestation_id: "sig-current",
    meaning: "approved",
    signed_by: "pi1",
    signed_at: "2027-03-21T00:00:00Z",
    reason: "Investigator Certification",
    content_hash: "sha256:form-v2",
    superseded: false,
    signed_values: null,
    ...overrides,
  };
}

describe("EcrfFormPanel: signing a form (#5463)", () => {
  let wrapper: VueWrapper | undefined;

  beforeEach(() => {
    setActivePinia(createPinia());
    vi.mocked(ecrfFormsApi.getForm).mockReset();
    vi.mocked(ecrfFormsApi.signForm).mockReset();
    vi.mocked(ecrfFormsApi.saveValues).mockReset();
    resetQueries();
  });

  afterEach(() => {
    wrapper?.unmount();
    wrapper = undefined;
  });

  async function mountWith() {
    wrapper = mount(EcrfFormPanel, {
      props: { formId: FORM_ID },
      global: { stubs: { SignatureCaptureModal: true } },
    });
    await flushPromises();
    return wrapper;
  }

  /** Opens the signature dialog and has it hand back a step-up token, as the gateway would. */
  async function signThroughDialog(view: VueWrapper) {
    await view.find("#ecrf-form-sign").trigger("click");
    const modal = view.findComponent({ name: "SignatureCaptureModal" });
    expect(modal.exists()).toBe(true);
    const onSign = modal.props("onSign") as (
      token: string,
      reason: string
    ) => Promise<unknown>;
    // Settle at once, so a rejection the dialog would show is not reported as unhandled.
    const result = onSign("sig-token-1", "Investigator Certification").then(
      () => null,
      (error: unknown) => error
    );
    await flushPromises();
    return { modal, result };
  }

  it("signs the form as read with the chosen meaning and a step-up token for this form, then reads it again", async () => {
    vi.mocked(ecrfFormsApi.getForm)
      .mockResolvedValueOnce(form() as never)
      .mockResolvedValueOnce(
        form({
          signatures: [signature({ meaning: "responsibility" })],
        }) as never
      );
    vi.mocked(ecrfFormsApi.signForm).mockResolvedValue(
      signature({ meaning: "responsibility" }) as never
    );
    const view = await mountWith();

    expect(
      (view.find("#ecrf-sign-meaning").element as HTMLSelectElement).value
    ).toBe("approved");
    await view.find("#ecrf-sign-meaning").setValue("responsibility");
    const { modal } = await signThroughDialog(view);

    expect(modal.props("actionUrl")).toBe(
      `/api/v1/execution/forms/${FORM_ID}/signatures`
    );
    expect(modal.props("semanticAction")).toBe("execution.form.sign");
    expect(modal.props("reasons")).toEqual([
      "Investigator Certification",
      "INVESTIGATOR_SIGNATURE",
      "APPROVAL",
      "CLINICAL_QC",
      "REVIEW",
    ]);
    const [formId, request, sigToken, reason, key] = vi.mocked(
      ecrfFormsApi.signForm
    ).mock.calls[0];
    expect(formId).toBe(FORM_ID);
    expect(request).toMatchObject({
      meaning: "responsibility",
      contentHash: "sha256:form-v2",
    });
    expect(sigToken).toBe("sig-token-1");
    expect(reason).toBe("Investigator Certification");
    expect(key).toMatch(/[0-9a-f-]{36}/);
    expect(ecrfFormsApi.getForm).toHaveBeenCalledTimes(2);
    expect(view.find('[data-testid="ecrf-sign-outcome"]').text()).toBe(
      "Form signed: Responsibility."
    );
    expect(view.find('[data-testid="ecrf-form-signatures"]').text()).toContain(
      "Responsibility by pi1"
    );
  });

  it("offers no signing while a page has unsaved entries or the form has no values", async () => {
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(form() as never);
    const view = await mountWith();

    expect(view.find("#ecrf-form-sign").attributes("disabled")).toBeUndefined();
    await view.find("#ecrf-item-COMMENT").setValue("note");
    expect(view.find("#ecrf-form-sign").attributes("disabled")).toBeDefined();
    expect(view.find('[data-testid="ecrf-sign-hint"]').text()).toContain(
      "Save or discard your changes"
    );

    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(
      form({ content_hash: null }) as never
    );
    wrapper?.unmount();
    const empty = await mountWith();
    expect(empty.find("#ecrf-form-sign").attributes("disabled")).toBeDefined();
    expect(empty.find('[data-testid="ecrf-sign-hint"]').text()).toBe(
      "The form has no values to sign yet."
    );
  });

  it("says the form changed and offers a reload when the content hash is stale (409)", async () => {
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(form() as never);
    vi.mocked(ecrfFormsApi.signForm).mockRejectedValue(
      new ApiError("The form changed.", 409, "Conflict", {
        code: "K.Trust.TargetContentChanged",
      })
    );
    const view = await mountWith();

    const { modal, result } = await signThroughDialog(view);
    expect(await result).toBeInstanceOf(Error);
    expect(String(await result)).toContain(
      "This form changed after you opened it"
    );

    // Dismissing the modal leaves the reload banner visible on the form panel.
    modal.vm.$emit("cancel");
    await nextTick();
    expect(view.findComponent({ name: "SignatureCaptureModal" }).exists()).toBe(
      false
    );

    const outcome = view.find('[data-testid="ecrf-sign-outcome"]');
    expect(outcome.attributes("role")).toBe("alert");
    expect(outcome.text()).toContain("This form changed after you opened it");
    expect(outcome.text()).toContain("Reload the form");
    await view.find("#ecrf-sign-reload").trigger("click");
    await flushPromises();
    expect(ecrfFormsApi.getForm).toHaveBeenCalledTimes(2);
    expect(view.find('[data-testid="ecrf-sign-outcome"]').exists()).toBe(false);
  });

  it("says the user may not sign (403) and rejects signWithToken so the modal does not close on error", async () => {
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(form() as never);
    vi.mocked(ecrfFormsApi.signForm).mockRejectedValue(
      new ApiError("Forbidden", 403, "Forbidden", {
        code: "K.Authorization.Denied",
      })
    );
    const view = await mountWith();

    const { result } = await signThroughDialog(view);

    expect(await result).toBeInstanceOf(Error);
    expect(String(await result)).toContain(
      "You are not authorised to sign this form"
    );
    expect(view.find('[data-testid="ecrf-sign-outcome"]').text()).toContain(
      "You are not authorised to sign this form"
    );
    expect(view.find("#ecrf-sign-reload").exists()).toBe(false);
  });

  it("prefills signer identity from auth store and surfaces 500 errors inline in SignatureCaptureModal", async () => {
    const auth = useAuthStore();
    auth.setAuth({
      authenticated: true,
      token: "token-pi-1", // pragma: allowlist secret
      tokenParsed: {
        sub: "pi-1",
        preferred_username: "pi.investigator",
        given_name: "Alex", // deid-ignore
        family_name: "Investigator", // deid-ignore
        tenant_id: "tenant-1",
        realm_access: { roles: ["Site Investigator"] },
      },
    });
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(form() as never);
    vi.mocked(ecrfFormsApi.signForm).mockRejectedValue(
      new ApiError("Kernel storage unavailable", 500, "Internal Server Error")
    );

    const view = await mountWith();
    await view.find("#ecrf-form-sign").trigger("click");

    const modal = view.findComponent({ name: "SignatureCaptureModal" });
    expect(modal.exists()).toBe(true);
    expect(modal.props("username")).toBe("pi.investigator");
    expect(modal.props("signerName")).toBe("Alex Investigator"); // deid-ignore
    expect(modal.props("reasons")).not.toContain("I agree to participate");

    const onSign = modal.props("onSign") as (
      token: string,
      reason: string
    ) => Promise<unknown>;
    await expect(
      onSign("sig-token-500", "Investigator Certification")
    ).rejects.toThrow("Kernel storage unavailable");
    expect(view.find('[data-testid="ecrf-sign-outcome"]').text()).toContain(
      "Kernel storage unavailable"
    );
  });

  it("hands a refused step-up token (401) back to the signature dialog", async () => {
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(form() as never);
    vi.mocked(ecrfFormsApi.signForm).mockRejectedValue(
      new ApiError("REAUTHENTICATION_REQUIRED", 401, "Unauthorized")
    );
    const view = await mountWith();

    const { result } = await signThroughDialog(view);

    expect(await result).toBeInstanceOf(Error);
    expect(String(await result)).toContain("REAUTHENTICATION_REQUIRED");
    expect(view.find('[data-testid="ecrf-sign-outcome"]').exists()).toBe(false);
  });

  it("marks a superseded signature and shows what it signed beside the current values", async () => {
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(
      form({
        signatures: [
          signature({
            attestation_id: "sig-old",
            content_hash: "sha256:form-v1",
            superseded: true,
            signed_values: [
              {
                item_id: "SYSBP",
                observation_id: OBS_ID,
                revision: 2,
                value: { type: "quantity", value: 118, unit: "mm[Hg]" },
              },
            ],
          }),
          signature(),
        ],
      }) as never
    );
    const view = await mountWith();

    const old = view.find('[data-signature="sig-old"]');
    expect(old.classes()).toContain("ecrf-signature--superseded");
    expect(old.find('[data-testid="ecrf-signature-superseded"]').text()).toBe(
      "Superseded by a later correction"
    );
    const signedScroll = old.find('[data-testid="ecrf-signed-values-scroll"]');
    expect(signedScroll.exists()).toBe(true);
    expect(signedScroll.attributes("role")).toBe("region");
    expect(signedScroll.attributes("tabindex")).toBe("0");
    expect(signedScroll.attributes("aria-label")).toContain(
      "Superseded signed values"
    );
    expect(
      signedScroll.find('[data-testid="ecrf-signed-values"]').exists()
    ).toBe(true);
    const row = old.find('[data-signed-item="SYSBP"]');
    const cells = row.findAll("th, td").map((c) => c.text());
    expect(cells[0]).toBe("SYSBP");
    expect(cells[1]).toContain("118");
    expect(cells[2]).toBe("2");
    expect(cells[3]).toContain("120");
    expect(cells[4]).toBe("3");
    expect(row.classes()).toContain("ecrf-signed-changed");

    const current = view.find('[data-signature="sig-current"]');
    expect(current.classes()).toContain("ecrf-signature--current");
    expect(
      current.find('[data-testid="ecrf-signature-superseded"]').exists()
    ).toBe(false);
    expect(current.find('[data-testid="ecrf-signed-values"]').exists()).toBe(
      false
    );
  });

  it("performs zero query/signature linear scans or baseline re-formatting per keystroke and preserves signature diffs for unchanged, duplicate, and removed items", async () => {
    let baselineTextReads = 0;
    const noteValue = { type: "text" as const, value: "Stable vitals" };
    Object.defineProperty(noteValue, "value", {
      enumerable: true,
      configurable: true,
      get() {
        baselineTextReads += 1;
        return "Stable vitals";
      },
    });

    const loadedForm = form({
      items: [
        form().items[0],
        item({
          item_id: "DIABP",
          unit: "mm[Hg]",
          value_kind: "Quantity",
          required: true,
          value: { type: "quantity", value: 80, unit: "mm[Hg]" },
          observation_id: "0192f3c4-5e6a-7b8c-9d0e-00000000a002",
          revision: 1,
          recorded_by: "crc1",
          recorded_at: "2027-03-14T09:05:00Z",
        }),
        item({
          item_id: "COMMENT",
          value_kind: "Text",
          value: noteValue,
          observation_id: "0192f3c4-5e6a-7b8c-9d0e-00000000a003",
          revision: 1,
          recorded_by: "crc1",
          recorded_at: "2027-03-14T09:05:00Z",
        }),
        item({
          item_id: "DUP_ITEM",
          unit: "/min",
          value_kind: "Quantity",
          value: { type: "quantity", value: 72, unit: "/min" },
          observation_id: "0192f3c4-5e6a-7b8c-9d0e-00000000a004",
          revision: 2,
          recorded_by: "crc1",
          recorded_at: "2027-03-14T09:05:00Z",
        }),
        item({
          item_id: "DUP_ITEM",
          unit: "/min",
          value_kind: "Quantity",
          value: { type: "quantity", value: 99, unit: "/min" },
          observation_id: "0192f3c4-5e6a-7b8c-9d0e-00000000a005",
          revision: 5,
          recorded_by: "crc1",
          recorded_at: "2027-03-14T09:06:00Z",
        }),
      ],
      signatures: [
        signature({
          attestation_id: "sig-old",
          content_hash: "sha256:form-v1",
          superseded: true,
          signed_values: [
            {
              item_id: "SYSBP",
              observation_id: OBS_ID,
              revision: 2,
              value: { type: "quantity", value: 118, unit: "mm[Hg]" },
            },
            {
              item_id: "DIABP",
              observation_id: "0192f3c4-5e6a-7b8c-9d0e-00000000a002",
              revision: 1,
              value: { type: "quantity", value: 80, unit: "mm[Hg]" },
            },
            {
              item_id: "DUP_ITEM",
              observation_id: "0192f3c4-5e6a-7b8c-9d0e-00000000a004",
              revision: 2,
              value: { type: "quantity", value: 72, unit: "/min" },
            },
            {
              item_id: "REMOVED_TEMP",
              observation_id: "0192f3c4-5e6a-7b8c-9d0e-00000000a009",
              revision: 1,
              value: { type: "quantity", value: 36.8, unit: "Cel" },
            },
          ],
        }),
      ],
    });

    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(loadedForm as never);
    vi.mocked(ecrfQueriesApi.listForForm).mockResolvedValue([
      formQuery({
        query_id: "query-open",
        item_id: "SYSBP",
        status: "open",
        value_changed: true,
      }),
      formQuery({
        query_id: "query-closed",
        item_id: "DIABP",
        status: "closed",
      }),
    ] as never);

    const view = await mountWith();
    await nextTick();

    // Superseded signature table reflects changed, unchanged, first-match duplicate, and removed items.
    const oldSig = view.find('[data-signature="sig-old"]');
    const sysRow = oldSig.find('[data-signed-item="SYSBP"]');
    expect(sysRow.classes()).toContain("ecrf-signed-changed");
    expect(sysRow.findAll("th, td").map((c) => c.text())).toEqual([
      "SYSBP",
      "118 mm[Hg]",
      "2",
      "120 mm[Hg]",
      "3",
    ]);

    const diaRow = oldSig.find('[data-signed-item="DIABP"]');
    expect(diaRow.classes()).not.toContain("ecrf-signed-changed");
    expect(diaRow.findAll("th, td").map((c) => c.text())).toEqual([
      "DIABP",
      "80 mm[Hg]",
      "1",
      "80 mm[Hg]",
      "1",
    ]);

    const dupRow = oldSig.find('[data-signed-item="DUP_ITEM"]');
    expect(dupRow.classes()).not.toContain("ecrf-signed-changed");
    expect(dupRow.findAll("th, td").map((c) => c.text())).toEqual([
      "DUP_ITEM",
      "72 /min",
      "2",
      "72 /min",
      "2",
    ]);

    const removedRow = oldSig.find('[data-signed-item="REMOVED_TEMP"]');
    expect(removedRow.classes()).toContain("ecrf-signed-changed");
    expect(removedRow.findAll("th, td").map((c) => c.text())).toEqual([
      "REMOVED_TEMP",
      "36.8 Cel",
      "1",
      "No value",
      "None",
    ]);

    expect(view.find('[data-testid="ecrf-query-badge-SYSBP"]').text()).toBe(
      "1 unresolved query"
    );
    expect(view.find('[data-testid="ecrf-query-changed-SYSBP"]').exists()).toBe(
      true
    );
    expect(view.find('[data-testid="ecrf-query-badge-DIABP"]').exists()).toBe(
      false
    );

    // Measure linear scans across keystrokes (clean -> dirty -> multi-char edits -> clean).
    let itemFindCalls = 0;
    let queryFilterCalls = 0;
    const origFind = Array.prototype.find;
    const origFilter = Array.prototype.filter;
    Array.prototype.find = function <T>(
      this: T[],
      predicate: (value: T, index: number, obj: T[]) => unknown,
      thisArg?: unknown
    ) {
      if (
        this.length > 0 &&
        this[0] &&
        typeof this[0] === "object" &&
        "item_id" in this[0] &&
        "value_kind" in this[0]
      ) {
        itemFindCalls += 1;
      }
      return origFind.call(this, predicate, thisArg);
    } as typeof Array.prototype.find;
    Array.prototype.filter = function <T>(
      this: T[],
      predicate: (value: T, index: number, obj: T[]) => unknown,
      thisArg?: unknown
    ) {
      if (
        this.length > 0 &&
        this[0] &&
        typeof this[0] === "object" &&
        "query_id" in this[0]
      ) {
        queryFilterCalls += 1;
      }
      return origFilter.call(this, predicate, thisArg);
    } as typeof Array.prototype.filter;

    try {
      baselineTextReads = 0;
      const sysInput = view.find("#ecrf-item-SYSBP");
      for (const typed of ["1", "12", "125", "120", "130", "120"]) {
        await sysInput.setValue(typed);
        await nextTick();
      }

      expect(itemFindCalls).toBe(0);
      expect(queryFilterCalls).toBe(0);
      expect(baselineTextReads).toBe(0);
    } finally {
      Array.prototype.find = origFind;
      Array.prototype.filter = origFilter;
    }

    // Query transition via put (splice) still updates live query badges reactively.
    vi.mocked(ecrfQueriesApi.transition).mockResolvedValueOnce(
      formQuery({
        query_id: "query-open",
        item_id: "SYSBP",
        status: "answered",
        value_changed: true,
        version: 2,
      }) as never
    );
    await view.find("#btn-answer-query-open").trigger("click");
    await view.find("#query-text-query-open").setValue("Confirmed 120 mm[Hg]");
    await view.find("#query-reason-query-open").setValue("Source chart match");
    await view.find("#query-query-open form").trigger("submit");
    await flushPromises();
    expect(view.find('[data-testid="ecrf-query-badge-SYSBP"]').text()).toBe(
      "1 unresolved query"
    );
  });
});

describe("EcrfFormPanel: account-scoped browser drafts", () => {
  let wrapper: VueWrapper | undefined;

  beforeEach(() => {
    vi.useFakeTimers();
    localStorage.clear();
    setActivePinia(createPinia());
    vi.mocked(ecrfFormsApi.getForm).mockReset();
    vi.mocked(ecrfFormsApi.saveValues).mockReset();
    resetQueries();
  });

  afterEach(() => {
    wrapper?.unmount();
    wrapper = undefined;
    vi.useRealTimers();
    vi.restoreAllMocks();
    localStorage.clear();
  });

  it("auto-saves dirty entries with revisions after 500ms, restores matching revisions on load, and deletes the draft on save", async () => {
    signInUser("crc-1", "tenant-1");
    const draftKey = scopedKey(
      { tenantId: "tenant-1", userId: "crc-1" },
      "ecrf_form_draft",
      FORM_ID
    )!;
    expect(draftKey).toBe(
      `cadence_user:tenant-1:crc-1:ecrf_form_draft:${FORM_ID}`
    );

    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(form() as never);
    wrapper = mount(EcrfFormPanel, { props: { formId: FORM_ID } });
    await flushPromises();

    await wrapper.find("#ecrf-item-SYSBP").setValue("132");
    await wrapper.find("#ecrf-item-DIABP").setValue("84");
    await nextTick();

    vi.advanceTimersByTime(400);
    expect(localStorage.getItem(draftKey)).toBeNull();

    vi.advanceTimersByTime(150);
    const savedDraft = JSON.parse(localStorage.getItem(draftKey)!);
    expect(savedDraft).toEqual({
      form_id: FORM_ID,
      items: {
        SYSBP: { text: "132", missing: "", revision: 3 },
        DIABP: { text: "84", missing: "", revision: 0 },
      },
    });

    // Simulate reload / remount: matching revisions restore dirty entries.
    wrapper.unmount();
    wrapper = mount(EcrfFormPanel, { props: { formId: FORM_ID } });
    await flushPromises();

    expect(
      (wrapper.find("#ecrf-item-SYSBP").element as HTMLInputElement).value
    ).toBe("132");
    expect(
      (wrapper.find("#ecrf-item-DIABP").element as HTMLInputElement).value
    ).toBe("84");
    expect(wrapper.find("#ecrf-form-save").text()).toBe("Save page (2)");
    expect(wrapper.emitted("dirty-change")?.at(-1)).toEqual([true]);

    // Saving deletes the persisted draft and does not re-apply it after reload.
    vi.mocked(ecrfFormsApi.saveValues).mockResolvedValue({
      form_id: FORM_ID,
      version: 3,
      values: [],
    } as never);
    await wrapper.find("#ecrf-save-reason").setValue("Initial entry");
    await wrapper.find("#ecrf-form-page").trigger("submit");
    await flushPromises();

    expect(localStorage.getItem(draftKey)).toBeNull();
    expect(wrapper.emitted("dirty-change")?.at(-1)).toEqual([false]);
  });

  it("surfaces skipped stale draft items when server revisions advance and preserves unapplied drafts on locked forms until cleared", async () => {
    signInUser("crc-1", "tenant-1");
    const draftKey = scopedKey(
      { tenantId: "tenant-1", userId: "crc-1" },
      "ecrf_form_draft",
      FORM_ID
    )!;
    localStorage.setItem(
      draftKey,
      JSON.stringify({
        form_id: FORM_ID,
        items: {
          SYSBP: { text: "132", missing: "", revision: 3 },
          DIABP: { text: "84", missing: "", revision: 0 },
        },
      })
    );

    // Server advanced SYSBP to revision 4 while DIABP is still at revision 0.
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValueOnce(
      form({
        items: [
          item({
            item_id: "SYSBP",
            unit: "mm[Hg]",
            value_kind: "Quantity",
            required: true,
            value: { type: "quantity", value: 125, unit: "mm[Hg]" },
            observation_id: OBS_ID,
            revision: 4,
          }),
          item({
            item_id: "DIABP",
            unit: "mm[Hg]",
            value_kind: "Quantity",
            required: true,
            revision: 0,
          }),
        ],
      }) as never
    );

    wrapper = mount(EcrfFormPanel, {
      props: { formId: FORM_ID },
      attachTo: document.body,
    });
    await flushPromises();

    expect(
      (wrapper.find("#ecrf-item-SYSBP").element as HTMLInputElement).value
    ).toBe("125");
    expect(
      (wrapper.find("#ecrf-item-DIABP").element as HTMLInputElement).value
    ).toBe("84");
    const staleBanner = wrapper.find('[data-testid="ecrf-draft-stale-banner"]');
    expect(staleBanner.exists()).toBe(true);
    expect(staleBanner.attributes("role")).toBe("alert");
    expect(staleBanner.text()).toContain("SYSBP");
    expect(staleBanner.text()).toContain("rev 3");
    expect(staleBanner.text()).toContain("rev 4");
    expect(JSON.parse(localStorage.getItem(draftKey)!)).toEqual({
      form_id: FORM_ID,
      items: {
        DIABP: { text: "84", missing: "", revision: 0 },
      },
    });

    const staleDismissBtn = wrapper.find("#ecrf-draft-stale-dismiss")
      .element as HTMLButtonElement;
    staleDismissBtn.focus();
    await wrapper.find("#ecrf-draft-stale-dismiss").trigger("click");
    await flushPromises();
    expect(
      wrapper.find('[data-testid="ecrf-draft-stale-banner"]').exists()
    ).toBe(false);
    expect(document.activeElement?.id).toBe("ecrf-draft-compare-toggle");

    // When the loaded form has a lock, unapplied draft values stay read-only in a banner until explicitly cleared.
    wrapper.unmount();
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValueOnce(
      form({
        locks: [
          {
            lock_id: "l1",
            kind: "Frozen",
            scope: `collection_instance:${FORM_ID}`,
            locked_at: "2027-04-01T00:00:00Z",
            locked_by: "dm1",
            reason: "Interim analysis",
          },
        ],
      }) as never
    );
    wrapper = mount(EcrfFormPanel, {
      props: { formId: FORM_ID },
      attachTo: document.body,
    });
    await flushPromises();

    expect(
      (wrapper.find("#ecrf-item-DIABP").element as HTMLInputElement).value
    ).toBe("");
    const lockedBanner = wrapper.find(
      '[data-testid="ecrf-draft-locked-banner"]'
    );
    expect(lockedBanner.exists()).toBe(true);
    expect(lockedBanner.attributes("role")).toBe("status");
    expect(lockedBanner.text()).toContain("Frozen");
    expect(lockedBanner.text()).toContain("DIABP");
    expect(lockedBanner.text()).toContain("84 mm[Hg]");
    expect(localStorage.getItem(draftKey)).not.toBeNull();

    const lockedClearBtn = wrapper.find("#ecrf-draft-locked-clear")
      .element as HTMLButtonElement;
    lockedClearBtn.focus();
    await wrapper.find("#ecrf-draft-locked-clear").trigger("click");
    await flushPromises();

    expect(
      wrapper.find('[data-testid="ecrf-draft-locked-banner"]').exists()
    ).toBe(false);
    expect(localStorage.getItem(draftKey)).toBeNull();
    expect(["ecrf-form-close", "ecrf-form-title"]).toContain(
      document.activeElement?.id
    );
  });

  it("skips localStorage when storageScopeOf returns null and tolerates storage quota errors", async () => {
    const getSpy = vi.spyOn(window.localStorage, "getItem");
    const setSpy = vi.spyOn(window.localStorage, "setItem");

    // Unauthenticated: storageScopeOf returns null -> no localStorage access.
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(form() as never);
    wrapper = mount(EcrfFormPanel, { props: { formId: FORM_ID } });
    await flushPromises();
    await wrapper.find("#ecrf-item-COMMENT").setValue("offline note");
    await nextTick();
    vi.advanceTimersByTime(600);

    expect(getSpy).not.toHaveBeenCalled();
    expect(setSpy).not.toHaveBeenCalled();

    // Authenticated, but setItem throws QuotaExceededError -> editing still succeeds without throwing.
    wrapper.unmount();
    signInUser("crc-1", "tenant-1");
    setSpy.mockImplementation(() => {
      throw new DOMException("Quota exceeded", "QuotaExceededError");
    });

    wrapper = mount(EcrfFormPanel, { props: { formId: FORM_ID } });
    await flushPromises();
    await wrapper.find("#ecrf-item-COMMENT").setValue("quota note");
    await nextTick();
    expect(() => vi.advanceTimersByTime(600)).not.toThrow();
    expect(
      (wrapper.find("#ecrf-item-COMMENT").element as HTMLInputElement).value
    ).toBe("quota note");
  });

  it("hides raw UUID inputs and provides structured Collection spec and Visit selectors when Clinical Canvas supplies enrollmentContext", async () => {
    const opened = form();
    vi.mocked(ecrfFormsApi.openForm).mockResolvedValue(opened as never);

    wrapper = mount(EcrfFormPanel, {
      props: {
        enrollmentContext: {
          enrollmentId: "0192f3c4-5e6a-7b8c-9d0e-00000000e001",
          subjectCode: "017-0001",
          collectionSpecs: [
            { id: "VS", name: "Vital Signs", plannedActivityIds: ["WEEK2"] },
            { id: "AE", name: "Adverse Events", plannedActivityIds: ["WEEK4"] },
          ],
          plannedActivities: [
            { id: "WEEK2", name: "Week 2", studyDay: 14 },
            { id: "WEEK4", name: "Week 4", studyDay: 28 },
          ],
        },
      },
      attachTo: document.body,
    });
    await flushPromises();

    expect(wrapper.find("#ecrf-form-load").exists()).toBe(false);
    expect(wrapper.find("#ecrf-form-id").exists()).toBe(false);
    expect(wrapper.find("#ecrf-enrollment-id").exists()).toBe(false);

    const specSelect = wrapper.find("#ecrf-collection-spec");
    const visitSelect = wrapper.find("#ecrf-planned-activity");
    expect(specSelect.element.tagName).toBe("SELECT");
    expect(visitSelect.element.tagName).toBe("SELECT");

    await specSelect.setValue("VS");
    await nextTick();
    expect(
      visitSelect.findAll("option").map((opt) => opt.attributes("value"))
    ).toEqual(["", "WEEK2"]);

    await visitSelect.setValue("WEEK2");
    await wrapper.find("#ecrf-collected-at").setValue("2027-03-14T09:00");
    await wrapper.find("#ecrf-form-new").trigger("submit");
    await flushPromises();

    expect(ecrfFormsApi.openForm).toHaveBeenCalledWith(
      expect.objectContaining({
        enrollmentId: "0192f3c4-5e6a-7b8c-9d0e-00000000e001",
        collectionSpecId: "VS",
        plannedActivityId: "WEEK2",
      })
    );
    const sentFormId = vi.mocked(ecrfFormsApi.openForm).mock.calls[0][0].formId;
    expect(sentFormId).toMatch(
      /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
    );
  });

  it("links recorded form item revisions and queries directly to observations in Clinical Canvas", async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: "/studies/:studyCode?",
          name: "study",
          component: { template: "<div />" },
        },
      ],
    });
    await router.push("/studies/CAD-101");
    await router.isReady();

    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(form() as never);
    vi.mocked(ecrfQueriesApi.listForForm).mockResolvedValue([
      {
        query_id: "q-canvas-1",
        form_id: FORM_ID,
        enrollment_id: "enr-1",
        item_id: "SYSBP",
        observation_id: OBS_ID,
        revision: 1,
        current_revision: 1,
        value_changed: false,
        status: "open",
        text: "Verify blood pressure",
        messages: [
          {
            kind: "raised",
            text: "Verify blood pressure",
            by: "cra1",
            at: "2027-03-14T10:00:00Z",
          },
        ],
        raised_by: "cra1",
        raised_at: "2027-03-14T10:00:00Z",
        version: 1,
      },
    ] as never);

    wrapper = mount(EcrfFormPanel, {
      props: {
        formId: FORM_ID,
        observationLocation: (obsId, fId) => ({
          name: "study",
          params: { studyCode: "CAD-101" },
          query: {
            stage: "conduct",
            form: `collection_instance:${fId}`,
            observation: `observation:${obsId}`,
          },
        }),
        formLocation: (fId) => ({
          name: "study",
          params: { studyCode: "CAD-101" },
          query: { stage: "conduct", form: `collection_instance:${fId}` },
        }),
      },
      global: { plugins: [router] },
    });
    await flushPromises();

    const itemObsLink = wrapper.find("#ecrf-item-observation-SYSBP");
    expect(itemObsLink.exists()).toBe(true);
    expect(itemObsLink.attributes("href")).toBe(
      `/studies/CAD-101?stage=conduct&form=collection_instance:${FORM_ID}&observation=observation:${OBS_ID}`
    );

    const queryObsLink = wrapper.find("#query-observation-q-canvas-1");
    expect(queryObsLink.exists()).toBe(true);
    expect(queryObsLink.attributes("href")).toBe(
      `/studies/CAD-101?stage=conduct&form=collection_instance:${FORM_ID}&observation=observation:${OBS_ID}`
    );
  });

  it("flushes pending <500ms debounced drafts on unmount and across form switches or prop resets without deleting saved drafts", async () => {
    const SECOND_FORM_ID = "0192f3c4-5e6a-7b8c-9d0e-00000000f002";
    signInUser("crc-1", "tenant-1");
    const firstKey = scopedKey(
      { tenantId: "tenant-1", userId: "crc-1" },
      "ecrf_form_draft",
      FORM_ID
    )!;
    const secondKey = scopedKey(
      { tenantId: "tenant-1", userId: "crc-1" },
      "ecrf_form_draft",
      SECOND_FORM_ID
    )!;

    vi.mocked(ecrfFormsApi.getForm).mockImplementation(
      async (requestedId) => form({ form_id: requestedId }) as never
    );

    wrapper = mount(EcrfFormPanel, { props: { formId: FORM_ID } });
    await flushPromises();

    await wrapper.find("#ecrf-item-DIABP").setValue("86");
    await nextTick();
    vi.advanceTimersByTime(100);
    // Debounce timer has not fired yet.
    expect(localStorage.getItem(firstKey)).toBeNull();

    // Unmounting before 500ms flushes the pending draft immediately.
    wrapper.unmount();
    wrapper = undefined;
    expect(JSON.parse(localStorage.getItem(firstKey)!)).toEqual({
      form_id: FORM_ID,
      items: {
        DIABP: { text: "86", missing: "", revision: 0 },
      },
    });

    // Remounting restores first form's draft; switching formId before 500ms flushes updated draft.
    wrapper = mount(EcrfFormPanel, { props: { formId: FORM_ID } });
    await flushPromises();
    expect(
      (wrapper.find("#ecrf-item-DIABP").element as HTMLInputElement).value
    ).toBe("86");

    await wrapper.find("#ecrf-item-COMMENT").setValue("switch before timer");
    await nextTick();
    vi.advanceTimersByTime(100);

    await wrapper.setProps({ formId: SECOND_FORM_ID });
    await flushPromises();

    expect(JSON.parse(localStorage.getItem(firstKey)!)).toEqual({
      form_id: FORM_ID,
      items: {
        DIABP: { text: "86", missing: "", revision: 0 },
        COMMENT: { text: "switch before timer", missing: "", revision: 0 },
      },
    });

    // Edit second form and clear formId prop before 500ms: dismiss flushes draft and resets panel without confirm or deletion.
    const confirmSpy = vi.spyOn(window, "confirm");
    await wrapper.find("#ecrf-item-SYSBP").setValue("134");
    await nextTick();
    vi.advanceTimersByTime(100);

    await wrapper.setProps({ formId: null });
    await flushPromises();

    expect(confirmSpy).not.toHaveBeenCalled();
    expect(useConfirmStore().activeRequest).toBeNull();
    expect(wrapper.find("#ecrf-form-new").exists()).toBe(true);
    expect(wrapper.emitted("dirty-change")?.at(-1)).toEqual([false]);
    expect(JSON.parse(localStorage.getItem(secondKey)!)).toEqual({
      form_id: SECOND_FORM_ID,
      items: {
        SYSBP: { text: "134", missing: "", revision: 3 },
      },
    });
    expect(localStorage.getItem(firstKey)).not.toBeNull();

    // Returning to the second form restores its preserved draft.
    await wrapper.setProps({ formId: SECOND_FORM_ID });
    await flushPromises();
    expect(
      (wrapper.find("#ecrf-item-SYSBP").element as HTMLInputElement).value
    ).toBe("134");
  });
});

describe("EcrfFormPanel: source data verification, lock scopes, and item highlighting", () => {
  const OBS_DIA = "0192f3c4-5e6a-7b8c-9d0e-00000000a002";
  const ASSERTION_ID = "0192f3c4-5e6a-7b8c-9d0e-00000000c001";
  let wrapper: VueWrapper | undefined;

  beforeEach(() => {
    setActivePinia(createPinia());
    vi.mocked(ecrfFormsApi.getForm).mockReset();
    vi.mocked(ecrfFormsApi.saveValues).mockReset();
    vi.mocked(ecrfFormsApi.previewValues).mockReset();
    vi.mocked(ecrfFormsApi.previewValues).mockResolvedValue({
      form_id: FORM_ID,
      evaluated_rule_packs: 0,
      warnings: [],
    } as never);
    resetQueries();
  });

  afterEach(() => {
    wrapper?.unmount();
    wrapper = undefined;
    vi.useRealTimers();
  });

  it("verifies a recorded item with a mandatory reason, withdraws the verification, and clears verified status when a newer revision exists", async () => {
    vi.mocked(ecrfFormsApi.getForm)
      .mockResolvedValueOnce(form() as never)
      .mockResolvedValueOnce(
        form({
          assertions: [
            {
              assertion_id: ASSERTION_ID,
              kind: "Verified",
              target: `observation:${OBS_ID}`,
              revision: 3,
              text: null,
              asserted_at: "2027-03-14T11:00:00Z",
              asserted_by: "cra1",
              reason: "Checked against bedside chart",
              retracted_at: null,
            },
          ],
        }) as never
      )
      .mockResolvedValueOnce(
        form({
          assertions: [
            {
              assertion_id: ASSERTION_ID,
              kind: "Verified",
              target: `observation:${OBS_ID}`,
              revision: 2,
              text: null,
              asserted_at: "2027-03-14T11:00:00Z",
              asserted_by: "cra1",
              reason: "Checked against bedside chart",
              retracted_at: null,
            },
          ],
        }) as never
      );
    vi.mocked(kernelApi.verifyObservation).mockResolvedValue({
      ref: `assertion:${ASSERTION_ID}`,
      kind: "Verified",
      target: `observation:${OBS_ID}`,
      revision: 3,
      retracts: null,
      asserted_at: "2027-03-14T11:00:00Z",
      asserted_by: "cra1",
    });
    vi.mocked(kernelApi.retractAssertion).mockResolvedValue({
      ref: "assertion:0192f3c4-5e6a-7b8c-9d0e-00000000c002",
      kind: "Verified",
      target: `observation:${OBS_ID}`,
      revision: 3,
      retracts: `assertion:${ASSERTION_ID}`,
      asserted_at: "2027-03-14T11:05:00Z",
      asserted_by: "cra1",
    });

    wrapper = mount(EcrfFormPanel, {
      props: { formId: FORM_ID, canVerifySource: true },
    });
    await flushPromises();

    expect(wrapper.find('[data-testid="ecrf-sdv-status-SYSBP"]').text()).toBe(
      "Unverified"
    );
    expect(wrapper.find("#ecrf-verify-DIABP").exists()).toBe(false);

    await wrapper.find("#ecrf-verify-SYSBP").trigger("click");
    expect(
      wrapper.find("#ecrf-sdv-confirm").attributes("disabled")
    ).toBeDefined();
    await wrapper
      .find("#ecrf-sdv-reason")
      .setValue(" Checked against bedside chart ");
    await wrapper.find("#ecrf-sdv-confirm").trigger("click");
    await flushPromises();

    expect(kernelApi.verifyObservation).toHaveBeenCalledWith(
      expect.objectContaining({
        observationRef: `observation:${OBS_ID}`,
        enrollmentRef: "enrollment:e",
        revision: 3,
        reason: "Checked against bedside chart",
      })
    );
    expect(wrapper.find('[data-testid="ecrf-sdv-status-SYSBP"]').text()).toBe(
      "Verified (rev 3)"
    );

    // Withdraw verification with mandatory reason; simulated reload has revision 3 vs Verified@2 -> Needs re-verification!
    await wrapper.find("#ecrf-unverify-SYSBP").trigger("click");
    await wrapper
      .find("#ecrf-sdv-reason")
      .setValue("Addendum updated source BP");
    await wrapper.find("#ecrf-sdv-confirm").trigger("click");
    await flushPromises();

    expect(kernelApi.retractAssertion).toHaveBeenCalledWith(
      expect.objectContaining({
        assertionRef: `assertion:${ASSERTION_ID}`,
        enrollmentRef: "enrollment:e",
        reason: "Addendum updated source BP",
      })
    );
    expect(wrapper.find('[data-testid="ecrf-sdv-status-SYSBP"]').text()).toBe(
      "Needs re-verification"
    );
  });

  it("selects all unverified recorded items, dispatches one verification command per item, and reports per-item outcomes", async () => {
    const twoRecorded = form({
      complete: true,
      missing_required: [],
      items: [
        form().items[0],
        item({
          item_id: "DIABP",
          unit: "mm[Hg]",
          value_kind: "Quantity",
          required: true,
          value: { type: "quantity", value: 78, unit: "mm[Hg]" },
          observation_id: OBS_DIA,
          revision: 1,
          recorded_by: "crc1",
          recorded_at: "2027-03-14T09:06:00Z",
        }),
        form().items[2],
      ],
    });
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(twoRecorded as never);
    vi.mocked(kernelApi.verifyObservation)
      .mockResolvedValueOnce({
        ref: `assertion:${ASSERTION_ID}`,
        kind: "Verified",
        target: `observation:${OBS_ID}`,
        revision: 3,
        retracts: null,
        asserted_at: "2027-03-14T11:00:00Z",
        asserted_by: "cra1",
      })
      .mockRejectedValueOnce(
        new ApiError("Conflict", 409, null, {
          code: "K.Assertion.RevisionStale",
        })
      );

    wrapper = mount(EcrfFormPanel, {
      props: { formId: FORM_ID, canVerifySource: true },
    });
    await flushPromises();

    await wrapper.find("#ecrf-sdv-select-all").trigger("click");
    await wrapper
      .find("#ecrf-bulk-sdv-reason")
      .setValue("Verified vitals page against clinic chart");
    await wrapper.find("#ecrf-bulk-sdv-submit").trigger("click");
    await flushPromises();

    expect(kernelApi.verifyObservation).toHaveBeenCalledTimes(2);
    const [call1, call2] = vi.mocked(kernelApi.verifyObservation).mock.calls;
    expect(call1[0].observationRef).toBe(`observation:${OBS_ID}`);
    expect(call2[0].observationRef).toBe(`observation:${OBS_DIA}`);
    expect(call1[0].idempotencyKey).not.toBe(call2[0].idempotencyKey);

    const results = wrapper.find('[data-testid="ecrf-bulk-sdv-results"]');
    expect(
      results.find('[data-sdv-result="SYSBP"]').attributes("data-ok")
    ).toBe("true");
    expect(
      results.find('[data-sdv-result="DIABP"]').attributes("data-ok")
    ).toBe("false");
    expect(results.find('[data-sdv-result="DIABP"]').text()).toContain(
      "value changed"
    );
  });

  it("allows SDV and queries while Frozen, blocks them while Locked, and highlights a target item", async () => {
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValueOnce(
      form({
        locks: [
          {
            lock_id: "l-frozen",
            kind: "Frozen",
            scope: `collection_instance:${FORM_ID}`,
            locked_at: "2027-04-01T00:00:00Z",
            locked_by: "dm1",
            reason: "Interim analysis",
          },
        ],
      }) as never
    );

    wrapper = mount(EcrfFormPanel, {
      props: {
        formId: FORM_ID,
        canVerifySource: true,
        canManageQueries: true,
        highlightItemId: "DIABP",
      },
    });
    await flushPromises();

    expect(wrapper.find("#ecrf-row-DIABP").attributes("data-highlighted")).toBe(
      "true"
    );
    expect(
      wrapper.find("#ecrf-item-SYSBP").attributes("disabled")
    ).toBeDefined();
    expect(
      wrapper.find("#ecrf-verify-SYSBP").attributes("disabled")
    ).toBeUndefined();
    expect(
      wrapper.find("#ecrf-raise-SYSBP").attributes("disabled")
    ).toBeUndefined();
    expect(
      wrapper.find('[data-testid="ecrf-lock-scope-note"]').text()
    ).toContain("remain available while Frozen");

    wrapper.unmount();
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValueOnce(
      form({
        locks: [
          {
            lock_id: "l-locked",
            kind: "Locked",
            scope: `study:s`,
            locked_at: "2027-05-01T00:00:00Z",
            locked_by: "dm1",
            reason: "Final database lock",
          },
        ],
      }) as never
    );

    wrapper = mount(EcrfFormPanel, {
      props: {
        formId: FORM_ID,
        canVerifySource: true,
        canManageQueries: true,
      },
    });
    await flushPromises();

    expect(
      wrapper.find("#ecrf-verify-SYSBP").attributes("disabled")
    ).toBeDefined();
    expect(
      wrapper.find("#ecrf-raise-SYSBP").attributes("disabled")
    ).toBeDefined();
    expect(
      wrapper.find('[data-testid="ecrf-lock-scope-note"]').text()
    ).toContain("Locked assertion is in force");
  });

  it("prompts the user to reload the form when verifying a stale revision (409 K.Assertion.RevisionStale)", async () => {
    vi.mocked(ecrfFormsApi.getForm)
      .mockResolvedValueOnce(form() as never)
      .mockResolvedValueOnce(
        form({
          version: 3,
          items: [
            item({
              ...form().items[0],
              value: { type: "quantity", value: 126, unit: "mm[Hg]" },
              revision: 4,
            }),
            form().items[1],
            form().items[2],
          ],
        }) as never
      );
    vi.mocked(kernelApi.verifyObservation).mockRejectedValueOnce(
      new ApiError("Stale revision", 409, null, {
        code: "K.Assertion.RevisionStale",
      })
    );

    wrapper = mount(EcrfFormPanel, {
      props: { formId: FORM_ID, canVerifySource: true },
    });
    await flushPromises();

    await wrapper.find("#ecrf-verify-SYSBP").trigger("click");
    await wrapper.find("#ecrf-sdv-reason").setValue("Source chart check");
    await wrapper.find("#ecrf-sdv-confirm").trigger("click");
    await flushPromises();

    const sdvOutcome = wrapper.find('[data-testid="ecrf-sdv-outcome"]');
    expect(sdvOutcome.attributes("role")).toBe("alert");
    expect(sdvOutcome.text()).toContain("reload the form");
    const reloadBtn = wrapper.find("#ecrf-sdv-reload");
    expect(reloadBtn.exists()).toBe(true);

    await reloadBtn.trigger("click");
    await flushPromises();

    expect(ecrfFormsApi.getForm).toHaveBeenCalledTimes(2);
    expect(wrapper.find('[data-testid="ecrf-sdv-outcome"]').exists()).toBe(
      false
    );
    expect(
      (wrapper.find("#ecrf-item-SYSBP").element as HTMLInputElement).value
    ).toBe("126");
  });

  it("wraps the six-column items table in a keyboard-focusable accessible horizontal scroll region", async () => {
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(form() as never);
    wrapper = mount(EcrfFormPanel, { props: { formId: FORM_ID } });
    await flushPromises();

    const scrollRegion = wrapper.find('[data-testid="ecrf-form-items-scroll"]');
    expect(scrollRegion.exists()).toBe(true);
    expect(scrollRegion.attributes("role")).toBe("region");
    expect(scrollRegion.attributes("tabindex")).toBe("0");
    expect(scrollRegion.attributes("aria-label")).toBe(
      "Case report form items"
    );
    expect(scrollRegion.find('[data-testid="ecrf-form-items"]').exists()).toBe(
      true
    );
    await expect(wrapper.element).toBeAccessible();
  });

  it("reloads the open form and its queries when ScopeLockControls emits changed", async () => {
    vi.mocked(ecrfFormsApi.getForm)
      .mockResolvedValueOnce(form() as never)
      .mockResolvedValueOnce(
        form({
          locks: [
            {
              lock_id: "lock-form-1",
              kind: "Frozen",
              scope: `collection_instance:${FORM_ID}`,
              locked_at: "2027-04-02T00:00:00Z",
              locked_by: "person:dm1",
              reason: "Interim freeze",
            },
          ],
        }) as never
      );

    wrapper = mount(EcrfFormPanel, { props: { formId: FORM_ID } });
    await flushPromises();
    expect(ecrfFormsApi.getForm).toHaveBeenCalledTimes(1);
    expect(ecrfQueriesApi.listForForm).toHaveBeenCalledTimes(1);

    const lockControls = wrapper.findComponent(ScopeLockControls);
    expect(lockControls.exists()).toBe(true);
    lockControls.vm.$emit("changed");
    await flushPromises();

    expect(ecrfFormsApi.getForm).toHaveBeenCalledTimes(2);
    expect(ecrfFormsApi.getForm).toHaveBeenLastCalledWith(FORM_ID);
    expect(ecrfQueriesApi.listForForm).toHaveBeenCalledTimes(2);
    expect(wrapper.find('[data-testid="ecrf-form-locks"]').text()).toContain(
      "Interim freeze"
    );
  });

  it("forwards item_id through formLocation and observationLocation and scrolls/focuses the item when clicking a query form link", async () => {
    const scrollSpy = vi.fn();
    const origScroll = HTMLElement.prototype.scrollIntoView;
    HTMLElement.prototype.scrollIntoView = scrollSpy;
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(form() as never);
    vi.mocked(ecrfQueriesApi.listForForm).mockResolvedValue([
      {
        query_id: "query-sys",
        form_id: FORM_ID,
        enrollment_id: "e",
        item_id: "SYSBP",
        observation_id: OBS_ID,
        revision: 3,
        current_revision: 3,
        value_changed: false,
        status: "open",
        text: "Confirm systolic reading",
        messages: [
          {
            kind: "raised",
            text: "Confirm systolic reading",
            by: "monitor",
            at: "2027-03-14T10:00:00Z",
          },
        ],
        raised_by: "monitor",
        raised_at: "2027-03-14T10:00:00Z",
        version: 1,
      },
    ] as never);

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: "/ecrf", name: "ecrf", component: { template: "<div />" } },
        {
          path: "/observations/:observationRef",
          name: "observation",
          component: { template: "<div />" },
        },
      ],
    });
    await router.push(`/ecrf?formId=${FORM_ID}`);
    await router.isReady();

    const formLocationSpy = vi.fn((fId: string, itemId?: string | null) => ({
      path: "/ecrf",
      query: {
        formId: fId,
        ...(itemId ? { itemId } : {}),
      },
    }));
    const obsLocationSpy = vi.fn(
      (obsId: string, fId?: string | null, itemId?: string | null) => ({
        name: "observation",
        params: { observationRef: `observation:${obsId}` },
        query: {
          ...(fId ? { form: `collection_instance:${fId}` } : {}),
          ...(itemId ? { item: itemId } : {}),
        },
      })
    );

    try {
      wrapper = mount(EcrfFormPanel, {
        props: {
          formId: FORM_ID,
          formLocation: formLocationSpy,
          observationLocation: obsLocationSpy,
        },
        global: { plugins: [router] },
        attachTo: document.body,
      });
      await flushPromises();

      expect(obsLocationSpy).toHaveBeenCalledWith(OBS_ID, FORM_ID, "SYSBP");
      expect(formLocationSpy).toHaveBeenCalledWith(FORM_ID, "SYSBP");
      expect(
        wrapper.find("#query-open-form-query-sys").attributes("href")
      ).toBe(`/ecrf?formId=${FORM_ID}&itemId=SYSBP`);

      scrollSpy.mockClear();
      await wrapper.find("#query-open-form-query-sys").trigger("click");
      await flushPromises();
      await wrapper.setProps({ highlightItemId: "SYSBP" });
      await flushPromises();

      expect(router.currentRoute.value.query).toEqual({
        formId: FORM_ID,
        itemId: "SYSBP",
      });
      expect(
        wrapper.find("#ecrf-row-SYSBP").attributes("data-highlighted")
      ).toBe("true");
      expect(scrollSpy).toHaveBeenCalledWith({ block: "center" });
      expect(document.activeElement).toBe(
        wrapper.find("#ecrf-item-SYSBP").element
      );
    } finally {
      HTMLElement.prototype.scrollIntoView = origScroll;
    }
  });

  it("hydrates form state and updates query value_changed in one round-trip when saveValues returns form", async () => {
    const initialForm = form();
    const hydratedForm = form({
      version: 3,
      complete: true,
      missing_required: [],
      items: [
        item({
          ...initialForm.items[0],
          value: { type: "quantity", value: 128, unit: "mm[Hg]" },
          revision: 4,
        }),
        item({
          ...initialForm.items[1],
          observation_id: OBS_DIA,
          value: { type: "quantity", value: 82, unit: "mm[Hg]" },
          revision: 1,
        }),
        initialForm.items[2],
      ],
    });
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValueOnce(initialForm as never);
    vi.mocked(ecrfQueriesApi.listForForm).mockResolvedValueOnce([
      formQuery({ revision: 3, current_revision: 3, value_changed: false }),
    ] as never);
    vi.mocked(ecrfFormsApi.saveValues).mockResolvedValueOnce({
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
          observation_id: OBS_DIA,
          value: { type: "quantity", value: "82", unit: "mm[Hg]" },
          revision: 1,
          changed: true,
        },
      ],
      form: hydratedForm,
    } as never);

    wrapper = mount(EcrfFormPanel, { props: { formId: FORM_ID } });
    await flushPromises();
    expect(ecrfFormsApi.getForm).toHaveBeenCalledTimes(1);
    expect(ecrfQueriesApi.listForForm).toHaveBeenCalledTimes(1);

    await wrapper.find("#ecrf-item-SYSBP").setValue("128");
    await wrapper.find("#ecrf-item-DIABP").setValue("82");
    await wrapper.find("#ecrf-save-reason").setValue("Corrected against chart");
    await wrapper.find("#ecrf-form-page").trigger("submit");
    await flushPromises();

    expect(ecrfFormsApi.getForm).toHaveBeenCalledTimes(1);
    expect(ecrfQueriesApi.listForForm).toHaveBeenCalledTimes(1);
    expect(wrapper.find('[data-testid="ecrf-form-status"]').text()).toBe(
      "Complete"
    );
    expect(
      wrapper.find('[data-testid="ecrf-query-changed-SYSBP"]').exists()
    ).toBe(true);
  });

  it("falls back to getForm when saveValues returns a mismatched form version", async () => {
    const initialForm = form();
    const reloadedForm = form({
      version: 3,
      complete: true,
      missing_required: [],
      items: [
        item({
          ...initialForm.items[0],
          value: { type: "quantity", value: 126, unit: "mm[Hg]" },
          revision: 4,
        }),
        item({
          ...initialForm.items[1],
          observation_id: OBS_DIA,
          value: { type: "quantity", value: 80, unit: "mm[Hg]" },
          revision: 1,
        }),
        initialForm.items[2],
      ],
    });
    vi.mocked(ecrfFormsApi.getForm)
      .mockResolvedValueOnce(initialForm as never)
      .mockResolvedValueOnce(reloadedForm as never);
    vi.mocked(ecrfQueriesApi.listForForm)
      .mockResolvedValueOnce([
        formQuery({ revision: 3, current_revision: 3, value_changed: false }),
      ] as never)
      .mockResolvedValueOnce([
        formQuery({ revision: 3, current_revision: 4, value_changed: true }),
      ] as never);
    vi.mocked(ecrfFormsApi.saveValues).mockResolvedValueOnce({
      form_id: FORM_ID,
      version: 3,
      values: [
        {
          item_id: "SYSBP",
          observation_id: OBS_ID,
          value: { type: "quantity", value: "126", unit: "mm[Hg]" },
          revision: 4,
          changed: true,
        },
      ],
      form: form({ version: 2 }),
    } as never);

    wrapper = mount(EcrfFormPanel, { props: { formId: FORM_ID } });
    await flushPromises();

    await wrapper.find("#ecrf-item-SYSBP").setValue("126");
    await wrapper.find("#ecrf-item-DIABP").setValue("80");
    await wrapper.find("#ecrf-save-reason").setValue("Fallback reload check");
    await wrapper.find("#ecrf-form-page").trigger("submit");
    await flushPromises();

    expect(ecrfFormsApi.getForm).toHaveBeenCalledTimes(2);
    expect(ecrfQueriesApi.listForForm).toHaveBeenCalledTimes(2);
    expect(wrapper.find('[data-testid="ecrf-form-status"]').text()).toBe(
      "Complete"
    );
  });

  it("caps bulk SDV verification requests at 4 concurrent in-flight calls while preserving item order", async () => {
    const sixItems = Array.from({ length: 6 }, (_, idx) => {
      const code = `ITEM_${idx + 1}`;
      const obsId = `0192f3c4-5e6a-7b8c-9d0e-00000000010${idx + 1}`;
      return item({
        item_id: code,
        concept: `concept:${obsId}`,
        value_kind: "Quantity",
        unit: "mm[Hg]",
        required: true,
        observation_id: obsId,
        value: { type: "quantity", value: 100 + idx, unit: "mm[Hg]" },
        observed_at: "2027-03-14T10:00:00Z",
        revision: 1,
      });
    });
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(
      form({ items: sixItems, missing_required: [], complete: true }) as never
    );

    let active = 0;
    let maxInFlight = 0;
    const pendingResolvers: Array<() => void> = [];
    vi.mocked(kernelApi.verifyObservation).mockImplementation(
      ({ observationRef, revision }) => {
        active += 1;
        if (active > maxInFlight) maxInFlight = active;
        return new Promise((resolve) => {
          pendingResolvers.push(() => {
            active -= 1;
            resolve({
              ref: `assertion:${observationRef}`,
              kind: "Verified",
              target: observationRef,
              revision,
              retracts: null,
              asserted_at: "2027-03-14T11:00:00Z",
              asserted_by: "cra1",
            });
          });
        });
      }
    );

    wrapper = mount(EcrfFormPanel, {
      props: { formId: FORM_ID, canVerifySource: true },
    });
    await flushPromises();

    await wrapper.find("#ecrf-sdv-select-all").trigger("click");
    await wrapper
      .find("#ecrf-bulk-sdv-reason")
      .setValue("Verified all 6 items against clinic chart");
    const submitPromise = wrapper
      .find("#ecrf-bulk-sdv-submit")
      .trigger("click");
    await flushPromises();

    expect(pendingResolvers).toHaveLength(4);
    expect(maxInFlight).toBe(4);

    while (pendingResolvers.length > 0) {
      const nextBatch = pendingResolvers.splice(0, pendingResolvers.length);
      for (const release of nextBatch) release();
      await flushPromises();
    }
    await submitPromise;
    await flushPromises();

    expect(kernelApi.verifyObservation).toHaveBeenCalledTimes(6);
    expect(maxInFlight).toBeLessThanOrEqual(4);
    const resultRows = wrapper.findAll("[data-sdv-result]");
    expect(resultRows.map((row) => row.attributes("data-sdv-result"))).toEqual([
      "ITEM_1",
      "ITEM_2",
      "ITEM_3",
      "ITEM_4",
      "ITEM_5",
      "ITEM_6",
    ]);
  });

  it("renders a <select> for codelist items, keeps text input when codelist_options is empty, and saves coded values", async () => {
    const csVersion = "0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d72";
    const codedForm = form({
      items: [
        item({
          item_id: "VSPOS",
          concept: "C71148@1",
          value_kind: "Coded",
          required: true,
          code_system_version_id: csVersion,
          codelist: "C71148",
          codelist_options: [
            { code: "SITTING", display: "Sitting" },
            { code: "SUPINE", display: "Supine" },
          ],
        }),
        item({
          item_id: "AELLT",
          concept: "AELLT@1",
          value_kind: "Coded",
          code_system_version_id: csVersion,
          codelist: null,
          codelist_options: null,
        }),
      ],
      missing_required: ["VSPOS"],
    });

    vi.mocked(ecrfFormsApi.getForm)
      .mockResolvedValueOnce(codedForm as never)
      .mockResolvedValueOnce(
        form({
          ...codedForm,
          version: 3,
          complete: true,
          missing_required: [],
        }) as never
      );
    vi.mocked(ecrfFormsApi.saveValues).mockResolvedValue({
      form_id: FORM_ID,
      version: 3,
      values: [],
    } as never);

    wrapper = mount(EcrfFormPanel, { props: { formId: FORM_ID } });
    await flushPromises();
    const view = wrapper;
    await expect(view.element).toBeAccessible();

    const posSelect = view.find("#ecrf-item-VSPOS");
    expect(posSelect.element.tagName).toBe("SELECT");
    expect(
      posSelect.findAll("option").map((opt) => opt.attributes("value"))
    ).toEqual(["", "SITTING", "SUPINE"]);

    const lltInput = view.find("#ecrf-item-AELLT");
    expect(lltInput.element.tagName).toBe("INPUT");

    await posSelect.setValue("SITTING");
    await lltInput.setValue("LLT_HEADACHE");
    await view.find("#ecrf-save-reason").setValue("Initial coded capture");
    await view.find("#ecrf-form-page").trigger("submit");
    await flushPromises();

    expect(vi.mocked(ecrfFormsApi.saveValues).mock.calls[0][0].values).toEqual([
      {
        item_id: "VSPOS",
        value: {
          type: "coded",
          code_system_version_id: csVersion,
          code: "SITTING",
        },
        expected_revision: 0,
        observation_id: null,
        observed_at: null,
      },
      {
        item_id: "AELLT",
        value: {
          type: "coded",
          code_system_version_id: csVersion,
          code: "LLT_HEADACHE",
        },
        expected_revision: 0,
        observation_id: null,
        observed_at: null,
      },
    ]);
  });

  it("displays a recorded code removed by an amendment until the user picks a valid codelist option", async () => {
    const csVersion = "0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d72";
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(
      form({
        items: [
          item({
            item_id: "VSPOS",
            value_kind: "Coded",
            required: true,
            code_system_version_id: csVersion,
            codelist: "C71148",
            codelist_options: [
              { code: "SITTING", display: "Sitting" },
              { code: "SUPINE", display: "Supine" },
            ],
            value: {
              type: "coded",
              code_system_version_id: csVersion,
              code: "STANDING",
            },
            observation_id: OBS_ID,
            revision: 1,
          }),
        ],
        missing_required: [],
        complete: true,
      }) as never
    );

    wrapper = mount(EcrfFormPanel, { props: { formId: FORM_ID } });
    await flushPromises();
    const view = wrapper;
    const posSelect = view.find("#ecrf-item-VSPOS");
    expect((posSelect.element as HTMLSelectElement).value).toBe("STANDING");
    expect(
      posSelect.findAll("option").map((opt) => opt.attributes("value"))
    ).toEqual(["", "STANDING", "SITTING", "SUPINE"]);

    await posSelect.setValue("SUPINE");
    expect((posSelect.element as HTMLSelectElement).value).toBe("SUPINE");
    expect(
      posSelect.findAll("option").map((opt) => opt.attributes("value"))
    ).toEqual(["", "SITTING", "SUPINE"]);
  });

  it("resolves bare <uuid>@<version> item concepts via NodeRefChip and collapses scheduleError inside SupportDetails", async () => {
    const bareConcept = "0192f3c4-5e6a-7b8c-9d0e-00000000c201@1";
    const getNodeSpy = vi.spyOn(kernelApi, "getNode").mockResolvedValue({
      ref: `concept:${bareConcept}`,
      kind: "concept",
      code: "HR",
      title: "Heart Rate",
      status: "Active",
      mask: null,
    });

    try {
      wrapper = mount(EcrfFormPanel, {
        props: { scheduleError: "Schedule gateway 503" },
      });
      await flushPromises();

      const schedAlert = wrapper.find("#ecrf-schedule-error");
      expect(schedAlert.exists()).toBe(true);
      expect(schedAlert.find("details.support-details").exists()).toBe(true);
      expect(schedAlert.text()).toContain("Schedule gateway 503");
      expect(wrapper.find("#ecrf-existing-form-select").exists()).toBe(true);
      expect(
        wrapper.find("details.ecrf-direct-id-disclosure").attributes("open")
      ).toBeUndefined();
      wrapper.unmount();

      vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(
        form({
          items: [
            item({
              item_id: "HR",
              concept: bareConcept,
              unit: "/min",
              value_kind: "Quantity",
            }),
          ],
        }) as never
      );

      wrapper = mount(EcrfFormPanel, { props: { formId: FORM_ID } });
      await flushPromises();

      expect(getNodeSpy).toHaveBeenCalledWith(`concept:${bareConcept}`);
      expect(wrapper.find(".ecrf-item-concept").text()).toContain("Heart Rate");
    } finally {
      getNodeSpy.mockRestore();
    }
  });

  it("renders Collection spec and Visit dropdowns when scheduleOptions exist even without enrollmentContext while still allowing direct form reference loading", async () => {
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(
      form({ form_id: FORM_ID }) as never
    );

    wrapper = mount(EcrfFormPanel, {
      props: {
        scheduleOptions: {
          collectionSpecs: [
            {
              id: "VS",
              name: "Vital Signs",
              plannedActivityIds: ["SCR", "W4"],
            },
            { id: "AE", name: "Adverse Events", plannedActivityIds: [] },
          ],
          plannedActivities: [
            { id: "SCR", name: "Screening", studyDay: -7 },
            { id: "W4", name: "Week 4", studyDay: 28 },
          ],
        },
      },
    });
    await flushPromises();

    // Raw Form id and Enrollment id are collapsed behind disclosure by default
    const disclosures = wrapper.findAll("details.ecrf-direct-id-disclosure");
    expect(disclosures.length).toBeGreaterThanOrEqual(2);
    for (const disclosure of disclosures) {
      expect(disclosure.attributes("open")).toBeUndefined();
    }

    // Collection spec and Visit render as structured <select> dropdowns
    const specSelect = wrapper.find("#ecrf-collection-spec");
    const visitSelect = wrapper.find("#ecrf-planned-activity");
    expect(specSelect.element.tagName).toBe("SELECT");
    expect(visitSelect.element.tagName).toBe("SELECT");
    expect(
      specSelect.findAll("option").map((opt) => opt.attributes("value"))
    ).toEqual(["", "VS", "AE"]);

    await specSelect.setValue("AE");
    await nextTick();
    // AE is unscheduled-only (plannedActivityIds: []) so only Unscheduled ("") is offered
    expect(
      visitSelect.findAll("option").map((opt) => opt.attributes("value"))
    ).toEqual([""]);

    // Users can still load an existing form directly when supplying a known form reference
    await wrapper.find("#ecrf-form-id").setValue(FORM_ID);
    await wrapper.find("form.ecrf-form-reuse-block").trigger("submit.prevent");
    await flushPromises();

    expect(ecrfFormsApi.getForm).toHaveBeenCalledWith(FORM_ID);
    expect(wrapper.find('[data-testid="ecrf-form-id"]').text()).toBe(FORM_ID);
  });

  it("aborts superseded in-flight draft previews on rapid edits and ignores AbortError without clearing newer warnings", async () => {
    vi.useFakeTimers();
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(form() as never);

    const capturedSignals: AbortSignal[] = [];
    let rejectFirstPreview: ((reason?: unknown) => void) | undefined;
    let resolveSecondPreview: ((value: unknown) => void) | undefined;

    vi.mocked(ecrfFormsApi.previewValues)
      .mockImplementationOnce((_formId, _values, options) => {
        const signal =
          options && "aborted" in options ? options : options?.signal;
        if (signal) capturedSignals.push(signal);
        return new Promise((_resolve, reject) => {
          rejectFirstPreview = reject;
        }) as never;
      })
      .mockImplementationOnce((_formId, _values, options) => {
        const signal =
          options && "aborted" in options ? options : options?.signal;
        if (signal) capturedSignals.push(signal);
        return new Promise((resolve) => {
          resolveSecondPreview = resolve;
        }) as never;
      });

    wrapper = mount(EcrfFormPanel, { props: { formId: FORM_ID } });
    await flushPromises();

    await wrapper.find("#ecrf-item-SYSBP").setValue("210");
    vi.advanceTimersByTime(260);
    await flushPromises();

    expect(ecrfFormsApi.previewValues).toHaveBeenCalledTimes(1);
    expect(capturedSignals).toHaveLength(1);
    expect(capturedSignals[0].aborted).toBe(false);

    // Typing a second edit immediately aborts the first in-flight preview request.
    await wrapper.find("#ecrf-item-SYSBP").setValue("245");
    expect(capturedSignals[0].aborted).toBe(true);

    vi.advanceTimersByTime(260);
    await flushPromises();

    expect(ecrfFormsApi.previewValues).toHaveBeenCalledTimes(2);
    expect(capturedSignals).toHaveLength(2);
    expect(capturedSignals[1].aborted).toBe(false);

    resolveSecondPreview?.({
      form_id: FORM_ID,
      evaluated_rule_packs: 1,
      warnings: [
        {
          pack_key: "vitals.pack",
          rule_id: "VS_SYSBP_MAX",
          rule_version: 1,
          severity: "Warning",
          dataset: "VS",
          item_id: "SYSBP",
          message: "Systolic BP 245 exceeds plausible upper bound.",
          remediation_guidance: null,
        },
      ],
    });
    await flushPromises();

    expect(
      wrapper.find("[data-testid='ecrf-preview-warnings-SYSBP']").text()
    ).toContain("VS_SYSBP_MAX");

    // Late AbortError from the superseded preview is silently ignored and keeps newer warnings intact.
    rejectFirstPreview?.(
      new DOMException("The operation was aborted.", "AbortError")
    );
    await flushPromises();

    expect(
      wrapper.find("[data-testid='ecrf-preview-warnings-SYSBP']").text()
    ).toContain("VS_SYSBP_MAX");
  });

  it("aborts an active draft preview before reverting, saving, reloading, or unmounting", async () => {
    vi.useFakeTimers();
    const initialForm = form();
    const savedForm = form({
      version: 3,
      complete: true,
      missing_required: [],
      items: [
        item({
          ...initialForm.items[0],
          value: { type: "quantity", value: 132, unit: "mm[Hg]" },
          revision: 4,
        }),
        item({
          ...initialForm.items[1],
          observation_id: OBS_DIA,
          value: { type: "quantity", value: 82, unit: "mm[Hg]" },
          revision: 1,
        }),
        initialForm.items[2],
      ],
    });
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(initialForm as never);
    vi.mocked(ecrfFormsApi.saveValues).mockResolvedValue({
      form_id: FORM_ID,
      version: 3,
      values: [
        {
          item_id: "SYSBP",
          observation_id: OBS_ID,
          value: { type: "quantity", value: 132, unit: "mm[Hg]" },
          revision: 4,
          changed: true,
        },
        {
          item_id: "DIABP",
          observation_id: OBS_DIA,
          value: { type: "quantity", value: 82, unit: "mm[Hg]" },
          revision: 1,
          changed: true,
        },
      ],
      form: savedForm,
    } as never);

    const capturedSignals: AbortSignal[] = [];
    vi.mocked(ecrfFormsApi.previewValues).mockImplementation(
      (_formId, _values, options) => {
        const signal =
          options && "aborted" in options ? options : options?.signal;
        if (signal) capturedSignals.push(signal);
        return new Promise(() => {}) as never;
      }
    );

    wrapper = mount(EcrfFormPanel, { props: { formId: FORM_ID } });
    await flushPromises();

    // 1. Reverting the last dirty item aborts active preview immediately
    await wrapper.find("#ecrf-item-DIABP").setValue("80");
    vi.advanceTimersByTime(260);
    await flushPromises();
    expect(capturedSignals).toHaveLength(1);
    expect(capturedSignals[0].aborted).toBe(false);

    await wrapper.find("#ecrf-revert-DIABP").trigger("click");
    await flushPromises();
    expect(capturedSignals[0].aborted).toBe(true);

    // 2. Save aborts active preview before saveValues runs
    await wrapper.find("#ecrf-item-SYSBP").setValue("132");
    await wrapper.find("#ecrf-item-DIABP").setValue("82");
    vi.advanceTimersByTime(260);
    await flushPromises();
    expect(capturedSignals).toHaveLength(2);
    expect(capturedSignals[1].aborted).toBe(false);

    await wrapper.find("#ecrf-save-reason").setValue("Corrected BP");
    await wrapper.find("#ecrf-form-page").trigger("submit");
    await flushPromises();
    expect(capturedSignals[1].aborted).toBe(true);
    expect(ecrfFormsApi.saveValues).toHaveBeenCalledTimes(1);

    // 3. Reloading another form aborts active preview
    const secondFormId = "01950000-0000-7000-8000-000000000999";
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValueOnce(
      form({ form_id: secondFormId }) as never
    );
    await wrapper.find("#ecrf-item-DIABP").setValue("84");
    vi.advanceTimersByTime(260);
    await flushPromises();
    expect(capturedSignals).toHaveLength(3);
    expect(capturedSignals[2].aborted).toBe(false);

    await wrapper.setProps({ formId: secondFormId });
    await flushPromises();
    expect(capturedSignals[2].aborted).toBe(true);

    // 4. Unmount aborts active preview
    await wrapper.find("#ecrf-item-DIABP").setValue("86");
    vi.advanceTimersByTime(260);
    await flushPromises();
    expect(capturedSignals).toHaveLength(4);
    expect(capturedSignals[3].aborted).toBe(false);

    wrapper.unmount();
    wrapper = undefined;
    expect(capturedSignals[3].aborted).toBe(true);
  });

  it("renders a status recovery banner with inline baseline comparison, draft discard, Unsaved badges beside Verified metadata, and axe compliance", async () => {
    signInUser("crc-1", "tenant-1");
    const draftKey = scopedKey(
      { tenantId: "tenant-1", userId: "crc-1" },
      "ecrf_form_draft",
      FORM_ID
    )!;
    localStorage.setItem(
      draftKey,
      JSON.stringify({
        form_id: FORM_ID,
        items: {
          SYSBP: { text: "138", missing: "", revision: 3 },
          DIABP: { text: "86", missing: "", revision: 0 },
        },
      })
    );

    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(
      form({
        assertions: [
          {
            assertion_id: "sdv-1",
            kind: "Verified",
            target: `observation:${OBS_ID}`,
            revision: 3,
            text: null,
            asserted_by: "cra1",
            asserted_at: "2027-03-20T00:00:00Z",
            reason: "Checked chart",
            retracted_at: null,
          },
        ],
      }) as never
    );

    wrapper = mount(EcrfFormPanel, {
      props: { formId: FORM_ID, canVerifySource: true },
      attachTo: document.body,
    });
    await flushPromises();

    // Recovery banner is visible with role="status".
    const banner = wrapper.find('[data-testid="ecrf-draft-restored-banner"]');
    expect(banner.exists()).toBe(true);
    expect(banner.attributes("role")).toBe("status");
    expect(banner.text()).toContain("SYSBP");
    expect(banner.text()).toContain("DIABP");

    // Edited rows carry .ecrf-item-edited and a visible Unsaved badge beside Verified metadata.
    expect(wrapper.find("#ecrf-row-SYSBP").classes()).toContain(
      "ecrf-item-edited"
    );
    expect(wrapper.find("#ecrf-row-DIABP").classes()).toContain(
      "ecrf-item-edited"
    );
    expect(wrapper.find("#ecrf-row-COMMENT").classes()).not.toContain(
      "ecrf-item-edited"
    );
    const sysUnsaved = wrapper.find('[data-testid="ecrf-unsaved-badge-SYSBP"]');
    const sysSdv = wrapper.find('[data-testid="ecrf-sdv-status-SYSBP"]');
    const sysSdvDraftNote = wrapper.find(
      '[data-testid="ecrf-sdv-draft-note-SYSBP"]'
    );
    expect(sysUnsaved.exists()).toBe(true);
    expect(sysUnsaved.text()).toBe("Unsaved");
    expect(sysSdv.text()).toContain("Verified (rev 3)");
    expect(sysSdvDraftNote.exists()).toBe(true);
    expect(sysSdvDraftNote.text()).toContain(
      "Verification applies only to saved revision 3; unsaved draft is not verified."
    );
    expect(
      wrapper.find("#ecrf-item-SYSBP").attributes("aria-describedby")
    ).toContain("ecrf-sdv-draft-note-SYSBP");
    expect(
      wrapper.find('[data-testid="ecrf-sdv-draft-note-DIABP"]').exists()
    ).toBe(false);
    expect(
      wrapper.find('[data-testid="ecrf-unsaved-badge-COMMENT"]').exists()
    ).toBe(false);

    // Baseline comparison is initially hidden, then revealed inline when toggled.
    const compareBtn = wrapper.find("#ecrf-draft-compare-toggle");
    expect(compareBtn.attributes("aria-pressed")).toBe("false");
    expect(
      wrapper.find('[data-testid="ecrf-committed-baseline-SYSBP"]').exists()
    ).toBe(false);

    await compareBtn.trigger("click");
    await flushPromises();
    expect(compareBtn.attributes("aria-pressed")).toBe("true");
    expect(
      wrapper.find('[data-testid="ecrf-committed-baseline-SYSBP"]').text()
    ).toContain("Committed: 120 mm[Hg]");
    expect(
      wrapper.find('[data-testid="ecrf-committed-baseline-DIABP"]').text()
    ).toContain("Committed: No recorded value");

    await expect(wrapper.element).toBeAccessible();

    // Per-item Revert resets DIABP, hands focus back to #ecrf-item-DIABP, updates localStorage synchronously, and keeps SYSBP's restored banner & comparison active.
    (wrapper.find("#ecrf-revert-DIABP").element as HTMLButtonElement).focus();
    await wrapper.find("#ecrf-revert-DIABP").trigger("click");
    await flushPromises();
    expect(document.activeElement?.id).toBe("ecrf-item-DIABP");
    expect(
      (wrapper.find("#ecrf-item-DIABP").element as HTMLInputElement).value
    ).toBe("");
    expect(wrapper.find("#ecrf-revert-DIABP").exists()).toBe(false);
    expect(
      wrapper.find('[data-testid="ecrf-draft-restored-banner"]').exists()
    ).toBe(true);
    expect(
      wrapper.find('[data-testid="ecrf-committed-baseline-SYSBP"]').exists()
    ).toBe(true);
    expect(JSON.parse(localStorage.getItem(draftKey)!)).toEqual({
      form_id: FORM_ID,
      items: {
        SYSBP: { text: "138", missing: "", revision: 3 },
      },
    });

    // Discard draft reverts remaining inputs to committed server values, removes storage key, hides Unsaved badges & banner, and hands off focus cleanly.
    (wrapper.find("#ecrf-draft-discard").element as HTMLButtonElement).focus();
    await wrapper.find("#ecrf-draft-discard").trigger("click");
    await flushPromises();

    expect(document.activeElement?.id).toBe("ecrf-item-SYSBP");

    expect(
      wrapper.find('[data-testid="ecrf-draft-restored-banner"]').exists()
    ).toBe(false);
    expect(
      (wrapper.find("#ecrf-item-SYSBP").element as HTMLInputElement).value
    ).toBe("120");
    expect(
      (wrapper.find("#ecrf-item-DIABP").element as HTMLInputElement).value
    ).toBe("");
    expect(
      wrapper.find('[data-testid="ecrf-unsaved-badge-SYSBP"]').exists()
    ).toBe(false);
    expect(localStorage.getItem(draftKey)).toBeNull();
    expect(wrapper.emitted("dirty-change")?.at(-1)).toEqual([false]);

    // Typing a fresh edit and reverting the last dirty item removes the storage key; reloading with unsaved edits into a Frozen form surfaces ecrf-draft-locked-banner.
    await wrapper.find("#ecrf-item-DIABP").setValue("88");
    await flushPromises();
    await wrapper.find("#ecrf-revert-DIABP").trigger("click");
    await flushPromises();
    expect(localStorage.getItem(draftKey)).toBeNull();
    expect(wrapper.emitted("dirty-change")?.at(-1)).toEqual([false]);

    await wrapper.find("#ecrf-item-DIABP").setValue("90");
    await flushPromises();
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValueOnce(
      form({
        locks: [
          {
            lock_id: "lock-reload-1",
            kind: "Frozen",
            scope: `collection_instance:${FORM_ID}`,
            locked_at: "2027-04-02T00:00:00Z",
            locked_by: "dm1",
            reason: "Interim cut",
          },
        ],
      }) as never
    );
    wrapper.findComponent(ScopeLockControls).vm.$emit("changed");
    await flushPromises();

    const lockedReloadBanner = wrapper.find(
      '[data-testid="ecrf-draft-locked-banner"]'
    );
    expect(lockedReloadBanner.exists()).toBe(true);
    expect(lockedReloadBanner.attributes("role")).toBe("status");
    expect(lockedReloadBanner.text()).toContain("DIABP");
    expect(lockedReloadBanner.text()).toContain("90 mm[Hg]");
    expect(localStorage.getItem(draftKey)).toBeNull();
    wrapper.unmount();

    // Reverting an item whose committed server baseline is a missing reason disables #ecrf-item-<id> and hands focus to its missing-reason select.
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValueOnce(
      form({
        items: [
          form().items[0],
          item({
            item_id: "DIABP",
            unit: "mm[Hg]",
            value_kind: "Quantity",
            required: true,
            value: { type: "missing", reason: "NotDone" },
            observation_id: "obs-diabp",
            revision: 1,
            recorded_by: "crc1",
            recorded_at: "2027-03-14T09:05:00Z",
          }),
          form().items[2],
        ],
      }) as never
    );
    wrapper = mount(EcrfFormPanel, {
      props: { formId: FORM_ID, canVerifySource: true },
      attachTo: document.body,
    });
    await flushPromises();

    await wrapper
      .find('select[aria-label="Why DIABP is missing"]')
      .setValue("");
    await wrapper.find("#ecrf-item-DIABP").setValue("82");
    await flushPromises();
    (
      wrapper.find('[data-testid="ecrf-revert-DIABP"]')
        .element as HTMLButtonElement
    ).focus();
    await wrapper.find('[data-testid="ecrf-revert-DIABP"]').trigger("click");
    await flushPromises();

    expect(
      (wrapper.find("#ecrf-item-DIABP").element as HTMLInputElement).disabled
    ).toBe(true);
    expect(document.activeElement?.getAttribute("aria-label")).toBe(
      "Why DIABP is missing"
    );
  });

  it("performs zero form.assertions reads and zero selectedSdvItems.includes scans across keystrokes and the 250 ms preview", async () => {
    vi.useFakeTimers();

    let assertionReads = 0;
    function trackedAssertion(raw: {
      assertion_id: string;
      kind: string;
      target: string;
      revision: number | null;
      text: string | null;
      asserted_at: string;
      asserted_by: string;
      reason: string;
      retracted_at: string | null;
    }) {
      const copy = { ...raw };
      const kindValue = raw.kind;
      Object.defineProperty(copy, "kind", {
        enumerable: true,
        configurable: true,
        get() {
          assertionReads += 1;
          return kindValue;
        },
      });
      return copy;
    }

    const rawAssertions = [
      trackedAssertion({
        assertion_id: "a-obs10-verified",
        kind: "Verified",
        target: "observation:obs-10@2",
        revision: 2,
        text: null,
        asserted_at: "2027-03-14T10:00:00Z",
        asserted_by: "cra1",
        reason: "Verified against bedside chart",
        retracted_at: null,
      }),
      trackedAssertion({
        assertion_id: "a-obs2-stale",
        kind: "Verified",
        target: "observation:obs-2",
        revision: 1,
        text: null,
        asserted_at: "2027-03-14T10:01:00Z",
        asserted_by: "cra1",
        reason: "Initial chart check",
        retracted_at: null,
      }),
      trackedAssertion({
        assertion_id: "a-obs3-retracted",
        kind: "Verified",
        target: "observation:obs-3",
        revision: 1,
        text: null,
        asserted_at: "2027-03-14T10:02:00Z",
        asserted_by: "cra1",
        reason: "Withdrawn check",
        retracted_at: "2027-03-14T10:05:00Z",
      }),
      trackedAssertion({
        assertion_id: "a-obs3-null-rev",
        kind: "Verified",
        target: "observation:obs-3",
        revision: null,
        text: null,
        asserted_at: "2027-03-14T10:03:00Z",
        asserted_by: "cra1",
        reason: "Unversioned note",
        retracted_at: null,
      }),
      trackedAssertion({
        assertion_id: "a-obs1-query",
        kind: "Queried",
        target: "observation:obs-1",
        revision: 3,
        text: "Confirm systolic BP",
        asserted_at: "2027-03-14T10:04:00Z",
        asserted_by: "dm1",
        reason: "Out of range",
        retracted_at: null,
      }),
    ];

    const trackedAssertionsArray = [...rawAssertions];
    rawAssertions.forEach((entry, index) => {
      Object.defineProperty(trackedAssertionsArray, index, {
        enumerable: true,
        configurable: true,
        get() {
          assertionReads += 1;
          return entry;
        },
      });
    });

    const loadedForm = form({
      items: [
        item({
          item_id: "SYSBP",
          unit: "mm[Hg]",
          value_kind: "Quantity",
          required: true,
          value: { type: "quantity", value: 120, unit: "mm[Hg]" },
          observation_id: "obs-1",
          revision: 3,
          recorded_by: "crc1",
          recorded_at: "2027-03-14T09:05:00Z",
        }),
        item({
          item_id: "DIABP",
          unit: "mm[Hg]",
          value_kind: "Quantity",
          required: true,
          value: { type: "quantity", value: 80, unit: "mm[Hg]" },
          observation_id: "obs-10",
          revision: 2,
          recorded_by: "crc1",
          recorded_at: "2027-03-14T09:05:00Z",
        }),
        item({
          item_id: "HR",
          unit: "/min",
          value_kind: "Quantity",
          required: false,
          value: { type: "quantity", value: 74, unit: "/min" },
          observation_id: "obs-2",
          revision: 2,
          recorded_by: "crc1",
          recorded_at: "2027-03-14T09:06:00Z",
        }),
        item({
          item_id: "TEMP",
          unit: "Cel",
          value_kind: "Quantity",
          required: false,
          value: { type: "quantity", value: 36.7, unit: "Cel" },
          observation_id: "obs-3",
          revision: 1,
          recorded_by: "crc1",
          recorded_at: "2027-03-14T09:06:00Z",
        }),
        item({ item_id: "COMMENT", value_kind: "Text" }),
      ],
      assertions: trackedAssertionsArray,
    });

    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(loadedForm as never);

    const apiWithPreview = ecrfFormsApi as Omit<
      typeof ecrfFormsApi,
      "previewValues"
    > & {
      previewValues?: typeof ecrfFormsApi.previewValues;
    };
    const origPreviewValues = apiWithPreview.previewValues;
    const origIncludes = Array.prototype.includes;
    let selectedIncludesCalls = 0;

    apiWithPreview.previewValues = vi.fn().mockResolvedValue({
      form_id: FORM_ID,
      warnings: [
        {
          pack_key: "vs-checks",
          rule_id: "VS_SYSBP_HIGH",
          rule_version: 1,
          severity: "Warning",
          dataset: "VS",
          item_id: "SYSBP",
          message: "Systolic BP exceeds 220 mm[Hg].",
          remediation_guidance: null,
        },
      ],
    });

    try {
      wrapper = mount(EcrfFormPanel, {
        props: { formId: FORM_ID, canVerifySource: true },
      });
      await flushPromises();

      expect(wrapper.find('[data-testid="ecrf-sdv-status-SYSBP"]').text()).toBe(
        "Unverified"
      );
      expect(wrapper.find('[data-testid="ecrf-sdv-status-DIABP"]').text()).toBe(
        "Verified (rev 2)"
      );
      expect(wrapper.find('[data-testid="ecrf-sdv-status-HR"]').text()).toBe(
        "Needs re-verification"
      );
      expect(wrapper.find('[data-testid="ecrf-sdv-status-TEMP"]').text()).toBe(
        "Unverified"
      );
      expect(
        wrapper.find('[data-testid="ecrf-sdv-status-COMMENT"]').text()
      ).toBe("No value");

      await wrapper.find("#ecrf-sdv-select-all").trigger("click");
      await nextTick();

      expect(
        (wrapper.find("#ecrf-sdv-select-SYSBP").element as HTMLInputElement)
          .checked
      ).toBe(true);
      expect(
        (wrapper.find("#ecrf-sdv-select-HR").element as HTMLInputElement)
          .checked
      ).toBe(true);
      expect(
        (wrapper.find("#ecrf-sdv-select-TEMP").element as HTMLInputElement)
          .checked
      ).toBe(true);
      expect(wrapper.find('[data-testid="ecrf-bulk-sdv"]').text()).toContain(
        "3 selected"
      );

      Array.prototype.includes = function <T>(
        this: T[],
        searchElement: T,
        fromIndex?: number
      ) {
        if (
          this.length === 3 &&
          this[0] === "SYSBP" &&
          this[1] === "HR" &&
          this[2] === "TEMP"
        ) {
          selectedIncludesCalls += 1;
        }
        return origIncludes.call(this, searchElement, fromIndex);
      } as typeof Array.prototype.includes;

      assertionReads = 0;
      selectedIncludesCalls = 0;

      const sysInput = wrapper.find("#ecrf-item-SYSBP");
      for (const typed of ["1", "13", "135", "245"]) {
        await sysInput.setValue(typed);
        await nextTick();
      }

      vi.advanceTimersByTime(250);
      await flushPromises();

      expect(
        wrapper.find('[data-testid="ecrf-preview-warnings-SYSBP"]').text()
      ).toContain("VS_SYSBP_HIGH");
      expect(assertionReads).toBe(0);
      expect(selectedIncludesCalls).toBe(0);
    } finally {
      Array.prototype.includes = origIncludes;
      if (origPreviewValues === undefined) {
        delete apiWithPreview.previewValues;
      } else {
        apiWithPreview.previewValues = origPreviewValues;
      }
      vi.useRealTimers();
    }
  });
});

describe("missing reasons, detail persistence, signature supersession, and 10-char change reasons", () => {
  let wrapper: VueWrapper | undefined;

  beforeEach(() => {
    localStorage.clear();
    setActivePinia(createPinia());
    vi.mocked(ecrfFormsApi.getForm).mockReset();
    vi.mocked(ecrfFormsApi.openForm).mockReset();
    vi.mocked(ecrfFormsApi.saveValues).mockReset();
    resetQueries();
  });

  afterEach(() => {
    wrapper?.unmount();
    wrapper = undefined;
    vi.useRealTimers();
    vi.restoreAllMocks();
    localStorage.clear();
  });

  it("hydrates, edits, persists in browser drafts, and submits MissingValue.Detail (--REASND), clearing stale text and detail on transitions", async () => {
    vi.useFakeTimers();
    signInUser("crc-1", "tenant-1");
    const draftKey = scopedKey(
      { tenantId: "tenant-1", userId: "crc-1" },
      "ecrf_form_draft",
      FORM_ID
    )!;
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(
      form({
        items: [
          item({
            item_id: "SYSBP",
            unit: "mm[Hg]",
            value_kind: "Quantity",
            required: true,
            value: {
              type: "missing",
              reason: "NotDone",
              detail: "Cuff unavailable",
            },
            observation_id: OBS_ID,
            revision: 2,
          }),
          item({
            item_id: "DIABP",
            unit: "mm[Hg]",
            value_kind: "Quantity",
            required: true,
            value: { type: "quantity", value: 82, unit: "mm[Hg]" },
            observation_id: "obs-diabp",
            revision: 1,
          }),
        ],
      }) as never
    );
    vi.mocked(ecrfFormsApi.saveValues).mockResolvedValue({
      form_id: FORM_ID,
      version: 3,
      values: [],
    } as never);

    wrapper = mount(EcrfFormPanel, { props: { formId: FORM_ID } });
    await flushPromises();

    // Hydrated missing detail renders and unchanged detail is not dirty
    const sysDetail = wrapper.find("#ecrf-missing-detail-SYSBP");
    expect(sysDetail.exists()).toBe(true);
    expect((sysDetail.element as HTMLInputElement).value).toBe(
      "Cuff unavailable"
    );
    expect(
      wrapper.find("#ecrf-form-save").attributes("disabled")
    ).toBeDefined();

    // Editing only MissingValue.Detail marks the row dirty
    await sysDetail.setValue("Cuff bladder punctured");
    expect(wrapper.find("#ecrf-form-save").text()).toBe("Save page (1)");

    // Typing text on DIABP then selecting a Missing reason clears stale typed text
    await wrapper.find("#ecrf-item-DIABP").setValue("88");
    await wrapper.find("#ecrf-missing-DIABP").setValue("AskedButUnknown");
    expect(
      (wrapper.find("#ecrf-item-DIABP").element as HTMLInputElement).value
    ).toBe("");
    await wrapper
      .find("#ecrf-missing-detail-DIABP")
      .setValue("Home log misplaced");

    // Persist draft and remount to verify draft restoration preserves detail
    vi.advanceTimersByTime(500);
    const savedRaw = localStorage.getItem(draftKey);
    expect(savedRaw).toContain("Cuff bladder punctured");
    expect(savedRaw).toContain("Home log misplaced");

    wrapper.unmount();
    wrapper = mount(EcrfFormPanel, { props: { formId: FORM_ID } });
    await flushPromises();
    expect(
      (wrapper.find("#ecrf-missing-detail-SYSBP").element as HTMLInputElement)
        .value
    ).toBe("Cuff bladder punctured");
    expect(
      (wrapper.find("#ecrf-missing-detail-DIABP").element as HTMLInputElement)
        .value
    ).toBe("Home log misplaced");

    // Submitting sends trimmed detail on MissingValue payloads
    await wrapper
      .find("#ecrf-save-reason")
      .setValue("Updated missing details against source worksheet");
    await wrapper.find("#ecrf-form-page").trigger("submit");
    await flushPromises();

    expect(ecrfFormsApi.saveValues).toHaveBeenCalledTimes(1);
    const payload = vi.mocked(ecrfFormsApi.saveValues).mock.calls[0][0];
    expect(payload.values).toEqual([
      {
        item_id: "SYSBP",
        observation_id: null,
        observed_at: null,
        value: {
          type: "missing",
          reason: "NotDone",
          detail: "Cuff bladder punctured",
        },
        expected_revision: 2,
      },
      {
        item_id: "DIABP",
        observation_id: null,
        observed_at: null,
        value: {
          type: "missing",
          reason: "AskedButUnknown",
          detail: "Home log misplaced",
        },
        expected_revision: 1,
      },
    ]);
  });

  it("marks cleared committed values and Missing->Not missing resets dirty immediately and blocks save until resolved", async () => {
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(
      form({
        items: [
          item({
            item_id: "SYSBP",
            unit: "mm[Hg]",
            value_kind: "Quantity",
            required: true,
            value: { type: "quantity", value: 120, unit: "mm[Hg]" },
            observation_id: OBS_ID,
            revision: 3,
          }),
          item({
            item_id: "PULSE",
            unit: "/min",
            value_kind: "Quantity",
            required: true,
            value: {
              type: "missing",
              reason: "NotDone",
              detail: "Patient declined",
            },
            observation_id: "obs-pulse",
            revision: 2,
          }),
        ],
      }) as never
    );

    wrapper = mount(EcrfFormPanel, {
      props: { formId: FORM_ID },
      attachTo: document.body,
    });
    await flushPromises();

    // 1. Clearing committed SYSBP value marks row dirty, shows Revert + amber guidance, and disables Save
    await wrapper.find("#ecrf-item-SYSBP").setValue("");
    await wrapper
      .find("#ecrf-save-reason")
      .setValue("Cleared outdated transcription value");

    expect(wrapper.find("#ecrf-revert-SYSBP").exists()).toBe(true);
    const sysGuidance = wrapper.find(
      '[data-testid="ecrf-incomplete-guidance-SYSBP"]'
    );
    expect(sysGuidance.exists()).toBe(true);
    expect(sysGuidance.text()).toContain(
      "Enter a corrected value or select why this item is missing."
    );
    expect(
      wrapper.find("#ecrf-form-save").attributes("disabled")
    ).toBeDefined();

    // Attempting form submit does not invoke saveValues while incomplete
    await wrapper.find("#ecrf-form-page").trigger("submit");
    await flushPromises();
    expect(ecrfFormsApi.saveValues).not.toHaveBeenCalled();

    // 2. Resetting committed Missing item PULSE to Not missing clears detail and marks row dirty immediately
    await wrapper.find("#ecrf-missing-PULSE").setValue("");
    expect(wrapper.find("#ecrf-missing-detail-PULSE").exists()).toBe(false);
    expect(wrapper.find("#ecrf-revert-PULSE").exists()).toBe(true);
    expect(
      wrapper.find('[data-testid="ecrf-incomplete-guidance-PULSE"]').exists()
    ).toBe(true);

    await expect(wrapper.element).toBeAccessible();

    // Selecting a missing reason for SYSBP and entering a replacement value for PULSE enables Save
    await wrapper.find("#ecrf-missing-SYSBP").setValue("NotDone");
    await wrapper.find("#ecrf-item-PULSE").setValue("74");
    expect(
      wrapper.find('[data-testid="ecrf-incomplete-guidance-SYSBP"]').exists()
    ).toBe(false);
    expect(
      wrapper.find('[data-testid="ecrf-incomplete-guidance-PULSE"]').exists()
    ).toBe(false);
    expect(
      wrapper.find("#ecrf-form-save").attributes("disabled")
    ).toBeUndefined();
  });

  it("enforces 10 trimmed characters on #ecrf-save-reason and warns + confirms before superseding a Current signature", async () => {
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(
      form({
        signatures: [
          {
            attestation_id: "sig-current-1",
            meaning: "Approved",
            signed_by: "usr-pi-01",
            signed_at: "2027-03-21T10:00:00Z",
            reason: "Investigator review complete",
            content_hash: "sha256:form-v2",
            superseded: false,
            signed_values: null,
          },
        ],
      }) as never
    );
    vi.mocked(ecrfFormsApi.saveValues).mockResolvedValue({
      form_id: FORM_ID,
      version: 3,
      values: [],
    } as never);

    wrapper = mount(EcrfFormPanel, {
      props: { formId: FORM_ID },
      attachTo: document.body,
    });
    const hostWrapper = mount(ConfirmationDialogHost, {
      attachTo: document.body,
    });
    await flushPromises();

    // Signed form notice does not appear before editing
    expect(
      wrapper.find('[data-testid="ecrf-signed-form-notice"]').exists()
    ).toBe(false);

    // Editing SYSBP reveals the inline signed-form warning banner
    await wrapper.find("#ecrf-item-SYSBP").setValue("126");
    const signedNotice = wrapper.find(
      '[data-testid="ecrf-signed-form-notice"]'
    );
    expect(signedNotice.exists()).toBe(true);
    expect(signedNotice.text()).toContain("Signed form notice");
    expect(signedNotice.text()).toContain(
      "supersede the Current attestation and require re-signing"
    );

    // Whitespace-padded <10-char reason keeps Save disabled and updates counter
    await wrapper.find("#ecrf-save-reason").setValue("   short   ");
    expect(wrapper.find("#ecrf-save-reason-counter").text()).toBe(
      "5 / 10 min characters"
    );
    expect(
      wrapper.find("#ecrf-form-save").attributes("disabled")
    ).toBeDefined();

    // >=10 trimmed characters enables Save
    await wrapper
      .find("#ecrf-save-reason")
      .setValue("Corrected against nursing triage chart");
    expect(wrapper.find("#ecrf-save-reason-counter").text()).toBe(
      "38 / 10 min characters"
    );
    expect(
      wrapper.find("#ecrf-form-save").attributes("disabled")
    ).toBeUndefined();

    await expect(wrapper.element).toBeAccessible();

    // Submitting prompts confirmation dialog; cancelling aborts saveValues
    const submitCancel = wrapper.find("#ecrf-form-page").trigger("submit");
    await nextTick();
    const dialog1 = document.querySelector(
      '.clinical-modal-dialog[role="alertdialog"]'
    );
    expect(dialog1).not.toBeNull();
    expect(dialog1?.textContent).toContain("Supersede Current Signature?");
    (dialog1?.querySelector(".btn-cancel") as HTMLElement).click();
    await submitCancel;
    await flushPromises();
    expect(ecrfFormsApi.saveValues).not.toHaveBeenCalled();

    // Submitting again and confirming invokes saveValues
    const submitConfirm = wrapper.find("#ecrf-form-page").trigger("submit");
    await nextTick();
    (
      document.querySelector(
        '.clinical-modal-dialog[role="alertdialog"] .btn-confirm'
      ) as HTMLElement
    ).click();
    await submitConfirm;
    await flushPromises();
    expect(ecrfFormsApi.saveValues).toHaveBeenCalledTimes(1);

    hostWrapper.unmount();
  });
});
