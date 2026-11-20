import { computed, ref } from "vue";
import { ApiError } from "../api/client";
import {
  describeQueryError,
  ecrfQueriesApi,
  QUERY_REVISION_STALE,
  queryErrorCode,
  type FormQueryAction,
  type FormQueryResponse,
  type StudyQueryFilters,
} from "../api/ecrfQueriesApi";

/** Where a list of queries comes from: one form, every form of an enrollment, or visible enrollments in a study. */
export type FormQueryScope =
  | { formId: string }
  | { enrollmentId: string }
  | { studyId: string; filters?: StudyQueryFilters };

/** What the screen tells the user after a query write. */
export type FormQueryOutcome =
  | { kind: "done"; message: string }
  | { kind: "stale"; message: string }
  | { kind: "failed"; message: string };

/** Per-query completion or error feedback from a multi-query workflow. */
export interface FormQueryBatchItemResult {
  queryId: string;
  formId: string;
  itemId: string;
  subjectCode: string | null;
  ok: boolean;
  message: string;
}

/** The value or unasked blank item a query is raised on, as the form read returned it. */
export interface QueryTarget {
  formId: string;
  itemId: string;
  observationId?: string | null;
  revision?: number | null;
}

const DONE: Record<FormQueryAction, string> = {
  answer: "Query answered.",
  reopen: "Query reopened.",
  close: "Query closed.",
  cancel: "Query cancelled.",
};

/** Queries the site or a monitor can still act on. */
export function isLiveQuery(query: FormQueryResponse): boolean {
  return query.status === "open" || query.status === "answered";
}

/**
 * The transitions to offer on a query, as the Execution module allows them: the site answers an open query; a monitor
 * reopens an answered or closed one, and closes or cancels one that is open or answered. Presentation only: the
 * module checks the caller's capabilities and answers 403 or 409.
 */
export function queryActions(
  query: FormQueryResponse,
  canManage: boolean
): FormQueryAction[] {
  const actions: FormQueryAction[] = [];
  if (query.status === "open") actions.push("answer");
  if (!canManage) return actions;
  if (query.status === "answered" || query.status === "closed")
    actions.push("reopen");
  if (isLiveQuery(query)) actions.push("close", "cancel");
  return actions;
}

function newKey(): string {
  return globalThis.crypto.randomUUID();
}

/**
 * The queries of one form, one enrollment, or a study (#5461): read them, raise one on a value or blank item, and
 * answer, reopen, close or cancel one or several. Each write carries its text as the reason for change and an
 * idempotency key; multi-query actions dispatch one independent command per query rather than an atomic batch.
 */
