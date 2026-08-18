import { beforeEach, describe, expect, it, vi } from "vitest";
import { ExecutionContext, HttpException, HttpStatus } from "@nestjs/common";
import {
  RateLimitGuard,
  RateLimitMiddleware,
  RateLimiterService,
} from "../src/index.js";

describe("RateLimiterService", () => {
  let rateLimiterService: RateLimiterService;

  beforeEach(() => {
    delete process.env.RATE_LIMIT_WINDOW;
    delete process.env.RATE_LIMIT_MAX_REQUESTS;
    rateLimiterService = new RateLimiterService();
    rateLimiterService.reset();
  });

  it("should parse environment variables or fallback to defaults", () => {
    expect(rateLimiterService.getWindowSeconds()).toBe(60.0);
    expect(rateLimiterService.getMaxRequests()).toBe(100);

    process.env.RATE_LIMIT_WINDOW = "30.5";
    process.env.RATE_LIMIT_MAX_REQUESTS = "50";

    expect(rateLimiterService.getWindowSeconds()).toBe(30.5);
    expect(rateLimiterService.getMaxRequests()).toBe(50);
  });

  it("should enforce sliding window rate limits per key", () => {
    const key = "user:dev_123";
    const windowSec = 10;
    const maxReq = 3;
    const now = 100000;

    // First 3 requests should succeed
    expect(rateLimiterService.isRateLimited(key, windowSec, maxReq, now)).toBe(false);
    expect(rateLimiterService.isRateLimited(key, windowSec, maxReq, now + 100)).toBe(false);
    expect(rateLimiterService.isRateLimited(key, windowSec, maxReq, now + 200)).toBe(false);

    // 4th request within 10s window should be rate limited
    expect(rateLimiterService.isRateLimited(key, windowSec, maxReq, now + 300)).toBe(true);

    // Request after window boundary (10s = 10000ms later) should succeed as old requests expire
    expect(rateLimiterService.isRateLimited(key, windowSec, maxReq, now + 10500)).toBe(false);
  });

  it("should extract client key from Bearer token sub claim without signature verification", () => {
    // Construct unverified JWT token with sub="usr_abc123"
    const payload = { sub: "usr_abc123", role: "investigator" };
    const encodedPayload = Buffer.from(JSON.stringify(payload)).toString("base64url");
    const mockJwt = `eyJhbGciOiJIUzI1NiJ9.${encodedPayload}.mock_signature`;

    const req = {
      headers: {
        authorization: `Bearer ${mockJwt}`,
      },
    };

    const key = rateLimiterService.getClientKey(req);
    expect(key).toBe("user:usr_abc123");
  });

  it("should share the same rate-limiting pool for authenticated requests with same user sub", () => {
    const payload = { sub: "usr_shared_456" };
    const encodedPayload = Buffer.from(JSON.stringify(payload)).toString("base64url");
    const mockJwt = `eyJhbGciOiJIUzI1NiJ9.${encodedPayload}.signature_1`;
    const mockJwt2 = `eyJhbGciOiJIUzI1NiJ9.${encodedPayload}.signature_2`;

    const req1 = { headers: { authorization: `Bearer ${mockJwt}` }, ip: "10.0.0.1" };
    const req2 = { headers: { authorization: `Bearer ${mockJwt2}` }, ip: "10.0.0.2" };

    const key1 = rateLimiterService.getClientKey(req1);
    const key2 = rateLimiterService.getClientKey(req2);

    expect(key1).toBe("user:usr_shared_456");
    expect(key2).toBe("user:usr_shared_456");

    process.env.RATE_LIMIT_MAX_REQUESTS = "2";
    process.env.RATE_LIMIT_WINDOW = "60";

    expect(rateLimiterService.isRateLimited(key1)).toBe(false);
    expect(rateLimiterService.isRateLimited(key2)).toBe(false);
    // 3rd request from same user pool should be limited
    expect(rateLimiterService.isRateLimited(key1)).toBe(true);
  });

  it("should fall back to client IP for unauthenticated requests", () => {
    const req1 = { headers: {}, ip: "192.168.1.50" };
    const req2 = {
      headers: { "x-forwarded-for": "203.0.113.195, 70.41.3.18" },
    };

    expect(rateLimiterService.getClientKey(req1)).toBe("192.168.1.50");
    expect(rateLimiterService.getClientKey(req2)).toBe("203.0.113.195");
  });

  it("should identify excluded paths correctly", () => {
    expect(rateLimiterService.isExcludedPath("/health")).toBe(true);
    expect(rateLimiterService.isExcludedPath("/health/")).toBe(true);
    expect(rateLimiterService.isExcludedPath("")).toBe(true);
    expect(rateLimiterService.isExcludedPath("/")).toBe(true);

    expect(rateLimiterService.isExcludedPath("/api/v1/studies")).toBe(false);
    expect(rateLimiterService.isExcludedPath("/designer/test")).toBe(false);
  });

  it("should execute rate limit checks in under 2ms", () => {
    const key = "user:perf_test";
    const start = performance.now();
    for (let i = 0; i < 100; i++) {
      rateLimiterService.isRateLimited(key, 60, 1000);
    }
    const totalDuration = performance.now() - start;
    const averagePerReq = totalDuration / 100;

    expect(averagePerReq).toBeLessThan(2.0);
  });
});

