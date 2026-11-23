/**
 * Shared mode-aware Content-Security-Policy profile builder and <head> hoisting
 * helpers for Vite single-page applications (ADR-2498).
 */

/**
 * Resolves the configured Keycloak OIDC origin for non-demo Vite builds,
 * returning an empty string in isolated demo builds (`--mode demo` or `VITE_DEMO_MODE=true`).
 *
 * @param {{ mode?: string, isProduction?: boolean, env?: Record<string, string | undefined> }} [options]
 * @returns {string}
 */
export function resolveViteOidcOrigin({
  mode = "production",
  isProduction = true,
  env = {},
} = {}) {
  if (mode === "demo" || env.VITE_DEMO_MODE === "true") {
    return "";
  }
  const rawUrl =
    (env.VITE_KEYCLOAK_URL || "").trim() ||
    (isProduction ? "" : "http://localhost:8080/");
  if (!rawUrl) {
    return "";
  }
  try {
    const parsed = new URL(rawUrl);
    if (parsed.protocol === "http:" || parsed.protocol === "https:") {
      return parsed.origin;
    }
  } catch {
    // Ignore relative or malformed URLs; 'self' already covers same-origin paths.
  }
  return "";
}

/**
 * Builds a strict Content-Security-Policy directive string tailored to the Vite build mode
 * and optional `blob:` media/worker/frame capabilities.
 *
 * @param {{ mode?: string, isProduction?: boolean, env?: Record<string, string | undefined> }} [options]
 * @param {{ allowBlobMedia?: boolean, allowBlobWorker?: boolean, allowBlobFrame?: boolean }} [capabilities]
 * @returns {string}
 */
export function buildModeAwareCspContent(
  options = {},
  {
    allowBlobMedia = false,
    allowBlobWorker = false,
    allowBlobFrame = false,
  } = {}
) {
  const oidcOrigin = resolveViteOidcOrigin(options);
  const mediaSrc = allowBlobMedia ? "'self' blob:" : "'self'";
  const workerSrc = allowBlobWorker ? "'self' blob:" : "'self'";
  const connectSrc = oidcOrigin ? `'self' ${oidcOrigin}` : "'self'";
  const baseFrameSrc = allowBlobFrame ? "'self' blob:" : "'self'";
  const frameSrc = oidcOrigin ? `${baseFrameSrc} ${oidcOrigin}` : baseFrameSrc;

  return [
    "default-src 'self'",
    "script-src 'self'",
    "style-src 'self' 'unsafe-inline'",
    "img-src 'self' data: blob:",
    "font-src 'self' data:",
    `media-src ${mediaSrc}`,
    `worker-src ${workerSrc}`,
    `connect-src ${connectSrc}`,
    `frame-src ${frameSrc}`,
    "manifest-src 'self'",
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
  ].join("; ");
}

/**
 * Strips any existing `<meta http-equiv="Content-Security-Policy">` tags and inserts
 * the authoritative CSP `<meta>` immediately after `<head>` before any `<script>` or `<link>`.
 *
 * @param {string} html
 * @param {string} cspContent
 * @returns {string}
 */
export function injectHoistedCspMeta(html, cspContent) {
  const cspTag = `<meta http-equiv="Content-Security-Policy" content="${cspContent}" />`;
  const stripped = html.replace(
    /\s*<meta\b[^>]*\bhttp-equiv\s*=\s*(?:"Content-Security-Policy"|'Content-Security-Policy'|Content-Security-Policy)[^>]*>/gi,
    ""
  );
  if (/<head\b[^>]*>/i.test(stripped)) {
    return stripped.replace(
      /<head\b[^>]*>/i,
      (headOpen) => `${headOpen}\n    ${cspTag}`
    );
  }
  return `${cspTag}\n${stripped}`;
}
