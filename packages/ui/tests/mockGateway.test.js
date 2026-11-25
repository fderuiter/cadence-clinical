import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import {
  isGatewayApiCall,
  validateGatewayRequest,
  isWhitelistedSubjectRoute,
  isPathSignatureGated,
  normalizeMswBasePath,
  resolveMswServiceWorkerOptions,
  waitForMswController,
  ensureServiceWorkerController,
  createMswWorkerStarter,
  evaluateDemoBoundaryRequest,
} from "../mockGateway.js";
import * as mockGatewayModule from "../mockGateway.js";
import { generateGatewaySignature } from "../signing.js";
import * as uiRoot from "../index.js";

// A per-test secret: the simulator ships none of its own (#5335).
const TEST_GATEWAY_SECRET = "mock-gateway-unit-test-secret"; // pragma: allowlist secret

describe("Mock Gateway Simulator Unit Tests", () => {
  let consoleSpy;

  beforeEach(() => {
    consoleSpy = vi.spyOn(console, "error").mockImplementation(() => {});
  });

  afterEach(() => {
    consoleSpy.mockRestore();
  });

  describe("Path Prefix Validation and Stripping", () => {
    it("should accept valid service path prefixes and strip them", async () => {
      const req = new Request(
        "http://localhost:8000/designer/studies/STUDY-01",
        {
          method: "GET",
        }
      );
      const result = await validateGatewayRequest(req);
      expect(result.valid).toBe(true);
      expect(result.strippedPath).toBe("studies/STUDY-01");
    });

    it("should reject invalid path prefixes and log console error", async () => {
      const req = new Request(
        "http://localhost:8000/invalid-service/endpoint",
        {
          method: "GET",
        }
      );
      const result = await validateGatewayRequest(req);
      expect(result.valid).toBe(false);
      expect(result.status).toBe(400);
      expect(result.body.error).toBe("INVALID_PATH_PREFIX");
      expect(consoleSpy).toHaveBeenCalledWith(
        expect.stringContaining("Invalid path prefix intercepted")
      );
    });
  });

  describe("Subject Role Administrative Access Enforcement", () => {
    it("should allow whitelisted Subject routes for Subject principal", async () => {
      const req = new Request(
        "http://localhost:8000/api/v1/interop/assignments/subject/sub-001",
        {
          method: "GET",
          headers: {
            "X-User-Roles": "Subject",
            "X-User-Id": "sub-001",
          },
        }
      );
      const result = await validateGatewayRequest(req);
      expect(result.valid).toBe(true);
    });

    it("should block unwhitelisted administrative routes for Subject principal with 403 Forbidden", async () => {
      const req = new Request("http://localhost:8000/api/v1/studies", {
        method: "GET",
        headers: {
          "X-User-Roles": "Subject",
          "X-User-Id": "sub-001",
        },
      });
      const result = await validateGatewayRequest(req);
      expect(result.valid).toBe(false);
      expect(result.status).toBe(403);
      expect(result.body.detail).toContain(
        "Access denied: Subject principal is not authorized"
      );
      expect(consoleSpy).toHaveBeenCalledWith(
        expect.stringContaining(
          "403 Forbidden: Subject role attempted to access administrative route"
        )
      );
    });

    it("should block administrative eCRF mutation attempts from Subject principal", async () => {
      const req = new Request(
        "http://localhost:8000/api/v1/execution/subjects",
        {
          method: "POST",
          headers: {
            "X-User-Roles": "subject",
            "X-User-Id": "sub-001",
          },
        }
      );
      const result = await validateGatewayRequest(req);
      expect(result.valid).toBe(false);
      expect(result.status).toBe(403);
    });
  });

  describe("Dual-Signature Header Validation", () => {
    it("should reject signature-gated mutations missing X-Sig-Token and log console error", async () => {
      const req = new Request(
        "http://localhost:8000/api/v1/execution/queries/sync",
        {
          method: "POST",
          headers: {
            "X-User-Roles": "Data Manager",
            "X-User-Id": "user-001",
          },
        }
      );
      const result = await validateGatewayRequest(req);
      expect(result.valid).toBe(false);
      expect(result.status).toBe(401);
      expect(result.body.detail).toBe("REAUTHENTICATION_REQUIRED");
      expect(consoleSpy).toHaveBeenCalledWith(
        expect.stringContaining(
          "Missing or invalid e-signature header (X-Sig-Token)"
        )
      );
    });

    it("should accept signature-gated mutations when valid X-Sig-Token is supplied", async () => {
      const req = new Request(
        "http://localhost:8000/api/v1/execution/queries/sync",
        {
          method: "POST",
          headers: {
            "X-User-Roles": "Data Manager",
            "X-User-Id": "user-001",
            "X-Sig-Token": "valid-reauth-sig-token-jwt",
          },
        }
      );
      const result = await validateGatewayRequest(req);
      expect(result.valid).toBe(true);
    });

    it("should verify valid X-Gateway-Signature HMAC-SHA256 headers generated by browser Crypto API", async () => {
      const userId = "usr-123";
      const roles = "Sponsor Admin";
      const timestamp = "1700000000";
      const changeReason = "Initial baseline release";

      const validSig = await generateGatewaySignature(
        userId,
        roles,
        timestamp,
        "2",
        changeReason,
        TEST_GATEWAY_SECRET
      );

      const req = new Request("http://localhost:8000/designer/studies", {
        method: "GET",
        headers: {
          "X-User-Id": userId,
          "X-User-Roles": roles,
          "X-Gateway-Timestamp": timestamp,
          "X-Gateway-Signature": validSig,
          "X-Signature-Version": "2",
          "X-Change-Reason": changeReason,
        },
      });

      const result = await validateGatewayRequest(req, {
        secret: TEST_GATEWAY_SECRET,
      });
      expect(result.valid).toBe(true);
    });

    it("rejects a signed request when no secret is configured, even if the signature is genuine", async () => {
      const timestamp = "1700000000";
      const validSig = await generateGatewaySignature(
        "usr-123",
        "Sponsor Admin",
        timestamp,
        "2",
        null,
        TEST_GATEWAY_SECRET
      );

      const req = new Request("http://localhost:8000/designer/studies", {
        method: "GET",
        headers: {
          "X-User-Id": "usr-123",
          "X-User-Roles": "Sponsor Admin",
          "X-Gateway-Timestamp": timestamp,
          "X-Gateway-Signature": validSig,
          "X-Signature-Version": "2",
        },
      });

      const result = await validateGatewayRequest(req);
      expect(result.valid).toBe(false);
      expect(result.status).toBe(401);
      expect(consoleSpy).toHaveBeenCalledWith(
        expect.stringContaining("has no secret to verify it")
      );
    });

    it("keeps the gateway simulator and any gateway secret out of the library's root export", () => {
      expect(uiRoot).not.toHaveProperty("GATEWAY_SECRET");
      expect(uiRoot).not.toHaveProperty("validateGatewayRequest");
      expect(uiRoot).not.toHaveProperty("isGatewayApiCall");
      expect(uiRoot).not.toHaveProperty("resolveMswServiceWorkerOptions");
      expect(uiRoot).not.toHaveProperty("ensureServiceWorkerController");
      expect(uiRoot).not.toHaveProperty("createMswWorkerStarter");
    });

    it("should reject invalid/tampered X-Gateway-Signature and log console error", async () => {
      const req = new Request("http://localhost:8000/designer/studies", {
        method: "GET",
        headers: {
          "X-User-Id": "usr-123",
          "X-User-Roles": "Sponsor Admin",
          "X-Gateway-Timestamp": "1700000000",
          "X-Gateway-Signature": "deadbeef1234567890badc0de", // pragma: allowlist secret
          "X-Signature-Version": "2",
        },
      });

      const result = await validateGatewayRequest(req, {
        secret: TEST_GATEWAY_SECRET,
      });
      expect(result.valid).toBe(false);
      expect(result.status).toBe(401);
      expect(result.body.detail).toBe("Invalid gateway signature");
      expect(consoleSpy).toHaveBeenCalledWith(
        expect.stringContaining(
          "Invalid or tampered X-Gateway-Signature header"
        )
      );
    });
  });

  describe("Helper Functions", () => {
    it("should identify signature-gated paths correctly", () => {
      expect(isPathSignatureGated("execution/sign")).toBe(true);
      expect(isPathSignatureGated("execution/approve")).toBe(true);
      expect(isPathSignatureGated("execution/unblind")).toBe(true);
      expect(isPathSignatureGated("execution/read")).toBe(false);
      expect(isPathSignatureGated("")).toBe(false);
    });

    it("should check subject route whitelisting correctly", () => {
      expect(isWhitelistedSubjectRoute("health", "GET")).toBe(true);
      expect(isWhitelistedSubjectRoute("me", "GET")).toBe(true);
      expect(isWhitelistedSubjectRoute("designer/studies", "GET")).toBe(false);
      expect(isWhitelistedSubjectRoute("", "GET")).toBe(false);
    });
  });
});

