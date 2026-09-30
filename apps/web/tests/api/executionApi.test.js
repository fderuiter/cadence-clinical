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

describe("executionApi site activation", () => {
  let fetchSpy;

  beforeEach(() => {
    setActivePinia(createPinia());
    fetchSpy = vi.spyOn(globalThis, "fetch");
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  it("reads a site's compliance status for the study", async () => {
    const body = {
      study_id: "CAD-101",
      site_id: "SITE 7",
      milestone: "SITE_ACTIVATION",
      is_complete: false,
      missing_documents: ["IRB approval"],
    };
    fetchSpy.mockResolvedValueOnce(jsonResponse(200, body));

    expect(
      await executionApi.getSiteComplianceStatus("SITE 7", "CAD-101")
    ).toEqual(body);

    const request = fetchSpy.mock.calls[0][0];
    const url = new URL(request.url);
    expect(request.method).toBe("GET");
    expect(url.pathname).toBe(
      "/api/v1/execution/sites/SITE%207/compliance-status"
    );
    expect(Object.fromEntries(url.searchParams)).toEqual({
      study_id: "CAD-101",
    });
  });

  it("activates a site with the study in the body and the reason as X-Change-Reason", async () => {
    fetchSpy.mockResolvedValueOnce(
      jsonResponse(200, { status: "success", message: "ok" })
    );

    await executionApi.activateSite("SITE-7", "CAD-101", "IRB approved.");

    const request = fetchSpy.mock.calls[0][0];
    expect(request.method).toBe("POST");
    expect(new URL(request.url).pathname).toBe(
      "/api/v1/execution/sites/SITE-7/activate"
    );
    expect(request.headers.get("X-Change-Reason")).toBe("IRB approved.");
    expect(await request.json()).toEqual({ study_id: "CAD-101" });
  });

  it("sends a reason with an em dash and CJK encoded instead of failing in fetch (#4895)", async () => {
    fetchSpy.mockResolvedValueOnce(
      jsonResponse(200, { status: "success", message: "ok" })
    );

    await executionApi.activateSite("SITE-7", "CAD-101", "IRB approved — 承認");

    const request = fetchSpy.mock.calls[0][0];
    expect(request.headers.get("X-Change-Reason")).toBe(
      "UTF-8''IRB%20approved%20%E2%80%94%20%E6%89%BF%E8%AA%8D"
    );
  });
});

describe("executionApi: participant data, SDV and queries (#4828)", () => {
  let fetchSpy;

  beforeEach(() => {
    setActivePinia(createPinia());
    fetchSpy = vi.spyOn(globalThis, "fetch");
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  async function sent(index = 0) {
    const request = fetchSpy.mock.calls[index][0];
    const text = await request.clone().text();
    return {
      method: request.method,
      path: new URL(request.url).pathname,
      search: Object.fromEntries(new URL(request.url).searchParams),
      reason: request.headers.get("X-Change-Reason"),
      body: text ? JSON.parse(text) : null,
    };
  }

  it("reads a participant's observations, forms and queries by study and subject code", async () => {
    fetchSpy
      .mockResolvedValueOnce(jsonResponse(200, []))
      .mockResolvedValueOnce(jsonResponse(200, []))
      .mockResolvedValueOnce(jsonResponse(200, []));

    await executionApi.listObservations("CAD-101", "017-0001");
    await executionApi.listFormSubmissions("CAD-101", "017-0001");
    await executionApi.listQueries("CAD-101", "017-0001");

    expect((await sent(0)).path).toBe(
      "/api/v1/execution/observations/subject/CAD-101/017-0001"
    );
    expect((await sent(1)).path).toBe(
      "/api/v1/execution/forms/subject/CAD-101/017-0001"
    );
    expect(await sent(2)).toMatchObject({
      method: "GET",
      path: "/api/v1/execution/queries",
      search: { study_id: "CAD-101", subject_id: "017-0001" },
    });
  });

  it("verifies without a body and flags with the reason, each with a reason for change", async () => {
    fetchSpy
      .mockResolvedValueOnce(jsonResponse(200, { id: "o1" }))
      .mockResolvedValueOnce(jsonResponse(200, { id: "o1" }));

    await executionApi.verifyObservationSdv("o1");
    await executionApi.flagObservationSdv("o1", "Unit differs");

    expect(await sent(0)).toMatchObject({
      method: "POST",
      path: "/api/v1/execution/observations/o1/sdv",
      body: null,
      reason: "Verified against source documents",
    });
    expect(await sent(1)).toMatchObject({
      path: "/api/v1/execution/observations/o1/sdv/flag",
      body: { reason: "Unit differs" },
      reason: "Unit differs",
    });
  });

  it("raises a query on an observation with Execution's defaults and no author of its own", async () => {
    fetchSpy.mockResolvedValueOnce(jsonResponse(201, { id: "q1" }));

    await executionApi.raiseQuery({
      studyId: "CAD-101",
      subjectCode: "017-0001",
      observation: {
        id: "o1",
        test_code: "SYSBP",
        domain: "VS",
        visit_id: "Week 12",
        site_id: "017",
      },
      explanation: "Confirm the cuff size",
    });

    const request = await sent();
    expect(request.path).toBe("/api/v1/execution/queries");
    expect(request.body).toEqual({
      study_id: "CAD-101",
      subject_id: "017-0001",
      test_code: "SYSBP",
      domain: "VS",
      observation_id: "o1",
      visit_id: "Week 12",
      site_id: "017",
      explanation: "Confirm the cuff size",
      status: "OPEN",
      origin: "MANUAL",
      priority: "MEDIUM",
    });
  });

  it("never sends a responder or resolver, and sends each reason where Execution reads it", async () => {
    for (let i = 0; i < 4; i++) {
      fetchSpy.mockResolvedValueOnce(jsonResponse(200, { id: "q1" }));
    }

    await executionApi.respondQuery("q1", "Corrected");
    await executionApi.closeQuery("q1", "Accepted");
    await executionApi.reopenQuery("q1", "Still wrong");
    await executionApi.cancelQuery("q1", "Raised in error");

    expect(await sent(0)).toMatchObject({
      path: "/api/v1/execution/queries/q1/respond",
      body: { response: "Corrected" },
      reason: null,
    });
    expect((await sent(0)).body).toEqual({ response: "Corrected" });
    expect(await sent(1)).toMatchObject({
      path: "/api/v1/execution/queries/q1/close",
      body: null,
      reason: "Accepted",
    });
    expect(await sent(2)).toMatchObject({
      path: "/api/v1/execution/queries/q1/reopen",
      body: { reason: "Still wrong" },
      reason: "Still wrong",
    });
    expect(await sent(3)).toMatchObject({
      path: "/api/v1/execution/queries/q1/cancel",
      body: { reason: "Raised in error" },
      reason: "Raised in error",
    });
  });

  it("encodes a reason outside printable ASCII (ADR-2495) and keeps the body as typed", async () => {
    fetchSpy.mockResolvedValueOnce(jsonResponse(200, { id: "q1" }));

    await executionApi.reopenQuery("q1", "Wert falsch — bitte prüfen");

    const request = await sent();
    expect(request.reason).toBe(
      "UTF-8''Wert%20falsch%20%E2%80%94%20bitte%20pr%C3%BCfen"
    );
    expect(request.body).toEqual({ reason: "Wert falsch — bitte prüfen" });
  });
});

describe("executionApi: adding participants and changing their state", () => {
  let fetchSpy;

  beforeEach(() => {
    setActivePinia(createPinia());
    fetchSpy = vi.spyOn(globalThis, "fetch");
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  async function sent(index = 0) {
    const request = fetchSpy.mock.calls[index][0];
    return {
      method: request.method,
      path: new URL(request.url).pathname,
      reason: request.headers.get("X-Change-Reason"),
      body: await request.clone().json(),
    };
  }

  it("adds a participant with no demographics or arm, and the reason as X-Change-Reason", async () => {
    const created = { id: "s1", subject_id: "017-0009", status: "SCREENING" };
    fetchSpy.mockResolvedValueOnce(jsonResponse(201, created));

    const result = await executionApi.addSubject({
      studyId: "CAD-101",
      subjectCode: "017-0009",
      siteId: "017",
      reason: "Signed pre-screening log — 017",
    });

    expect(result).toEqual(created);
    expect(await sent()).toEqual({
      method: "POST",
      path: "/api/v1/execution/subjects",
      reason: "UTF-8''Signed%20pre-screening%20log%20%E2%80%94%20017",
      body: {
        subject_id: "017-0009",
        study_id: "CAD-101",
        site_id: "017",
        arm_id: null,
        demographics: null,
      },
    });
  });

  it("sends no site when none is given, rather than an empty code Execution would reject", async () => {
    fetchSpy.mockResolvedValueOnce(jsonResponse(201, { id: "s1" }));

    await executionApi.addSubject({
      studyId: "CAD-101",
      subjectCode: "017-0009",
      siteId: "",
      reason: "Referred",
    });

    expect((await sent()).body.site_id).toBeNull();
  });

  it("changes a participant's state by id, with the target as status and the reason as X-Change-Reason", async () => {
    fetchSpy.mockResolvedValueOnce(
      jsonResponse(200, { id: "s1", status: "WITHDRAWN" })
    );

    await executionApi.changeSubjectState(
      "s1",
      "WITHDRAWN",
      "Consent withdrawn"
    );

    expect(await sent()).toEqual({
      method: "PATCH",
      path: "/api/v1/execution/subjects/s1/state",
      reason: "Consent withdrawn",
      body: { status: "WITHDRAWN", state: null },
    });
  });
});
