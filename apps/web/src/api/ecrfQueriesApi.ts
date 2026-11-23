import { encodeChangeReason } from "./changeReason";
import { createOpenApiClient, unwrapResponse } from "./client";
import { formErrorCode } from "./ecrfFormsApi";

/**
 * Typed client for the Execution module's bitemporal eCRF query endpoints (#5461, ADR-2625), served by the kernel host
 * under `/api/v1/execution/forms`.
 *
 * A query is raised on one value of a form, at the revision the user saw. The site answers it; a monitor or data
 * manager closes, reopens or cancels it (with separation of duties preventing self-closure on manual queries). Every
 * write is a kernel command, so it carries a dedicated `X-Change-Reason` and an `Idempotency-Key`; a retried request
 * with the same key is answered once. There are no batch routes: closing several queries is one close per query.
 */

export type FormQueryStatus = "open" | "answered" | "closed" | "cancelled";
export type FormQueryOrigin = "manual" | "system";
export type FormQueryOwnerRole = "site" | "monitor" | "data_manager";
export type FormQueryPriority = "low" | "normal" | "high";
export type FormQueryMessageKind =
  "raised" | "answered" | "reopened" | "closed" | "cancelled" | "escalated";

export interface FormQueryMessage {
  kind: FormQueryMessageKind;
  text: string;
  by: string;
  at: string;
  reason_for_change?: string | null;
}

/** One query on a form value or blank item (`FormQueryResponse`). */
export interface FormQueryResponse {
  query_id: string;
  form_id: string;
  enrollment_id: string;
  item_id: string;
  observation_id: string | null;
  /** The value's revision the query was raised on, or 0 for a blank item. */
  revision: number;
  /** The item's current revision now, or null while the item remains blank. */
  current_revision: number | null;
  /** True once a correction moved the value past the queried revision; the monitor can then close the query. */
  value_changed: boolean;
  status: FormQueryStatus;
  text: string;
  messages: FormQueryMessage[];
  raised_by: string;
  raised_at: string;
  version: number;
  origin?: FormQueryOrigin;
  owner_role?: FormQueryOwnerRole;
  assignee?: string | null;
  priority?: FormQueryPriority;
  opened_at?: string;
  status_since?: string;
  due_at?: string | null;
  escalated_at?: string | null;
  last_answered_by?: string | null;
  study_id?: string | null;
  study_site_id?: string | null;
  site_code?: string | null;
  subject_code?: string | null;
  collection_spec_id?: string | null;
  planned_activity_id?: string | null;
}

/** Optional bitemporal coordinates for reading historical query state. */
export interface FormQueryTemporalFilters {
  asOf?: string;
  validAt?: string;
  knownAt?: string;
}

