import { describe, expect, it } from "vitest";
import {
  buildWebCspContent,
  injectHoistedWebCspMeta,
  resolveWebOidcOrigin,
} from "../vite.config.js";

describe("apps/web mode-aware CSP profiles", () => {
  it("emits strict same-origin + blob CSP with zero localhost tokens in demo mode even when VITE_KEYCLOAK_URL is set", () => {
    const csp = buildWebCspContent({
      mode: "demo",
      isProduction: true,
      env: {
        VITE_KEYCLOAK_URL: "http://127.0.0.1:8080/",
      },
    });

    expect(csp).toContain("script-src 'self'");
    expect(csp).toContain("media-src 'self' blob:");
    expect(csp).toContain("worker-src 'self'");
    expect(csp).not.toContain("worker-src 'self' blob:");
    expect(csp).toContain("connect-src 'self'");
    expect(csp).toContain("frame-src 'self' blob:");
    expect(csp).not.toContain("localhost");
    expect(csp).not.toContain("127.0.0.1");
    expect(csp.includes("style-src 'self' 'unsafe-inline'")).toBe(true);
    expect(csp).not.toMatch(
      /script-src[^;]*(?:unsafe-inline|unsafe-hashes|blob:|data:)/
    );
    expect(csp).not.toMatch(/frame-src[^;]*data:/);
  });

  it("includes configured VITE_KEYCLOAK_URL origin in connect-src and frame-src for non-demo builds", () => {
    expect(
      resolveWebOidcOrigin({
        mode: "production",
        isProduction: true,
        env: { VITE_KEYCLOAK_URL: "https://sso.cadence.example/auth/" },
      })
    ).toBe("https://sso.cadence.example");

    const prodWithOidc = buildWebCspContent({
      mode: "production",
      isProduction: true,
      env: { VITE_KEYCLOAK_URL: "https://sso.cadence.example/auth/" },
    });
    expect(prodWithOidc).toContain("worker-src 'self'");
    expect(prodWithOidc).not.toContain("worker-src 'self' blob:");
    expect(prodWithOidc).toContain(
      "connect-src 'self' https://sso.cadence.example"
    );
    expect(prodWithOidc).toContain(
      "frame-src 'self' blob: https://sso.cadence.example"
    );

    const devDefault = buildWebCspContent({
      mode: "development",
      isProduction: false,
      env: {},
    });
    expect(devDefault).toContain("connect-src 'self' http://localhost:8080");
    expect(devDefault).toContain(
      "frame-src 'self' blob: http://localhost:8080"
    );
  });

  it("hoists CSP meta immediately after <head> before all <script> and <link> tags", () => {
    const rawHtml = `<!doctype html>
<html>
  <head>
    <link rel="modulepreload" href="/assets/vendor.js" />
    <script type="module" src="/assets/index.js"></script>
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'" />
  </head>
  <body><div id="app"></div></body>
</html>`;

    const transformed = injectHoistedWebCspMeta(
      rawHtml,
      buildWebCspContent({ mode: "demo", isProduction: true, env: {} })
    );

    const cspCount = (
      transformed.match(/http-equiv="Content-Security-Policy"/g) || []
    ).length;
    expect(cspCount).toBe(1);

    const cspIdx = transformed.indexOf('http-equiv="Content-Security-Policy"');
    const linkIdx = transformed.indexOf("<link");
    const scriptIdx = transformed.indexOf("<script");
    expect(cspIdx).toBeGreaterThan(0);
    expect(cspIdx).toBeLessThan(linkIdx);
    expect(cspIdx).toBeLessThan(scriptIdx);
  });
});
