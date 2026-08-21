import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { gatewayHandlers } from "../src/mocks/handlers.js";
import { generateGatewaySignature, GATEWAY_SECRET } from "ui";

describe("Web App MSW Mock Gateway Interceptor Integration Tests", () => {
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
    // Call MSW handler directly with request
    for (const handler of gatewayHandlers) {
      const result = await handler.run({ request: req });
      if (result && result.response) {
        return result.response;
      }
    }
    return null;
  };

  it("intercepts requests with invalid path prefixes, logs warning/error, and returns 400", async () => {
    const response = await runHandler(
      "http://localhost:8000/unregistered/service"
    );
    expect(response).not.toBeNull();
    expect(response.status).toBe(400);
    const data = await response.json();
    expect(data.error).toBe("INVALID_PATH_PREFIX");
    expect(consoleSpy).toHaveBeenCalledWith(
      expect.stringContaining("Invalid path prefix intercepted")
    );
  });

  it("intercepts signature-gated mutations missing X-Sig-Token and returns 401 REAUTHENTICATION_REQUIRED", async () => {
    const response = await runHandler(
      "http://localhost:8000/api/v1/execution/queries/sync",
      "POST",
      {
        "X-User-Roles": "Data Manager",
        "X-User-Id": "usr-dm",
      },
      { blocks: [] }
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

  it("returns 403 Forbidden for unwhitelisted administrative requests from Subject role", async () => {
    const response = await runHandler(
      "http://localhost:8000/api/v1/studies",
      "GET",
      {
        "X-User-Roles": "Subject",
        "X-User-Id": "sub-101",
      }
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

  it("verifies V2 HMAC-SHA256 X-Gateway-Signature header generated via browser Crypto API", async () => {
    const timestamp = "1700000000";
    const sig = await generateGatewaySignature(
      "user-001",
      "Sponsor Admin",
      timestamp,
      "2",
      null,
      GATEWAY_SECRET
    );

    const response = await runHandler(
      "http://localhost:8000/designer/studies",
      "GET",
      {
        "X-User-Id": "user-001",
        "X-User-Roles": "Sponsor Admin",
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
      }
    );

    // Passed gateway validation -> MSW passthrough
    expect(response).not.toBeNull();
    expect(response.headers.get("x-msw-intention")).toBe("passthrough");
  });
});
