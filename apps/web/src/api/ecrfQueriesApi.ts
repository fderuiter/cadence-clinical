import { encodeChangeReason } from "./changeReason";
import { createOpenApiClient, unwrapResponse } from "./client";
import { formErrorCode } from "./ecrfFormsApi";

/**
 * Typed client for the Execution module's eCRF query endpoints (#5461), served by the kernel host under
 * `/api/v1/execution/forms`.
 *
 * A query is raised on one value of a form, at the revision the user saw. The site answers it; a monitor closes,
 * reopens or cancels it. Every write is a kernel command, so it carries the reason as `X-Change-Reason` and an
 * `Idempotency-Key`; a retried request with the same key is answered once. There are no batch routes: closing several
 * queries is one close per query.
 *
 * The routes are not in docs/openapi/kernel_openapi.json yet, so the request and response shapes are typed here by
 * hand in the generated client's form. Replace `QueryPaths` with the generated `paths` once the spec carries them.
 */

export type FormQueryStatus = "open" | "answered" | "closed" | "cancelled";
export type FormQueryMessageKind =
  "raised" | "answered" | "reopened" | "closed" | "cancelled";

export interface FormQueryMessage {
  kind: FormQueryMessageKind;
  text: string;
  by: string;
  at: string;
}

/** One query on a form value (`FormQueryResponse`). */
export interface FormQueryResponse {
  query_id: string;
  form_id: string;
  enrollment_id: string;
  item_id: string;
  observation_id: string;
  /** The value's revision the query was raised on. */
  revision: number;
  /** The value's revision now. */
  current_revision: number;
  /** True once a correction moved the value past the queried revision; the monitor can then close the query. */
  value_changed: boolean;
  status: FormQueryStatus;
  text: string;
  messages: FormQueryMessage[];
  raised_by: string;
  raised_at: string;
  version: number;
  study_id?: string | null;
  study_site_id?: string | null;
  site_code?: string | null;
  subject_code?: string | null;
  collection_spec_id?: string | null;
  planned_activity_id?: string | null;
}

/** Optional filters on a study-wide query worklist read (`GET /api/v1/execution/forms/queries?study_id=…`). */
export interface StudyQueryFilters {
  status?: FormQueryStatus | "";
  site?: string;
  participant?: string;
  valueChanged?: boolean | null;
  limit?: number;
  offset?: number;
}

/** Optional per-request options for cancellable query reads. */
export interface QueryReadOptions {
  signal?: AbortSignal;
}

/** The kernel and module error codes a query call answers with. */
export const QUERY_INVALID_TRANSITION = "M.Execution.Query.InvalidTransition";
export const QUERY_REVISION_STALE = "M.Execution.Query.RevisionStale";
export const QUERY_OBSERVATION_UNKNOWN = "M.Execution.Query.ObservationUnknown";
export const AUTHORIZATION_DENIED = "K.Authorization.Denied";
export const ASSERTION_LOCKED = "K.Assertion.Locked";

/** The transitions on an existing query. */
export type FormQueryAction = "answer" | "reopen" | "close" | "cancel";

interface JsonContent<T> {
  content: { "application/json": T };
}

interface QueryPathParams {
  path: { formId: string };
}

interface QueryTransition {
  parameters: { path: { formId: string; queryId: string } };
  requestBody?: JsonContent<{ text: string }>;
  responses: { 200: JsonContent<FormQueryResponse> };
}

/** The query routes in the generated client's `paths` form. */
interface QueryPaths {
  "/api/v1/execution/forms/{formId}/queries": {
    get: {
      parameters: QueryPathParams;
      responses: { 200: JsonContent<FormQueryResponse[]> };
    };
    post: {
      parameters: QueryPathParams;
      requestBody: JsonContent<{
        observation_id: string | null;
        revision: number | null;
        item_id?: string;
        text: string;
        query_id?: string | null;
      }>;
      responses: { 201: JsonContent<FormQueryResponse> };
    };
  };
  "/api/v1/execution/forms/queries": {
    get: {
      parameters: {
        query: {
          enrollment_id?: string;
          study_id?: string;
          status?: string;
          site?: string;
          participant?: string;
          value_changed?: boolean;
          limit?: number;
          offset?: number;
        };
      };
      responses: { 200: JsonContent<FormQueryResponse[]> };
    };
  };
  "/api/v1/execution/forms/{formId}/queries/{queryId}/answer": {
    post: QueryTransition;
  };
  "/api/v1/execution/forms/{formId}/queries/{queryId}/reopen": {
    post: QueryTransition;
  };
  "/api/v1/execution/forms/{formId}/queries/{queryId}/close": {
    post: QueryTransition;
  };
  "/api/v1/execution/forms/{formId}/queries/{queryId}/cancel": {
    post: QueryTransition;
  };
}

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

const client = createOpenApiClient<QueryPaths>();

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

const TRANSITION_PATHS = {
  answer: "/api/v1/execution/forms/{formId}/queries/{queryId}/answer",
  reopen: "/api/v1/execution/forms/{formId}/queries/{queryId}/reopen",
  close: "/api/v1/execution/forms/{formId}/queries/{queryId}/close",
  cancel: "/api/v1/execution/forms/{formId}/queries/{queryId}/cancel",
} as const;

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
  listForForm(
    formId: string,
    options?: QueryReadOptions
  ): Promise<FormQueryResponse[]> {
    return unwrapResponse(
      client.GET("/api/v1/execution/forms/{formId}/queries", {
        params: { path: { formId } },
        ...(options?.signal ? { signal: options.signal } : {}),
      })
    );
  },

  /** Every query on an enrollment's forms (GET /api/v1/execution/forms/queries?enrollment_id=). */
  listForEnrollment(
    enrollmentId: string,
    options?: QueryReadOptions
  ): Promise<FormQueryResponse[]> {
    return unwrapResponse(
      client.GET("/api/v1/execution/forms/queries", {
        params: { query: { enrollment_id: enrollmentId } },
        ...(options?.signal ? { signal: options.signal } : {}),
      })
    );
  },

  /** Queries across visible enrollments in a study, with optional server-side filters (GET /api/v1/execution/forms/queries?study_id=). */
  listForStudy(
    studyId: string,
    filters: StudyQueryFilters = {},
    options?: QueryReadOptions
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
            limit: filters.limit,
            offset: filters.offset,
          },
        },
        ...(options?.signal ? { signal: options.signal } : {}),
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
    return unwrapResponse(
      client.POST(TRANSITION_PATHS[action], {
        params: { path: { formId, queryId } },
        body: actionTakesText(action) ? { text: text ?? "" } : undefined,
        headers: writeHeaders(reason, idempotencyKey),
      })
    );
  },
};
