import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import {
  ecrfFormsApi,
  formErrorCode,
  formFieldErrors,
} from "../../src/api/ecrfFormsApi";
import { ApiError } from "../../src/api/client";

const FORM = "0192f3c4-5e6a-7b8c-9d0e-00000000f001";
const ENROLLMENT = "0192f3c4-5e6a-7b8c-9d0e-00000000e001";

function jsonResponse(status, body, contentType = "application/json") {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": contentType },
  });
}

// ADR-2622: the eCRF opens, reads and saves kernel form instances through the Execution module on the kernel host.
describe("ecrfFormsApi (ADR-2622)", () => {
  let fetchSpy;

  beforeEach(() => {
    setActivePinia(createPinia());
    fetchSpy = vi.spyOn(globalThis, "fetch");
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  function lastRequest() {
    return fetchSpy.mock.calls.at(-1)[0];
  }

  it("opens a form with the reason and idempotency key on the request", async () => {
    const state = {
      form_id: FORM,
      enrollment_id: ENROLLMENT,
      protocol_version_id: "pv",
      collection_spec_id: "VS",
      planned_activity_id: "WEEK2",
      collected_at: "2027-03-14T09:00:00.000Z",
      version: 1,
    };
    fetchSpy.mockResolvedValueOnce(jsonResponse(201, state));

    const result = await ecrfFormsApi.openForm({
      formId: FORM,
      enrollmentId: ENROLLMENT,
      collectionSpecId: "VS",
      plannedActivityId: "WEEK2",
      collectedAt: "2027-03-14T09:00:00.000Z",
      reason: "Form opened for data entry",
      idempotencyKey: "open-1",
    });

    expect(result).toEqual(state);
    const request = lastRequest();
    expect(request.method).toBe("POST");
    expect(new URL(request.url).pathname).toBe("/api/v1/execution/forms");
    expect(request.headers.get("Idempotency-Key")).toBe("open-1");
    expect(request.headers.get("X-Change-Reason")).toBe(
      "Form opened for data entry"
    );
    expect(await request.json()).toEqual({
      form_id: FORM,
      enrollment_id: ENROLLMENT,
      collection_spec_id: "VS",
      planned_activity_id: "WEEK2",
      collected_at: "2027-03-14T09:00:00.000Z",
    });
  });

  it("reads a form by id", async () => {
    fetchSpy.mockResolvedValueOnce(jsonResponse(200, { form_id: FORM }));

    await ecrfFormsApi.getForm(FORM);

    const request = lastRequest();
    expect(request.method).toBe("GET");
    expect(new URL(request.url).pathname).toBe(
      `/api/v1/execution/forms/${FORM}`
    );
  });

  it("saves a page with each value's expected revision, the reason and the idempotency key", async () => {
    fetchSpy.mockResolvedValueOnce(
      jsonResponse(200, { form_id: FORM, version: 2, values: [] })
    );
    const values = [
      {
        item_id: "SYSBP",
        value: { type: "quantity", value: "128", unit: "mm[Hg]" },
        expected_revision: 0,
        observation_id: null,
        observed_at: null,
      },
    ];

    await ecrfFormsApi.saveValues({
      formId: FORM,
      values,
      reason: "Initial entry – visit 2",
      idempotencyKey: "save-1",
    });

    const request = lastRequest();
    expect(request.method).toBe("PUT");
    expect(new URL(request.url).pathname).toBe(
      `/api/v1/execution/forms/${FORM}/values`
    );
    expect(request.headers.get("Idempotency-Key")).toBe("save-1");
    expect(request.headers.get("X-Change-Reason")).toBe(
      "UTF-8''Initial%20entry%20%E2%80%93%20visit%202"
    );
    expect(await request.json()).toEqual({ values });
  });

  it("rejects a stale save with the kernel's error code", async () => {
    fetchSpy.mockResolvedValueOnce(
      jsonResponse(
        409,
        {
          title: "K.Collection.RevisionStale",
          detail: "The form changed since you opened it.",
          code: "K.Collection.RevisionStale",
        },
        "application/problem+json"
      )
    );

    const error = await ecrfFormsApi
      .saveValues({
        formId: FORM,
        values: [],
        reason: "r",
        idempotencyKey: "k",
      })
      .catch((e) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect(error.status).toBe(409);
    expect(formErrorCode(error)).toBe("K.Collection.RevisionStale");
  });

  it("reads per-item errors from an invalid save", () => {
    const error = new ApiError("invalid", 422, null, {
      code: "K.Collection.ValuesInvalid",
      errors: { SYSBP: ["Expected mm[Hg]."], DIABP: "Out of kind." },
    });

    expect(formFieldErrors(error)).toEqual({
      SYSBP: ["Expected mm[Hg]."],
      DIABP: ["Out of kind."],
    });
    expect(formFieldErrors(new Error("x"))).toEqual({});
    expect(formErrorCode(new Error("x"))).toBeNull();
  });
});
