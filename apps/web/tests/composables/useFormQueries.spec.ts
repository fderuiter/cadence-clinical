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
    ["open", true, ["answer", "close", "cancel"]],
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
});