/** Optional filters on a study-wide query worklist read (`GET /api/v1/execution/forms/queries?study_id=…`). */
export interface StudyQueryFilters extends FormQueryTemporalFilters {
  status?: FormQueryStatus | "";
  owner?: FormQueryOwnerRole | "";
  origin?: FormQueryOrigin | "";
  priority?: FormQueryPriority | "";
  escalated?: boolean | null;
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

/** Optional per-request options and bitemporal coordinates for form and enrollment query reads. */
export interface FormQueryReadOptions
  extends QueryReadOptions, FormQueryTemporalFilters {}

export type StudyFormQueryFilters = StudyQueryFilters;

/** The kernel and module error codes a query call answers with. */
export const QUERY_INVALID_TRANSITION = "M.Execution.Query.InvalidTransition";
export const QUERY_SEPARATION_OF_DUTIES =
  "M.Execution.Query.SeparationOfDuties";
export const QUERY_REVISION_STALE = "M.Execution.Query.RevisionStale";
export const QUERY_OBSERVATION_UNKNOWN = "M.Execution.Query.ObservationUnknown";
export const AUTHORIZATION_DENIED = "K.Authorization.Denied";
export const ASSERTION_LOCKED = "K.Assertion.Locked";

/** The transitions on an existing query. */
export type FormQueryAction = "answer" | "reopen" | "close" | "cancel";

interface JsonContent<T> {
  content: { "application/json": T };
}

interface TemporalQueryParams {
  as_of?: string;
  valid_at?: string;
  known_at?: string;
}

interface QueryPathParams {
  path: { formId: string };
  query?: TemporalQueryParams;
}

interface QueryTransition {
  parameters: { path: { formId: string; queryId: string } };
  requestBody?: JsonContent<{ text?: string }>;
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
      parameters: { path: { formId: string } };
      requestBody: JsonContent<{
        observation_id: string | null;
        revision: number | null;
        item_id?: string;
        text: string;
        query_id?: string | null;
        owner_role?: FormQueryOwnerRole;
        priority?: FormQueryPriority;
        assignee?: string | null;
      }>;
      responses: { 201: JsonContent<FormQueryResponse> };
    };
  };
  "/api/v1/execution/forms/queries": {
    get: {
      parameters: {
        query: TemporalQueryParams & {
          enrollment_id?: string;
          study_id?: string;
          status?: string;
          owner?: string;
          origin?: string;
          priority?: string;
          escalated?: boolean;
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
  ownerRole?: FormQueryOwnerRole;
  priority?: FormQueryPriority;
  assignee?: string | null;
  reason: string;
  idempotencyKey: string;
}

export interface FormQueryTransition {
  formId: string;
  queryId: string;
  /** Conversational answer, reopening note, or optional closing/cancellation note distinct from `reason`. */
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

function toTemporalQuery(
  temporal?: FormQueryTemporalFilters
): TemporalQueryParams {
  if (!temporal) return {};
  return {
    ...(temporal.asOf?.trim() ? { as_of: temporal.asOf.trim() } : {}),
    ...(temporal.validAt?.trim() ? { valid_at: temporal.validAt.trim() } : {}),
    ...(temporal.knownAt?.trim() ? { known_at: temporal.knownAt.trim() } : {}),
  };
}

/** The error code of a failed query call (`M.Execution.Query.RevisionStale`, …), or null. */
export const queryErrorCode = formErrorCode;

/**
 * What to tell the user when a query call fails: the stale, separation-of-duties, and locked cases say what to do
 * next; anything else is the server's own message.
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
  if (code === QUERY_SEPARATION_OF_DUTIES) {
    return "You answered this query, so another monitor or data manager must review and close it.";
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

/** Whether a transition requires a conversational message body (`answer` and `reopen`). */
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
    ownerRole,
    priority,
    assignee,
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
          ...(ownerRole ? { owner_role: ownerRole } : {}),
          ...(priority ? { priority } : {}),
          ...(assignee ? { assignee } : {}),
        },
        headers: writeHeaders(reason, idempotencyKey),
      })
    );
  },

  /** A form's queries, oldest first (GET /api/v1/execution/forms/{formId}/queries). */
  listForForm(
    formId: string,
    options?: FormQueryReadOptions
  ): Promise<FormQueryResponse[]> {
    const query = toTemporalQuery(options);
    return unwrapResponse(
      client.GET("/api/v1/execution/forms/{formId}/queries", {
        params: {
          path: { formId },
          ...(Object.keys(query).length > 0 ? { query } : {}),
        },
        ...(options?.signal ? { signal: options.signal } : {}),
      })
    );
  },

  /** Every query on an enrollment's forms (GET /api/v1/execution/forms/queries?enrollment_id=). */
  listForEnrollment(
    enrollmentId: string,
    options?: FormQueryReadOptions
  ): Promise<FormQueryResponse[]> {
    return unwrapResponse(
      client.GET("/api/v1/execution/forms/queries", {
        params: {
          query: {
            enrollment_id: enrollmentId,
            ...toTemporalQuery(options),
          },
        },
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
            owner: filters.owner || undefined,
            origin: filters.origin || undefined,
            priority: filters.priority || undefined,
            escalated:
              typeof filters.escalated === "boolean"
                ? filters.escalated
                : undefined,
            site: filters.site?.trim() || undefined,
            participant: filters.participant?.trim() || undefined,
            value_changed:
              typeof filters.valueChanged === "boolean"
                ? filters.valueChanged
                : undefined,
            ...toTemporalQuery(filters),
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
   * (`M.Execution.Query.InvalidTransition` or `M.Execution.Query.SeparationOfDuties`), 403 or 423.
   */
  transition(
    action: FormQueryAction,
    { formId, queryId, text, reason, idempotencyKey }: FormQueryTransition
  ): Promise<FormQueryResponse> {
    const trimmedNote = text?.trim() ?? "";
    const body = actionTakesText(action)
      ? { text: text ?? "" }
      : trimmedNote && trimmedNote !== reason.trim()
        ? { text: trimmedNote }
        : undefined;
    return unwrapResponse(
      client.POST(TRANSITION_PATHS[action], {
        params: { path: { formId, queryId } },
        body,
        headers: writeHeaders(reason, idempotencyKey),
      })
    );
  },
};
