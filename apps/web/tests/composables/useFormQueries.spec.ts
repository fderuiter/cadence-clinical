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
    origin: "manual",
    owner_role: "site",
    priority: "normal",
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

describe("queryActions (#5461, ADR-2625)", () => {
  it.each([
    ["open", "manual", false, undefined, undefined, ["answer"]],
    ["open", "manual", true, undefined, undefined, ["cancel"]],
    ["open", "system", true, undefined, undefined, ["close", "cancel"]],
    ["answered", "manual", false, undefined, undefined, []],
    [
      "answered",
      "manual",
      true,
      "cra@study.test",
      "crc@study.test",
      ["reopen", "close", "cancel"],
    ],
    [
      "answered",
      "manual",
      true,
      "CRC@study.test",
      "crc@study.test",
      ["reopen", "cancel"],
    ],
    ["closed", "manual", true, undefined, undefined, ["reopen"]],
    ["cancelled", "manual", true, undefined, undefined, []],
  ] as const)(
    "offers on a %s %s query (monitor: %s, caller: %s, answeredBy: %s) %j",
    (status, origin, canManage, currentUserId, lastAnsweredBy, actions) => {
      expect(
        queryActions(
          query({ status, origin, last_answered_by: lastAnsweredBy }),
          canManage,
          currentUserId
        )
      ).toEqual(actions);
    }
  );
});

describe("useFormQueries (#5461, ADR-2625)", () => {
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

  it("retries a raise that never reached the server with the same key and query id, preserving separate text and reason", async () => {
    vi.mocked(ecrfQueriesApi.raise)
      .mockRejectedValueOnce(new TypeError("Failed to fetch"))
      .mockResolvedValueOnce(query());
    const queries = useFormQueries();

    expect(
      await queries.raise(TARGET, "Please confirm", "Source chart review", {
        priority: "high",
      })
    ).toBe(false);
    expect(queries.outcome.value).toEqual({
      kind: "failed",
      message: "Failed to fetch",
    });
    expect(
      await queries.raise(TARGET, "Please confirm", "Source chart review", {
        priority: "high",
      })
    ).toBe(true);

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
      reason: "Source chart review",
      priority: "high",
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

    await queries.raise(TARGET, "Please confirm", "Audit review");
    await queries.raise(TARGET, "Please confirm", "Audit review");

    const keys = vi
      .mocked(ecrfQueriesApi.raise)
      .mock.calls.map(([request]) => request.idempotencyKey);
    expect(keys[0]).not.toBe(keys[1]);
    expect(queries.outcome.value?.kind).toBe("stale");
  });

  it("replaces a query with the module's answer after a transition with separate note and reason", async () => {
    vi.mocked(ecrfQueriesApi.listForForm).mockResolvedValue([query()]);
    vi.mocked(ecrfQueriesApi.transition).mockResolvedValue(
      query({ status: "cancelled", version: 2 })
    );
    const queries = useFormQueries();
    await queries.load({ formId: "form-a" });

    expect(
      await queries.act(
        queries.queries.value[0],
        "cancel",
        "Opened on wrong row",
        "Raised in error"
      )
    ).toBe(true);

    expect(ecrfQueriesApi.transition).toHaveBeenCalledWith(
      "cancel",
      expect.objectContaining({
        formId: "form-a",
        queryId: "query-a",
        text: "Opened on wrong row",
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

  it("reads a study's queries with owner/origin/priority filters and raises on a blank item by itemId", async () => {
    vi.mocked(ecrfQueriesApi.listForStudy).mockResolvedValue([
      query({ site_code: "017", subject_code: "017-0001" }),
    ]);
    vi.mocked(ecrfQueriesApi.raise).mockResolvedValue(
      query({ item_id: "DIABP", observation_id: "obs-blank", revision: 1 })
    );
    const queries = useFormQueries();

    await queries.load({
      studyId: "study-1",
      filters: {
        status: "open",
        owner: "site",
        origin: "manual",
        priority: "high",
        site: "017",
        valueChanged: false,
      },
    });
    expect(ecrfQueriesApi.listForStudy).toHaveBeenCalledWith("study-1", {
      status: "open",
      owner: "site",
      origin: "manual",
      priority: "high",
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
        "Missing diastolic BP",
        "Baseline required value missing"
      )
    ).toBe(true);
    expect(ecrfQueriesApi.raise).toHaveBeenCalledWith(
      expect.objectContaining({
        formId: "form-a",
        itemId: "DIABP",
        observationId: null,
        revision: 0,
        text: "Missing diastolic BP",
        reason: "Baseline required value missing",
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
      "Accepted source corrections",
      "Batch review complete"
    );

    expect(ecrfQueriesApi.transition).toHaveBeenCalledTimes(2);
    const [firstCall, secondCall] = vi.mocked(ecrfQueriesApi.transition).mock
      .calls;
    expect(firstCall[1].idempotencyKey).not.toBe(secondCall[1].idempotencyKey);
    expect(firstCall[1].text).toBe("Accepted source corrections");
    expect(firstCall[1].reason).toBe("Batch review complete");
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

    const first = queries.load({ studyId: "study-1" });
    expect(queries.loading.value).toBe(true);
    expect(queries.refreshing.value).toBe(false);
    resolveInitial([query({ query_id: "query-initial" })]);
    await first;
    expect(queries.loading.value).toBe(false);
    expect(queries.refreshing.value).toBe(false);

    const second = queries.load({
      studyId: "study-1",
      filters: { status: "open" },
    });
    expect(queries.loading.value).toBe(false);
    expect(queries.refreshing.value).toBe(true);
    resolveRefresh([query({ query_id: "query-refreshed" })]);
    await second;
    expect(queries.refreshing.value).toBe(false);
    expect(queries.queries.value.map((q) => q.query_id)).toEqual([
      "query-refreshed",
    ]);

    await queries.load({
      studyId: "study-1",
      filters: { status: "answered" },
    });
    expect(queries.queries.value.map((q) => q.query_id)).toEqual([
      "query-refreshed",
    ]);
    expect(queries.loadError.value).toBe("Network glitch");
  });
});
