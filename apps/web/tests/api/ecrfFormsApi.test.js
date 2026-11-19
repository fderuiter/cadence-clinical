import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import {
  ecrfFormsApi,
  formErrorCode,
  formFieldErrors,
  formSignatureAction,
  FORM_SIGN_SEMANTIC_ACTION,
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

  it("lists opened form summaries for an enrollment", async () => {
    const summaries = [
      {
        form_id: FORM,
        enrollment_id: ENROLLMENT,
        protocol_version_id: "pv",
        collection_spec_id: "VS",
        collection_title: "Vital Signs",
        planned_activity_id: "WEEK2",
        collected_at: "2027-03-14T09:00:00.000Z",
        opened_at: "2027-03-14T09:00:00.000Z",
        opened_by: "crc1",
        complete: false,
        item_count: 3,
        recorded_count: 1,
        version: 1,
      },
    ];
    fetchSpy.mockResolvedValueOnce(jsonResponse(200, summaries));

    const result = await ecrfFormsApi.listForEnrollment(ENROLLMENT);

    expect(result).toEqual(summaries);
    const request = lastRequest();
    expect(request.method).toBe("GET");
    const url = new URL(request.url);
    expect(url.pathname).toBe("/api/v1/execution/forms");
    expect(url.searchParams.get("enrollment_id")).toBe(ENROLLMENT);
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

  // #5463: a form signature carries the meaning and the content hash as read, with a step-up token for this form.
  it("signs a form with the meaning, the content hash and the step-up token", async () => {
    const signature = {
      attestation_id: "0192f3c4-5e6a-7b8c-9d0e-00000000a501",
      meaning: "approved",
      signed_by: "pi1",
      signed_at: "2027-03-21T00:00:00Z",
      reason: "Investigator Certification",
      content_hash: "sha256:abc",
      superseded: false,
      signed_values: null,
    };
    fetchSpy.mockResolvedValueOnce(jsonResponse(201, signature));

    const result = await ecrfFormsApi.signForm(
      FORM,
      { meaning: "approved", contentHash: "sha256:abc", signatureId: null },
      "sig-token-1",
      "Investigator Certification",
      "sign-1"
    );

    expect(result).toEqual(signature);
    const request = lastRequest();
    expect(request.method).toBe("POST");
    expect(new URL(request.url).pathname).toBe(
      `/api/v1/execution/forms/${FORM}/signatures`
    );
    expect(request.headers.get("X-Sig-Token")).toBe("sig-token-1");
    expect(request.headers.get("Idempotency-Key")).toBe("sign-1");
    expect(request.headers.get("X-Change-Reason")).toBe(
      "Investigator Certification"
    );
    expect(await request.json()).toEqual({
      meaning: "approved",
      content_hash: "sha256:abc",
      signature_id: null,
    });
  });

  it("names the step-up action as the form's signing URL in lowercase", () => {
    expect(formSignatureAction(FORM.toUpperCase())).toBe(
      `/api/v1/execution/forms/${FORM}/signatures`
    );
    expect(FORM_SIGN_SEMANTIC_ACTION).toBe("execution.form.sign");
  });

  it("rejects a signature over a form that changed since it was read", async () => {
    fetchSpy.mockResolvedValueOnce(
      jsonResponse(
        409,
        {
          title: "K.Trust.TargetContentChanged",
          code: "K.Trust.TargetContentChanged",
        },
        "application/problem+json"
      )
    );

    const error = await ecrfFormsApi
      .signForm(FORM, { meaning: "approved", contentHash: "h" }, "t", "r", "k")
      .catch((e) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect(error.status).toBe(409);
    expect(formErrorCode(error)).toBe("K.Trust.TargetContentChanged");
  });
});
