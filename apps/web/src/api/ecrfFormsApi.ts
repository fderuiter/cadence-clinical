import { encodeChangeReason } from "./changeReason";
import { ApiError, createOpenApiClient, unwrapResponse } from "./client";
import type { components, paths } from "./generated/kernel";

/**
 * Typed client for the Execution module's eCRF form endpoints (ADR-2622), served by the kernel host and generated
 * from docs/openapi/kernel_openapi.json.
 *
 * A form is a kernel CollectionInstance: it is opened once for a participant's visit, read with its items in layout
 * order, and saved a page at a time. Every write is a kernel command, so it carries the reason as `X-Change-Reason`
 * and an `Idempotency-Key`; a retried request with the same key is answered once. Saving is commit: there are no
 * server-side drafts.
 */
type Schemas = components["schemas"];

export type EcrfFormState = Schemas["Kernel_FormStateResponse"];
export type EcrfForm = Schemas["Kernel_FormResponse"];
export type EcrfFormItem = Schemas["Kernel_FormItemResponse"];
export type EcrfFormLock = Schemas["Kernel_FormLockResponse"];
export type EcrfFormAssertion = Schemas["Kernel_FormAssertionResponse"];
export type EcrfFormSignature = Schemas["Kernel_FormSignatureResponse"];
export type EcrfFormSaved = Schemas["Kernel_FormSavedResponse"];
export type EcrfFormValue = Schemas["Kernel_FormValueRequest"];

/** The kernel error codes a page save answers with (CollectionErrors). */
export const REVISION_STALE = "K.Collection.RevisionStale";
export const VALUES_INVALID = "K.Collection.ValuesInvalid";

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
};
