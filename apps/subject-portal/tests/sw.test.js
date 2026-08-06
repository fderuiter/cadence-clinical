import { describe, it, expect, beforeEach, vi, afterEach } from "vitest";
import fs from "fs";
import path from "path";

describe("Subject Portal Service Worker (sw.js)", () => {
  let swCode;
  let listeners = {};
  let mockCaches;
  let mockCache;

  beforeEach(() => {
    vi.useFakeTimers();
    // Read sw.js code
    const swPath = path.resolve(__dirname, "../public/sw.js");
    swCode = fs.readFileSync(swPath, "utf8");

    listeners = {};
    mockCache = {
      put: vi.fn().mockResolvedValue(undefined),
      match: vi.fn().mockResolvedValue(null),
      addAll: vi.fn().mockResolvedValue(undefined),
    };
    mockCaches = {
      open: vi.fn().mockResolvedValue(mockCache),
      keys: vi.fn().mockResolvedValue([]),
      delete: vi.fn().mockResolvedValue(true),
      match: vi.fn().mockResolvedValue(null),
    };

    // Set up global mocks to simulate the service worker environment
    globalThis.self = {
      location: {
        origin: "http://localhost:5174",
        pathname: "/subject-portal/sw.js",
      },
      addEventListener: vi.fn((event, callback) => {
        listeners[event] = callback;
      }),
      skipWaiting: vi.fn(),
      clients: {
        claim: vi.fn(),
      },
    };

    globalThis.caches = mockCaches;
    globalThis.Response = class Response {
      constructor(body, options = {}) {
        this.body = body;
        this.status = options.status || 200;
        this.type = options.type || "basic";
      }
      clone() {
        return new Response(this.body, {
          status: this.status,
          type: this.type,
        });
      }
    };
    globalThis.Request = class Request {
      constructor(url, options = {}) {
        this.url = url;
        this.method = options.method || "GET";
        this.mode = options.mode || "navigate";
      }
    };

    // Execute the service worker code in the mocked global context
    const runSW = new Function(
      "self",
      "caches",
      "Response",
      "Request",
      "setTimeout",
      "clearTimeout",
      swCode
    );
    runSW(
      globalThis.self,
      globalThis.caches,
      globalThis.Response,
      globalThis.Request,
      setTimeout,
      clearTimeout
    );
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  it("should pre-cache unhashed core shell resources on install", async () => {
    expect(listeners["install"]).toBeDefined();

    const mockEvent = {
      waitUntil: vi.fn((promise) => promise),
    };

    await listeners["install"](mockEvent);

    expect(mockCaches.open).toHaveBeenCalledWith("portal-cache-v1");
    expect(mockCache.addAll).toHaveBeenCalledWith([
      "/subject-portal/",
      "/subject-portal/index.html",
      "/subject-portal/style.css",
      "/subject-portal/index.js",
      "/subject-portal/manifest.json",
    ]);
  });

  it("should cleanup older caches on activation", async () => {
    expect(listeners["activate"]).toBeDefined();
    mockCaches.keys.mockResolvedValue(["old-cache", "portal-cache-v1"]);

    const mockEvent = {
      waitUntil: vi.fn((promise) => promise),
    };

    await listeners["activate"](mockEvent);
    expect(mockCaches.delete).toHaveBeenCalledWith("old-cache");
    expect(mockCaches.delete).not.toHaveBeenCalledWith(
      "portal-cache-v1"
    );
  });

  it("should bypass timeout and caching for non-static-asset GET requests (e.g. dynamic API queries)", async () => {
    expect(listeners["fetch"]).toBeDefined();

    const mockResponse = new globalThis.Response("{}", {
      status: 200,
      type: "cors",
    });
    globalThis.fetch = vi.fn().mockResolvedValue(mockResponse);

    const request = new globalThis.Request(
      "http://localhost:8000/api/v1/assignments"
    );
    const mockEvent = {
      request,
      respondWith: vi.fn(),
    };

    await listeners["fetch"](mockEvent);

    expect(mockEvent.respondWith).toHaveBeenCalled();
    const resultResponse = await mockEvent.respondWith.mock.calls[0][0];
    expect(resultResponse).toBe(mockResponse);
    expect(globalThis.fetch).toHaveBeenCalledWith(request);

    // Ensure cache is NOT open/put
    expect(mockCaches.open).not.toHaveBeenCalled();
  });

  it("should handle static asset with network-first success within 2 seconds", async () => {
    expect(listeners["fetch"]).toBeDefined();

    const mockResponse = new globalThis.Response("index.html content", {
      status: 200,
      type: "basic",
    });
    globalThis.fetch = vi.fn().mockResolvedValue(mockResponse);

    const request = new globalThis.Request(
      "http://localhost:5174/subject-portal/index.html"
    );
    const mockEvent = {
      request,
      respondWith: vi.fn(),
    };

    await listeners["fetch"](mockEvent);

    const respondWithPromise = mockEvent.respondWith.mock.calls[0][0];
    const response = await respondWithPromise;

    expect(response).toBe(mockResponse);
    expect(globalThis.fetch).toHaveBeenCalledWith(request);

    // It should dynamically cache the successfully retrieved asset
    expect(mockCaches.open).toHaveBeenCalledWith("portal-cache-v1");
    expect(mockCache.put).toHaveBeenCalledWith(
      request,
      expect.any(globalThis.Response)
    );
  });

  it("should fallback to cache within 2 seconds if static asset request times out", async () => {
    expect(listeners["fetch"]).toBeDefined();

    // Simulate a hung fetch promise that never resolves
    globalThis.fetch = vi.fn().mockReturnValue(new Promise(() => {}));

    const cachedResponse = new globalThis.Response("cached index.html", {
      status: 200,
    });
    mockCaches.match.mockResolvedValue(cachedResponse);
    mockCache.match.mockResolvedValue(cachedResponse);

    const request = new globalThis.Request(
      "http://localhost:5174/subject-portal/index.html"
    );
    const mockEvent = {
      request,
      respondWith: vi.fn(),
    };

    await listeners["fetch"](mockEvent);

    const respondWithPromise = mockEvent.respondWith.mock.calls[0][0];

    // Fast-forward time to trigger timeout
    vi.advanceTimersByTime(2000);

    const response = await respondWithPromise;
    expect(response).toBe(cachedResponse);
    expect(mockCaches.match).toHaveBeenCalledWith(request);
  });
});
