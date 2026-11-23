import { encodeChangeReason } from "./changeReason";
import { createOpenApiClient, unwrapResponse } from "./client";
import { formErrorCode } from "./ecrfFormsApi";
import type { components, paths } from "./generated/execution_forms";

/**
 * Typed client for the Execution module's eCRF query endpoints (#5461), served by the kernel host under
 * `/api/v1/execution/forms` and generated from `docs/openapi/execution_forms_openapi.json`.
 *
 * A query is raised on one value of a form, at the revision the user saw. The site answers it; a monitor closes,
 * reopens or cancels it. Every write is a kernel command, so it carries the reason as `X-Change-Reason` and an
 * `Idempotency-Key`; a retried request with the same key is answered once. There are no batch routes: closing several
 * queries is one close per query.
 */
type Schemas = components["schemas"];

export type FormQueryStatus = "open" | "answered" | "closed" | "cancelled";
export type FormQueryMessageKind =
  "raised" | "answered" | "reopened" | "closed" | "cancelled";

export type FormQueryMessage =
  Schemas["ExecutionForms_FormQueryMessageResponse"];

/** One query on a form value (`FormQueryResponse`). */
export type FormQueryResponse = Schemas["ExecutionForms_FormQueryResponse"];

/** Optional filters on a study-wide query worklist read (`GET /api/v1/execution/forms/queries?study_id=…`). */
export interface StudyQueryFilters {
  status?: FormQueryStatus | "";
  site?: string;
  participant?: string;
  valueChanged?: boolean | null;
}

/** The kernel and module error codes a query call answers with. */
export const QUERY_INVALID_TRANSITION = "M.Execution.Query.InvalidTransition";
export const QUERY_REVISION_STALE = "M.Execution.Query.RevisionStale";
export const QUERY_OBSERVATION_UNKNOWN = "M.Execution.Query.ObservationUnknown";
export const AUTHORIZATION_DENIED = "K.Authorization.Denied";
export const ASSERTION_LOCKED = "K.Assertion.Locked";

/** The transitions on an existing query. */
export type FormQueryAction = "answer" | "reopen" | "close" | "cancel";

export interface RaiseFormQuery {
  formId: string;
  /** The value's observation, from the form read, or null/omitted when raising on an unasked blank item. */
  observationId?: string | null;
  /** The value's current revision, from the form read, or null/omitted when raising on an unasked blank item. */
  revision?: number | null;
  /** The form item id when raising a missing-data query on an unasked blank item. */
  itemId?: string | null;
  text: string;
  /** The new query's id; reuse it when retrying with the same idempotency key. */
  queryId?: string | null;
  reason: string;
  idempotencyKey: string;
}

export interface FormQueryTransition {
  formId: string;
  queryId: string;
  /** The answer or reopening text; close and cancel take none (their note is the reason). */
  text?: string;
  reason: string;
  idempotencyKey: string;
}

const client = createOpenApiClient<paths>();

function writeHeaders(reason: string, idempotencyKey: string) {
  return {
    "X-Change-Reason": encodeChangeReason(reason),
    "Idempotency-Key": idempotencyKey,
  };
}

/** The error code of a failed query call (`M.Execution.Query.RevisionStale`, …), or null. */
export const queryErrorCode = formErrorCode;

/**
 * What to tell the user when a query call fails: the stale and locked cases say what to do next; anything else is
 * the server's own message.
 */
export function describeQueryError(error: unknown): string {
  const code = queryErrorCode(error);
  const status = (error as { status?: number | null } | null)?.status ?? null;
  if (code === QUERY_REVISION_STALE) {
    return "The value changed since you loaded the form; reload the form and raise the query on the current value.";
  }
  if (code === QUERY_OBSERVATION_UNKNOWN) {
    return "That value is not on this form any more; reload the form.";
  }
  if (code === QUERY_INVALID_TRANSITION) {
    return "Someone changed this query since you loaded it; reload the queries to see where it stands.";
  }
  if (code === ASSERTION_LOCKED || status === 423) {
    return "The value is locked (Locked), so its queries cannot change until the lock is released.";
  }
  if (code === AUTHORIZATION_DENIED || status === 403) {
    return "You are not allowed to do this on this participant's queries.";
  }
  return error instanceof Error ? error.message : String(error);
}

