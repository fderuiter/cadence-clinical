/**
 * Demo mode is opt-in at build time: `vite --mode demo` / `pnpm build:demo`, or VITE_DEMO_MODE=true.
 *
 * Only a demo build may seed the offline demo identity, start the MSW mock worker, or substitute sample data
 * when an API call fails. Every other build talks to the real gateway and surfaces failures (#4523), so a
 * network or authorization error is never mistaken for clinical data.
 */
export function isDemoBuild(env) {
  if (env !== undefined) {
    return env?.MODE === "demo" || env?.VITE_DEMO_MODE === "true";
  }
  return (
    import.meta.env?.MODE === "demo" ||
    import.meta.env?.VITE_DEMO_MODE === "true"
  );
}

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
