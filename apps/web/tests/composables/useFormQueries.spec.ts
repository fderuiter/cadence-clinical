import { describe, it, expect, vi, beforeEach } from "vitest";
import { ApiError } from "../../src/api/client";
import {
  ecrfQueriesApi,
  type FormQueryResponse,
} from "../../src/api/ecrfQueriesApi";
import {
  queryActions,
  useFormQueries,
} from "../../src/composables/useFormQueries";

vi.mock("../../src/api/ecrfQueriesApi", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("../../src/api/ecrfQueriesApi")>();
  return {
    ...actual,
    ecrfQueriesApi: {
      raise: vi.fn(),
      listForForm: vi.fn(),
      listForEnrollment: vi.fn(),
      listForStudy: vi.fn(),
      transition: vi.fn(),
    },
  };
});

function query(overrides: Partial<FormQueryResponse> = {}): FormQueryResponse {
  return {
    query_id: "query-a",
    form_id: "form-a",
    enrollment_id: "enrollment-a",
    item_id: "SYSBP",
    observation_id: "observation-a",
    revision: 1,
    current_revision: 1,
    value_changed: false,
    status: "open",
    text: "Please confirm",
    messages: [],
    raised_by: "monitor",
    raised_at: "2027-03-14T10:00:00Z",
    version: 1,
    ...overrides,
  };
}

const TARGET = {
  formId: "form-a",
  itemId: "SYSBP",
  observationId: "observation-a",
  revision: 1,
};

describe("queryActions (#5461)", () => {
  it.each([
    ["open", false, ["answer"]],
    ["open", true, ["close", "cancel"]],
    ["answered", false, []],
    ["answered", true, ["reopen", "close", "cancel"]],
    ["closed", true, ["reopen"]],
    ["cancelled", true, []],
  ] as const)(
    "offers on a %s query (monitor: %s) %j",
    (status, canManage, actions) => {
      expect(queryActions(query({ status }), canManage)).toEqual(actions);
    }
  );
});

