import { describe, it, expect, vi, beforeEach } from "vitest";
import { ComplianceSDK } from "../sdk.ts";

describe("ComplianceSDK TS Wrapper Client", () => {
  let sdk: ComplianceSDK;

  beforeEach(() => {
    sdk = new ComplianceSDK("http://localhost:8000");
    vi.restoreAllMocks();
  });

  describe("serialize (RFC 8785)", () => {
    it("serializes nested user payloads into deterministic key-sorted JSON", () => {
      const payload = { b: 2, a: 1, c: { y: "z", x: "w" } };
      const expected = '{"a":1,"b":2,"c":{"x":"w","y":"z"}}';
      expect(sdk.serialize(payload)).toBe(expected);
    });

    it("handles Date objects and undefined values correctly", () => {
      const d = new Date("2026-08-03T12:00:00.000Z");
      const payload = { timestamp: d, value: undefined, list: [1, undefined, 3] };
      const expected = '{"list":[1,null,3],"timestamp":"2026-08-03T12:00:00.000Z"}';
      expect(sdk.serialize(payload)).toBe(expected);
    });
  });

  describe("generateSignature", () => {
    it("delegates PKCS#7 generation to the backend signatures/sign endpoint", async () => {
      const payload = { test: "data" };
      const mockSignedData = "PEM_ENCODED_SIGNATURE_BLOCK";

      const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue({
        ok: true,
        json: async () => ({ signed_data: mockSignedData }),
      } as Response);

      const signature = await sdk.generateSignature(payload);

      expect(fetchSpy).toHaveBeenCalledWith(
        "http://localhost:8000/api/v1/execution/signatures/sign",
        {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
          },
          body: JSON.stringify({ data: '{"test":"data"}' }),
        }
      );
      expect(signature).toBe(mockSignedData);
    });

    it("throws an error if the backend sign response is not OK", async () => {
      vi.spyOn(globalThis, "fetch").mockResolvedValue({
        ok: false,
        statusText: "Internal Server Error",
      } as Response);

      await expect(sdk.generateSignature({ test: "data" })).rejects.toThrow(
        "Backend signature generation failed: Internal Server Error"
      );
    });
  });

  describe("verifySignature", () => {
    it("correctly queries the backend signatures/verify endpoint", async () => {
      const signedData = "PEM_ENCODED_SIGNATURE_BLOCK";
      const mockVerifyResult = { is_valid: true, status: "VALID" };

      const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue({
        ok: true,
        json: async () => mockVerifyResult,
      } as Response);

      const result = await sdk.verifySignature(signedData);

      expect(fetchSpy).toHaveBeenCalledWith(
        "http://localhost:8000/api/v1/execution/signatures/verify",
        {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
          },
          body: JSON.stringify({ signed_data: signedData }),
        }
      );
      expect(result).toEqual(mockVerifyResult);
    });

    it("returns BACKEND_ERROR if backend query fails", async () => {
      vi.spyOn(globalThis, "fetch").mockResolvedValue({
        ok: false,
        statusText: "Service Unavailable",
      } as Response);

      const result = await sdk.verifySignature("PEM_BLOCK");
      expect(result.is_valid).toBe(false);
      expect(result.status).toBe("BACKEND_ERROR");
      expect(result.failure_reason).toContain("Service Unavailable");
    });

    it("fails and blocks client-side bypass attempts immediately without calling backend", async () => {
      const fetchSpy = vi.spyOn(globalThis, "fetch");

      const result = await sdk.verifySignature("PEM_BLOCK", true);

      expect(fetchSpy).not.toHaveBeenCalled();
      expect(result.is_valid).toBe(false);
      expect(result.status).toBe("BYPASS_ATTEMPT_BLOCKED");
      expect(result.failure_reason).toContain("Client-side validation bypass");
    });
  });
});
