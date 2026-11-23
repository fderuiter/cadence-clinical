import { encodeChangeReason } from "./changeReason";
import { ApiError, createOpenApiClient, unwrapResponse } from "./client";
import type { components, paths } from "./generated/execution_forms";

/**
 * Typed client for the Execution module's eCRF form endpoints (ADR-2622), served by the kernel host and generated
 * from docs/openapi/execution_forms_openapi.json.
 *
 * A form is a kernel CollectionInstance: it is opened once for a participant's visit, read with its items in layout
 * order, and saved a page at a time. Every write is a kernel command, so it carries the reason as `X-Change-Reason`
 * and an `Idempotency-Key`; a retried request with the same key is answered once. Saving is commit: there are no
 * server-side drafts.
 */
type Schemas = components["schemas"];

export type EcrfFormState = Schemas["ExecutionForms_FormStateResponse"];
export type EcrfFormSummary = Schemas["ExecutionForms_FormSummaryResponse"];
export type EcrfForm = Schemas["ExecutionForms_FormResponse"];
export type EcrfFormItem = Schemas["ExecutionForms_FormItemResponse"];
export type EcrfFormLock = Schemas["ExecutionForms_FormLockResponse"];
export type EcrfFormAssertion = Schemas["ExecutionForms_FormAssertionResponse"];
/** A signature on the form; `superseded` once a later correction changed what it signed (then `signed_values` says what that was). */
export type EcrfFormSignature = Schemas["ExecutionForms_FormSignatureResponse"];
export type EcrfFormSignedValue =
  Schemas["ExecutionForms_FormSignedValueResponse"];
export type EcrfFormSaved = Schemas["ExecutionForms_FormSavedResponse"];
export type EcrfFormValue = Schemas["ExecutionForms_FormValueRequest"];
export type EcrfFormPreview = Schemas["ExecutionForms_FormPreviewResponse"];
export type EcrfFormPreviewWarning =
  Schemas["ExecutionForms_FormPreviewWarningResponse"];

/** Normalizes an OpenAPI integer|string|undefined summary count to a non-negative integer. */
export function toFormSummaryCount(
  value: number | string | null | undefined
): number {
  const n = Number(value);
  return Number.isFinite(n) && n > 0 ? Math.trunc(n) : 0;
}

/** The kernel error codes a page save answers with (CollectionErrors). */
export const REVISION_STALE = "K.Collection.RevisionStale";
export const VALUES_INVALID = "K.Collection.ValuesInvalid";

/** The kernel error codes a form signature answers with (CollectionErrors, TrustErrors). */
export const TARGET_CONTENT_CHANGED = "K.Trust.TargetContentChanged";
export const TARGET_NOT_SIGNABLE = "K.Trust.TargetNotSignable";
export const NOT_READY_TO_SIGN = "K.Collection.NotReadyToSign";
export const SIGNATURE_MEANING_INVALID = "K.Collection.SignatureMeaningInvalid";

/** What a form signature means (21 CFR 11.50): the signer approves the values, or takes responsibility for them. */
export type EcrfSignatureMeaning = "approved" | "responsibility";

/** The gateway step-up action a form signature's X-Sig-Token must be issued for: the signing URL itself. */
export function formSignatureAction(formId: string): string {
  return `/api/v1/execution/forms/${formId.toLowerCase()}/signatures`;
}

/** The semantic action a form signature's X-Sig-Token must be issued for. */
export const FORM_SIGN_SEMANTIC_ACTION = "execution.form.sign";

export interface OpenEcrfForm {
  /** The enrollment's id (a UUID, without the `enrollment:` prefix). */
  enrollmentId: string;
  /** The CollectionSpec the form collects, from the protocol design. */
  collectionSpecId: string;
  /** The visit the form belongs to; null for an unscheduled or log form. */
  plannedActivityId?: string | null;
  /** When the values were collected, as an ISO date-time. */
  collectedAt: string;
  /** The new form's id; reuse it when retrying with the same idempotency key. */
  formId?: string | null;
  reason: string;
  idempotencyKey: string;
}

export interface SaveEcrfFormValues {
  formId: string;
  values: EcrfFormValue[];
  reason: string;
  idempotencyKey: string;
}

/** Optional per-request options for cancellable draft rule previews. */
export interface EcrfFormPreviewOptions {
  signal?: AbortSignal;
}

export interface SignEcrfForm {
  meaning: EcrfSignatureMeaning;
  /** The form's `content_hash` as it was read; the kernel refuses (409) if the form changed since. */
  contentHash: string;
  /** The new signature's id; reuse it when retrying with the same idempotency key. */
  signatureId?: string | null;
}

/** The kernel error code of a failed form call (`K.Collection.RevisionStale`, …), or null. */
export function formErrorCode(error: unknown): string | null {
  if (!(error instanceof ApiError)) return null;
  const code = (error.data as { code?: unknown } | null)?.code;
  return typeof code === "string" ? code : null;
}

