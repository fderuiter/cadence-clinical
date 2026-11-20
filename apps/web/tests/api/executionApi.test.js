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

  it("adds a participant with no demographics, and the reason as X-Change-Reason", async () => {
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

describe("executionApi: RTSM, consent, subject detail, and step-up signature", () => {
  let fetchSpy;
  const isoSample = new Date(Date.UTC(2027, 5, 10)).toISOString();

  beforeEach(() => {
    setActivePinia(createPinia());
    fetchSpy = vi.spyOn(globalThis, "fetch");
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  it("requests a step-up X-Sig-Token from the gateway signature-verification route", async () => {
    fetchSpy.mockResolvedValueOnce(
      jsonResponse(200, { sig_token: "sig-tok-1", expires_in: 60 })
    );

    const token = await executionApi.requestSignatureToken({
      password: "Password-123!",
      totp: "654321",
      username: "stat.approver",
      action: "/api/v1/execution/randomization/schemes/abc/approve",
      semanticAction: "execution.form.approve",
    });

    expect(token).toEqual({ sig_token: "sig-tok-1", expires_in: 60 });
    const [url, init] = fetchSpy.mock.calls[0];
    expect(new URL(url, "http://localhost").pathname).toBe(
      "/api/v1/auth/signature-verification"
    );
    expect(init.method).toBe("POST");
    expect(JSON.parse(init.body)).toEqual({
      password: "Password-123!",
      totp: "654321",
      username: "stat.approver",
      action: "/api/v1/execution/randomization/schemes/abc/approve",
      semantic_action: "execution.form.approve",
    });
  });

  it("lists, drafts, and sign-approves randomization schemes", async () => {
    const scheme = {
      id: "scheme-1",
      study_id: "CAD-101",
      version: 1,
      method: "permuted_block",
      status: "DRAFT",
      arms: { ACTIVE: 1, PLACEBO: 1 },
      stratification_factors: { AGE: ["ADULT", "SENIOR"] },
      block_sizes: [4],
      slots_per_stratum: 16,
      preferred_arm_probability: 0.8,
      requires_kit: true,
      list_size: 32,
      configuration_hash: "sha256:123",
      drafted_by: "stat.author",
      drafted_at: isoSample,
    };
    fetchSpy
      .mockResolvedValueOnce(jsonResponse(200, [scheme]))
      .mockResolvedValueOnce(jsonResponse(201, scheme))
      .mockResolvedValueOnce(
        jsonResponse(200, { ...scheme, status: "APPROVED" })
      );

    expect(await executionApi.listRandomizationSchemes("CAD-101")).toEqual([
      scheme,
    ]);
    expect(
      new URL(fetchSpy.mock.calls[0][0].url).pathname +
        new URL(fetchSpy.mock.calls[0][0].url).search
    ).toBe("/api/v1/execution/randomization/schemes?study_id=CAD-101");

    await executionApi.draftRandomizationScheme(
      {
        study_id: "CAD-101",
        method: "permuted_block",
        arms: { ACTIVE: 1, PLACEBO: 1 },
        requires_kit: true,
      },
      "Initial randomization design"
    );
    const draftReq = fetchSpy.mock.calls[1][0];
    expect(draftReq.method).toBe("POST");
    expect(new URL(draftReq.url).pathname).toBe(
      "/api/v1/execution/randomization/schemes"
    );
    expect(draftReq.headers.get("X-Change-Reason")).toBe(
      "Initial randomization design"
    );

    const approved = await executionApi.approveRandomizationScheme("scheme-1", {
      reason: "Biostatistics sign-off",
      sigToken: "sig-tok-approve",
    });
    expect(approved.status).toBe("APPROVED");
    const approveReq = fetchSpy.mock.calls[2][0];
    expect(approveReq.method).toBe("POST");
    expect(new URL(approveReq.url).pathname).toBe(
      "/api/v1/execution/randomization/schemes/scheme-1/approve"
    );
    expect(approveReq.headers.get("X-Sig-Token")).toBe("sig-tok-approve");
    expect(approveReq.headers.get("X-Change-Reason")).toBe(
      "Biostatistics sign-off"
    );
  });

  it("randomizes a participant with X-Sig-Token and stratification factors", async () => {
    const allocation = {
      randomization_id: "rand-1",
      subject_uuid: "s-uuid-1",
      subject_id: "017-0001",
      study_id: "CAD-101",
      site_id: "017",
      scheme_id: "scheme-1",
      scheme_version: 1,
      stratum_key: "AGE=ADULT",
      kit_number: "KIT-0101",
      status: "RANDOMIZED",
      randomized_at: isoSample,
    };
    fetchSpy.mockResolvedValueOnce(jsonResponse(200, allocation));

    const result = await executionApi.randomizeSubject("s-uuid-1", {
      stratificationFactors: { AGE: "ADULT" },
      reason: "Eligible and consented",
      sigToken: "sig-tok-rand",
    });

    expect(result).toEqual(allocation);
    const req = fetchSpy.mock.calls[0][0];
    expect(req.method).toBe("POST");
    expect(new URL(req.url).pathname).toBe(
      "/api/v1/execution/subjects/s-uuid-1/randomize"
    );
    expect(req.headers.get("X-Sig-Token")).toBe("sig-tok-rand");
    expect(req.headers.get("X-Change-Reason")).toBe("Eligible and consented");
    expect(await req.json()).toEqual({
      stratification_factors: { AGE: "ADULT" },
    });
  });

  it("records informed consent, loads blind-aware subject detail, and seeds IP kits", async () => {
    const consentResp = {
      consent_id: "c-1",
      subject_uuid: "s-uuid-1",
      study_id: "CAD-101",
      version_tag: "1.0",
      version_index: 1,
      icf_signed: true,
      icf_signed_date: isoSample,
      requires_reconsent: false,
      is_paper_override: false,
      recorded_at: isoSample,
    };
    const detailResp = {
      id: "s-uuid-1",
      subject_id: "017-0001",
      study_id: "CAD-101",
      site_id: "017",
      status: "RANDOMIZED",
      active_protocol_version: "1.0",
      enrollment_index: 1,
      treatment_group: null,
      investigational_product_id: null,
    };
    fetchSpy
      .mockResolvedValueOnce(jsonResponse(200, consentResp))
      .mockResolvedValueOnce(jsonResponse(200, detailResp))
      .mockResolvedValueOnce(jsonResponse(200, []));

    await executionApi.recordSubjectConsent(
      "s-uuid-1",
      {
        protocol_version: {
          study_id: "CAD-101",
          version_tag: "1.0",
          version_index: 1,
        },
        icf_signed: true,
        icf_signed_date: isoSample,
      },
      "Signed ICF on file"
    );
    const consentReq = fetchSpy.mock.calls[0][0];
    expect(consentReq.method).toBe("POST");
    expect(new URL(consentReq.url).pathname).toBe(
      "/api/v1/execution/subjects/s-uuid-1/consent"
    );
    expect(consentReq.headers.get("X-Change-Reason")).toBe(
      "Signed ICF on file"
    );

    const detail = await executionApi.getSubjectDetail("017-0001");
    expect(detail).toEqual(detailResp);
    expect(new URL(fetchSpy.mock.calls[1][0].url).pathname).toBe(
      "/api/v1/execution/subjects/017-0001"
    );

    await executionApi.seedIpKitInventory(
      {
        study_id: "CAD-101",
        site_id: "017",
        kits: [
          {
            kit_number: "KIT-0101",
            lot_number: "LOT-A1",
            expiration_date: isoSample,
            treatment_arm: "ACTIVE",
          },
        ],
      },
      "Initial site stock"
    );
    const seedReq = fetchSpy.mock.calls[2][0];
    expect(seedReq.method).toBe("POST");
    expect(new URL(seedReq.url).pathname).toBe(
      "/api/v1/execution/rtsm/kits/seed-inventory"
    );
    expect(seedReq.headers.get("X-Change-Reason")).toBe("Initial site stock");
  });
});
