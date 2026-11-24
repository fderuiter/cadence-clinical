import { encodeChangeReason } from "./changeReason";
import { createOpenApiClient, unwrapResponse } from "./client";
import { formErrorCode } from "./ecrfFormsApi";
import type { components, paths } from "./generated/execution_forms";

/**
 * Typed client for the Execution module's bitemporal eCRF query endpoints (#5461, ADR-2625), served by the kernel host
 * under `/api/v1/execution/forms` and generated from `docs/openapi/execution_forms_openapi.json`.
 *
 * A query is raised on one value of a form, at the revision the user saw. The site answers it; a monitor or data
 * manager closes, reopens or cancels it (with separation of duties preventing self-closure on manual queries). Every
 * write is a kernel command, so it carries a dedicated `X-Change-Reason` and an `Idempotency-Key`; a retried request
 * with the same key is answered once. There are no batch routes: closing several queries is one close per query.
 */
type Schemas = components["schemas"];
type RawFormQueryMessage = Schemas["ExecutionForms_FormQueryMessageResponse"];
type RawFormQueryResponse = Schemas["ExecutionForms_FormQueryResponse"];

export type FormQueryStatus = "open" | "answered" | "closed" | "cancelled";
export type FormQueryOrigin = "manual" | "system";
export type FormQueryOwnerRole = "site" | "monitor" | "data_manager";
export type FormQueryPriority = "low" | "normal" | "high";
export type FormQueryMessageKind =
  | "raised"
  | "answered"
  | "reopened"
  | "reassigned"
  | "closed"
  | "cancelled"
  | "escalated";

export type FormQueryMessage = Omit<
  RawFormQueryMessage,
  "reason_for_change"
> & {
  reason_for_change?: RawFormQueryMessage["reason_for_change"] | null;
};

/** One query on a form value (`FormQueryResponse`). */
export type FormQueryResponse = Omit<
  RawFormQueryResponse,
  "messages" | "origin" | "owner_role" | "priority"
> & {
  messages: FormQueryMessage[];
  origin?: RawFormQueryResponse["origin"];
  owner_role?: RawFormQueryResponse["owner_role"];
  priority?: RawFormQueryResponse["priority"];
};

export type FormQueryAgingDigestEntry =
  Schemas["ExecutionForms_FormQueryAgingDigestEntry"] & {
    collection_spec_id?: string | null;
  };
export type FormQueryAgingDigestResponse = Omit<
  Schemas["ExecutionForms_FormQueryAgingDigestResponse"],
  "entries"
> & {
  entries: FormQueryAgingDigestEntry[];
};
export type FormQueryAgingDigest = FormQueryAgingDigestResponse;
export type FormQueryEscalationPassResponse =
  Schemas["ExecutionForms_FormQueryEscalationPassResponse"];
export type FormQueryEscalationPassResult = FormQueryEscalationPassResponse;

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
  assignee?: string;
  site?: string;
  participant?: string;
  valueChanged?: boolean | null;
  limit?: number;
  offset?: number;
}

/** Optional filters on daily query aging digest reads (`GET /api/v1/execution/forms/queries/digests?study_id=…`). */
export interface StudyQueryDigestFilters {
  site?: string;
  recipientRole?: string;
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
export const SUBSCRIPTION_EXPIRED = "K.Subscription.Expired";
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
  /** Optional replacement assignee when answering or reopening flips the responsible role. */
  assignee?: string | null;
  reason: string;
  idempotencyKey: string;
}

export interface ReassignFormQuery {
  formId: string;
  queryId: string;
  assignee?: string | null;
  ownerRole?: FormQueryOwnerRole | null;
  text?: string | null;
  note?: string | null;
  reason: string;
  idempotencyKey: string;
}

export interface EscalateStudyQueries {
  studyId: string;
  reason: string;
  idempotencyKey: string;
}

interface TemporalQueryParams {
  as_of?: string;
  valid_at?: string;
  known_at?: string;
}

