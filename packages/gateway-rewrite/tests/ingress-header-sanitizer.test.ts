import { describe, it, expect, vi } from "vitest";
import {
  IngressHeaderSanitizerMiddleware,
  IngressHeaderSanitizationMiddleware,
  IngressHeaderSanitizerService,
  DEFAULT_ALLOWED_HEADERS,
  DEFAULT_PROHIBITED_HEADERS,
} from "../src/index.js";

describe("IngressHeaderSanitizerMiddleware", () => {
  it("should purge all prohibited identity and scope headers case-insensitively", () => {
    const middleware = new IngressHeaderSanitizerMiddleware();
    const headers: Record<string, any> = {
      "X-User-Id": "spoofed-user-123",
      "user-id": "spoofed-user-456",
      "X-USER": "attacker",
      "X-User-Roles": "super-admin",
      "X-ROLES": "admin",
      "user-roles": "root",
      "X-GATEWAY-SIGNATURE": "fake-sig-123",
      "x-signature": "fake-sig",
      "X-Signature-Version": "1.0",
      "X-Gateway-Timestamp": "1700000000",
      "X-TIMESTAMP": "1700000000",
      "X-Site-Id": "SITE-999",
      "site-id": "SITE-888",
      "X-Sponsor-Id": "SPONSOR-999",
      "sponsor-id": "SPONSOR-888",
      "X-TENANT-ID": "tenant-evil",
      "tenant-id": "tenant-evil-2",
      "X-Change-Reason": "malicious-change",
      "X-Unblinded-Access": "true",
      "authorization": "Bearer valid.jwt.token",
      "x-correlation-id": "corr-req-001",
      "content-type": "application/json",
    };
    const req = { headers };
    const res = {};
    const next = vi.fn();

    middleware.use(req, res, next);

    expect(next).toHaveBeenCalledTimes(1);

    // Identity and scope headers must be purged
    expect(req.headers["X-User-Id"]).toBeUndefined();
    expect(req.headers["user-id"]).toBeUndefined();
    expect(req.headers["X-USER"]).toBeUndefined();
    expect(req.headers["X-User-Roles"]).toBeUndefined();
    expect(req.headers["X-ROLES"]).toBeUndefined();
    expect(req.headers["user-roles"]).toBeUndefined();
    expect(req.headers["X-GATEWAY-SIGNATURE"]).toBeUndefined();
    expect(req.headers["x-signature"]).toBeUndefined();
    expect(req.headers["X-Signature-Version"]).toBeUndefined();
    expect(req.headers["X-Gateway-Timestamp"]).toBeUndefined();
    expect(req.headers["X-TIMESTAMP"]).toBeUndefined();
    expect(req.headers["X-Site-Id"]).toBeUndefined();
    expect(req.headers["site-id"]).toBeUndefined();
    expect(req.headers["X-Sponsor-Id"]).toBeUndefined();
    expect(req.headers["sponsor-id"]).toBeUndefined();
    expect(req.headers["X-TENANT-ID"]).toBeUndefined();
    expect(req.headers["tenant-id"]).toBeUndefined();
    expect(req.headers["X-Change-Reason"]).toBeUndefined();
    expect(req.headers["X-Unblinded-Access"]).toBeUndefined();

    // Allowed headers must be preserved untouched
    expect(req.headers["authorization"]).toBe("Bearer valid.jwt.token");
    expect(req.headers["x-correlation-id"]).toBe("corr-req-001");
    expect(req.headers["content-type"]).toBe("application/json");
  });

  it("should enforce strict allowlist filtering by removing unapproved custom headers", () => {
    const middleware = new IngressHeaderSanitizerMiddleware();
    const headers: Record<string, any> = {
      "authorization": "Bearer client-secret-token",
      "x-correlation-id": "trace-12345",
      "content-type": "application/json",
      "accept": "application/json",
      "user-agent": "Mozilla/5.0",
      "x-custom-spoofed-header": "inject-code",
      "x-debug-override": "true",
      "x-admin-mode": "enabled",
    };
    const req = { headers };
    const res = {};
    const next = vi.fn();

    middleware.use(req, res, next);

    expect(next).toHaveBeenCalled();

    // Standard allowlisted headers preserved
    expect(req.headers["authorization"]).toBe("Bearer client-secret-token");
    expect(req.headers["x-correlation-id"]).toBe("trace-12345");
    expect(req.headers["content-type"]).toBe("application/json");
    expect(req.headers["accept"]).toBe("application/json");
    expect(req.headers["user-agent"]).toBe("Mozilla/5.0");

    // Unapproved custom headers stripped
    expect(req.headers["x-custom-spoofed-header"]).toBeUndefined();
    expect(req.headers["x-debug-override"]).toBeUndefined();
    expect(req.headers["x-admin-mode"]).toBeUndefined();
  });

  it("should preserve bearer tokens without generating gateway signatures", () => {
    const middleware = new IngressHeaderSanitizerMiddleware();
    const token = "Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.test";
    const headers: Record<string, any> = {
      "authorization": token,
      "x-request-id": "req-999",
    };
    const req = { headers };
    const next = vi.fn();

    middleware.use(req, {}, next);

    expect(req.headers["authorization"]).toBe(token);
    expect(req.headers["x-gateway-signature"]).toBeUndefined();
  });

  it("should support alias export IngressHeaderSanitizationMiddleware", () => {
    const middleware = new IngressHeaderSanitizationMiddleware();
    const headers: Record<string, any> = {
      "X-User-Id": "12345",
      "Authorization": "Bearer token",
    };
    const req = { headers };
    const next = vi.fn();

    middleware.use(req, {}, next);

    expect(req.headers["X-User-Id"]).toBeUndefined();
    expect(req.headers["Authorization"]).toBe("Bearer token");
  });

  it("should work when initialized with IngressHeaderSanitizerService or custom options", () => {
    const service = new IngressHeaderSanitizerService({
      allowedHeaders: ["authorization", "x-custom-allowed"],
      prohibitedHeaders: ["x-bad-header"],
      strictMode: true,
    });
    const middleware = new IngressHeaderSanitizerMiddleware(service);

    const headers: Record<string, any> = {
      "Authorization": "Bearer token",
      "X-Custom-Allowed": "allowed-val",
      "X-Bad-Header": "bad-val",
      "X-Unapproved": "unapproved-val",
    };
    const req = { headers };
    const next = vi.fn();

    middleware.use(req, {}, next);

    expect(req.headers["Authorization"]).toBe("Bearer token");
    expect(req.headers["X-Custom-Allowed"]).toBe("allowed-val");
    expect(req.headers["X-Bad-Header"]).toBeUndefined();
    expect(req.headers["X-Unapproved"]).toBeUndefined();
  });

  it("should allow disabling strict mode while still purging prohibited headers", () => {
    const middleware = new IngressHeaderSanitizerMiddleware({
      strictMode: false,
    });

    const headers: Record<string, any> = {
      "X-User-Id": "spoofed",
      "x-custom-partner-header": "partner-data",
    };
    const req = { headers };
    const next = vi.fn();

    middleware.use(req, {}, next);

    expect(req.headers["X-User-Id"]).toBeUndefined();
    expect(req.headers["x-custom-partner-header"]).toBe("partner-data");
  });

  it("should execute in constant time with negligible latency", () => {
    const middleware = new IngressHeaderSanitizerMiddleware();
    const sampleReq = () => ({
      headers: {
        "authorization": "Bearer token",
        "content-type": "application/json",
        "x-correlation-id": "corr-123",
        "x-user-id": "spoofed-user",
        "x-tenant-id": "spoofed-tenant",
        "x-custom-header": "unapproved",
      },
    });

    const start = performance.now();
    for (let i = 0; i < 1000; i++) {
      const req = sampleReq();
      middleware.use(req, {}, () => {});
    }
    const durationMs = performance.now() - start;

    // 1000 iterations should execute well under 20ms total (<20µs per call)
    expect(durationMs).toBeLessThan(20);
  });

  it("should handle null, undefined, or missing headers gracefully without throwing", () => {
    const middleware = new IngressHeaderSanitizerMiddleware();
    const next = vi.fn();

    expect(() => middleware.use(null, {}, next)).not.toThrow();
    expect(() => middleware.use({}, {}, next)).not.toThrow();
    expect(() => middleware.use({ headers: null }, {}, next)).not.toThrow();
    expect(next).toHaveBeenCalledTimes(3);
  });
});
