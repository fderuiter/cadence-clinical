import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import {
  ASSERTION_SCOPE_NOT_READY,
  describeAssertionError,
  describeLockError,
  extractKernelExplanation,
  isMasked,
  kernelApi,
  kernelEntitlementsApi,
  kernelLocksApi,
  kernelNavigationApi,
  kernelObservationsApi,
  kernelProposalsApi,
  kernelStudiesApi,
  kernelValidationApi,
  kernelWorkQueuesApi,
} from "../../src/api/kernelApi";
import { kernelEntitlementsApi as directEntitlementsSlice } from "../../src/api/kernel/entitlements";
import { kernelLocksApi as directLocksSlice } from "../../src/api/kernel/locks";
import { kernelNavigationApi as directNavigationSlice } from "../../src/api/kernel/navigation";
import { kernelObservationsApi as directObservationsSlice } from "../../src/api/kernel/observations";
import { kernelProposalsApi as directProposalsSlice } from "../../src/api/kernel/proposals";
import { kernelStudiesApi as directStudiesSlice } from "../../src/api/kernel/studies";
import { kernelValidationApi as directValidationSlice } from "../../src/api/kernel/validation";
import { kernelWorkQueuesApi as directWorkQueuesSlice } from "../../src/api/kernel/workQueues";
import { ApiError } from "../../src/api/client";

const STUDY = "study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e@3";
const VERSION = "protocol_version:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6f";

function jsonResponse(status, body, contentType = "application/json") {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": contentType },
  });
}

