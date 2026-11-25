import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { encodeChangeReason } from "../../src/api/changeReason";
import { apiClient } from "../../src/api/apiClient";
import { createOpenApiClient } from "../../src/api/client";
import { openApiClient } from "../../src/api/legacyClient";

// The same values the .NET ChangeReasonHeader tests decode (ADR-2495, #4895).
const EM_DASH_AND_CJK = "IRB approval — 治験審査委員会の承認";
const EM_DASH_AND_CJK_ENCODED =
  "UTF-8''IRB%20approval%20%E2%80%94%20%E6%B2%BB%E9%A8%93%E5%AF%A9%E6%9F%BB%E5%A7%94%E5%93%A1%E4%BC%9A%E3%81%AE%E6%89%BF%E8%AA%8D";

describe("encodeChangeReason (#4895)", () => {
  it("leaves a printable ASCII reason unchanged", () => {
    expect(encodeChangeReason("Corrected transcription error (5% off)")).toBe(
      "Corrected transcription error (5% off)"
    );
  });

  it("percent-encodes a reason with an em dash and CJK as RFC 8187 UTF-8", () => {
    expect(encodeChangeReason(EM_DASH_AND_CJK)).toBe(EM_DASH_AND_CJK_ENCODED);
  });

  it("encodes Latin-1 letters too, so the server never guesses a charset", () => {
    expect(encodeChangeReason("Réponse")).toBe("UTF-8''R%C3%A9ponse");
  });

  it("escapes the characters encodeURIComponent leaves but RFC 8187 forbids", () => {
    expect(encodeChangeReason("it’s (re)done*")).toBe(
      "UTF-8''it%E2%80%99s%20%28re%29done%2A"
    );
  });

  it("encodes a plain reason that would read as the extended form", () => {
    expect(encodeChangeReason("utf-8''abc")).toBe("UTF-8''utf-8%27%27abc");
  });

  it("encodes line breaks, which a header cannot carry raw", () => {
    expect(encodeChangeReason("line one\nline two")).toBe(
      "UTF-8''line%20one%0Aline%20two"
    );
  });

  it("always produces a value fetch accepts as a header", () => {
    expect(() =>
      new Headers().set("X-Change-Reason", encodeChangeReason(EM_DASH_AND_CJK))
    ).not.toThrow();
  });
});

describe("clients send an encoded reason for change (#4895)", () => {
  let fetchSpy;

  beforeEach(() => {
    setActivePinia(createPinia());
    fetchSpy = vi
      .spyOn(globalThis, "fetch")
      .mockImplementation(async () => new Response("{}", { status: 200 }));
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  it("apiClient encodes the changeReason option", async () => {
    await apiClient.post(
      "/api/v1/mutations",
      {},
      { changeReason: EM_DASH_AND_CJK }
    );

    const [, init] = fetchSpy.mock.calls[0];
    expect(new Headers(init.headers).get("X-Change-Reason")).toBe(
      EM_DASH_AND_CJK_ENCODED
    );
  });

  it("openApiClient's middleware encodes the changeReason option", async () => {
    await openApiClient.POST("/api/v1/mutations", {
      body: {},
      changeReason: EM_DASH_AND_CJK,
    });

    const [request] = fetchSpy.mock.calls[0];
    expect(request.headers.get("X-Change-Reason")).toBe(
      EM_DASH_AND_CJK_ENCODED
    );
  });

  it("createOpenApiClient buffers POST bodies when VITE_API_BASE_URL changes after initialization", async () => {
    const previousBaseUrl = import.meta.env.VITE_API_BASE_URL;
    try {
      delete import.meta.env.VITE_API_BASE_URL;
      const dynamicClient = createOpenApiClient();
      await dynamicClient.GET("/api/v1/mutations");

      import.meta.env.VITE_API_BASE_URL = "http://localhost:3000";
      await dynamicClient.POST("/api/v1/mutations", {
        body: { value: 128, unit: "mm[Hg]" },
        changeReason: EM_DASH_AND_CJK,
      });

      const [request] = fetchSpy.mock.calls[1];
      expect(request.url).toBe("http://localhost:3000/api/v1/mutations");
      expect(request.method).toBe("POST");
      expect(request.headers.get("X-Change-Reason")).toBe(
        EM_DASH_AND_CJK_ENCODED
      );
      expect(await request.json()).toEqual({ value: 128, unit: "mm[Hg]" });
    } finally {
      if (previousBaseUrl === undefined) {
        delete import.meta.env.VITE_API_BASE_URL;
      } else {
        import.meta.env.VITE_API_BASE_URL = previousBaseUrl;
      }
    }
  });
});
