import {
  describe,
  it,
  expect,
  vi,
  beforeEach,
  afterEach,
  beforeAll,
} from "vitest";
import * as crypto from "crypto";
import { JwksCoalescerService } from "../src/jwks-coalescer.service.js";

// Helper to generate a test RSA key pair and a signed JWT token
function generateKeyAndToken(
  kid: string,
  payload: any
): { jwk: any; token: string } {
  const { publicKey, privateKey } = crypto.generateKeyPairSync("rsa", {
    modulusLength: 2048,
  });

  const jwk = publicKey.export({ format: "jwk" }) as any;
  jwk.kid = kid;
  jwk.use = "sig";
  jwk.alg = "RS256";

  const header = { alg: "RS256", kid, typ: "JWT" };
  const headerB64 = Buffer.from(JSON.stringify(header)).toString("base64url");
  const payloadB64 = Buffer.from(JSON.stringify(payload)).toString("base64url");

  const sign = crypto.createSign("SHA256");
  sign.write(`${headerB64}.${payloadB64}`);
  sign.end();
  const signatureB64 = sign.sign(privateKey).toString("base64url");

  const token = `${headerB64}.${payloadB64}.${signatureB64}`;

  return { jwk, token };
}

describe("JwksCoalescerService", () => {
  let service: JwksCoalescerService;
  const originalFetch = globalThis.fetch;

  beforeAll(async () => {
    // Warm up Node's crypto library / JwksCoalescerService to eliminate cold-start overhead
    try {
      const { jwk, token } = generateKeyAndToken("warmup-kid", {
        sub: "warmup",
        exp: Math.floor(Date.now() / 1000) + 3600,
      });
      const tempService = new JwksCoalescerService();
      tempService.getCachedKeys().set("warmup-kid", jwk);
      for (let i = 0; i < 5; i++) {
        await tempService.verifyToken(token);
      }
    } catch (e) {
      // Ignore warmup errors
    }
  });

  beforeEach(() => {
    service = new JwksCoalescerService();
    // Reset env variables
    delete process.env.SKIP_JWKS_FETCH;
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
    vi.restoreAllMocks();
  });

  describe("Requirement 1: Eager prefetching on startup", () => {
    it("should fetch and cache public keys automatically on module initialization", async () => {
      const { jwk } = generateKeyAndToken("startup-kid", { sub: "user1" });
      const mockFetch = vi.fn().mockResolvedValue({
        ok: true,
        json: async () => ({ keys: [jwk] }),
      });
      globalThis.fetch = mockFetch;

      service.configure({ jwksUrl: "https://idp.example.com/certs" });
      await service.onModuleInit();

      expect(mockFetch).toHaveBeenCalledTimes(1);
      expect(service.getFetchCount()).toBe(1);

      const cachedKeys = service.getCachedKeys();
      expect(cachedKeys.has("startup-kid")).toBe(true);
      expect(cachedKeys.get("startup-kid")).toEqual(jwk);
    });

    it("should skip prefetching if SKIP_JWKS_FETCH environment variable is set to true", async () => {
      process.env.SKIP_JWKS_FETCH = "true";
      const mockFetch = vi.fn();
      globalThis.fetch = mockFetch;

      await service.onModuleInit();

      expect(mockFetch).not.toHaveBeenCalled();
      expect(service.getFetchCount()).toBe(0);
    });
  });

  describe("Requirement 2: Startup resilience", () => {
    it("should complete module initialization successfully even if initial JWKS fetch fails", async () => {
      const mockFetch = vi
        .fn()
        .mockRejectedValue(new Error("Identity provider offline"));
      globalThis.fetch = mockFetch;

      service.configure({ jwksUrl: "https://idp.example.com/certs" });

      // Should NOT throw an error on startup
      await expect(service.onModuleInit()).resolves.not.toThrow();
      expect(service.getFetchCount()).toBe(1);
      expect(service.getCachedKeys().size).toBe(0);
    });
  });

  describe("Requirement 3: Local cache fast-path", () => {
    it("should check the local cache before performing any on-demand network calls", async () => {
      const { jwk, token } = generateKeyAndToken("cached-kid", {
        sub: "user2",
        exp: Math.floor(Date.now() / 1000) + 3600,
      });

      // Seed the cache manually
      service.getCachedKeys().set("cached-kid", jwk);

      const mockFetch = vi.fn();
      globalThis.fetch = mockFetch;

      const startTime = performance.now();
      const payload = await service.verifyToken(token);
      const duration = performance.now() - startTime;

      expect(payload.sub).toBe("user2");
      expect(mockFetch).not.toHaveBeenCalled();
      expect(service.getFetchCount()).toBe(0);

      // Gateway token verification overhead remains under 15 milliseconds for cached keys
      expect(duration).toBeLessThan(15.0);
    });
  });

  describe("Requirement 4: Request coalescing (stampede protection)", () => {
    it("should merge multiple concurrent requests for the same uncached kid into exactly one network call", async () => {
      const { publicKey, privateKey } = crypto.generateKeyPairSync("rsa", {
        modulusLength: 2048,
      });
      const jwk = publicKey.export({ format: "jwk" }) as any;
      jwk.kid = "uncached-kid";
      jwk.use = "sig";
      jwk.alg = "RS256";

      const signToken = (payload: any) => {
        const header = { alg: "RS256", kid: "uncached-kid", typ: "JWT" };
        const headerB64 = Buffer.from(JSON.stringify(header)).toString(
          "base64url"
        );
        const payloadB64 = Buffer.from(JSON.stringify(payload)).toString(
          "base64url"
        );
        const sign = crypto.createSign("SHA256");
        sign.write(`${headerB64}.${payloadB64}`);
        sign.end();
        const signatureB64 = sign.sign(privateKey).toString("base64url");
        return `${headerB64}.${payloadB64}.${signatureB64}`;
      };

      const token1 = signToken({
        sub: "user-a",
        exp: Math.floor(Date.now() / 1000) + 3600,
      });
      const token2 = signToken({
        sub: "user-b",
        exp: Math.floor(Date.now() / 1000) + 3600,
      });

      let fetchTriggered = 0;
      const mockFetch = vi.fn().mockImplementation(async () => {
        fetchTriggered++;
        // Simulate a slight network latency (100ms) to allow concurrent requests to overlap
        await new Promise((resolve) => setTimeout(resolve, 100));
        return {
          ok: true,
          json: async () => ({ keys: [jwk] }),
        };
      });
      globalThis.fetch = mockFetch;

      service.configure({ jwksUrl: "https://idp.example.com/certs" });

      // Fire multiple concurrent verifications for the same uncached kid
      const promises = [
        service.verifyToken(token1),
        service.verifyToken(token2),
        service.verifyToken(token1),
      ];

      const results = await Promise.all(promises);

      // Verify payloads are correct
      expect(results[0].sub).toBe("user-a");
      expect(results[1].sub).toBe("user-b");
      expect(results[2].sub).toBe("user-a");

      // Verify only exactly ONE network call was initiated
      expect(mockFetch).toHaveBeenCalledTimes(1);
      expect(service.getFetchCount()).toBe(1);
      expect(fetchTriggered).toBe(1);
    });

    it("should coalesce multiple concurrent requests with unknown key IDs into exactly one outbound fetch request", async () => {
      let fetchCount = 0;
      const mockFetch = vi.fn().mockImplementation(async () => {
        fetchCount++;
        await new Promise((resolve) => setTimeout(resolve, 100));
        return {
          ok: true,
          json: async () => ({ keys: [] }),
        };
      });
      globalThis.fetch = mockFetch;

      service.configure({ jwksUrl: "https://idp.example.com/certs" });

      // Fire multiple concurrent lookups for DIFFERENT unknown key IDs
      const promises = [
        service.getKey("unknown-kid-1").catch((e) => e),
        service.getKey("unknown-kid-2").catch((e) => e),
        service.getKey("unknown-kid-3").catch((e) => e),
      ];

      const results = await Promise.all(promises);

      // All requests should fail with key not found
      results.forEach((res) => {
        expect(res).toBeInstanceOf(Error);
        expect(res.message).toContain("not found in retrieved JWKS keys");
      });

      // Exactly ONE network call was made
      expect(mockFetch).toHaveBeenCalledTimes(1);
      expect(service.getFetchCount()).toBe(1);
      expect(fetchCount).toBe(1);

      // All three unknown key IDs should now be in negative cache
      expect(service.getNegativeCacheSize()).toBe(3);
      expect(service.getNegativeCache().has("unknown-kid-1")).toBe(true);
      expect(service.getNegativeCache().has("unknown-kid-2")).toBe(true);
      expect(service.getNegativeCache().has("unknown-kid-3")).toBe(true);
    });
  });

  describe("Requirement 2 & Acceptance Criteria: Short-Lived Negative Cache", () => {
    it("should trigger zero outbound network calls on repeated invalid key ID lookups within 10 seconds", async () => {
      const mockFetch = vi.fn().mockResolvedValue({
        ok: true,
        json: async () => ({ keys: [] }),
      });
      globalThis.fetch = mockFetch;

      service.configure({
        jwksUrl: "https://idp.example.com/certs",
        negativeCacheTtlMs: 10000,
      });

      // Initial lookup triggers fetch
      await expect(service.getKey("invalid-kid-x")).rejects.toThrow(
        'Key ID "invalid-kid-x" not found in retrieved JWKS keys.'
      );
      expect(mockFetch).toHaveBeenCalledTimes(1);
      expect(service.getFetchCount()).toBe(1);

      // Subsequent sequential lookups within 10 seconds should hit negative cache and NOT trigger any fetch
      for (let i = 0; i < 5; i++) {
        await expect(service.getKey("invalid-kid-x")).rejects.toThrow(
          'Key ID "invalid-kid-x" not found in retrieved JWKS keys.'
        );
      }

      expect(mockFetch).toHaveBeenCalledTimes(1);
      expect(service.getFetchCount()).toBe(1);
    });

    it("should expire negative cache entries after 10 seconds and trigger a new fetch", async () => {
      const mockFetch = vi.fn().mockResolvedValue({
        ok: true,
        json: async () => ({ keys: [] }),
      });
      globalThis.fetch = mockFetch;

      service.configure({
        jwksUrl: "https://idp.example.com/certs",
        negativeCacheTtlMs: 10000,
      });

      // 1. Initial lookup - fails and caches negatively
      await expect(service.getKey("invalid-kid-ttl")).rejects.toThrow();
      expect(service.getFetchCount()).toBe(1);

      // 2. Immediate repeat lookup - negative cache hit
      await expect(service.getKey("invalid-kid-ttl")).rejects.toThrow();
      expect(service.getFetchCount()).toBe(1);

      // 3. Fast-forward system time past 10 seconds (10001 ms)
      const now = Date.now();
      const dateSpy = vi.spyOn(Date, "now").mockReturnValue(now + 10005);

      // 4. Repeat lookup after TTL expiration triggers a new fetch
      await expect(service.getKey("invalid-kid-ttl")).rejects.toThrow();
      expect(service.getFetchCount()).toBe(2);

      dateSpy.mockRestore();
    });

    it("should enforce maximum capacity limit for negative cache and evict oldest entries", async () => {
      const mockFetch = vi.fn().mockResolvedValue({
        ok: true,
        json: async () => ({ keys: [] }),
      });
      globalThis.fetch = mockFetch;

      // Configure a small max capacity of 3
      service.configure({
        jwksUrl: "https://idp.example.com/certs",
        negativeCacheMaxCapacity: 3,
        negativeCacheTtlMs: 10000,
      });

      // Add 3 unique invalid key IDs
      await expect(service.getKey("invalid-1")).rejects.toThrow();
      await expect(service.getKey("invalid-2")).rejects.toThrow();
      await expect(service.getKey("invalid-3")).rejects.toThrow();

      expect(service.getNegativeCacheSize()).toBe(3);
      expect(service.getNegativeCache().has("invalid-1")).toBe(true);

      // Add 4th invalid key ID, exceeding max capacity of 3
      await expect(service.getKey("invalid-4")).rejects.toThrow();

      // Negative cache size should remain strictly at 3
      expect(service.getNegativeCacheSize()).toBe(3);
      // Oldest entry "invalid-1" should have been evicted
      expect(service.getNegativeCache().has("invalid-1")).toBe(false);
      expect(service.getNegativeCache().has("invalid-2")).toBe(true);
      expect(service.getNegativeCache().has("invalid-3")).toBe(true);
      expect(service.getNegativeCache().has("invalid-4")).toBe(true);
    });

    it("should clear negative cache entry if key becomes present in subsequent JWKS response", async () => {
      const { jwk } = generateKeyAndToken("rotated-kid", {
        sub: "user-rotated",
      });

      // Step 1: Initial fetch doesn't return rotated-kid
      const mockFetch1 = vi.fn().mockResolvedValueOnce({
        ok: true,
        json: async () => ({ keys: [] }),
      });
      globalThis.fetch = mockFetch1;

      service.configure({
        jwksUrl: "https://idp.example.com/certs",
        negativeCacheTtlMs: 10000,
      });

      await expect(service.getKey("rotated-kid")).rejects.toThrow();
      expect(service.getNegativeCache().has("rotated-kid")).toBe(true);

      // Simulate TTL expiration to force next fetch
      const dateSpy = vi.spyOn(Date, "now").mockReturnValue(Date.now() + 15000);

      // Step 2: Next fetch returns rotated-kid
      const mockFetch2 = vi.fn().mockResolvedValueOnce({
        ok: true,
        json: async () => ({ keys: [jwk] }),
      });
      globalThis.fetch = mockFetch2;

      const retrievedKey = await service.getKey("rotated-kid");
      expect(retrievedKey).toEqual(jwk);
      // Negative cache entry cleared
      expect(service.getNegativeCache().has("rotated-kid")).toBe(false);

      dateSpy.mockRestore();
    });
  });

  describe("Requirement 5: Fail-safe cache retention", () => {
    it("should keep all previously cached keys intact if a subsequent dynamic fetch fails", async () => {
      const { jwk: jwk1 } = generateKeyAndToken("kid-1", { sub: "user-old" });
      const { token: token2 } = generateKeyAndToken("kid-2", {
        sub: "user-new",
      });

      // Pre-populate key-1 in cache
      service.getCachedKeys().set("kid-1", jwk1);

      // Subsequent fetch for kid-2 fails
      const mockFetch = vi
        .fn()
        .mockRejectedValue(new Error("Network timeout or provider offline"));
      globalThis.fetch = mockFetch;

      service.configure({ jwksUrl: "https://idp.example.com/certs" });

      // Fetching kid-2 should fail
      await expect(service.getKey("kid-2")).rejects.toThrow();

      // But kid-1 MUST still reside in the local cache intact!
      expect(service.getCachedKeys().has("kid-1")).toBe(true);
      expect(service.getCachedKeys().get("kid-1")).toEqual(jwk1);
    });
  });

  describe("Guardrails: Network timeout enforcement", () => {
    it("should abort the network request if it exceeds the configured timeout threshold", async () => {
      const mockFetch = vi.fn().mockImplementation(async (url, options) => {
        const signal = options?.signal;
        await new Promise((resolve, reject) => {
          const timeoutId = setTimeout(resolve, 1000);
          if (signal) {
            if (signal.aborted) {
              reject(new Error("The user aborted a request."));
              return;
            }
            signal.addEventListener("abort", () => {
              clearTimeout(timeoutId);
              reject(new Error("The user aborted a request."));
            });
          }
        });
        return {
          ok: true,
          json: async () => ({ keys: [] }),
        };
      });
      globalThis.fetch = mockFetch;

      service.configure({
        jwksUrl: "https://idp.example.com/certs",
        timeoutMs: 50, // Ultra short timeout to force abort
      });

      await expect(service.getKey("some-kid")).rejects.toThrow(
        /aborted|timeout|failed/i
      );
    });
  });

  describe("Edge cases & Token decoding safety", () => {
    it("should reject token with invalid structural format", async () => {
      await expect(service.verifyToken("invalidtoken")).rejects.toThrow(
        "Invalid token structure"
      );
    });

    it("should reject token with missing kid in header", async () => {
      const payloadB64 = Buffer.from(JSON.stringify({ sub: "user" })).toString(
        "base64url"
      );
      const headerB64 = Buffer.from(JSON.stringify({ alg: "RS256" })).toString(
        "base64url"
      );
      const token = `${headerB64}.${payloadB64}.fakesig`;
      await expect(service.verifyToken(token)).rejects.toThrow(
        "Token header does not contain a Key ID (kid)"
      );
    });

    it("should reject expired tokens even if signature is valid", async () => {
      const { jwk, token } = generateKeyAndToken("expired-kid", {
        sub: "user-expired",
        exp: Math.floor(Date.now() / 1000) - 10, // Expired 10s ago
      });

      service.getCachedKeys().set("expired-kid", jwk);
      await expect(service.verifyToken(token)).rejects.toThrow(
        "Token signature is valid but the token has expired."
      );
    });
  });
});
