import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import {
  describeQueryError,
  ecrfQueriesApi,
  QUERY_SEPARATION_OF_DUTIES,
  queryErrorCode,
} from "../../src/api/ecrfQueriesApi";
import { ApiError } from "../../src/api/client";

const QUERY = { query_id: "query-a", form_id: "form-a", status: "open" };

function jsonResponse(status, body, contentType = "application/json") {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": contentType },
  });
}

// #5461, ADR-2625: eCRF queries are served by the Execution module on the kernel host, under /api/v1/execution/forms.
describe("ecrfQueriesApi (#5461, ADR-2625)", () => {
  let fetchSpy;

  beforeEach(() => {
    setActivePinia(createPinia());
    fetchSpy = vi.spyOn(globalThis, "fetch");
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  function lastRequest() {
    return fetchSpy.mock.calls.at(-1)[0];
  }

  it("raises a query on a value's revision, with separate reason, routing metadata, and idempotency key", async () => {
    fetchSpy.mockResolvedValueOnce(jsonResponse(201, QUERY));

    const result = await ecrfQueriesApi.raise({
      formId: "form-a",
      observationId: "observation-a",
      revision: 3,
      text: "Please confirm the value",
      queryId: "query-a",
      ownerRole: "site",
      priority: "high",
      reason: "Source medical chart review",
      idempotencyKey: "raise-1",
    });

    expect(result).toEqual(QUERY);
    const request = lastRequest();
    expect(request.method).toBe("POST");
    expect(new URL(request.url).pathname).toBe(
      "/api/v1/execution/forms/form-a/queries"
    );
    expect(request.headers.get("Idempotency-Key")).toBe("raise-1");
    expect(request.headers.get("X-Change-Reason")).toBe(
      "Source medical chart review"
    );
    expect(await request.json()).toEqual({
      observation_id: "observation-a",
      revision: 3,
      text: "Please confirm the value",
      query_id: "query-a",
      owner_role: "site",
      priority: "high",
    });
  });

  it("lists a form's queries and an enrollment's, including bitemporal coordinates", async () => {
    fetchSpy
      .mockResolvedValueOnce(jsonResponse(200, [QUERY]))
      .mockResolvedValueOnce(jsonResponse(200, [QUERY]))
      .mockResolvedValueOnce(jsonResponse(200, [QUERY]));

    expect(await ecrfQueriesApi.listForForm("form-a")).toEqual([QUERY]);
    expect(new URL(lastRequest().url).pathname).toBe(
      "/api/v1/execution/forms/form-a/queries"
    );

    await ecrfQueriesApi.listForForm("form-a", {
      asOf: "2026-08-10T00:00:00Z",
    });
    expect(new URL(lastRequest().url).searchParams.get("as_of")).toBe(
      "2026-08-10T00:00:00Z"
    );

    expect(await ecrfQueriesApi.listForEnrollment("enrollment-a")).toEqual([
      QUERY,
    ]);
    const url = new URL(lastRequest().url);
    expect(url.pathname).toBe("/api/v1/execution/forms/queries");
    expect(url.searchParams.get("enrollment_id")).toBe("enrollment-a");
  });

  it("raises a missing-data query on a blank item by item_id without observation_id", async () => {
    fetchSpy.mockResolvedValueOnce(jsonResponse(201, QUERY));

    await ecrfQueriesApi.raise({
      formId: "form-a",
      observationId: null,
      revision: 0,
      itemId: "DIABP",
      text: "Diastolic BP is missing on source",
      queryId: "query-blank",
      reason: "Missing required baseline value",
      idempotencyKey: "raise-blank-1",
    });

    const request = lastRequest();
    expect(new URL(request.url).pathname).toBe(
      "/api/v1/execution/forms/form-a/queries"
    );
    expect(request.headers.get("X-Change-Reason")).toBe(
      "Missing required baseline value"
    );
    expect(await request.json()).toEqual({
      observation_id: null,
      revision: 0,
      item_id: "DIABP",
      text: "Diastolic BP is missing on source",
      query_id: "query-blank",
    });
  });

  it("lists study-wide queries with status, owner, origin, priority, escalated, site, participant, value_changed, and bitemporal filters", async () => {
    fetchSpy.mockResolvedValueOnce(jsonResponse(200, [QUERY]));

    expect(
      await ecrfQueriesApi.listForStudy("study-1", {
        status: "open",
        owner: "site",
        origin: "manual",
        priority: "high",
        escalated: true,
        site: "017",
        participant: "017-0001",
        valueChanged: true,
        validAt: "2026-08-18T00:00:00Z",
        knownAt: "2026-08-19T00:00:00Z",
      })
    ).toEqual([QUERY]);

    const url = new URL(lastRequest().url);
    expect(url.pathname).toBe("/api/v1/execution/forms/queries");
    expect(Object.fromEntries(url.searchParams)).toEqual({
      study_id: "study-1",
      status: "open",
      owner: "site",
      origin: "manual",
      priority: "high",
      escalated: "true",
      site: "017",
      participant: "017-0001",
      value_changed: "true",
      valid_at: "2026-08-18T00:00:00Z",
      known_at: "2026-08-19T00:00:00Z",
    });
  });

  it.each([
    ["answer", { text: "Confirmed" }],
    ["reopen", { text: "Still differs from source" }],
  ])(
    "sends %s with its text and separate X-Change-Reason",
    async (action, body) => {
      fetchSpy.mockResolvedValueOnce(jsonResponse(200, QUERY));

      await ecrfQueriesApi.transition(action, {
        formId: "form-a",
        queryId: "query-a",
        text: body.text,
        reason: "Signed source review",
        idempotencyKey: `${action}-1`,
      });

      const request = lastRequest();
      expect(new URL(request.url).pathname).toBe(
        `/api/v1/execution/forms/form-a/queries/query-a/${action}`
      );
      expect(request.headers.get("Idempotency-Key")).toBe(`${action}-1`);
      expect(request.headers.get("X-Change-Reason")).toBe(
        "Signed source review"
      );
      expect(await request.json()).toEqual(body);
    }
  );

  it.each(["close", "cancel"])(
    "sends %s with no body when no distinct note is given and encodes X-Change-Reason",
    async (action) => {
      fetchSpy.mockResolvedValueOnce(jsonResponse(200, QUERY));

      await ecrfQueriesApi.transition(action, {
        formId: "form-a",
        queryId: "query-a",
        text: "Answer accepted — thanks",
        reason: "Answer accepted — thanks",
        idempotencyKey: `${action}-1`,
      });

      const request = lastRequest();
      expect(new URL(request.url).pathname).toBe(
        `/api/v1/execution/forms/form-a/queries/query-a/${action}`
      );
      // Encoded per ADR-2495, so a typographic dash reaches the audit trail as typed.
      expect(request.headers.get("X-Change-Reason")).toBe(
        "UTF-8''Answer%20accepted%20%E2%80%94%20thanks"
      );
      expect(await request.text()).toBe("");
    }
  );

  it.each(["close", "cancel"])(
    "sends %s with JSON note body when text is distinct from X-Change-Reason",
    async (action) => {
      fetchSpy.mockResolvedValueOnce(jsonResponse(200, QUERY));

      await ecrfQueriesApi.transition(action, {
        formId: "form-a",
        queryId: "query-a",
        text: "Accepted corrected systolic value",
        reason: "SDV completed against flowsheet",
        idempotencyKey: `${action}-distinct`,
      });

      const request = lastRequest();
      expect(request.headers.get("X-Change-Reason")).toBe(
        "SDV completed against flowsheet"
      );
      expect(await request.json()).toEqual({
        text: "Accepted corrected systolic value",
      });
    }
  );

  it("raises the module's problem as an ApiError with its code", async () => {
    fetchSpy.mockResolvedValueOnce(
      jsonResponse(
        409,
        {
          title: "Invalid transition",
          detail: "The query is closed.",
          code: "M.Execution.Query.InvalidTransition",
        },
        "application/problem+json"
      )
    );

    const error = await ecrfQueriesApi
      .transition("close", {
        formId: "form-a",
        queryId: "query-a",
        reason: "Done",
        idempotencyKey: "close-2",
      })
      .catch((e) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect(error.status).toBe(409);
    expect(queryErrorCode(error)).toBe("M.Execution.Query.InvalidTransition");
    expect(describeQueryError(error)).toContain("Someone changed this query");
  });

  it.each([
    [
      409,
      "M.Execution.Query.RevisionStale",
      "The value changed since you loaded the form",
    ],
    [
      409,
      QUERY_SEPARATION_OF_DUTIES,
      "another monitor or data manager must review and close it",
    ],
    [404, "M.Execution.Query.ObservationUnknown", "not on this form"],
    [423, "K.Assertion.Locked", "locked"],
    [403, "K.Authorization.Denied", "not allowed"],
  ])("describes a %s %s for the user", (status, code, text) => {
    expect(
      describeQueryError(new ApiError("Refused", status, null, { code }))
    ).toContain(text);
  });

  it("falls back to the server's message", () => {
    expect(describeQueryError(new ApiError("Gateway timeout", 504))).toBe(
      "Gateway timeout"
    );
  });
});