describe("kernelApi (ADR-2540)", () => {
  let fetchSpy;

  beforeEach(() => {
    setActivePinia(createPinia());
    fetchSpy = vi.spyOn(globalThis, "fetch");
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  function lastRequest() {
    const request = fetchSpy.mock.calls.at(-1)[0];
    return { request, url: new URL(request.url) };
  }

  it.each([
    ["getNode", `/api/v1/kernel/nodes/${encodeURIComponent(STUDY)}`],
    ["getZoomPath", `/api/v1/kernel/nodes/${encodeURIComponent(STUDY)}/path`],
    [
      "getChildren",
      `/api/v1/kernel/nodes/${encodeURIComponent(STUDY)}/children`,
    ],
    [
      "getCommands",
      `/api/v1/kernel/nodes/${encodeURIComponent(STUDY)}/commands`,
    ],
    [
      "listProtocolVersions",
      `/api/v1/kernel/studies/${encodeURIComponent(STUDY)}/protocol-versions`,
    ],
  ])("%s reads the object named by its NodeRef", async (method, path) => {
    fetchSpy.mockResolvedValueOnce(jsonResponse(200, []));

    await kernelApi[method](STUDY);

    const { request, url } = lastRequest();
    expect(request.method).toBe("GET");
    expect(url.pathname).toBe(path);
    expect(url.search).toBe("");
  });

  it("reads one protocol version", async () => {
    const version = { ref: VERSION, number: 2, status: "Draft", mask: null };
    fetchSpy.mockResolvedValueOnce(jsonResponse(200, version));

    await expect(kernelApi.getProtocolVersion(VERSION)).resolves.toEqual(
      version
    );
    expect(lastRequest().url.pathname).toBe(
      `/api/v1/kernel/protocol-versions/${encodeURIComponent(VERSION)}`
    );
  });

  it("asks CanI with the capability token and the resource only", async () => {
    const decision = { capability: "protocol.approve", permitted: false };
    fetchSpy.mockResolvedValueOnce(jsonResponse(200, decision));

    const result = await kernelApi.canI("protocol.approve", VERSION);

    expect(result).toEqual(decision);
    const { url } = lastRequest();
    expect(url.pathname).toBe("/api/v1/kernel/authorization/can-i");
    expect(Object.fromEntries(url.searchParams)).toEqual({
      capability: "protocol.approve",
      resource: VERSION,
    });
  });

  it("searches with the text, each kind and the limit", async () => {
    fetchSpy.mockResolvedValueOnce(jsonResponse(200, []));

    await kernelApi.search({
      text: "CAD 101",
      kinds: ["study", "site"],
      limit: 5,
    });

    const { url } = lastRequest();
    expect(url.pathname).toBe("/api/v1/kernel/search");
    expect(url.searchParams.get("q")).toBe("CAD 101");
    expect(url.searchParams.getAll("kind")).toEqual(["study", "site"]);
    expect(url.searchParams.get("limit")).toBe("5");
  });

  it("sends no kind when none is asked for", async () => {
    fetchSpy.mockResolvedValueOnce(jsonResponse(200, []));

    await kernelApi.search({ text: "CAD", kinds: [] });

    expect(lastRequest().url.searchParams.has("kind")).toBe(false);
  });

  it("looks up an enrollment by study code, subject code, and optional site code", async () => {
    const found = {
      study_ref: STUDY,
      study_code: "CAD-101",
      study_title: "Study",
      study_site_ref: "study_site:0192f3c4-5e6a-7b8c-9d0e-00000000c002@1",
      site_code: "017",
      site_title: "Site 017",
      enrollment_ref: "enrollment:0192f3c4-5e6a-7b8c-9d0e-00000000c004@2",
      subject_code: "017-0001",
      status: "Enrolled",
    };
    fetchSpy.mockResolvedValueOnce(jsonResponse(200, found));

    await expect(
      kernelApi.lookupEnrollment({
        studyCode: "CAD-101",
        subjectCode: "017-0001",
        siteCode: "017",
      })
    ).resolves.toEqual(found);

    let { request, url } = lastRequest();
    expect(request.method).toBe("GET");
    expect(url.pathname).toBe("/api/v1/kernel/enrollments/lookup");
    expect(Object.fromEntries(url.searchParams)).toEqual({
      study_code: "CAD-101",
      subject_code: "017-0001",
      site_code: "017",
    });

    fetchSpy.mockResolvedValueOnce(jsonResponse(200, found));
    await kernelApi.lookupEnrollment({
      studyCode: "CAD-101",
      subjectCode: "017-0001",
      siteCode: null,
    });
    ({ url } = lastRequest());
    expect(Object.fromEntries(url.searchParams)).toEqual({
      study_code: "CAD-101",
      subject_code: "017-0001",
    });
  });

  it("traces backward by default and sends nothing it wasn't given", async () => {
    fetchSpy.mockResolvedValueOnce(jsonResponse(200, { nodes: [], edges: [] }));

    await kernelApi.trace(STUDY);

    const { url } = lastRequest();
    expect(url.pathname).toBe(
      `/api/v1/kernel/nodes/${encodeURIComponent(STUDY)}/lineage`
    );
    expect(url.search).toBe("");
  });

  it("traces with the direction, depth, each edge kind and both times", async () => {
    fetchSpy.mockResolvedValueOnce(jsonResponse(200, { nodes: [], edges: [] }));

    await kernelApi.trace(STUDY, {
      direction: "Forward",
      depth: 3,
      edgeKinds: ["DerivedFrom", "GovernedBy"],
      validAt: "2026-06-01T00:00:00Z",
      knownAt: "2026-07-01T00:00:00Z",
    });

    const { url } = lastRequest();
    expect(url.searchParams.get("direction")).toBe("Forward");
    expect(url.searchParams.get("depth")).toBe("3");
    expect(url.searchParams.getAll("edge_kind")).toEqual([
      "DerivedFrom",
      "GovernedBy",
    ]);
    expect(url.searchParams.get("valid_at")).toBe("2026-06-01T00:00:00Z");
    expect(url.searchParams.get("known_at")).toBe("2026-07-01T00:00:00Z");
  });

  it("reads an object's dependents, with no edge kind when none is asked for", async () => {
    const report = { start: STUDY, groups: [], total: 0, truncated: false };
    fetchSpy.mockResolvedValueOnce(jsonResponse(200, report));

    await expect(
      kernelApi.dependents(STUDY, { depth: 5, edgeKinds: [] })
    ).resolves.toEqual(report);

    const { url } = lastRequest();
    expect(url.pathname).toBe(
      `/api/v1/kernel/nodes/${encodeURIComponent(STUDY)}/dependents`
    );
    expect(Object.fromEntries(url.searchParams)).toEqual({ depth: "5" });
  });

  it("reads a protocol amendment's impact", async () => {
    fetchSpy.mockResolvedValueOnce(jsonResponse(200, { amended: VERSION }));

    await kernelApi.amendmentImpact(VERSION, {
      validAt: "2026-06-01T00:00:00Z",
    });

    const { url } = lastRequest();
    expect(url.pathname).toBe(
      `/api/v1/kernel/protocol-versions/${encodeURIComponent(VERSION)}/impact`
    );
    expect(Object.fromEntries(url.searchParams)).toEqual({
      valid_at: "2026-06-01T00:00:00Z",
    });
  });

  it("reads a scope's activity with its filters, and continues from a cursor", async () => {
    const page = { entries: [], next: null };
    fetchSpy.mockResolvedValueOnce(jsonResponse(200, page));

    await expect(
      kernelApi.activity({
        scope: STUDY,
        families: ["Execution", "Quality"],
        actor: "crc@site.test",
        before: "AZ2x_cursor",
        limit: 20,
      })
    ).resolves.toEqual(page);

    const { url } = lastRequest();
    expect(url.pathname).toBe("/api/v1/kernel/activity");
    expect(url.searchParams.get("scope")).toBe(STUDY);
    expect(url.searchParams.getAll("family")).toEqual(["Execution", "Quality"]);
    expect(url.searchParams.get("actor")).toBe("crc@site.test");
    expect(url.searchParams.get("before")).toBe("AZ2x_cursor");
    expect(url.searchParams.get("limit")).toBe("20");
    expect(url.searchParams.has("from")).toBe(false);
  });

  it("reads the whole tenant's activity when no scope is named", async () => {
    fetchSpy.mockResolvedValueOnce(
      jsonResponse(200, { entries: [], next: null })
    );

    await kernelApi.activity();

    const { url } = lastRequest();
    expect(url.pathname).toBe("/api/v1/kernel/activity");
    expect([...url.searchParams.keys()]).toEqual([]);
  });

  it.each([
    [403, "Your sign-in's tenant is not an active kernel tenant."],
    [404, "study:x does not exist in your tenant."],
  ])(
    "raises the host's %i problem as an ApiError with its detail",
    async (status, detail) => {
      fetchSpy.mockResolvedValueOnce(
        jsonResponse(
          status,
          { title: "Refused", detail },
          "application/problem+json"
        )
      );

      const error = await kernelApi.getNode(STUDY).catch((e) => e);

      expect(error).toBeInstanceOf(ApiError);
      expect(error.status).toBe(status);
      expect(error.message).toBe(detail);
    }
  );
});

describe("kernelApi observations (ADR-2620)", () => {
  const OBSERVATION = "observation:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d70";
  const ENROLLMENT = "enrollment:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d71";
  let fetchSpy;

  beforeEach(() => {
    setActivePinia(createPinia());
    fetchSpy = vi.spyOn(globalThis, "fetch");
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  it("reads the observation without its version suffix", async () => {
    fetchSpy.mockResolvedValueOnce(jsonResponse(200, { ref: OBSERVATION }));

    await kernelApi.getObservation(`${OBSERVATION}@2`);

    const request = fetchSpy.mock.calls.at(-1)[0];
    expect(request.method).toBe("GET");
    expect(new URL(request.url).pathname).toBe(
      `/api/v1/kernel/observations/${encodeURIComponent(OBSERVATION)}`
    );
  });

  it("sends a correction with the expected version, the encoded reason and the idempotency key", async () => {
    const state = { ref: `${OBSERVATION}@2`, value: "128", version: 2 };
    fetchSpy.mockResolvedValueOnce(jsonResponse(200, state));

    const result = await kernelApi.correctObservation({
      observationRef: `${OBSERVATION}@1`,
      enrollmentRef: ENROLLMENT,
      value: { type: "quantity", value: "128", unit: "mm[Hg]" },
      expectedVersion: 1,
      reason: "Transcription error – source reads 128",
      idempotencyKey: "key-1",
    });

    expect(result).toEqual(state);
    const request = fetchSpy.mock.calls.at(-1)[0];
    expect(request.method).toBe("POST");
    expect(new URL(request.url).pathname).toBe(
      `/api/v1/kernel/observations/${encodeURIComponent(OBSERVATION)}/corrections`
    );
    expect(request.headers.get("Idempotency-Key")).toBe("key-1");
    expect(request.headers.get("X-Change-Reason")).toBe(
      "UTF-8''Transcription%20error%20%E2%80%93%20source%20reads%20128"
    );
    expect(await request.json()).toEqual({
      enrollment_ref: ENROLLMENT,
      value: { type: "quantity", value: "128", unit: "mm[Hg]" },
      expected_version: 1,
    });
  });

  it.each([409, 423])(
    "rejects a refused correction (%i) with its status",
    async (status) => {
      fetchSpy.mockResolvedValueOnce(
        jsonResponse(
          status,
          { title: "K.Observation", detail: "refused", code: "K.Observation" },
          "application/problem+json"
        )
      );

      const error = await kernelApi
        .correctObservation({
          observationRef: OBSERVATION,
          enrollmentRef: ENROLLMENT,
          value: { type: "quantity", value: "128", unit: "mm[Hg]" },
          expectedVersion: 1,
          reason: "Transcription error",
          idempotencyKey: "key-2",
        })
        .catch((e) => e);

      expect(error).toBeInstanceOf(ApiError);
      expect(error.status).toBe(status);
    }
  );

  it("sends source verification and assertion retraction with encoded reason and idempotency key", async () => {
    const ASSERTION = "assertion:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d75";
    const verified = {
      ref: ASSERTION,
      kind: "Verified",
      target: OBSERVATION,
      revision: 2,
      retracts: null,
      asserted_at: "2027-03-14T10:00:00Z",
      asserted_by: "cra@example.test",
    };
    fetchSpy.mockResolvedValueOnce(jsonResponse(201, verified));

    await expect(
      kernelApi.verifyObservation({
        observationRef: `${OBSERVATION}@2`,
        enrollmentRef: ENROLLMENT,
        revision: 2,
        reason: "Verified against inpatient chart",
        idempotencyKey: "sdv-1",
      })
    ).resolves.toEqual(verified);

    let request = fetchSpy.mock.calls.at(-1)[0];
    expect(request.method).toBe("POST");
    expect(new URL(request.url).pathname).toBe(
      `/api/v1/kernel/observations/${encodeURIComponent(OBSERVATION)}/verifications`
    );
    expect(request.headers.get("Idempotency-Key")).toBe("sdv-1");
    expect(request.headers.get("X-Change-Reason")).toBe(
      "Verified against inpatient chart"
    );
    expect(await request.json()).toEqual({
      enrollment_ref: ENROLLMENT,
      revision: 2,
    });

    const retracted = { ...verified, retracts: ASSERTION };
    fetchSpy.mockResolvedValueOnce(jsonResponse(200, retracted));

    await expect(
      kernelApi.retractAssertion({
        assertionRef: `${ASSERTION}@1`,
        enrollmentRef: ENROLLMENT,
        reason: "Withdrawn after source addendum",
        idempotencyKey: "retract-1",
      })
    ).resolves.toEqual(retracted);

    request = fetchSpy.mock.calls.at(-1)[0];
    expect(new URL(request.url).pathname).toBe(
      `/api/v1/kernel/assertions/${encodeURIComponent(ASSERTION)}/retraction`
    );
    expect(request.headers.get("Idempotency-Key")).toBe("retract-1");
    expect(await request.json()).toEqual({
      enrollment_ref: ENROLLMENT,
    });
  });

  it.each([
    [409, "K.Assertion.RevisionStale", "value changed"],
    [409, "K.Assertion.AlreadyRetracted", "already withdrawn"],
    [423, "K.Assertion.Locked", "locked"],
    [403, "K.Authorization.Denied", "not authorised"],
  ])("describes assertion error %s %s", (status, code, snippet) => {
    expect(
      describeAssertionError(new ApiError("Refused", status, null, { code }))
    ).toContain(snippet);
  });
});

describe("kernelApi proposals (ADR-2635)", () => {
  const PROPOSAL = "proposal:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d70";
  let fetchSpy;

  beforeEach(() => {
    setActivePinia(createPinia());
    fetchSpy = vi.spyOn(globalThis, "fetch");
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  it("lists a study's proposals with the status and limit asked for", async () => {
    fetchSpy.mockResolvedValueOnce(jsonResponse(200, []));

    await kernelApi.listProposals(STUDY, { status: "Open", limit: 20 });

    const url = new URL(fetchSpy.mock.calls.at(-1)[0].url);
    expect(url.pathname).toBe(
      `/api/v1/kernel/studies/${encodeURIComponent(STUDY)}/proposals`
    );
    expect(url.searchParams.get("status")).toBe("Open");
    expect(url.searchParams.get("limit")).toBe("20");
  });

  it("reads a proposal by its unversioned reference", async () => {
    fetchSpy.mockResolvedValueOnce(jsonResponse(200, { ref: PROPOSAL }));

    await kernelApi.getProposal(`${PROPOSAL}@3`);

    expect(new URL(fetchSpy.mock.calls.at(-1)[0].url).pathname).toBe(
      `/api/v1/kernel/proposals/${encodeURIComponent(PROPOSAL)}`
    );
  });

  it("rejects with the reason for change and the idempotency key as headers", async () => {
    const state = {
      ref: `${PROPOSAL}@2`,
      status: "Rejected",
      content_hash: "sha256:x",
      decided_at: "2026-10-20T10:00:00Z",
      decided_by: "reviewer-1",
    };
    fetchSpy.mockResolvedValueOnce(jsonResponse(200, state));

    const result = await kernelApi.rejectProposal({
      proposalRef: `${PROPOSAL}@1`,
      reason: "Value is in range",
      idempotencyKey: "key-9",
    });

    expect(result).toEqual(state);
    const request = fetchSpy.mock.calls.at(-1)[0];
    expect(request.method).toBe("POST");
    expect(new URL(request.url).pathname).toBe(
      `/api/v1/kernel/proposals/${encodeURIComponent(PROPOSAL)}/rejection`
    );
    expect(request.headers.get("Idempotency-Key")).toBe("key-9");
    expect(request.headers.get("X-Change-Reason")).toBe("Value is in range");
  });
});

describe("isMasked", () => {
  it("is true only when the kernel names a mask reason", () => {
    expect(isMasked({ kind: "study_site", ref: null, mask: "Blinded" })).toBe(
      true
    );
    expect(isMasked({ kind: "study", ref: STUDY, mask: null })).toBe(false);
  });
});

describe("kernelApi simulation and matrix gap check", () => {
  let fetchSpy;

  beforeEach(() => {
    setActivePinia(createPinia());
    fetchSpy = vi.spyOn(globalThis, "fetch");
  });

  afterEach(() => {
    vi.unstubAllEnvs();
    fetchSpy.mockRestore();
  });

  it("sends simulate through the authenticated OpenAPI client honoring dynamic VITE_API_BASE_URL", async () => {
    const { useAuthStore } = await import("../../src/stores/auth");
    useAuthStore().setAuth({
      authenticated: true,
      token: "sim-jwt-token",
      tokenParsed: { sub: "designer-1" },
    });
    vi.stubEnv("VITE_API_BASE_URL", "https://gateway.cadence.test");

    const simulation = {
      scope: STUDY,
      as_of: {
        valid_at: "2027-03-14T10:00:00Z",
        known_at: "2027-03-14T10:00:00Z",
      },
      candidate: {
        kind: "ProtocolVersion",
        version: VERSION,
        base_version: null,
        content_hash: "sha256:c-1",
        base_content_hash: null,
      },
      engine_versions: [],
      input_hash: "sha256:in-1",
      result_hash: "sha256:out-1",
      structural_diff: [],
      schedule_changes: [],
      masked: [],
      dependents: null,
      matrix_gaps: [],
    };
    fetchSpy.mockResolvedValueOnce(jsonResponse(200, simulation));

    const result = await kernelApi.simulate({ storedVersionRef: VERSION });

    expect(result).toEqual(simulation);
    const request = fetchSpy.mock.calls.at(-1)[0];
    const url = new URL(request.url);
    expect(request.method).toBe("POST");
    expect(url.origin).toBe("https://gateway.cadence.test");
    expect(url.pathname).toBe("/api/v1/kernel/simulation/simulate");
    expect(request.headers.get("Authorization")).toBe("Bearer sim-jwt-token");
    expect(await request.json()).toEqual({
      base_version_ref: null,
      stored_version_ref: VERSION,
      candidate_content: null,
      valid_at: null,
      known_at: null,
    });
  });

  it("sends checkMatrixGaps through the authenticated OpenAPI client and surfaces 422 ApiError details", async () => {
    const { useAuthStore } = await import("../../src/stores/auth");
    useAuthStore().setAuth({
      authenticated: true,
      token: "gap-jwt-token",
      tokenParsed: { sub: "designer-1" },
    });

    const findings = [
      {
        element_id: "act.vs",
        kind: "UnboundCollection",
        severity: "Warning",
        json_pointer_path: "/schedule/0",
        description: "Unscheduled vital signs collection",
        recommendation: "Bind VS to Visit 1",
      },
    ];
    fetchSpy.mockResolvedValueOnce(jsonResponse(200, findings));

    await expect(kernelApi.checkMatrixGaps('{"usdm":"3.0"}')).resolves.toEqual(
      findings
    );
    const request = fetchSpy.mock.calls.at(-1)[0];
    expect(request.method).toBe("POST");
    expect(new URL(request.url).pathname).toBe(
      "/api/v1/kernel/protocols/matrix-gap-check"
    );
    expect(request.headers.get("Authorization")).toBe("Bearer gap-jwt-token");
    expect(await request.json()).toEqual({
      protocol_content: '{"usdm":"3.0"}',
    });

    fetchSpy.mockResolvedValueOnce(
      jsonResponse(
        422,
        {
          title: "Unprocessable Entity",
          detail: "Protocol JSON is invalid.",
          code: "K.Simulation.ContentInvalid",
        },
        "application/problem+json"
      )
    );

    const err = await kernelApi
      .simulate({ candidateContent: "bad" })
      .catch((e) => e);
    expect(err).toBeInstanceOf(ApiError);
    expect(err.status).toBe(422);
    expect(err.message).toBe("Protocol JSON is invalid.");
  });
});

describe("kernelApi domain slices", () => {
  it("re-exports each domain slice and composes all slice methods onto kernelApi without collisions", () => {
    expect(kernelNavigationApi).toBe(directNavigationSlice);
    expect(kernelStudiesApi).toBe(directStudiesSlice);
    expect(kernelObservationsApi).toBe(directObservationsSlice);
    expect(kernelProposalsApi).toBe(directProposalsSlice);
    expect(kernelLocksApi).toBe(directLocksSlice);
    expect(kernelValidationApi).toBe(directValidationSlice);
    expect(kernelWorkQueuesApi).toBe(directWorkQueuesSlice);
    expect(kernelEntitlementsApi).toBe(directEntitlementsSlice);

    const slices = [
      directNavigationSlice,
      directStudiesSlice,
      directObservationsSlice,
      directProposalsSlice,
      directLocksSlice,
      directValidationSlice,
      directWorkQueuesSlice,
      directEntitlementsSlice,
    ];
    const seen = new Set();
    for (const slice of slices) {
      const keys = Object.keys(slice);
      expect(keys.length).toBeGreaterThan(0);
      for (const key of keys) {
        expect(seen.has(key)).toBe(false);
        seen.add(key);
        expect(kernelApi[key]).toBe(slice[key]);
      }
    }

    expect(Object.keys(kernelApi).sort()).toEqual([...seen].sort());
  });
});

describe("kernelLocksApi readiness, explanations, and archive exports (#5976)", () => {
  const SITE = "study_site:0192f3c4-5e6a-7b8c-9d0e-00000000c002@1";
  const ENROLLMENT = "enrollment:0192f3c4-5e6a-7b8c-9d0e-00000000c004@2";
  let fetchSpy;

  beforeEach(() => {
    setActivePinia(createPinia());
    fetchSpy = vi.spyOn(globalThis, "fetch");
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  it("queries lock readiness with unversioned study and optional scope refs", async () => {
    const readiness = {
      study_ref: "study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e",
      scope_ref: "study_site:0192f3c4-5e6a-7b8c-9d0e-00000000c002",
      ready_for_lock: false,
      open_query_count: 1,
      missing_required_item_count: 0,
      unverified_revision_count: 2,
      missing_signature_count: 1,
      blockers: [],
    };
    fetchSpy.mockResolvedValueOnce(jsonResponse(200, readiness));

    await expect(kernelApi.getStudyLockReadiness(STUDY, SITE)).resolves.toEqual(
      readiness
    );

    const url = new URL(fetchSpy.mock.calls.at(-1)[0].url);
    expect(url.pathname).toBe(
      "/api/v1/kernel/studies/study%3A0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e/locks/readiness"
    );
    expect(Object.fromEntries(url.searchParams)).toEqual({
      scope_ref: "study_site:0192f3c4-5e6a-7b8c-9d0e-00000000c002",
    });
  });

  it("formats K.Assertion.ScopeNotReady explanations and unmet conditions", () => {
    const error = new ApiError("Scope not ready", 409, null, {
      code: ASSERTION_SCOPE_NOT_READY,
      detail: "Cannot lock study while blockers remain.",
      explanation: {
        claim: "Scope is not ready to lock.",
        conditions: [
          { met: true, description: "Signature verified" },
          { met: false, description: "2 open queries remain" },
        ],
        next_actions: [{ description: "Resolve open queries in Site 017" }],
      },
    });

    expect(extractKernelExplanation(error)?.claim).toBe(
      "Scope is not ready to lock."
    );
    expect(describeLockError(error)).toBe(
      "Cannot lock study while blockers remain. 2 open queries remain Next actions: Resolve open queries in Site 017"
    );
  });

  it("streams study_archive ZIP and certified casebook PDF with matching fallback filenames", async () => {
    fetchSpy.mockResolvedValueOnce(
      new Response(new Uint8Array([0x50, 0x4b]), {
        status: 200,
        headers: {
          "Content-Type": "application/zip",
          "X-Content-Sha256": "abc123",
        },
      })
    );

    const archive = await kernelLocksApi.streamStudyExport({
      studyRef: STUDY,
      format: "study_archive",
    });
    expect(archive.filename).toBe("study-export.study_archive.zip");
    expect(archive.contentSha256).toBe("abc123");

    fetchSpy.mockResolvedValueOnce(
      new Response(new Uint8Array([0x25, 0x50, 0x44, 0x46]), {
        status: 200,
        headers: {
          "Content-Type": "application/pdf",
          "X-Content-Sha256": "def456",
        },
      })
    );

    const pdf = await kernelLocksApi.streamEnrollmentCasebook({
      enrollmentRef: ENROLLMENT,
      format: "pdf",
    });
    expect(pdf.filename).toBe("participant.casebook.pdf");
    expect(pdf.contentSha256).toBe("def456");
  });
});

describe("kernelStudiesApi site and organization setup (ADR-2552)", () => {
  let fetchSpy;

  beforeEach(() => {
    setActivePinia(createPinia());
    fetchSpy = vi.spyOn(globalThis, "fetch");
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  it("creates organizations, registers sites, lists/links study sites, and forwards initial site parameters on createStudy", async () => {
    const org = {
      ref: "organization:0192f3c4-5e6a-7b8c-9d0e-00000000d001",
      code: "INST-017",
      name: "Metro Clinical Center",
      kind: "SiteInstitution",
      mask: null,
    };
    fetchSpy.mockResolvedValueOnce(jsonResponse(201, org));
    await expect(
      kernelStudiesApi.createOrganization({
        name: "Metro Clinical Center",
        code: "INST-017",
        reason: "Register site institution",
        idempotencyKey: "idem-org-1",
      })
    ).resolves.toEqual(org);
    let req = fetchSpy.mock.calls.at(-1)[0];
    expect(new URL(req.url).pathname).toBe("/api/v1/kernel/organizations");
    expect(req.headers.get("Idempotency-Key")).toBe("idem-org-1");
    expect(await req.json()).toEqual({
      name: "Metro Clinical Center",
      code: "INST-017",
      kind: "SiteInstitution",
    });

    const site = {
      ref: "site:0192f3c4-5e6a-7b8c-9d0e-00000000d002",
      site_code: "017",
      name: "Boston Site 017",
      country: "US",
      institution_ref: org.ref,
      institution_code: org.code,
      institution_name: org.name,
      mask: null,
    };
    fetchSpy.mockResolvedValueOnce(jsonResponse(200, [site]));
    await expect(kernelApi.listSites()).resolves.toEqual([site]);
    expect(new URL(fetchSpy.mock.calls.at(-1)[0].url).pathname).toBe(
      "/api/v1/kernel/sites"
    );

    fetchSpy.mockResolvedValueOnce(jsonResponse(201, site));
    await expect(
      kernelStudiesApi.createSite({
        siteCode: "017",
        name: "Boston Site 017",
        country: "US",
        institutionRef: org.ref,
        reason: "Register investigational site",
        idempotencyKey: "idem-site-1",
      })
    ).resolves.toEqual(site);
    req = fetchSpy.mock.calls.at(-1)[0];
    expect(new URL(req.url).pathname).toBe("/api/v1/kernel/sites");
    expect(await req.json()).toEqual({
      site_code: "017",
      name: "Boston Site 017",
      country: "US",
      institution_ref: org.ref,
      institution_code: null,
      institution_name: null,
    });

    const studySite = {
      ref: "study_site:0192f3c4-5e6a-7b8c-9d0e-00000000d003",
      study_ref: STUDY,
      site_ref: site.ref,
      site_code: "017",
      site_name: "Boston Site 017",
      country: "US",
      status: "Active",
      mask: null,
    };
    fetchSpy.mockResolvedValueOnce(jsonResponse(200, [studySite]));
    await expect(kernelApi.listStudySites(STUDY)).resolves.toEqual([studySite]);
    expect(new URL(fetchSpy.mock.calls.at(-1)[0].url).pathname).toBe(
      `/api/v1/kernel/studies/${encodeURIComponent(STUDY)}/sites`
    );

    fetchSpy.mockResolvedValueOnce(jsonResponse(201, studySite));
    await expect(
      kernelStudiesApi.linkSiteToStudy({
        studyRef: STUDY,
        siteCode: "017",
        reason: "Activate site 017 on study",
        idempotencyKey: "idem-link-1",
      })
    ).resolves.toEqual(studySite);
    req = fetchSpy.mock.calls.at(-1)[0];
    expect(new URL(req.url).pathname).toBe(
      `/api/v1/kernel/studies/${encodeURIComponent(STUDY)}/sites`
    );
    expect(await req.json()).toEqual({
      site_ref: null,
      site_code: "017",
    });

    const createdStudy = {
      ref: STUDY,
      study_code: "CAD-201",
      title: "Phase II Trial",
      phase: "Phase II",
      mask: null,
    };
    fetchSpy.mockResolvedValueOnce(jsonResponse(201, createdStudy));
    await expect(
      kernelStudiesApi.createStudy({
        code: "CAD-201",
        title: "Phase II Trial",
        phase: "Phase II",
        linkSandboxSite: true,
        initialSiteCode: "017",
        reason: "Initialize study with starter site",
        idempotencyKey: "idem-study-201",
      })
    ).resolves.toEqual(createdStudy);
    req = fetchSpy.mock.calls.at(-1)[0];
    expect(await req.json()).toEqual({
      study_code: "CAD-201",
      title: "Phase II Trial",
      phase: "Phase II",
      sponsor_ref: null,
      sponsor_code: null,
      sponsor_name: null,
      link_sandbox_site: true,
      initial_site_ref: null,
      initial_site_code: "017",
    });
  });
});
