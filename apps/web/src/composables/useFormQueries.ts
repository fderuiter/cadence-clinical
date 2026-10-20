import { computed, ref } from "vue";
import { ApiError } from "../api/client";
import {
  describeQueryError,
  ecrfQueriesApi,
  QUERY_REVISION_STALE,
  queryErrorCode,
  type FormQueryAction,
  type FormQueryResponse,
} from "../api/ecrfQueriesApi";

/** Where a list of queries comes from: one form, or every form of an enrollment. */
export type FormQueryScope = { formId: string } | { enrollmentId: string };

/** What the screen tells the user after a query write. */
export type FormQueryOutcome =
  | { kind: "done"; message: string }
  | { kind: "stale"; message: string }
  | { kind: "failed"; message: string };

/** The value a query is raised on, as the form read returned it. */
export interface QueryTarget {
  formId: string;
  itemId: string;
  observationId: string;
  revision: number;
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
 * The queries of one form or one enrollment (#5461): read them, raise one on a value, and answer, reopen, close or
 * cancel one. Each write carries its text as the reason for change and an idempotency key; a request that never
 * reached the server is retried with the same key (and, for a raise, the same query id), so it is committed at most
 * once. The list shows only what the module returned.
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
      const list =
        "formId" in next
          ? await ecrfQueriesApi.listForForm(next.formId)
          : await ecrfQueriesApi.listForEnrollment(next.enrollmentId);
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

  /** Raises a query on a value at the revision the form read returned. */
  function raise(target: QueryTarget, text: string): Promise<boolean> {
    return write(
      { raise: target, text },
      ({ key, queryId }) =>
        ecrfQueriesApi.raise({
          formId: target.formId,
          observationId: target.observationId,
          revision: target.revision,
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
  };
}