describe("isGatewayApiCall", () => {
  it("intercepts the gateway port and service paths", () => {
    expect(isGatewayApiCall(new URL("http://localhost:8000/anything"))).toBe(
      true
    );
    expect(
      isGatewayApiCall(new URL("http://localhost:5173/api/v1/studies"))
    ).toBe(true);
    expect(
      isGatewayApiCall(new URL("http://localhost:5173/execution/forms"))
    ).toBe(true);
  });

  it("passes through assets and app routes", () => {
    expect(isGatewayApiCall(new URL("http://localhost:5173/src/main.js"))).toBe(
      false
    );
    expect(isGatewayApiCall(new URL("http://localhost:5173/studies/1"))).toBe(
      false
    );
  });
});

describe("MSW Service Worker Options & Controller Synchronization", () => {
  const originalServiceWorker = Object.getOwnPropertyDescriptor(
    globalThis.navigator ?? {},
    "serviceWorker"
  );

  afterEach(() => {
    if (globalThis.navigator) {
      if (originalServiceWorker) {
        Object.defineProperty(
          globalThis.navigator,
          "serviceWorker",
          originalServiceWorker
        );
      } else {
        delete globalThis.navigator.serviceWorker;
      }
    }
  });

  it("normalizes base paths and resolves serviceWorker url and scope", () => {
    expect(normalizeMswBasePath(undefined)).toBe("/");
    expect(normalizeMswBasePath("demo/web")).toBe("/demo/web/");
    expect(normalizeMswBasePath("/demo/portal/")).toBe("/demo/portal/");

    expect(resolveMswServiceWorkerOptions("/cadence/demo/")).toEqual({
      url: "/cadence/demo/mockServiceWorker.js",
      options: { scope: "/cadence/demo/" },
    });

    expect(
      resolveMswServiceWorkerOptions("demo/portal", "/sw.js?msw=1")
    ).toEqual({
      url: "/demo/portal/sw.js?msw=1",
      options: { scope: "/demo/portal/" },
    });

    expect(
      resolveMswServiceWorkerOptions("", {
        workerScript: "sw.js?msw=1",
        fallbackBase: "/portal",
      })
    ).toEqual({
      url: "/portal/sw.js?msw=1",
      options: { scope: "/portal/" },
    });
  });

  it("returns immediately when serviceWorker is unavailable or controller is already attached", async () => {
    Object.defineProperty(globalThis.navigator, "serviceWorker", {
      configurable: true,
      value: { controller: { state: "activated" } },
    });
    await expect(waitForMswController(25)).resolves.toBeUndefined();
    await expect(ensureServiceWorkerController(25)).resolves.toBeUndefined();
  });

  it("waits for controllerchange when serviceWorker.ready resolves before controller claims page", async () => {
    const listeners = new Set();
    const fakeServiceWorker = {
      controller: null,
      ready: Promise.resolve({ active: { state: "activated" } }),
      addEventListener: vi.fn((event, handler) => {
        if (event === "controllerchange") {
          listeners.add(handler);
        }
      }),
      removeEventListener: vi.fn((event, handler) => {
        if (event === "controllerchange") {
          listeners.delete(handler);
        }
      }),
    };
    Object.defineProperty(globalThis.navigator, "serviceWorker", {
      configurable: true,
      value: fakeServiceWorker,
    });

    // waitForMswController resolves on ready, while ensureServiceWorkerController waits for controllerchange
    await waitForMswController(200);
    expect(fakeServiceWorker.controller).toBeNull();

    setTimeout(() => {
      fakeServiceWorker.controller = { scriptURL: "/mockServiceWorker.js" };
      for (const listener of [...listeners]) {
        listener();
      }
    }, 10);

    await ensureServiceWorkerController(200);
    expect(fakeServiceWorker.controller).toEqual({
      scriptURL: "/mockServiceWorker.js",
    });
  });

  it("resolves cleanly after timeout when controller never attaches", async () => {
    Object.defineProperty(globalThis.navigator, "serviceWorker", {
      configurable: true,
      value: {
        controller: null,
        ready: new Promise(() => {}),
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
      },
    });
    await expect(waitForMswController(10)).resolves.toBeUndefined();
    await expect(ensureServiceWorkerController(10)).resolves.toBeUndefined();
  });
});

