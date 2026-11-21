import { DEMO_EPOCH_ISO, DEMO_EPOCH_MS } from "demo-fixtures";
import { pinia } from "../stores/pinia.js";
import { useSessionStore } from "../stores/session.js";

const FALLBACK_DEMO_EPOCH_ISO = "2026-01-05T09:00:00Z";
const FALLBACK_DEMO_EPOCH_MS = Date.UTC(2026, 0, 5, 9, 0, 0);
const UINT32_MODULO = 2 ** 32;

const demoPrngStreams = new Map();

function resolveDemoEpochMs() {
  return DEMO_EPOCH_MS > 0 ? DEMO_EPOCH_MS : FALLBACK_DEMO_EPOCH_MS;
}

function resolveDemoEpochIso() {
  return DEMO_EPOCH_ISO || FALLBACK_DEMO_EPOCH_ISO;
}

function hashDemoSeed(input) {
  let hash = 0x811c9dc5;
  const text = String(input);
  for (let i = 0; i < text.length; i++) {
    hash ^= text.charCodeAt(i);
    hash = Math.imul(hash, 0x01000193);
  }
  return hash >>> 0 || 0x6d2b79f5;
}

/**
 * Demo mode is opt-in at build time: `vite --mode demo` / `pnpm build:demo`, or VITE_DEMO_MODE=true.
 *
 * Only an explicit demo build (or a unit test explicitly opting in via __MOCK_TEST_ENV__ + session.isDemoMode)
 * may seed synthetic tasks/alerts, start the MSW mock worker, or run unbacked clinical/stipend workflows.
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

/**
 * Returns true when synthetic demo mode is active (build-time demo mode, or explicit Vitest sandbox opt-in).
 * Never returns true in production or standard development browser builds unless built in demo mode.
 */
export function isDemoModeActive(env) {
  if (isDemoBuild(env)) {
    return true;
  }
  const resolvedEnv = env !== undefined ? env : import.meta.env;
  const isVitestSandbox =
    resolvedEnv?.MODE === "test" ||
    (typeof window !== "undefined" && Boolean(window.__MOCK_TEST_ENV__));
  if (isVitestSandbox) {
    const sessionStore = useSessionStore(pinia);
    return Boolean(sessionStore.session?.isDemoMode);
  }
  return false;
}

export function createDemoPrng(seedInput = "default") {
  let state =
    typeof seedInput === "number"
      ? seedInput >>> 0 || 0x6d2b79f5
      : hashDemoSeed(`cadence-demo:${resolveDemoEpochIso()}:${seedInput}`);
  return function nextRandom() {
    state = (state + 0x6d2b79f5) >>> 0;
    let t = Math.imul(state ^ (state >>> 15), 1 | state);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / UINT32_MODULO;
  };
}

export function resetDemoRandom(streamKey) {
  if (streamKey !== undefined) {
    demoPrngStreams.delete(streamKey);
    return;
  }
  demoPrngStreams.clear();
}

export function demoNowMs(stepOffset = 0, stepSizeMs = 1000) {
  if (!isDemoModeActive()) {
    return Date.now();
  }
  return resolveDemoEpochMs() + Math.trunc(stepOffset) * stepSizeMs;
}

export function demoNowIso(stepOffset = 0, stepSizeMs = 1000) {
  if (!isDemoModeActive()) {
    return new Date().toISOString();
  }
  return new Date(demoNowMs(stepOffset, stepSizeMs)).toISOString();
}

export function demoRandom(streamKey = "default") {
  if (!isDemoModeActive()) {
    const buf = new Uint32Array(1);
    globalThis.crypto.getRandomValues(buf);
    return buf[0] / UINT32_MODULO;
  }
  let stream = demoPrngStreams.get(streamKey);
  if (!stream) {
    stream = createDemoPrng(streamKey);
    demoPrngStreams.set(streamKey, stream);
  }
  return stream();
}

export function demoRandomToken(
  length = 13,
  radix = 36,
  streamKey = "default"
) {
  const alphabet = "0123456789abcdefghijklmnopqrstuvwxyz".slice(0, radix);
  let out = "";
  for (let i = 0; i < length; i++) {
    const idx = Math.floor(demoRandom(streamKey) * alphabet.length);
    out += alphabet[idx];
  }
  return out;
}
