import { DEMO_EPOCH_ISO, DEMO_EPOCH_MS } from "demo-fixtures";
import { createDemoRuntime, isDemoBuild } from "demo-fixtures/runtime";

/**
 * Demo mode is opt-in at build time: `vite --mode demo` / `pnpm build:demo`, or VITE_DEMO_MODE=true.
 *
 * Only a demo build may seed the offline demo identity, start the MSW mock worker, or substitute sample data
 * when an API call fails. Every other build talks to the real gateway and surfaces failures (#4523), so a
 * network or authorization error is never mistaken for clinical data.
 */
export { isDemoBuild };

function resolveSameOriginBaseUrl(fallback = "") {
  return typeof globalThis.location?.origin === "string" &&
    globalThis.location.origin.startsWith("http")
    ? globalThis.location.origin.replace(/\/+$/, "")
    : fallback;
}

export function resolveGatewayBaseUrl() {
  const configured = (import.meta.env?.VITE_API_BASE_URL || "").trim();
  if (configured) {
    if (configured === "/" || configured.toLowerCase() === "same-origin") {
      return resolveSameOriginBaseUrl("");
    }
    let parsed;
    try {
      parsed = new URL(configured);
    } catch {
      throw new Error(
        `Invalid VITE_API_BASE_URL '${configured}': must be an absolute http(s) URL or '/' for same-origin routing.`
      );
    }
    if (parsed.protocol !== "http:" && parsed.protocol !== "https:") {
      throw new Error(
        `Invalid VITE_API_BASE_URL '${configured}': must use http: or https: protocol.`
      );
    }
    return configured.replace(/\/+$/, "");
  }
  if (import.meta.env.MODE === "demo") {
    return resolveSameOriginBaseUrl("/");
  }
  if (isDemoBuild()) {
    const demoOrigin = resolveSameOriginBaseUrl("");
    if (demoOrigin) {
      return demoOrigin;
    }
    return "/";
  }
  if (
    (import.meta.env.MODE === "development" ||
      import.meta.env.MODE === "test") &&
    !import.meta.env?.PROD
  ) {
    return "http://localhost:8000";
  }
  throw new Error(
    "VITE_API_BASE_URL must be configured (or set to '/' for explicit same-origin routing) in non-development builds."
  );
}

const webDemoRuntime = /* @__PURE__ */ createDemoRuntime({
  isDemoActive: () => isDemoBuild(),
  epochIso: DEMO_EPOCH_ISO,
  epochMs: DEMO_EPOCH_MS,
  defaultTokenLength: 7,
});

export const {
  createDemoPrng,
  resetDemoRandom,
  nextDemoStep,
  demoNowMs,
  demoNowDate,
  demoNowIso,
  demoRandom,
  demoRandomInt,
} = webDemoRuntime;

export function demoRandomToken(length = 7, radix = 36, streamKey = "default") {
  return webDemoRuntime.demoRandomToken(length, radix, streamKey);
}
