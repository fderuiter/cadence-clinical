import { pinia } from "../stores/pinia.js";
import { useSessionStore } from "../stores/session.js";

/**
 * Demo mode is opt-in at build time: `vite --mode demo` / `pnpm build:demo`, or VITE_DEMO_MODE=true.
 *
 * Only an explicit demo build (or a unit test explicitly opting in via __MOCK_TEST_ENV__ + session.isDemoMode)
 * may seed synthetic tasks/alerts, start the MSW mock worker, or run unbacked clinical/stipend workflows.
 */
export function isDemoBuild(env = import.meta.env) {
  return (
    env?.MODE === "demo" ||
    env?.VITE_DEMO_MODE === "true" ||
    (typeof process !== "undefined" && process.env?.VITE_DEMO_MODE === "true")
  );
}

/**
 * Returns true when synthetic demo mode is active (build-time demo mode, or explicit Vitest sandbox opt-in).
 * Never returns true in production or standard development browser builds unless built in demo mode.
 */
export function isDemoModeActive(env = import.meta.env) {
  if (isDemoBuild(env)) {
    return true;
  }
  const isVitestSandbox =
    env?.MODE === "test" ||
    (typeof window !== "undefined" && Boolean(window.__MOCK_TEST_ENV__));
  if (isVitestSandbox) {
    const sessionStore = useSessionStore(pinia);
    return Boolean(sessionStore.session?.isDemoMode);
  }
  return false;
}