describe("useFormQueries (#5461)", () => {
  beforeEach(() => {
    for (const fn of Object.values(ecrfQueriesApi)) vi.mocked(fn).mockReset();
  });

  it("reads a form's queries or an enrollment's", async () => {
    vi.mocked(ecrfQueriesApi.listForForm).mockResolvedValue([query()]);
    vi.mocked(ecrfQueriesApi.listForEnrollment).mockResolvedValue([
      query({ query_id: "query-b", status: "answered" }),
      query({ query_id: "query-c", status: "closed" }),
    ]);
    const queries = useFormQueries();

    await queries.load({ formId: "form-a" });
    expect(ecrfQueriesApi.listForForm).toHaveBeenCalledWith("form-a");
    expect(queries.forItem("form-a", "SYSBP")).toHaveLength(1);

    await queries.load({ enrollmentId: "enrollment-a" });
    expect(ecrfQueriesApi.listForEnrollment).toHaveBeenCalledWith(
      "enrollment-a"
    );
    expect(queries.queries.value).toHaveLength(2);
    expect(queries.liveCount.value).toBe(1);
  });

  it("keeps only the latest read when two overlap", async () => {
    let first: (value: FormQueryResponse[]) => void = () => {};
    vi.mocked(ecrfQueriesApi.listForForm)
      .mockReturnValueOnce(new Promise((resolve) => (first = resolve)))
      .mockResolvedValueOnce([query({ query_id: "query-b" })]);
    const queries = useFormQueries();

    const stale = queries.load({ formId: "form-a" });
    await queries.load({ formId: "form-b" });
    first([query()]);
    await stale;

    expect(queries.queries.value.map((q) => q.query_id)).toEqual(["query-b"]);
    expect(queries.loading.value).toBe(false);
  });

  it("reports a read that fails", async () => {
    vi.mocked(ecrfQueriesApi.listForForm).mockRejectedValue(
      new ApiError("Forbidden", 403, null, { code: "K.Authorization.Denied" })
    );
    const queries = useFormQueries();

    await queries.load({ formId: "form-a" });

    expect(queries.queries.value).toEqual([]);
    expect(queries.loadError.value).toContain("not allowed");
  });

  it("retries a raise that never reached the server with the same key and query id", async () => {
    vi.mocked(ecrfQueriesApi.raise)
      .mockRejectedValueOnce(new TypeError("Failed to fetch"))
      .mockResolvedValueOnce(query());
    const queries = useFormQueries();

    expect(await queries.raise(TARGET, "Please confirm")).toBe(false);
    expect(queries.outcome.value).toEqual({
      kind: "failed",
      message: "Failed to fetch",
    });
    expect(await queries.raise(TARGET, "Please confirm")).toBe(true);

    const [first, second] = vi
      .mocked(ecrfQueriesApi.raise)
      .mock.calls.map(([request]) => request);
    expect(second.idempotencyKey).toBe(first.idempotencyKey);
    expect(second.queryId).toBe(first.queryId);
    expect(second).toMatchObject({
      formId: "form-a",
      observationId: "observation-a",
      revision: 1,
      text: "Please confirm",
      reason: "Please confirm",
    });
    expect(queries.queries.value).toHaveLength(1);
    expect(queries.outcome.value).toEqual({
      kind: "done",
      message: "Query raised.",
    });
  });

  it("uses a new key after the server answered, and marks a stale revision", async () => {
    vi.mocked(ecrfQueriesApi.raise).mockRejectedValue(
      new ApiError("Conflict", 409, null, {
        code: "M.Execution.Query.RevisionStale",
      })
    );
    const queries = useFormQueries();

    await queries.raise(TARGET, "Please confirm");
    await queries.raise(TARGET, "Please confirm");

    const keys = vi
      .mocked(ecrfQueriesApi.raise)
      .mock.calls.map(([request]) => request.idempotencyKey);
    expect(keys[0]).not.toBe(keys[1]);
    expect(queries.outcome.value?.kind).toBe("stale");
  });

  it("replaces a query with the module's answer after a transition", async () => {
    vi.mocked(ecrfQueriesApi.listForForm).mockResolvedValue([query()]);
    vi.mocked(ecrfQueriesApi.transition).mockResolvedValue(
      query({ status: "cancelled", version: 2 })
    );
    const queries = useFormQueries();
    await queries.load({ formId: "form-a" });

    expect(
      await queries.act(queries.queries.value[0], "cancel", "Raised in error")
    ).toBe(true);

    expect(ecrfQueriesApi.transition).toHaveBeenCalledWith(
      "cancel",
      expect.objectContaining({
        formId: "form-a",
        queryId: "query-a",
        reason: "Raised in error",
      })
    );
    expect(queries.queries.value).toEqual([
      query({ status: "cancelled", version: 2 }),
    ]);
    expect(queries.outcome.value?.message).toBe("Query cancelled.");
  });

  it("forgets everything on clear", async () => {
    vi.mocked(ecrfQueriesApi.listForForm).mockResolvedValue([query()]);
    const queries = useFormQueries();
    await queries.load({ formId: "form-a" });

    queries.clear();
    await queries.reload();

    expect(queries.queries.value).toEqual([]);
    expect(ecrfQueriesApi.listForForm).toHaveBeenCalledTimes(1);
  });

  it("reads a study's queries with filters and raises on a blank item by itemId", async () => {
    vi.mocked(ecrfQueriesApi.listForStudy).mockResolvedValue([
      query({ site_code: "017", subject_code: "017-0001" }),
    ]);
    vi.mocked(ecrfQueriesApi.raise).mockResolvedValue(
      query({ item_id: "DIABP", observation_id: "obs-blank", revision: 1 })
    );
    const queries = useFormQueries();

    await queries.load({
      studyId: "study-1",
      filters: { status: "open", site: "017", valueChanged: false },
    });
    expect(ecrfQueriesApi.listForStudy).toHaveBeenCalledWith("study-1", {
      status: "open",
      site: "017",
      valueChanged: false,
    });

    expect(
      await queries.raise(
        {
          formId: "form-a",
          itemId: "DIABP",
          observationId: null,
          revision: 0,
        },
        "Missing diastolic BP"
      )
    ).toBe(true);
    expect(ecrfQueriesApi.raise).toHaveBeenCalledWith(
      expect.objectContaining({
        formId: "form-a",
        itemId: "DIABP",
        observationId: null,
        revision: 0,
        text: "Missing diastolic BP",
      })
    );
  });

  it("dispatches one independent transition per query in actMany and reports partial failures", async () => {
    const q1 = query({ query_id: "query-1", status: "answered" });
    const q2 = query({ query_id: "query-2", status: "answered" });
    vi.mocked(ecrfQueriesApi.listForStudy).mockResolvedValue([q1, q2]);
    vi.mocked(ecrfQueriesApi.transition)
      .mockResolvedValueOnce({ ...q1, status: "closed", version: 2 })
      .mockRejectedValueOnce(
        new ApiError("Locked", 423, null, { code: "K.Assertion.Locked" })
      );

    const queries = useFormQueries();
    await queries.load({ studyId: "study-1" });
    const results = await queries.actMany(
      [q1, q2],
      "close",
      "Batch review complete"
    );

    expect(ecrfQueriesApi.transition).toHaveBeenCalledTimes(2);
    const [firstCall, secondCall] = vi.mocked(ecrfQueriesApi.transition).mock
      .calls;
    expect(firstCall[1].idempotencyKey).not.toBe(secondCall[1].idempotencyKey);
    expect(results).toHaveLength(2);
    expect(results[0]).toMatchObject({ queryId: "query-1", ok: true });
    expect(results[1].queryId).toBe("query-2");
    expect(results[1].ok).toBe(false);
    expect(results[1].message).toContain("locked");
    expect(queries.queries.value.map((q) => q.status)).toEqual([
      "closed",
      "answered",
    ]);
    expect(queries.outcome.value).toEqual({
      kind: "failed",
      message: "Completed 1 of 2 queries; 1 could not be updated.",
    });
  });

  it("separates initial loading from same-scope refreshing and keeps existing queries if a refresh fails", async () => {
    let resolveInitial!: (rows: FormQueryResponse[]) => void;
    let resolveRefresh!: (rows: FormQueryResponse[]) => void;
    vi.mocked(ecrfQueriesApi.listForStudy)
      .mockImplementationOnce(
        () => new Promise((resolve) => (resolveInitial = resolve))
      )
      .mockImplementationOnce(
        () => new Promise((resolve) => (resolveRefresh = resolve))
      )
      .mockRejectedValueOnce(new TypeError("Network glitch"));

    const queries = useFormQueries();
    const initialLoad = queries.load({ studyId: "study-1" });
    expect(queries.loading.value).toBe(true);
    expect(queries.refreshing.value).toBe(false);

    resolveInitial([query({ query_id: "query-initial" })]);
    await initialLoad;
    expect(queries.loading.value).toBe(false);
    expect(queries.refreshing.value).toBe(false);
    expect(queries.queries.value.map((q) => q.query_id)).toEqual([
      "query-initial",
    ]);

    const filterRefresh = queries.load({
      studyId: "study-1",
      filters: { site: "017" },
    });
    expect(queries.loading.value).toBe(false);
    expect(queries.refreshing.value).toBe(true);
    expect(queries.queries.value.map((q) => q.query_id)).toEqual([
      "query-initial",
    ]);

    resolveRefresh([query({ query_id: "query-filtered" })]);
    await filterRefresh;
    expect(queries.refreshing.value).toBe(false);
    expect(queries.queries.value.map((q) => q.query_id)).toEqual([
      "query-filtered",
    ]);

    await queries.load({ studyId: "study-1", filters: { site: "022" } });
    expect(queries.refreshing.value).toBe(false);
    expect(queries.queries.value.map((q) => q.query_id)).toEqual([
      "query-filtered",
    ]);
    expect(queries.loadError.value).toContain("Network glitch");
  });

  it("forwards AbortSignal to study query reads and ignores AbortError without clearing existing queries", async () => {
    vi.mocked(ecrfQueriesApi.listForStudy)
      .mockResolvedValueOnce([query({ query_id: "query-keep" })])
      .mockImplementationOnce((_studyId, _filters, options) => {
        return new Promise((_resolve, reject) => {
          options?.signal?.addEventListener("abort", () => {
            const abortErr = new Error("Aborted");
            abortErr.name = "AbortError";
            reject(abortErr);
          });
        });
      });

    const queries = useFormQueries();
    await queries.load({ studyId: "study-1" });
    expect(queries.queries.value.map((q) => q.query_id)).toEqual([
      "query-keep",
    ]);

    const controller = new AbortController();
    const abortedLoad = queries.load(
      { studyId: "study-1", filters: { site: "017" } },
      { signal: controller.signal }
    );
    expect(ecrfQueriesApi.listForStudy).toHaveBeenLastCalledWith(
      "study-1",
      { site: "017" },
      { signal: controller.signal }
    );

    controller.abort();
    await abortedLoad;

    expect(queries.loadError.value).toBeNull();
    expect(queries.loading.value).toBe(false);
    expect(queries.refreshing.value).toBe(false);
    expect(queries.queries.value.map((q) => q.query_id)).toEqual([
      "query-keep",
    ]);
  });

  it("updates current_revision and value_changed in memory via applySavedValues", async () => {
    vi.mocked(ecrfQueriesApi.listForForm).mockResolvedValue([
      query({
        query_id: "query-sys",
        form_id: "form-a",
        item_id: "SYSBP",
        observation_id: "obs-sys",
        revision: 2,
        current_revision: 2,
        value_changed: false,
      }),
    ]);

    const queries = useFormQueries();
    await queries.load({ formId: "form-a" });
    queries.applySavedValues("form-a", [
      {
        item_id: "SYSBP",
        observation_id: "obs-sys",
        revision: 3,
        changed: true,
      },
    ]);

    expect(queries.queries.value[0]).toMatchObject({
      query_id: "query-sys",
      current_revision: 3,
      value_changed: true,
    });
    expect(ecrfQueriesApi.listForForm).toHaveBeenCalledTimes(1);
  });
});
