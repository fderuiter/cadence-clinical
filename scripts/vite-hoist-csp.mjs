/**
 * Shared mode-aware Content-Security-Policy profile builder and <head> hoisting
 * helpers for Vite single-page applications (ADR-2498).
 */
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const THEME_CONTROLLER_PATH = path.resolve(
  __dirname,
  "../packages/ui/src/utils/themeController.js"
);

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

/**
 * Compiles packages/ui/src/utils/themeController.js into a self-contained IIFE
 * suitable for synchronous same-origin execution in <head> before first paint.
 *
 * @returns {string}
 */
export function buildThemeBootstrapScript() {
  const controllerSource = fs.readFileSync(THEME_CONTROLLER_PATH, "utf8");
  const storageKey =
    controllerSource.match(/THEME_STORAGE_KEY\s*=\s*"([^"]+)"/)?.[1] ||
    "vitepress-theme-appearance";
  const mediaQuery =
    controllerSource.match(/PREFERS_DARK_MEDIA_QUERY\s*=\s*"([^"]+)"/)?.[1] ||
    "(prefers-color-scheme: dark)";
  return `function bootstrapTheme(){var p="auto",d,r=document.documentElement;try{var v=(localStorage.getItem("${storageKey}")||"").trim().toLowerCase();p=v==="light"||v==="dark"?v:v==="true"?"dark":v==="false"?"light":"auto"}catch{}try{d=matchMedia("${mediaQuery}").matches}catch{}v=p==="dark"||p==="auto"&&d?"dark":"light";r.dataset.theme=p;r.classList.toggle("dark",v==="dark");r.style.colorScheme=v}bootstrapTheme();\n`;
}

/**
 * Vite plugin serving and emitting `theme-bootstrap.js` from `packages/ui` so
 * `apps/web` and `apps/subject-portal` apply `data-theme`, `.dark`, and
 * `color-scheme` before first paint under `script-src 'self'`.
 *
 * @returns {import("vite").Plugin}
 */
const VIRTUAL_THEME_BOOTSTRAP_ID = "\0cadence-theme-bootstrap";

export function createThemeBootstrapPlugin() {
  let resolvedBase = "/";
  let resolvedCommand = "serve";
  return {
    name: "cadence-theme-bootstrap",
    configResolved(resolved) {
      resolvedBase = resolved.base || "/";
      resolvedCommand = resolved.command || "serve";
    },
    configureServer(server) {
      server.middlewares.use((req, res, next) => {
        const cleanPath = (req.url || "").split("?")[0];
        const baseNormalized = resolvedBase.endsWith("/")
          ? resolvedBase
          : `${resolvedBase}/`;
        if (
          cleanPath === "/theme-bootstrap.js" ||
          cleanPath === `${baseNormalized}theme-bootstrap.js`
        ) {
          res.statusCode = 200;
          res.setHeader(
            "Content-Type",
            "application/javascript; charset=utf-8"
          );
          res.setHeader("Cache-Control", "no-store");
          res.end(buildThemeBootstrapScript());
          return;
        }
        next();
      });
    },
    buildStart() {
      if (resolvedCommand !== "build") {
        return;
      }
      this.emitFile({
        type: "chunk",
        id: VIRTUAL_THEME_BOOTSTRAP_ID,
        fileName: "theme-bootstrap.js",
      });
    },
    resolveId(id) {
      if (id === VIRTUAL_THEME_BOOTSTRAP_ID) {
        return VIRTUAL_THEME_BOOTSTRAP_ID;
      }
      return null;
    },
    load(id) {
      if (id === VIRTUAL_THEME_BOOTSTRAP_ID) {
        return buildThemeBootstrapScript();
      }
      return null;
    },
  };
}

/**
 * Merges `.env*` `VITE_*` variables with `process.env` for a resolved Vite config.
 *
 * @param {{ mode: string }} resolved
 * @param {string} rootDir
 * @param {(mode: string, envDir: string, prefixes?: string | string[]) => Record<string, string>} loadEnv
 * @returns {Record<string, string | undefined>}
 */
export function resolveMergedViteEnv(resolved, rootDir, loadEnv) {
  const fileEnv = loadEnv(resolved.mode, rootDir, "VITE_");
  return { ...fileEnv, ...process.env };
}

/**
 * Vite plugin ensuring `serve` in `development` mode points `VITE_API_BASE_URL` at the local dev server
 * when unset so `/api` requests flow through Vite's same-origin gateway proxy without CORS errors,
 * without mutating `process.env` or leaking localhost URLs into non-development modes.
 *
 * @param {{ rootDir: string, devPort: number, loadEnv: (mode: string, envDir: string, prefixes?: string | string[]) => Record<string, string> }} options
 * @returns {import("vite").Plugin}
 */
export function createSameOriginApiInDevPlugin({
  rootDir,
  devPort,
  loadEnv,
  demoCacheDir,
}) {
  return {
    name: "same-origin-api-in-dev",
    config(_config, { command, mode }) {
      const patch = {};
      if (demoCacheDir && mode === "demo") {
        patch.cacheDir = demoCacheDir;
      }
      const gatewayProxy = resolveViteGatewayProxy({ command, mode });
      if (gatewayProxy) {
        patch.server = { proxy: gatewayProxy };
      }
      if (command === "serve" && mode === "development") {
        const configured =
          (process.env.VITE_API_BASE_URL || "").trim() ||
          (
            loadEnv(mode, rootDir, "VITE_API_BASE_URL").VITE_API_BASE_URL || ""
          ).trim();
        if (!configured) {
          patch.define = {
            "import.meta.env.VITE_API_BASE_URL": JSON.stringify(
              `http://localhost:${devPort}`
            ),
          };
        }
      }
      return Object.keys(patch).length > 0 ? patch : undefined;
    },
  };
}

/**
 * Applies reverse-proxy friendly keep-alive and header timeouts to a Vite preview server.
 *
 * @param {{ httpServer?: { keepAliveTimeout?: number, headersTimeout?: number } | null }} server
 */
export function configureVitePreviewTimeouts(server) {
  if (server?.httpServer) {
    server.httpServer.keepAliveTimeout = 65000;
    server.httpServer.headersTimeout = 66000;
  }
}

/**
 * Builds the shared `/api` Vite server proxy rule forwarding to the API gateway.
 * Proxy TLS certificate verification (`secure: true`) is enforced outside `development` mode.
 *
 * @param {string} gatewayUrl
 * @param {{ mode?: string }} [options]
 */
export function createViteGatewayProxy(
  gatewayUrl,
  { mode = "production" } = {}
) {
  return {
    "/api": {
      target: gatewayUrl,
      changeOrigin: true,
      secure: mode !== "development",
    },
  };
}

/**
 * Resolves the `/api` Vite server proxy configuration for the active command and mode.
 * Only `serve` in `development` mode falls back to `http://localhost:8000`; non-development
 * configurations omit `/api` proxy rules unless `CADENCE_GATEWAY_URL` is explicitly set.
 *
 * @param {{ command?: string, mode?: string, env?: Record<string, string | undefined> }} [options]
 */
export function resolveViteGatewayProxy({
  command = "build",
  mode = "production",
  env = process.env,
} = {}) {
  const explicitGatewayUrl = (env.CADENCE_GATEWAY_URL || "").trim();
  const gatewayUrl =
    explicitGatewayUrl ||
    (command === "serve" && mode === "development"
      ? "http://localhost:8000"
      : "");
  if (!gatewayUrl) {
    return undefined;
  }
  return createViteGatewayProxy(gatewayUrl, { mode });
}