/** Whether a transition carries a text body; close and cancel record the reason instead. */
export function actionTakesText(action: FormQueryAction): boolean {
  return action === "answer" || action === "reopen";
}

export const ecrfQueriesApi = {
  /** Raises a query on one value or unasked blank item of a form (POST /api/v1/execution/forms/{formId}/queries). */
  raise({
    formId,
    observationId,
    revision,
    itemId,
    text,
    queryId,
    reason,
    idempotencyKey,
  }: RaiseFormQuery): Promise<FormQueryResponse> {
    return unwrapResponse(
      client.POST("/api/v1/execution/forms/{formId}/queries", {
        params: { path: { formId } },
        body: {
          observation_id: observationId ?? null,
          revision: revision ?? null,
          ...(itemId ? { item_id: itemId } : {}),
          text,
          query_id: queryId ?? null,
        },
        headers: writeHeaders(reason, idempotencyKey),
      })
    );
  },

  /** A form's queries, oldest first (GET /api/v1/execution/forms/{formId}/queries). */
  listForForm(formId: string): Promise<FormQueryResponse[]> {
    return unwrapResponse(
      client.GET("/api/v1/execution/forms/{formId}/queries", {
        params: { path: { formId } },
      })
    );
  },

  /** Every query on an enrollment's forms (GET /api/v1/execution/forms/queries?enrollment_id=). */
  listForEnrollment(enrollmentId: string): Promise<FormQueryResponse[]> {
    return unwrapResponse(
      client.GET("/api/v1/execution/forms/queries", {
        params: { query: { enrollment_id: enrollmentId } },
      })
    );
  },

  /** Queries across visible enrollments in a study, with optional server-side filters (GET /api/v1/execution/forms/queries?study_id=). */
  listForStudy(
    studyId: string,
    filters: StudyQueryFilters = {}
  ): Promise<FormQueryResponse[]> {
    return unwrapResponse(
      client.GET("/api/v1/execution/forms/queries", {
        params: {
          query: {
            study_id: studyId,
            status: filters.status || undefined,
            site: filters.site?.trim() || undefined,
            participant: filters.participant?.trim() || undefined,
            value_changed:
              typeof filters.valueChanged === "boolean"
                ? filters.valueChanged
                : undefined,
          },
        },
      })
    );
  },

  /**
   * Answers, reopens, closes or cancels a query (POST …/queries/{queryId}/{action}). Rejects with an ApiError of 409
   * (`M.Execution.Query.InvalidTransition`), 403 or 423.
   */
  transition(
    action: FormQueryAction,
    { formId, queryId, text, reason, idempotencyKey }: FormQueryTransition
  ): Promise<FormQueryResponse> {
    const headers = writeHeaders(reason, idempotencyKey);
    if (action === "answer" || action === "reopen") {
      const path =
        action === "answer"
          ? "/api/v1/execution/forms/{formId}/queries/{queryId}/answer"
          : "/api/v1/execution/forms/{formId}/queries/{queryId}/reopen";
      return unwrapResponse(
        client.POST(path, {
          params: { path: { formId, queryId } },
          body: { text: text ?? "" },
          headers,
        })
      );
    }

    const path =
      action === "close"
        ? "/api/v1/execution/forms/{formId}/queries/{queryId}/close"
        : "/api/v1/execution/forms/{formId}/queries/{queryId}/cancel";
    return unwrapResponse(
      client.POST(path, {
        params: { path: { formId, queryId } },
        headers,
      })
    );
  },
};
