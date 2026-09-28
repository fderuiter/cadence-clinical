import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { gatewayHandlers } from "../src/mocks/handlers.js";

describe("Subject Portal MSW Mock Gateway Interceptor Integration Tests", () => {
  let consoleSpy;

  beforeEach(() => {
    consoleSpy = vi.spyOn(console, "error").mockImplementation(() => {});
  });

  afterEach(() => {
    consoleSpy.mockRestore();
  });

  const runHandler = async (url, method = "GET", headers = {}, body = null) => {
    const req = new Request(url, {
      method,
      headers,
      body: body ? JSON.stringify(body) : null,
    });
    for (const handler of gatewayHandlers) {
      const result = await handler.run({ request: req });
      if (result && result.response) {
        return result.response;
      }
    }
    return null;
  };

  it("intercepts requests with invalid path prefixes, logs error, and returns 400", async () => {
    const response = await runHandler(
      "http://localhost:8000/invalid-prefix/subjects"
    );
    expect(response).not.toBeNull();
    expect(response.status).toBe(400);
    const data = await response.json();
    expect(data.error).toBe("INVALID_PATH_PREFIX");
    expect(consoleSpy).toHaveBeenCalledWith(
      expect.stringContaining("Invalid path prefix intercepted")
    );
  });

  it("allows whitelisted Subject routes for Subject principal", async () => {
    const response = await runHandler(
      "http://localhost:8000/api/v1/interop/assignments/subject/sub-001",
      "GET",
      {
        "X-User-Roles": "Subject",
        "X-User-Id": "sub-001",
      }
    );

    // Whitelisted Subject route -> passes validation (MSW passthrough)
    expect(response).not.toBeNull();
    expect(response.status).not.toBe(403);
    expect(response.headers.get("x-msw-intention")).toBe("passthrough");
  });

  it("returns 403 Forbidden for unwhitelisted administrative requests from Subject role", async () => {
    const response = await runHandler(
      "http://localhost:8000/api/v1/execution/subjects",
      "POST",
      {
        "X-User-Roles": "Subject",
        "X-User-Id": "sub-001",
      },
      { site_id: "SITE-01" }
    );

    expect(response).not.toBeNull();
    expect(response.status).toBe(403);
    const data = await response.json();
    expect(data.detail).toContain(
      "Access denied: Subject principal is not authorized"
    );
    expect(consoleSpy).toHaveBeenCalledWith(
      expect.stringContaining(
        "403 Forbidden: Subject role attempted to access administrative route"
      )
    );
  });

  it("intercepts e-signature gated route mutations missing X-Sig-Token and returns 401", async () => {
    const response = await runHandler(
      "http://localhost:8000/api/v1/execution/form-submissions/sign-off",
      "POST",
      {
        "X-User-Roles": "Site Investigator",
        "X-User-Id": "pi-001",
      },
      { form_id: "F-01" }
    );

    expect(response).not.toBeNull();
    expect(response.status).toBe(401);
    const data = await response.json();
    expect(data.detail).toBe("REAUTHENTICATION_REQUIRED");
    expect(consoleSpy).toHaveBeenCalledWith(
      expect.stringContaining(
        "Missing or invalid e-signature header (X-Sig-Token)"
      )
    );
  });
});