describe("RateLimitMiddleware", () => {
  let rateLimiterService: RateLimiterService;
  let middleware: RateLimitMiddleware;

  beforeEach(() => {
    rateLimiterService = new RateLimiterService();
    rateLimiterService.reset();
    middleware = new RateLimitMiddleware(rateLimiterService);
    process.env.RATE_LIMIT_MAX_REQUESTS = "2";
    process.env.RATE_LIMIT_WINDOW = "60";
  });

  it("should allow excluded paths even if limit is breached", () => {
    const req = { url: "/health", headers: {} };
    const res = {};
    const next = vi.fn();

    middleware.use(req, res, next);
    expect(next).toHaveBeenCalledTimes(1);
  });

  it("should pass normal requests within rate limit", () => {
    const req = { path: "/api/v1/data", headers: {}, ip: "127.0.0.1" };
    const res = {};
    const next = vi.fn();

    middleware.use(req, res, next);
    expect(next).toHaveBeenCalledTimes(1);
  });

  it("should block requests exceeding rate limit with 429 status code and detail payload", () => {
    const req = { path: "/api/v1/data", headers: {}, ip: "10.0.0.99" };
    const jsonSpy = vi.fn();
    const res = {
      status: vi.fn().mockReturnValue({ json: jsonSpy }),
    };
    const next = vi.fn();

    // Max requests is 2
    middleware.use(req, res, next); // 1st
    middleware.use(req, res, next); // 2nd
    expect(next).toHaveBeenCalledTimes(2);

    // 3rd request should be blocked
    middleware.use(req, res, next);
    expect(res.status).toHaveBeenCalledWith(429);
    expect(jsonSpy).toHaveBeenCalledWith(
      expect.objectContaining({
        detail: "Too Many Requests. Rate limit exceeded.",
        statusCode: 429,
      })
    );
    expect(next).toHaveBeenCalledTimes(2); // not called 3rd time
  });
});

describe("RateLimitGuard", () => {
  let rateLimiterService: RateLimiterService;
  let guard: RateLimitGuard;

  beforeEach(() => {
    rateLimiterService = new RateLimiterService();
    rateLimiterService.reset();
    guard = new RateLimitGuard(rateLimiterService);
    process.env.RATE_LIMIT_MAX_REQUESTS = "1";
    process.env.RATE_LIMIT_WINDOW = "60";
  });

  it("should throw HttpException with status 429 when rate limit is exceeded", () => {
    const req = { path: "/api/v1/resource", headers: {}, ip: "192.168.0.1" };
    const context = {
      switchToHttp: () => ({
        getRequest: () => req,
      }),
    } as unknown as ExecutionContext;

    // First call allowed
    expect(guard.canActivate(context)).toBe(true);

    // Second call throws 429 HttpException
    try {
      guard.canActivate(context);
      expect.fail("Should have thrown HttpException");
    } catch (err) {
      expect(err).toBeInstanceOf(HttpException);
      const httpErr = err as HttpException;
      expect(httpErr.getStatus()).toBe(HttpStatus.TOO_MANY_REQUESTS);
      expect(httpErr.getResponse()).toEqual(
        expect.objectContaining({
          detail: "Too Many Requests. Rate limit exceeded.",
          statusCode: 429,
        })
      );
    }
  });
});
