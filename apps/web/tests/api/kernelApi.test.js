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

describe("isMasked", () => {
  it("is true only when the kernel names a mask reason", () => {
    expect(isMasked({ kind: "study_site", ref: null, mask: "Blinded" })).toBe(
      true
    );
    expect(isMasked({ kind: "study", ref: STUDY, mask: null })).toBe(false);
  });
});