describe("createMswWorkerStarter & evaluateDemoBoundaryRequest", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("memoizes default worker startup, respects beforeStart, and resets startPromise on failure", async () => {
    const logSpy = vi.spyOn(console, "log").mockImplementation(() => {});
    const warnSpy = vi.spyOn(console, "warn").mockImplementation(() => {});

    try {
      const startFn = vi
        .fn()
        .mockRejectedValueOnce(new Error("sw registration failed"))
        .mockResolvedValue({ scope: "/demo/" });

      const defaultWorker = { start: startFn };
      const startMswWorker = createMswWorkerStarter({
        getWorker: () => defaultWorker,
        resolveServiceWorkerOptions: (baseUrl) =>
          resolveMswServiceWorkerOptions(baseUrl, "sw.js?msw=1"),
        isDemoBuild: (env) => env?.MODE === "demo",
        beforeStart: (opts) => (opts.shortcut ? "in-process" : undefined),
        startedLogMessage: "[MSW] Custom started",
        warningLogPrefix: "[MSW] Custom warning:",
        controllerTimeoutMs: 5,
      });

      // Returns null when window is undefined (SSR / Node environment)
      expect(await startMswWorker({ env: { MODE: "demo" } })).toBeNull();
      expect(startFn).not.toHaveBeenCalled();

      vi.stubGlobal("window", {});

      expect(await startMswWorker({ shortcut: true })).toBe("in-process");
      expect(startFn).not.toHaveBeenCalled();

      // First call fails and logs warning outside test mode, resetting startPromise
      const failed = await startMswWorker({
        baseUrl: "/demo/",
        env: { MODE: "demo" },
      });
      expect(failed).toBeNull();
      expect(warnSpy).toHaveBeenCalledWith(
        "[MSW] Custom warning:",
        expect.any(Error)
      );

      // Second call succeeds and memoizes singleton startPromise
      const [first, second] = await Promise.all([
        startMswWorker({
          baseUrl: "/demo/",
          quiet: true,
          env: { MODE: "demo" },
        }),
        startMswWorker({
          baseUrl: "/demo/",
          quiet: true,
          env: { MODE: "demo" },
        }),
      ]);
      expect(first).toEqual({ scope: "/demo/" });
      expect(second).toBe(first);
      expect(startFn).toHaveBeenCalledTimes(2);
      expect(startFn).toHaveBeenLastCalledWith({
        onUnhandledFrame: "error",
        quiet: true,
        serviceWorker: {
          url: "/demo/sw.js?msw=1",
          options: { scope: "/demo/" },
        },
      });
      expect(logSpy).toHaveBeenCalledWith("[MSW] Custom started");

      // Custom worker bypasses memoized singleton and uses warn outside demo mode
      const customWorker = {
        start: vi.fn().mockResolvedValue({ scope: "/custom/" }),
      };
      const customReg = await startMswWorker({
        worker: customWorker,
        env: { MODE: "development" },
      });
      expect(customReg).toEqual({ scope: "/custom/" });
      expect(customWorker.start).toHaveBeenCalledWith(
        expect.objectContaining({ onUnhandledFrame: "warn", quiet: false })
      );

      // Null worker returns null cleanly
      const emptyStarter = createMswWorkerStarter();
      expect(await emptyStarter()).toBeNull();
    } finally {
      logSpy.mockRestore();
      warnSpy.mockRestore();
    }
  });

  it("evaluates demo boundary requests for off-origin, invalid gateway, unmocked demo, and live passthrough", async () => {
    const consoleSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    vi.stubGlobal("location", new URL("http://localhost:5173"));
    try {
      const offOriginDemo = await evaluateDemoBoundaryRequest(
        new Request("https://external.example.org/telemetry"),
        { demoActive: true, appLabel: "demo" }
      );
      expect(offOriginDemo.passthrough).toBe(false);
      expect(offOriginDemo.status).toBe(503);
      expect(offOriginDemo.body.error).toBe("DEMO_OFF_ORIGIN_BLOCKED");

      const offOriginLive = await evaluateDemoBoundaryRequest(
        new Request("https://external.example.org/telemetry"),
        { demoActive: false }
      );
      expect(offOriginLive).toEqual({ passthrough: true });

      const invalidPrefix = await evaluateDemoBoundaryRequest(
        new Request("http://localhost:8000/invalid-prefix/test"),
        { demoActive: true }
      );
      expect(invalidPrefix.passthrough).toBe(false);
      expect(invalidPrefix.status).toBe(400);

      const unmockedDemo = await evaluateDemoBoundaryRequest(
        new Request("http://localhost:8000/api/v1/unmocked"),
        { demoActive: true, appLabel: "Subject Portal demo" }
      );
      expect(unmockedDemo.passthrough).toBe(false);
      expect(unmockedDemo.status).toBe(501);
      expect(unmockedDemo.body.error).toBe("DEMO_UNMOCKED_ENDPOINT");

      const unmockedLive = await evaluateDemoBoundaryRequest(
        new Request("http://localhost:8000/api/v1/unmocked"),
        { demoActive: false }
      );
      expect(unmockedLive).toEqual({ passthrough: true });
    } finally {
      consoleSpy.mockRestore();
    }
  });

  it("retains verifyGatewaySignature re-export without unused HMAC or JWT re-exports", () => {
    expect(mockGatewayModule.verifyGatewaySignature).toBeTypeOf("function");
    expect(uiRoot.verifyGatewaySignature).toBeTypeOf("function");
    expect("generateGatewaySignature" in mockGatewayModule).toBe(false);
    expect("generateCanonicalSignature" in mockGatewayModule).toBe(false);
  });
});
