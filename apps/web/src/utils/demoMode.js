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

export function resolveGatewayBaseUrl() {
  const configured = import.meta.env?.VITE_API_BASE_URL;
  if (configured) return configured;
  if (import.meta.env?.MODE === "demo") {
    return typeof globalThis.location?.origin === "string" &&
      globalThis.location.origin.startsWith("http")
      ? globalThis.location.origin
      : "/";
  }
  if (
    isDemoBuild() &&
    typeof globalThis.location?.origin === "string" &&
    globalThis.location.origin.startsWith("http")
  ) {
    return globalThis.location.origin;
  }
  return "http://localhost:8000";
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
