import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { isMasked, kernelApi } from "../../src/api/kernelApi";
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
});

describe("isMasked", () => {
  it("is true only when the kernel names a mask reason", () => {
    expect(isMasked({ kind: "study_site", ref: null, mask: "Blinded" })).toBe(
      true
    );
    expect(isMasked({ kind: "study", ref: STUDY, mask: null })).toBe(false);
  });
});