export function useFormQueries() {
  const queries = ref<FormQueryResponse[]>([]);
  const loading = ref(false);
  const loadError = ref<string | null>(null);
  const busy = ref(false);
  const outcome = ref<FormQueryOutcome | null>(null);
  let scope: FormQueryScope | null = null;
  let loadRequest = 0;
  let pending: { request: string; key: string; queryId: string } | null = null;

  const liveCount = computed(() => queries.value.filter(isLiveQuery).length);

  /** The form's queries on one item, oldest first. */
  function forItem(formId: string, itemId: string): FormQueryResponse[] {
    return queries.value.filter(
      (q) => q.form_id === formId && q.item_id === itemId
    );
  }

  async function load(next: FormQueryScope): Promise<void> {
    scope = next;
    const request = ++loadRequest;
    loading.value = true;
    loadError.value = null;
    try {
      let list: FormQueryResponse[];
      if ("formId" in next) {
        list = await ecrfQueriesApi.listForForm(next.formId);
      } else if ("enrollmentId" in next) {
        list = await ecrfQueriesApi.listForEnrollment(next.enrollmentId);
      } else {
        list = await ecrfQueriesApi.listForStudy(next.studyId, next.filters);
      }
      if (request !== loadRequest) return;
      queries.value = list;
    } catch (error) {
      if (request !== loadRequest) return;
      queries.value = [];
      loadError.value = describeQueryError(error);
    } finally {
      if (request === loadRequest) loading.value = false;
    }
  }

  async function reload(): Promise<void> {
    if (scope) await load(scope);
  }

  /** Forgets the list, for example when the form closes. */
  function clear() {
    scope = null;
    loadRequest++;
    queries.value = [];
    loading.value = false;
    loadError.value = null;
    outcome.value = null;
    pending = null;
  }

  function pendingFor(request: unknown) {
    const sent = JSON.stringify(request);
    if (pending?.request !== sent) {
      pending = { request: sent, key: newKey(), queryId: newKey() };
    }
    return pending;
  }

  function put(updated: FormQueryResponse) {
    const index = queries.value.findIndex(
      (q) => q.query_id === updated.query_id
    );
    if (index >= 0) queries.value.splice(index, 1, updated);
    else queries.value.push(updated);
  }

  async function write(
    request: unknown,
    send: (p: { key: string; queryId: string }) => Promise<FormQueryResponse>,
    done: string
  ): Promise<boolean> {
    if (busy.value) return false;
    const attempt = pendingFor(request);
    busy.value = true;
    outcome.value = null;
    try {
      put(await send(attempt));
      pending = null;
      outcome.value = { kind: "done", message: done };
      return true;
    } catch (error) {
      // A request that never reached the server is retried with the same key.
      if (error instanceof ApiError && error.status !== null) pending = null;
      outcome.value = {
        kind:
          queryErrorCode(error) === QUERY_REVISION_STALE ? "stale" : "failed",
        message: describeQueryError(error),
      };
      return false;
    } finally {
      busy.value = false;
    }
  }

  /** Raises a query on a value at the revision the form read returned, or on an unasked blank item. */
  function raise(target: QueryTarget, text: string): Promise<boolean> {
    return write(
      { raise: target, text },
      ({ key, queryId }) =>
        ecrfQueriesApi.raise({
          formId: target.formId,
          observationId: target.observationId ?? null,
          revision: target.revision ?? null,
          itemId: target.observationId ? undefined : target.itemId,
          text,
          queryId,
          reason: text,
          idempotencyKey: key,
        }),
      "Query raised."
    );
  }

  /** Answers, reopens, closes or cancels a query; the text is the answer, the reopening note or the reason. */
  function act(
    query: FormQueryResponse,
    action: FormQueryAction,
    text: string
  ): Promise<boolean> {
    return write(
      { action, query: query.query_id, version: query.version, text },
      ({ key }) =>
        ecrfQueriesApi.transition(action, {
          formId: query.form_id,
          queryId: query.query_id,
          text,
          reason: text,
          idempotencyKey: key,
        }),
      DONE[action]
    );
  }

  /**
   * Dispatches one independent transition command per selected query using a shared reason note, updating each
   * succeeded query in place and reporting per-query completion or error results.
   */
  async function actMany(
    targets: FormQueryResponse[],
    action: FormQueryAction,
    text: string
  ): Promise<FormQueryBatchItemResult[]> {
    if (busy.value || targets.length === 0) return [];
    busy.value = true;
    outcome.value = null;
    const results: FormQueryBatchItemResult[] = [];
    try {
      for (const target of targets) {
        try {
          const updated = await ecrfQueriesApi.transition(action, {
            formId: target.form_id,
            queryId: target.query_id,
            text,
            reason: text,
            idempotencyKey: newKey(),
          });
          put(updated);
          results.push({
            queryId: target.query_id,
            formId: target.form_id,
            itemId: target.item_id,
            subjectCode: target.subject_code ?? null,
            ok: true,
            message: DONE[action],
          });
        } catch (error) {
          results.push({
            queryId: target.query_id,
            formId: target.form_id,
            itemId: target.item_id,
            subjectCode: target.subject_code ?? null,
            ok: false,
            message: describeQueryError(error),
          });
        }
      }
      const succeeded = results.filter((r) => r.ok).length;
      const failed = results.length - succeeded;
      if (failed === 0) {
        outcome.value = {
          kind: "done",
          message: `Completed ${succeeded} of ${results.length} ${results.length === 1 ? "query" : "queries"}.`,
        };
      } else {
        outcome.value = {
          kind: "failed",
          message: `Completed ${succeeded} of ${results.length} queries; ${failed} could not be updated.`,
        };
      }
      return results;
    } finally {
      busy.value = false;
    }
  }

  return {
    queries,
    loading,
    loadError,
    busy,
    outcome,
    liveCount,
    forItem,
    load,
    reload,
    clear,
    raise,
    act,
    actMany,
  };
}
