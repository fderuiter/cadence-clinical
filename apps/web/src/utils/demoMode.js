/**
 * Demo mode is opt-in at build time: `vite --mode demo` / `pnpm build:demo`, or VITE_DEMO_MODE=true.
 *
 * Only a demo build may seed the offline demo identity, start the MSW mock worker, or substitute sample data
 * when an API call fails. Every other build talks to the real gateway and surfaces failures (#4523), so a
 * network or authorization error is never mistaken for clinical data.
 */
export function isDemoBuild(env = import.meta.env) {
  return (
    env?.MODE === "demo" ||
    env?.VITE_DEMO_MODE === "true" ||
    (typeof process !== "undefined" && process?.env?.VITE_DEMO_MODE === "true")
  );
}
