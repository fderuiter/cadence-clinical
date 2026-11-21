import { DEMO_EPOCH_ISO, DEMO_EPOCH_MS } from "demo-fixtures";

const FALLBACK_DEMO_EPOCH_ISO = "2026-01-05T09:00:00Z";
const FALLBACK_DEMO_EPOCH_MS = Date.UTC(2026, 0, 5, 9, 0, 0);
const UINT32_MODULO = 2 ** 32;

const demoPrngStreams = new Map();
const demoStepCounters = new Map();

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
    demoStepCounters.delete(streamKey);
    return;
  }
  demoPrngStreams.clear();
  demoStepCounters.clear();
}

export function nextDemoStep(counterKey = "default") {
  const current = demoStepCounters.get(counterKey) || 0;
  demoStepCounters.set(counterKey, current + 1);
  return current;
}

export function demoNowMs(stepOffset = 0, stepSizeMs = 1000) {
  if (!isDemoBuild()) {
    return Date.now();
  }
  return resolveDemoEpochMs() + Math.trunc(stepOffset) * stepSizeMs;
}

export function demoNowDate(stepOffset = 0, stepSizeMs = 1000) {
  if (!isDemoBuild()) {
    return new Date();
  }
  return new Date(demoNowMs(stepOffset, stepSizeMs));
}

export function demoNowIso(stepOffset = 0, stepSizeMs = 1000) {
  if (!isDemoBuild()) {
    return new Date().toISOString();
  }
  return demoNowDate(stepOffset, stepSizeMs).toISOString();
}

export function demoRandom(streamKey = "default") {
  if (!isDemoBuild()) {
    if (
      typeof globalThis.crypto !== "undefined" &&
      typeof globalThis.crypto.getRandomValues === "function"
    ) {
      const buf = new Uint32Array(1);
      globalThis.crypto.getRandomValues(buf);
      return buf[0] / UINT32_MODULO;
    }
    return 0.5;
  }
  let stream = demoPrngStreams.get(streamKey);
  if (!stream) {
    stream = createDemoPrng(streamKey);
    demoPrngStreams.set(streamKey, stream);
  }
  return stream();
}

export function demoRandomInt(min, maxInclusive, streamKey = "default") {
  const lower = Math.ceil(min);
  const upper = Math.floor(maxInclusive);
  return Math.floor(lower + demoRandom(streamKey) * (upper - lower + 1));
}

export function demoRandomToken(length = 7, radix = 36, streamKey = "default") {
  const alphabet = "0123456789abcdefghijklmnopqrstuvwxyz".slice(0, radix);
  let out = "";
  for (let i = 0; i < length; i++) {
    const idx = Math.floor(demoRandom(streamKey) * alphabet.length);
    out += alphabet[idx];
  }
  return out;
}
