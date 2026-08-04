import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { resolveAssetUrl } from "../../src/utils/url";

describe("resolveAssetUrl Utility", () => {
  const originalBaseUrl = import.meta.env.BASE_URL;

  beforeEach(() => {
    // Reset global location properties if needed
    vi.stubGlobal("window", {
      location: {
        origin: "http://localhost:3000",
      },
    });
  });

  afterEach(() => {
    // Restore import.meta.env.BASE_URL
    import.meta.env.BASE_URL = originalBaseUrl;
    vi.restoreAllMocks();
  });

  it("should fall back to domain root if BASE_URL is not configured", () => {
    import.meta.env.BASE_URL = undefined;
    const url = resolveAssetUrl("silent-check-sso.html");
    expect(url).toBe("http://localhost:3000/silent-check-sso.html");
  });

  it("should use BASE_URL prefix when it is configured with trailing slash", () => {
    import.meta.env.BASE_URL = "/cadence-clinical/";
    const url = resolveAssetUrl("silent-check-sso.html");
    expect(url).toBe(
      "http://localhost:3000/cadence-clinical/silent-check-sso.html"
    );
  });

  it("should correctly append slash to BASE_URL if it lacks a trailing slash", () => {
    import.meta.env.BASE_URL = "/cadence-clinical";
    const url = resolveAssetUrl("silent-check-sso.html");
    expect(url).toBe(
      "http://localhost:3000/cadence-clinical/silent-check-sso.html"
    );
  });

  it("should handle asset path with leading slash without creating double slashes", () => {
    import.meta.env.BASE_URL = "/cadence-clinical/";
    const url = resolveAssetUrl("/silent-check-sso.html");
    expect(url).toBe(
      "http://localhost:3000/cadence-clinical/silent-check-sso.html"
    );
  });

  it("should handle asset path with leading slash and BASE_URL without trailing slash", () => {
    import.meta.env.BASE_URL = "/cadence-clinical";
    const url = resolveAssetUrl("/silent-check-sso.html");
    expect(url).toBe(
      "http://localhost:3000/cadence-clinical/silent-check-sso.html"
    );
  });

  it("should resolve correctly when BASE_URL is root '/'", () => {
    import.meta.env.BASE_URL = "/";
    const url = resolveAssetUrl("/silent-check-sso.html");
    expect(url).toBe("http://localhost:3000/silent-check-sso.html");
  });
});