/** The per-item messages of a 422 save, keyed by item id (or by the field the kernel named). */
export function formFieldErrors(error: unknown): Record<string, string[]> {
  if (!(error instanceof ApiError)) return {};
  const errors = (error.data as { errors?: unknown } | null)?.errors;
  if (!errors || typeof errors !== "object") return {};
  const out: Record<string, string[]> = {};
  for (const [field, messages] of Object.entries(errors)) {
    out[field] = Array.isArray(messages)
      ? messages.map((m) => String(m))
      : [String(messages)];
  }
  return out;
}

const client = createOpenApiClient<paths>();

function writeHeaders(reason: string, idempotencyKey: string) {
  return {
    "X-Change-Reason": encodeChangeReason(reason),
    "Idempotency-Key": idempotencyKey,
  };
}

export const ecrfFormsApi = {
  /** Opens a form for a participant's visit under the protocol version in force (POST /api/v1/execution/forms). */
  openForm({
    enrollmentId,
    collectionSpecId,
    plannedActivityId,
    collectedAt,
    formId,
    reason,
    idempotencyKey,
  }: OpenEcrfForm): Promise<EcrfFormState> {
    return unwrapResponse(
      client.POST("/api/v1/execution/forms", {
        body: {
          form_id: formId ?? null,
          enrollment_id: enrollmentId,
          collection_spec_id: collectionSpecId,
          planned_activity_id: plannedActivityId ?? null,
          collected_at: collectedAt,
        },
        headers: writeHeaders(reason, idempotencyKey),
      })
    );
  },

  /** Every form opened for an enrollment, oldest first (GET /api/v1/execution/forms?enrollment_id=). */
  listForEnrollment(enrollmentId: string): Promise<EcrfFormSummary[]> {
    return unwrapResponse(
      client.GET("/api/v1/execution/forms", {
        params: { query: { enrollment_id: enrollmentId } },
      })
    );
  },

  /** A form with its items in layout order, completeness, locks, assertions and signatures (GET /api/v1/execution/forms/{formId}). */
  getForm(formId: string): Promise<EcrfForm> {
    return unwrapResponse(
      client.GET("/api/v1/execution/forms/{formId}", {
        params: { path: { formId } },
      })
    );
  },

  /**
   * Saves a page of values (PUT /api/v1/execution/forms/{formId}/values): all of them or none. Rejects with an
   * ApiError of status 409 (`K.Collection.RevisionStale`: someone changed a value since it was loaded), 422
   * (`K.Collection.ValuesInvalid`, with per-item errors) or 423 (locked).
   */
  saveValues({
    formId,
    values,
    reason,
    idempotencyKey,
  }: SaveEcrfFormValues): Promise<EcrfFormSaved> {
    return unwrapResponse(
      client.PUT("/api/v1/execution/forms/{formId}/values", {
        params: { path: { formId } },
        body: { values },
        headers: writeHeaders(reason, idempotencyKey),
      })
    );
  },
  /**
   * Evaluates pinned study edit-check rule packs in memory over the form's current values overlaid with draft edits
   * (POST /api/v1/execution/forms/{formId}/preview). Writes no observations, validation runs, or audit entries.
   */
  previewValues(
    formId: string,
    values: EcrfFormValue[],
    options?: EcrfFormPreviewOptions | AbortSignal
  ): Promise<EcrfFormPreview> {
    const signal = options && "aborted" in options ? options : options?.signal;
    return unwrapResponse(
      client.POST("/api/v1/execution/forms/{formId}/preview", {
        params: { path: { formId } },
        body: { values },
        ...(signal ? { signal } : {}),
      })
    );
  },

  /**
   * Signs the form's current values (POST /api/v1/execution/forms/{formId}/signatures) with a gateway step-up token
   * issued for {@link formSignatureAction} and {@link FORM_SIGN_SEMANTIC_ACTION}. Rejects with an ApiError of status
   * 401 (the token is missing, expired or replayed), 403 (the caller may not sign: `collection.sign`), 409
   * (`K.Trust.TargetContentChanged`: the form changed since it was read) or 422/409 (`K.Collection.SignatureMeaningInvalid`,
   * `K.Trust.TargetNotSignable`: the form has no values).
   */
  signForm(
    formId: string,
    { meaning, contentHash, signatureId }: SignEcrfForm,
    sigToken: string,
    reason: string,
    idempotencyKey: string
  ): Promise<EcrfFormSignature> {
    return unwrapResponse(
      client.POST("/api/v1/execution/forms/{formId}/signatures", {
        params: { path: { formId } },
        body: {
          meaning,
          content_hash: contentHash,
          signature_id: signatureId ?? null,
        },
        headers: {
          ...writeHeaders(reason, idempotencyKey),
          "X-Sig-Token": sigToken,
        },
      })
    );
  },
};
