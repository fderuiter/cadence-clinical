import { describe, expect, it } from "vitest";
import {
  buildSubjectPortalCspContent,
  injectHoistedPortalCspMeta,
  resolvePortalOidcOrigin,
} from "../vite.config.js";

describe("apps/subject-portal mode-aware CSP profiles", () => {
  it("strips OIDC localhost origins in demo mode even when VITE_KEYCLOAK_URL is present in env", () => {
    const csp = buildSubjectPortalCspContent({
      mode: "demo",
      isProduction: true,
      env: { VITE_KEYCLOAK_URL: "http://127.0.0.1:8080/" },
    });

    expect(csp).toContain("script-src 'self'");
    expect(csp).toContain("connect-src 'self'");
    expect(csp).toContain("frame-src 'self'");
    expect(csp).not.toContain("localhost");
    expect(csp).not.toContain("127.0.0.1");
    expect(csp).not.toMatch(
      /script-src[^;]*(?:unsafe-inline|unsafe-hashes|blob:|data:)/
    );
    expect(csp).not.toMatch(/frame-src[^;]*data:/);
  });

  it("adds configured OIDC origin to connect-src and frame-src in non-demo builds", () => {
    expect(
      resolvePortalOidcOrigin({
        mode: "production",
        isProduction: true,
        env: { VITE_KEYCLOAK_URL: "http://127.0.0.1:8080/" },
      })
    ).toBe("http://127.0.0.1:8080");

    const nonDemoCsp = buildSubjectPortalCspContent({
      mode: "production",
      isProduction: true,
      env: { VITE_KEYCLOAK_URL: "http://127.0.0.1:8080/" },
    });
    expect(nonDemoCsp).toContain("connect-src 'self' http://127.0.0.1:8080");
    expect(nonDemoCsp).toContain("frame-src 'self' http://127.0.0.1:8080");
  });

  it("hoists CSP meta before any <link> or <script> tag in <head>", () => {
    const rawHtml = `<!doctype html>
<html>
  <head>
    <link rel="manifest" href="/subject-portal/manifest.json" />
    <script type="module" src="/subject-portal/assets/index.js"></script>
  </head>
  <body></body>
</html>`;

    const transformed = injectHoistedPortalCspMeta(
      rawHtml,
      buildSubjectPortalCspContent({
        mode: "demo",
        isProduction: true,
        env: {},
      })
    );
    const cspIdx = transformed.indexOf('http-equiv="Content-Security-Policy"');
    expect(cspIdx).toBeGreaterThan(0);
    expect(cspIdx).toBeLessThan(transformed.indexOf("<link"));
    expect(cspIdx).toBeLessThan(transformed.indexOf("<script"));
  });
});
