import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { executionApi } from "../../src/api/executionApi";
import { ApiError } from "../../src/api/client";

function jsonResponse(status, body, contentType = "application/json") {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": contentType },
  });
}

describe("executionApi.listSubjects (#4828)", () => {
  let fetchSpy;

  beforeEach(() => {
    setActivePinia(createPinia());
    fetchSpy = vi.spyOn(globalThis, "fetch");
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  it("asks the gateway's Execution route for one page of the study's subjects", async () => {
    const page = { items: [], total: 0, limit: 25, offset: 50 };
    fetchSpy.mockResolvedValueOnce(jsonResponse(200, page));

    const result = await executionApi.listSubjects({
      studyId: "CAD-101",
      siteId: "SITE-7",
      limit: 25,
      offset: 50,
    });

    expect(result).toEqual(page);
    const request = fetchSpy.mock.calls[0][0];
    const url = new URL(request.url);
    expect(request.method).toBe("GET");
    expect(url.pathname).toBe("/api/v1/execution/subjects");
    expect(Object.fromEntries(url.searchParams)).toEqual({
      study_id: "CAD-101",
      site_id: "SITE-7",
      limit: "25",
      offset: "50",
    });
  });

  it("raises the service's problem detail as an ApiError", async () => {
    fetchSpy.mockResolvedValueOnce(
      jsonResponse(
        400,
        { title: "Bad Request", detail: "study_id is required." },
        "application/problem+json"
      )
    );

    const error = await executionApi
      .listSubjects({ studyId: "" })
      .catch((e) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect(error.status).toBe(400);
    expect(error.message).toBe("study_id is required.");
  });
});