const client = createOpenApiClient<paths>();

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
  if (code === SUBSCRIPTION_EXPIRED) {
    return "Your Capture subscription is inactive or expired, so query updates are currently read-only.";
  }
  if (code === AUTHORIZATION_DENIED || status === 403) {
    return "You are not allowed to do this on this participant's queries.";
  }
  return error instanceof Error ? error.message : String(error);
}

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
            assignee: filters.assignee?.trim() || undefined,
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
    {
      formId,
      queryId,
      text,
      assignee,
      reason,
      idempotencyKey,
    }: FormQueryTransition
  ): Promise<FormQueryResponse> {
    const headers = writeHeaders(reason, idempotencyKey);
    if (action === "answer" || action === "reopen") {
      const path =
        action === "answer"
          ? "/api/v1/execution/forms/{formId}/queries/{queryId}/answer"
          : "/api/v1/execution/forms/{formId}/queries/{queryId}/reopen";
      const trimmedAssignee = assignee?.trim() || undefined;
      return unwrapResponse(
        client.POST(path, {
          params: { path: { formId, queryId } },
          body: {
            text: text ?? "",
            ...(trimmedAssignee ? { assignee: trimmedAssignee } : {}),
          },
          headers,
        })
      );
    }

    const path =
      action === "close"
        ? "/api/v1/execution/forms/{formId}/queries/{queryId}/close"
        : "/api/v1/execution/forms/{formId}/queries/{queryId}/cancel";
    const trimmedNote = text?.trim() ?? "";
    const body =
      trimmedNote && trimmedNote !== reason.trim()
        ? { text: trimmedNote }
        : undefined;
    return unwrapResponse(
      client.POST(path, {
        params: { path: { formId, queryId } },
        ...(body ? { body } : {}),
        headers,
      })
    );
  },

  /** Reassigns or unassigns an open or answered query with audited bitemporal history (POST …/queries/{queryId}/reassign). */
  reassign({
    formId,
    queryId,
    assignee,
    ownerRole,
    text,
    note,
    reason,
    idempotencyKey,
  }: ReassignFormQuery): Promise<FormQueryResponse> {
    const trimmedAssignee = assignee?.trim() ?? "";
    const trimmedNote = (text ?? note)?.trim() ?? "";
    return unwrapResponse(
      client.POST(
        "/api/v1/execution/forms/{formId}/queries/{queryId}/reassign",
        {
          params: { path: { formId, queryId } },
          body: {
            assignee: trimmedAssignee || null,
            ...(ownerRole ? { owner_role: ownerRole } : {}),
            ...(trimmedNote ? { text: trimmedNote } : {}),
          },
          headers: writeHeaders(reason, idempotencyKey),
        }
      )
    );
  },

  /** Lists daily de-identified query aging digests for visible scopes of a study (GET /api/v1/execution/forms/queries/digests). */
  listDigests(
    studyId: string,
    filters: StudyQueryDigestFilters = {},
    options?: QueryReadOptions
  ): Promise<FormQueryAgingDigestResponse[]> {
    return unwrapResponse(
      client.GET("/api/v1/execution/forms/queries/digests", {
        params: {
          query: {
            study_id: studyId,
            site: filters.site?.trim() || undefined,
            recipient_role: filters.recipientRole?.trim() || undefined,
          },
        },
        ...(options?.signal ? { signal: options.signal } : {}),
      })
    );
  },

  /** Evaluates overdue open queries against the 14-day SLA and records daily aging digests (POST /api/v1/execution/forms/queries/escalate). */
  escalate({
    studyId,
    reason,
    idempotencyKey,
  }: EscalateStudyQueries): Promise<FormQueryEscalationPassResponse> {
    return unwrapResponse(
      client.POST("/api/v1/execution/forms/queries/escalate", {
        params: {
          query: {
            study_id: studyId,
          },
        },
        headers: writeHeaders(reason, idempotencyKey),
      })
    );
  },
};
